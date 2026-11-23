using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(80)]
    internal sealed class NpcIdleMotion : MonoBehaviour
    {
        const float AttentionRange = 7.5f;
        internal const float AttentionPoll = 0.19f;
        static readonly int StandingIdle = Animator.StringToHash("Base Layer.Standing Idle");
        static readonly int LookAround = Animator.StringToHash("Base Layer.Look Around");

        [SerializeField] RuntimeAnimatorController _idleController;

        Animator _animator;
        NpcSpeechState _speech;
        Camera _listener;
        Transform _spine, _head;
        RestingArm _leftArm, _rightArm;
        Transform[] _bones;
        Quaternion[] _basePose;
        Vector3 _previousPosition;
        float _armSpread, _phase, _nextGlance, _glanceStarted, _glanceSide;
        float _nextAttentionPoll, _attentionStarted = float.NegativeInfinity;
        float _yaw, _pitch, _idleWeight = 1f, _attentionWeight;
        bool _attention, _restorePose;

        internal static void Attach(MonoBehaviour owner, NpcSpeechState speech, float armClearance = 0f)
        {
            var animator = owner.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman || owner.TryGetComponent<NpcIdleMotion>(out _))
                return;

            var motion = owner.gameObject.AddComponent<NpcIdleMotion>();
            motion._animator = animator;
            motion._speech = speech;
            motion._armSpread = Mathf.Tan(armClearance * Mathf.Deg2Rad);
        }

        void Start()
        {
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
            if (_animator == null || !_animator.isHuman)
            {
                enabled = false;
                return;
            }

            if (_idleController != null)
            {
                _animator.runtimeAnimatorController = _idleController;
                _animator.applyRootMotion = false;
            }

            _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
            if (GetComponent<DeniConversationAnimation>() == null)
                _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (_armSpread > 0f)
            {
                _leftArm = new RestingArm(_animator, right: false);
                _rightArm = new RestingArm(_animator, right: true);
            }
            _bones = new[] { _spine, _head, _leftArm.Upper, _leftArm.Forearm, _leftArm.Hand,
                _rightArm.Upper, _rightArm.Forearm, _rightArm.Hand };
            _basePose = new Quaternion[_bones.Length];
            _previousPosition = transform.position;
            int seed = GetEntityId().GetHashCode();
            _phase = (seed & 255) * 0.074f;
            _glanceSide = (seed & 1) == 0 ? -1f : 1f;
            _nextGlance = Time.time + (_idleController != null ? Random.Range(5f, 9f) : 3.5f + Mathf.Repeat(_phase, 4f));
            _glanceStarted = float.NegativeInfinity;
        }

        void Update() => RestorePose();

        void LateUpdate()
        {
            if (_bones == null || _animator == null || !_animator.isActiveAndEnabled)
                return;

            float dt = Time.deltaTime;
            Vector3 position = transform.position;
            if (dt > 0f)
            {
                float speed = Vector3.Distance(position, _previousPosition) / dt;
                float idle = 1f - Mathf.InverseLerp(0.06f, 0.75f, speed);
                _idleWeight = Damp(_idleWeight, idle, 7f, dt);
            }
            _previousPosition = position;

            float now = Time.time;
            if (now >= _nextAttentionPoll)
                UpdateAttention(now);

            _attentionWeight = Damp(_attentionWeight, _attention ? 1f : 0f, 6f, dt);
            bool speaking = _speech != null && _speech.is_talking;
            if (now >= _nextGlance)
            {
                if (!_attention && !speaking && _idleWeight > 0.8f)
                {
                    if (_idleController != null)
                        _animator.CrossFadeInFixedTime(LookAround, 0.35f, 0);
                    else
                    {
                        _glanceStarted = now;
                        _glanceSide = -_glanceSide;
                    }
                }
                _nextGlance = now + (_idleController != null ? Random.Range(9f, 16f)
                    : 5.8f + Mathf.Repeat(now * 1.37f + _phase, 3.4f));
            }

            float breath = _idleController == null ? Mathf.Sin(now * 1.65f + _phase) : 0f;
            float sway = _idleController == null ? Mathf.Sin(now * 0.94f + _phase) : 0f;
            float acknowledge = Peak(now - _attentionStarted, 0.68f) * _idleWeight;
            float glance = Peak(now - _glanceStarted, 1.55f) * (1f - _attentionWeight);
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null)
                    _basePose[i] = _bones[i].localRotation;
            _restorePose = true;

            Rotate(_spine, transform.right, breath * 0.88f * _idleWeight + acknowledge * 1.1f);
            Rotate(_spine, transform.forward, sway * 0.65f * _idleWeight);
            Rotate(_spine, transform.up, _glanceSide * 1.6f * glance);
            _leftArm.Pose(transform, _armSpread, -0.018f + sway * 0.012f, _idleWeight);
            _rightArm.Pose(transform, _armSpread, 0.028f - sway * 0.009f, _idleWeight);

            if (_head != null)
                PoseHead(dt, acknowledge, glance, speaking, now);
        }

        void UpdateAttention(float now)
        {
            _nextAttentionPoll = now + AttentionPoll;
            if (_listener == null || !_listener.isActiveAndEnabled)
                _listener = Camera.main;

            Vector3 eye = _head != null ? _head.position : transform.position + Vector3.up * 1.45f;
            bool visible = CanSeePlayer(transform, _listener, eye);

            if (visible && !_attention)
            {
                if (now - _attentionStarted > 2.8f)
                    _attentionStarted = now;
                if (_idleController != null && (_animator.GetCurrentAnimatorStateInfo(0).fullPathHash == LookAround ||
                    (_animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).fullPathHash == LookAround)))
                    _animator.CrossFadeInFixedTime(StandingIdle, 0.3f, 0);
            }
            _attention = visible;
        }

        internal static bool CanSeePlayer(Transform actor, Camera listener, Vector3 eye)
        {
            if (listener == null || listener.gameObject.scene != actor.gameObject.scene)
                return false;

            Vector3 target = listener.transform.position;
            Vector3 direction = target - eye;
            if (direction.sqrMagnitude > AttentionRange * AttentionRange ||
                Vector3.Dot(actor.forward, direction.normalized) < -0.26f)
                return false;

            return !Physics.Linecast(target, eye, out var hit, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) || hit.transform.IsChildOf(actor);
        }

        void PoseHead(float dt, float acknowledge, float glance, bool speaking, float now)
        {
            Vector3 aim = _listener != null
                ? transform.InverseTransformDirection(_listener.transform.position - _head.position)
                : Vector3.forward;
            float yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg;
            _yaw = Damp(_yaw, Mathf.Clamp(yaw, -60f, 60f) * _attentionWeight + _glanceSide * 14f * glance, 7f, dt);
            _pitch = Damp(_pitch, Mathf.Clamp(pitch, -15f, 16f) * _attentionWeight, 8f, dt);
            float emphasis = speaking ? Mathf.Sin(now * 4.1f + _phase) * 1.5f : 0f;
            float weight = Mathf.Lerp(0.55f, 1f, _idleWeight);
            Rotate(_head, transform.up, _yaw * weight);
            Rotate(_head, transform.right, (_pitch + acknowledge * 4.6f + emphasis) * weight);
        }

        static float Peak(float time, float duration)
        {
            float progress = Mathf.InverseLerp(0f, duration, time);
            return Mathf.Sin(Mathf.SmoothStep(0f, 1f, progress) * Mathf.PI);
        }

        static float Damp(float from, float to, float rate, float dt) =>
            Mathf.Lerp(from, to, 1f - Mathf.Exp(-rate * dt));

        static void Rotate(Transform bone, Vector3 axis, float angle)
        {
            if (bone != null)
                bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
        }

        void RestorePose()
        {
            if (!_restorePose)
                return;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null)
                    _bones[i].localRotation = _basePose[i];
            _restorePose = false;
        }

        void OnDisable() => RestorePose();

        readonly struct RestingArm
        {
            internal readonly Transform Upper, Forearm, Hand;
            readonly float _side;
            readonly Quaternion? _palmToHand;

            internal RestingArm(Animator animator, bool right)
            {
                _side = right ? 1f : -1f;
                Upper = animator.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
                Forearm = animator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
                Hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                _palmToHand = null;

                var index = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
                var middle = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                var pinky = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
                if (Hand == null || index == null || middle == null || pinky == null)
                    return;

                Vector3 fingers = middle.position - Hand.position;
                Vector3 palm = Vector3.Cross(fingers, index.position - pinky.position) * _side;
                if (palm.sqrMagnitude > 0.000001f)
                    _palmToHand = Quaternion.Inverse(Quaternion.LookRotation(fingers, palm)) * Hand.rotation;
            }

            internal void Pose(Transform body, float spread, float stagger, float weight)
            {
                if (Upper == null || Forearm == null || Hand == null || weight <= 0f)
                    return;

                Vector3 down = -body.up;
                Vector3 outward = body.right * _side;
                Vector3 forward = body.forward;
                // Leave room at the elbows and give the forearms a slight forward bend.
                AimSegment(Upper, Forearm, down + outward * spread + forward * (-0.14f + stagger), weight);
                AimSegment(Forearm, Hand, down + outward * 0.075f + forward * (0.17f + stagger), weight);

                if (_palmToHand.HasValue)
                {
                    Quaternion palm = Quaternion.LookRotation(down + forward * (0.12f + stagger),
                        -outward + forward * 0.16f);
                    Hand.rotation = Quaternion.Slerp(Hand.rotation, palm * _palmToHand.Value, weight);
                }
            }

            static void AimSegment(Transform bone, Transform child, Vector3 direction, float weight)
            {
                Quaternion target = Quaternion.FromToRotation(child.position - bone.position, direction) * bone.rotation;
                bone.rotation = Quaternion.Slerp(bone.rotation, target, weight);
            }
        }
    }
}
