using StarterAssets;
using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FirstPersonController))]
    public sealed class PlayerFootsteps : MonoBehaviour
    {
        const float StopFadeSeconds = 0.36f;
        const float StartFadeSeconds = 0.055f;
        const float MinimumStepInterval = 0.21f;

        [SerializeField] AudioClip[] _clips;
        [SerializeField, Range(0f, 1f)] float _volume = 0.224f;

        readonly AudioSource[] _voices = new AudioSource[3];
        readonly float[] _voiceVolumes = new float[3];
        FirstPersonController _movement;
        int _lastMovementFrame = -2;
        int _lastClip = -1;
        int _nextVoice;
        float _gain;
        float _progress;
        float _nextStepAt;

        void Awake()
        {
            _movement = GetComponent<FirstPersonController>();
            if (_clips == null || _clips.Length == 0)
            {
                enabled = false;
                return;
            }

            var emitter = new GameObject("Concrete Footsteps");
            emitter.transform.SetParent(transform, false);
            for (int i = 0; i < _voices.Length; i++)
            {
                var voice = new GameObject($"Footstep {i + 1}");
                voice.transform.SetParent(emitter.transform, false);
                var source = voice.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.dopplerLevel = 0f;
                var echo = voice.AddComponent<AudioEchoFilter>();
                echo.delay = 106f;
                echo.decayRatio = 0.37f;
                echo.dryMix = 1f;
                echo.wetMix = 0.23f;
                _voices[i] = source;
            }
        }

        void OnEnable() => _movement.GroundedTravel += ReportTravel;

        void ReportTravel(float distance)
        {
            float speed = distance / Time.deltaTime;
            if (speed < 0.18f)
                return;

            if (_lastMovementFrame < Time.frameCount - 1)
                _progress = 0.54f;
            _lastMovementFrame = Time.frameCount;

            bool quiet = _movement.IsWalkingQuietly;
            float pace = Mathf.InverseLerp(1.6f, 6.6f, speed);
            float stepLength = quiet ? 0.96f : Mathf.Lerp(1.14f, 1.8f, pace);
            _progress += distance / stepLength;
            if (_progress < 1f || Time.time < _nextStepAt)
                return;

            _progress %= 1f;
            _nextStepAt = Time.time + MinimumStepInterval;
            PlayStep((quiet ? 0.24f : Mathf.Lerp(0.75f, 1f, pace)) * _volume);
        }

        void PlayStep(float volume)
        {
            int clipIndex = Random.Range(0, _clips.Length);
            if (_clips.Length > 1 && clipIndex == _lastClip)
                clipIndex = (clipIndex + Random.Range(1, _clips.Length)) % _clips.Length;
            _lastClip = clipIndex;

            AudioClip clip = _clips[clipIndex];
            if (clip == null)
                return;

            AudioSource voice = _voices[_nextVoice];
            _voiceVolumes[_nextVoice] = volume * Random.Range(0.92f, 1.08f);
            voice.clip = clip;
            voice.pitch = Random.Range(0.92f, 1.03f);
            voice.volume = _gain * _voiceVolumes[_nextVoice];
            voice.Play();
            _nextVoice = (_nextVoice + 1) % _voices.Length;
        }

        void LateUpdate()
        {
            if (AudioListener.pause)
                return;

            bool moving = _lastMovementFrame == Time.frameCount;
            float duration = moving ? StartFadeSeconds : StopFadeSeconds;
            _gain = Mathf.MoveTowards(_gain, moving ? 1f : 0f, Time.deltaTime / duration);
            float gain = Mathf.SmoothStep(0f, 1f, _gain);
            for (int i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];
                voice.volume = gain * _voiceVolumes[i];
                if (gain == 0f && voice.isPlaying)
                    voice.Stop();
            }
        }

        void OnDisable()
        {
            if (_movement != null)
                _movement.GroundedTravel -= ReportTravel;

            foreach (var voice in _voices)
                if (voice != null)
                    voice.Stop();
            _gain = 0f;
            _progress = 0f;
            _lastMovementFrame = -2;
            _nextStepAt = 0f;
        }
    }
}
