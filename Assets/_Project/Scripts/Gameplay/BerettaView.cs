using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BerettaView : MonoBehaviour
    {
        enum Motion { Idle, Fire, LastShot, Reload, EmptyReload, Draw, Holster, RaiseAfterFlashlight }

        // The locked slide position in the last-shot and empty-reload clips.
        const float LockedSlideZ = -0.0122f;
        const uint DefaultRenderingLayer = 1u;
        internal const float HolsterDuration = 0.29f;
        internal const float FlashlightRaiseDuration = 0.36f;

        [SerializeField] Animator _animator;
        [SerializeField] Transform _slide;
        [SerializeField] Transform _muzzle;
        [SerializeField] AnimationClip _idle;
        [SerializeField] AnimationClip _fire;
        [SerializeField] AnimationClip _lastShot;
        [SerializeField] AnimationClip _reload;
        [SerializeField] AnimationClip _emptyReload;
        [SerializeField] AnimationClip _draw;
        [SerializeField] AnimationClip _holster;
        [SerializeField] AudioClip _magazineOut;
        [SerializeField] AudioClip _magazineIn;
        [SerializeField] AudioClip _slideRack;

        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable[] _poses;
        AnimationClip[] _clips;
        AudioSource _handlingAudio;
        Motion _motion;
        Motion _previousMotion;
        float _startedAt;
        float _duration;
        float _blendTime;
        float _previousPoseTime;
        float _poseTime;
        float _idleTime;
        float _lastElapsed;

        internal Transform Muzzle => _muzzle;
        internal Vector3 GripLightPosition => _muzzle.parent.TransformPoint(new Vector3(0f, -0.045f, 0f));
        internal bool IsDrawing =>
            (_motion == Motion.Draw || _motion == Motion.RaiseAfterFlashlight) && ActionElapsed < _duration;
        internal bool IsHolstering => _motion == Motion.Holster && ActionElapsed < _duration;
        float ActionElapsed => Time.time - _startedAt;

        internal bool Initialize(AudioSource handlingAudio)
        {
            _clips = new[] { _idle, _fire, _lastShot, _reload, _emptyReload, _draw, _holster, _holster };
            if (_animator == null || _slide == null || _muzzle == null)
                return false;
            foreach (var clip in _clips)
                if (clip == null || clip.length <= 0f)
                    return false;

            _handlingAudio = handlingAudio;
            SetFlashlightReceivers();
            IncludeSceneLights();
            SceneManager.sceneLoaded += OnSceneLoaded;
            _animator.applyRootMotion = false;
            _animator.fireEvents = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _graph = PlayableGraph.Create("Beretta");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _mixer = AnimationMixerPlayable.Create(_graph, _clips.Length);
            _poses = new AnimationClipPlayable[_clips.Length];
            for (int i = 0; i < _clips.Length; i++)
            {
                _poses[i] = AnimationClipPlayable.Create(_graph, _clips[i]);
                _poses[i].SetSpeed(0);
                _graph.Connect(_poses[i], 0, _mixer, i);
            }
            AnimationPlayableOutput.Create(_graph, "Beretta pose", _animator).SetSourcePlayable(_mixer);
            _graph.Play();
            return true;
        }

        void SetFlashlightReceivers()
        {
            // The sights are part of the slide. Only the arms and lower pistol assembly receive chest spill.
            var hands = _animator.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var frame = _muzzle.parent;
            AddFlashlightReceiver(hands);
            AddFlashlightReceiver(frame.GetComponent<Renderer>());
            AddFlashlightReceiver(frame.parent.Find("mag")?.GetComponent<Renderer>());
            AddFlashlightReceiver(frame.parent.Find("trigger")?.GetComponent<Renderer>());
        }

        static void AddFlashlightReceiver(Renderer renderer)
        {
            if (renderer != null)
                renderer.renderingLayerMask |= Flashlight.WeaponSpillRenderingLayer;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => IncludeSceneLights();

        static void IncludeSceneLights()
        {
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                // Keep the carried flashlight from flooding the hands and gun at point-blank range.
                if (light.GetComponentInParent<Flashlight>(true) != null)
                    continue;

                var data = light.GetUniversalAdditionalLightData();
                uint layers = data.renderingLayers;
                if ((layers & DefaultRenderingLayer) != 0)
                    data.renderingLayers = layers | PlayerPistol.WeaponRenderingLayer;
            }
        }

        internal void Draw(bool wasVisible) => Play(Motion.Draw, 0.85f, wasVisible ? 0.12f : 0f);
        internal void Holster() => Play(Motion.Holster, HolsterDuration, 0.07f);
        internal void RaiseAfterFlashlight() => Play(Motion.RaiseAfterFlashlight, FlashlightRaiseDuration, 0f);
        internal void Fire(bool lastShot, float shotInterval) =>
            Play(lastShot ? Motion.LastShot : Motion.Fire, shotInterval, 0f);

        internal float Reload(bool empty)
        {
            Motion reload = empty ? Motion.EmptyReload : Motion.Reload;
            float duration = _clips[(int)reload].length;
            Play(reload, duration, 0.10f);
            return duration;
        }

        internal void ResetPose()
        {
            _motion = Motion.Idle;
            _previousMotion = Motion.Idle;
            _poseTime = 0f;
            _idleTime = 0f;
        }

        void Play(Motion motion, float duration, float blend)
        {
            _previousMotion = _motion;
            _previousPoseTime = _poseTime;
            _motion = motion;
            _startedAt = Time.time;
            _duration = duration;
            _blendTime = blend;
            _lastElapsed = -0.001f;
            _poseTime = 0f;
        }

        internal void UpdatePose(float aim, int loaded)
        {
            float elapsed = ActionElapsed;
            if (_motion != Motion.Idle)
            {
                PlayHandlingSounds(elapsed);
                if (elapsed >= _duration && _motion != Motion.Holster)
                {
                    _motion = Motion.Idle;
                    _previousMotion = Motion.Idle;
                    _idleTime = 0f;
                }
            }

            float weight = 1f;
            float idleWeight = 0f;
            if (_motion == Motion.Idle)
            {
                // Keep both sights steady while aiming. At the hip, sample the soft part of the idle.
                _idleTime += Time.deltaTime;
                float idleSwing = (1f - Mathf.Cos(_idleTime * 3f)) * 0.14f;
                _poseTime = idleSwing * (1f - aim) * _idle.length;
            }
            else
            {
                float progress = Mathf.Clamp01(elapsed / _duration);
                if (_motion == Motion.RaiseAfterFlashlight)
                {
                    idleWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, progress));
                    progress = Mathf.SmoothStep(1f, 0f, progress);
                }
                _poseTime = progress * _clips[(int)_motion].length;
                if (_previousMotion != _motion && _blendTime > 0f)
                    weight = Mathf.SmoothStep(0f, 1f, elapsed / _blendTime);
            }

            for (int i = 0; i < _poses.Length; i++)
                _mixer.SetInputWeight(i, 0f);
            if (weight < 1f)
            {
                _poses[(int)_previousMotion].SetTime(_previousPoseTime);
                _mixer.SetInputWeight((int)_previousMotion, 1f - weight);
            }
            if (idleWeight > 0f)
            {
                _poses[(int)Motion.Idle].SetTime(0f);
                _mixer.SetInputWeight((int)Motion.Idle, idleWeight);
                weight -= idleWeight;
            }
            _poses[(int)_motion].SetTime(_poseTime);
            _mixer.SetInputWeight((int)_motion, weight);
            _graph.Evaluate(0f);

            if (loaded == 0 && (_motion == Motion.Idle || IsDrawing || _motion == Motion.Holster))
            {
                Vector3 slidePosition = _slide.localPosition;
                slidePosition.z = LockedSlideZ;
                _slide.localPosition = slidePosition;
            }
        }

        void PlayHandlingSounds(float elapsed)
        {
            // Each recording includes lead-in. Start it ahead of the corresponding keyframe.
            switch (_motion)
            {
                case Motion.Reload:
                    PlayCue(_magazineOut, 0.03f, elapsed);
                    PlayCue(_magazineIn, 0.77f, elapsed);
                    PlayCue(_slideRack, 1.48f, elapsed);
                    break;
                case Motion.EmptyReload:
                    PlayCue(_magazineOut, 0.02f, elapsed);
                    PlayCue(_magazineIn, 0.57f, elapsed);
                    PlayCue(_slideRack, 1.81f, elapsed);
                    break;
                case Motion.Draw:
                    PlayCue(_slideRack, 0.34f, elapsed);
                    break;
            }
            _lastElapsed = elapsed;
        }

        void PlayCue(AudioClip clip, float cueTime, float elapsed)
        {
            if (clip != null && _lastElapsed < cueTime && elapsed >= cueTime)
                _handlingAudio.PlayOneShot(clip);
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_graph.IsValid())
                _graph.Destroy();
        }
    }
}
