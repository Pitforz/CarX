using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class DeniConversationAnimation : MonoBehaviour
    {
        [SerializeField] Animator _animator;
        [SerializeField] SkinnedMeshRenderer _face;
        [SerializeField] SkinnedMeshRenderer _eyes;
        [SerializeField] SkinnedMeshRenderer _lashes;
        [SerializeField] Vector3 _eyesInHead;
        [SerializeField] Vector3 _mouthInHead;
        [SerializeField] Vector3 _faceForwardInHead = Vector3.forward;
        [SerializeField] Vector3 _faceUpInHead = Vector3.up;
        [SerializeField, Min(0f)] float _attentionDistance = 4.5f;

        readonly DialogueLipSync _lipSync = new();
        Transform _chest, _neck, _head;
        Transform _leftArm, _leftForearm, _leftHand;
        Transform[] _drivenBones;
        Quaternion[] _basePose;
        Camera _listener;
        AudioSource _lineSource;
        AudioClip _lineClip;
        int _mouthOpen, _lipRound, _lipSpread, _lipPress, _faceBlink, _lashBlink;
        int _lookLeft, _lookRight, _lookUp, _lookDown;
        bool _poseApplied, _conversation, _hasLine, _speaking, _rightGesture = true, _visible;
        float _lineStart, _lineDuration, _nextBlink, _nextSightCheck;
        float _attention, _yaw, _pitch, _eyeYaw, _eyePitch, _nod;
        float _nodStrength, _headEmphasis;
        float _dismissiveWaveAt;
        Vector4 _mouth;
        System.Random _blinkTimes;

        void Awake()
        {
            if (_animator == null || !_animator.isHuman)
            {
                enabled = false;
                return;
            }

            _chest = _animator.GetBoneTransform(HumanBodyBones.UpperChest) ??
                _animator.GetBoneTransform(HumanBodyBones.Chest);
            _neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            _leftArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _leftForearm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _drivenBones = new[] { _chest, _neck, _head, _leftArm, _leftForearm, _leftHand };
            _basePose = new Quaternion[_drivenBones.Length];
            _mouthOpen = Shape(_face, "MouthOpen");
            _lipRound = Shape(_face, "LipRound");
            _lipSpread = Shape(_face, "LipSpread");
            _lipPress = Shape(_face, "LipPress");
            _faceBlink = Shape(_face, "Blink");
            _lashBlink = Shape(_lashes, "Blink");
            _lookLeft = Shape(_eyes, "LookLeft");
            _lookRight = Shape(_eyes, "LookRight");
            _lookUp = Shape(_eyes, "LookUp");
            _lookDown = Shape(_eyes, "LookDown");
            _blinkTimes = new System.Random();
            _nextBlink = Time.time + BlinkDelay();
            enabled = _head != null;
        }

        internal void BeginConversation(Camera listener)
        {
            if (listener != null)
                _listener = listener;
            _conversation = true;
            _nextSightCheck = 0f;
        }

        internal void AttachVoice(Transform emitter)
        {
            if (_head == null)
                return;
            emitter.SetParent(_head, false);
            emitter.localPosition = _mouthInHead;
        }

        internal void BeginLine(string text, bool speaking, float duration, AudioSource voice, AudioClip clip,
            float dismissiveWaveAt = 0f)
        {
            _lineSource = voice;
            _lineClip = clip;
            _lineDuration = duration;
            _lineStart = Time.time;
            _hasLine = true;
            _speaking = speaking;
            _dismissiveWaveAt = dismissiveWaveAt;
            _nodStrength = Mathf.InverseLerp(0.65f, 2.8f, duration);
            if (!speaking)
                return;

            _rightGesture = !_rightGesture;
            _lipSync.Prepare(text, clip);
        }

        internal void EndLine()
        {
            _hasLine = _speaking = false;
            _lineSource = null;
            _lineClip = null;
        }

        internal void EndConversation()
        {
            _conversation = false;
            EndLine();
        }

        // Remove last frame's offsets before Animator evaluates the next idle pose.
        void Update() => RestorePose();

        void LateUpdate()
        {
            if (_head == null)
                return;

            for (int i = 0; i < _drivenBones.Length; i++)
                if (_drivenBones[i] != null)
                    _basePose[i] = _drivenBones[i].localRotation;
            _poseApplied = true;

            float dt = Time.deltaTime;
            float elapsed = !_hasLine ? 0f : _lineSource != null && _lineClip != null
                ? (float)_lineSource.timeSamples / _lineClip.frequency : Time.time - _lineStart;
            Vector4 mouth = _speaking ? _lipSync.Evaluate(elapsed, _lineDuration) : Vector4.zero;
            _mouth = Vector4.Lerp(_mouth, mouth, 1f - Mathf.Exp(-27f * dt));
            ApplyMouth(_mouth);
            UpdateBlink();

            float beat = _speaking ? Pulse(elapsed, Mathf.Min(0.8f, _lineDuration * 0.45f), 0.72f) : 0f;
            if (_speaking && _lineDuration > 3.8f)
                beat = Mathf.Max(beat, 0.62f * Pulse(elapsed, _lineDuration * 0.73f, 0.75f));
            _headEmphasis = Damp(_headEmphasis, beat * 0.45f, 8f, dt);
            float nod = _hasLine && !_speaking ? _nodStrength * Pulse(elapsed, _lineDuration * 0.66f, 0.26f) : 0f;
            _nod = Damp(_nod, nod, 14f, dt);

            Vector3 gaze = UpdateAttention(dt, elapsed);
            ApplyBodyPose();
            if (_speaking && _dismissiveWaveAt > 0f)
                ApplyDismissiveWave(elapsed - _dismissiveWaveAt);
            ApplyEyeGaze(gaze, dt);
        }

        void ApplyDismissiveWave(float t)
        {
            // Anticipate the accented word, brush outward once, then let the wrist settle.
            float lift = Ease(-0.25f, 0.28f, t) * (1f - Ease(0.48f, 1.22f, t));
            float sweep = Ease(0.28f, 0.77f, t) * (1f - Ease(0.8f, 1.43f, t));
            Rotate(_leftArm, transform.right, -31f * lift);
            Rotate(_leftArm, transform.forward, -16f * sweep);
            Rotate(_leftForearm, transform.right, -46f * lift);
            Rotate(_leftForearm, transform.up, -23f * sweep);
            Rotate(_leftHand, transform.right, -12f * lift + 22f * sweep);
        }

        static float Ease(float from, float to, float time) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, time));

        Vector3 UpdateAttention(float dt, float elapsed)
        {
            if (_listener == null || !_listener.isActiveAndEnabled || _listener.gameObject.scene != gameObject.scene)
                _listener = Camera.main;

            Vector3 origin = _head.TransformPoint(_eyesInHead);
            Vector3 target = _listener != null ? _listener.transform.position : origin + transform.forward;
            Vector3 offset = target - origin;
            Vector3 local = transform.InverseTransformDirection(offset);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            float range = _attentionDistance + (_conversation ? 2f : 0f);
            bool inReach = _listener != null && _listener.gameObject.scene == gameObject.scene &&
                offset.sqrMagnitude < range * range && Mathf.Abs(yaw) < 102f;

            if (inReach && Time.time >= _nextSightCheck)
            {
                _nextSightCheck = Time.time + 0.13f;
                _visible = !Physics.Linecast(target, origin, out var hit, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) || hit.transform.IsChildOf(transform);
            }

            float weight = inReach && _visible ? 1f : 0f;
            _attention = Damp(_attention, weight, weight > _attention ? 7f : 4.5f, dt);
            float glance = _speaking && _lineDuration > 2.8f
                ? Pulse(elapsed, _lineDuration * 0.57f, 0.36f) * (_rightGesture ? 1f : -1f)
                : 0f;
            _yaw = Damp(_yaw, Mathf.Clamp(yaw + 8f * glance, -64f, 64f) * _attention, 8f, dt);
            _pitch = Damp(_pitch, Mathf.Clamp(pitch + 3f * glance, -21f, 24f) * _attention, 8f, dt);
            return Quaternion.AngleAxis(8f * glance, transform.up) * offset;
        }

        void ApplyBodyPose()
        {
            Rotate(_chest, transform.up, _yaw * 0.19f);
            Rotate(_chest, transform.right, _pitch * 0.13f);
            Rotate(_neck, transform.up, _yaw * 0.21f);
            Rotate(_neck, transform.right, _pitch * 0.2f + _nod * 2f);
            Rotate(_head, transform.up, _yaw * 0.6f);
            Rotate(_head, transform.right, _pitch * 0.67f + _nod * 5f - _headEmphasis * 1.8f);
            Rotate(_head, transform.forward, _headEmphasis * (_rightGesture ? 1f : -1f));
        }

        void ApplyEyeGaze(Vector3 gaze, float dt)
        {
            var faceRotation = Quaternion.LookRotation(_head.TransformDirection(_faceForwardInHead),
                _head.TransformDirection(_faceUpInHead));
            Vector3 local = Quaternion.Inverse(faceRotation) * gaze;
            float yaw = Mathf.Atan2(local.x, Mathf.Max(local.z, 0.01f)) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            _eyeYaw = Damp(_eyeYaw, Mathf.Clamp(yaw, -18f, 18f) * _attention, 20f, dt);
            _eyePitch = Damp(_eyePitch, Mathf.Clamp(pitch, -12f, 12f) * _attention, 20f, dt);
            ApplyEyes(_eyeYaw, _eyePitch);
        }

        void ApplyMouth(Vector4 pose)
        {
            SetShape(_face, _mouthOpen, pose.x);
            SetShape(_face, _lipRound, pose.y);
            SetShape(_face, _lipSpread, pose.z);
            SetShape(_face, _lipPress, pose.w);
        }

        void ApplyEyes(float yaw, float pitch)
        {
            SetShape(_eyes, _lookLeft, -yaw / 18f);
            SetShape(_eyes, _lookRight, yaw / 18f);
            SetShape(_eyes, _lookUp, -pitch / 12f);
            SetShape(_eyes, _lookDown, pitch / 12f);
        }

        void UpdateBlink()
        {
            float elapsed = Time.time - _nextBlink;
            float weight = 0f;
            if (elapsed >= 0.19f)
                _nextBlink = Time.time + BlinkDelay();
            else if (elapsed > 0f)
                weight = Mathf.SmoothStep(0f, 1f, Mathf.Min(elapsed / 0.054f, (0.19f - elapsed) / 0.12f));
            SetShape(_face, _faceBlink, weight);
            SetShape(_lashes, _lashBlink, weight);
        }

        float BlinkDelay() => 2.6f + (float)_blinkTimes.NextDouble() * 2.55f;

        void RestorePose()
        {
            if (!_poseApplied)
                return;
            for (int i = 0; i < _drivenBones.Length; i++)
                if (_drivenBones[i] != null)
                    _drivenBones[i].localRotation = _basePose[i];
            _poseApplied = false;
        }

        void OnDisable()
        {
            RestorePose();
            EndConversation();
            if (_drivenBones == null)
                return;
            _mouth = Vector4.zero;
            ApplyMouth(_mouth);
            ApplyEyes(0f, 0f);
            SetShape(_face, _faceBlink, 0f);
            SetShape(_lashes, _lashBlink, 0f);
        }

        static float Damp(float current, float target, float rate, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));
        static float Pulse(float elapsed, float center, float halfWidth) =>
            Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(elapsed - center) / halfWidth);
        static int Shape(SkinnedMeshRenderer renderer, string name) =>
            renderer != null && renderer.sharedMesh != null ? renderer.sharedMesh.GetBlendShapeIndex(name) : -1;

        static void SetShape(SkinnedMeshRenderer renderer, int index, float value)
        {
            if (renderer != null && index >= 0)
                renderer.SetBlendShapeWeight(index, Mathf.Clamp01(value) * 100f);
        }

        static void Rotate(Transform bone, Vector3 axis, float angle)
        {
            if (bone != null && Mathf.Abs(angle) > 0.001f)
                bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
        }
    }
}
