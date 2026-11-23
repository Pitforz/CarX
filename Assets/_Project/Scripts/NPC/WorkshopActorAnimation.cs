using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class WorkshopActorAnimation : MonoBehaviour
    {
        [SerializeField] Animator _animator;
        [SerializeField] Transform _crowbar;
        [SerializeField] AnimationClip _strikeClip;
        internal const float StrikeContactTime = 0.43f;
        const float StrikeDuration = 0.81f;
        const float ClipContact = 0.39f;
        Transform _arm, _elbow, _hand;
        Transform _threatTarget;
        Transform _indexBase, _indexMiddle, _indexTip;
        Transform _leftThigh, _leftKnee, _leftFoot, _rightThigh, _rightKnee, _rightFoot;
        Transform[] _bones;
        Quaternion[] _pose;
        bool _posed, _threatening, _surrendered, _retreatStarted;
        float _surrender, _retreatTime;
        float _strikeStarted = float.NegativeInfinity;
        float _strikeWeight;
        PlayableGraph _strikeGraph;
        AnimationLayerMixerPlayable _strikeMixer;
        AnimationClipPlayable _strikeMotion;
        AvatarMask _strikeMask;
        Vector3 _retreatFrom, _retreatTo;
        Vector3 _leftFootStart, _rightFootStart;

        void Awake()
        {
            _arm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _elbow = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _hand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _indexBase = _animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            _indexMiddle = _animator.GetBoneTransform(HumanBodyBones.RightIndexIntermediate);
            _indexTip = _animator.GetBoneTransform(HumanBodyBones.RightIndexDistal);
            _leftThigh = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _leftKnee = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rightThigh = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _rightKnee = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            _bones = new[] { _arm, _elbow, _hand, _indexBase, _indexMiddle, _indexTip,
                _leftThigh, _leftKnee, _leftFoot, _rightThigh, _rightKnee, _rightFoot };
            _pose = new Quaternion[_bones.Length];
        }

        void OnEnable()
        {
            if (_strikeClip == null || !_strikeClip.humanMotion || _animator == null ||
                _animator.runtimeAnimatorController == null)
                return;

            _strikeMask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                _strikeMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            _strikeMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);

            _strikeGraph = PlayableGraph.Create("Workshop Actor Strike");
            var idle = AnimatorControllerPlayable.Create(_strikeGraph, _animator.runtimeAnimatorController);
            _strikeMotion = AnimationClipPlayable.Create(_strikeGraph, _strikeClip);
            _strikeMotion.SetApplyFootIK(false);
            _strikeMotion.SetSpeed(0);
            _strikeMixer = AnimationLayerMixerPlayable.Create(_strikeGraph, 2);
            _strikeGraph.Connect(idle, 0, _strikeMixer, 0);
            _strikeGraph.Connect(_strikeMotion, 0, _strikeMixer, 1);
            _strikeMixer.SetInputWeight(0, 1f);
            _strikeMixer.SetInputWeight(1, 0f);
            _strikeMixer.SetLayerMaskFromAvatarMask(1, _strikeMask);
            var output = AnimationPlayableOutput.Create(_strikeGraph, "Workshop Actor", _animator);
            output.SetSourcePlayable(_strikeMixer);
            _strikeGraph.Play();
        }

        internal void Strike() => _strikeStarted = Time.time;

        internal void Threaten(Transform target)
        {
            _threatTarget = target;
            _threatening = true;
        }

        internal void Surrender()
        {
            CancelThreat();
            _surrendered = true;
            if (_retreatStarted)
                return;
            _retreatStarted = true;
            _retreatFrom = _retreatTo = transform.position;
            _leftFootStart = _leftFoot.position;
            _rightFootStart = _rightFoot.position;
            var box = GetComponent<BoxCollider>();
            Vector3 direction = -transform.forward;
            float distance = 0.42f;
            var hits = Physics.BoxCastAll(transform.TransformPoint(box.center), box.size * 0.45f,
                direction, transform.rotation, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
                if (!hit.transform.IsChildOf(transform) && hit.normal.y < 0.65f)
                    distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.05f));
            Vector3 destination = transform.position + direction * distance;
            float bottom = box.center.y - box.size.y * 0.5f;
            var floors = Physics.RaycastAll(destination + Vector3.up * 0.2f, Vector3.down, 1.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float floor = float.NegativeInfinity;
            foreach (var hit in floors)
                if (!hit.transform.IsChildOf(transform) && hit.normal.y > 0.8f)
                    floor = Mathf.Max(floor, hit.point.y);
            if (float.IsNegativeInfinity(floor))
                return;
            _retreatTo = new Vector3(destination.x, floor - bottom, destination.z);
        }

        internal void CancelThreat()
        {
            _threatening = false;
            _strikeStarted = float.NegativeInfinity;
        }

        void Update()
        {
            Restore();
            float elapsed = Time.time - _strikeStarted;
            float fade = Mathf.Clamp01(Mathf.Min(elapsed / 0.14f, (StrikeDuration - elapsed) / 0.18f));
            _strikeWeight = Mathf.SmoothStep(0f, 1f, fade);
            if (!_strikeGraph.IsValid())
                return;

            if (_strikeWeight > 0f)
            {
                float progress = elapsed <= StrikeContactTime
                    ? Mathf.Lerp(0f, ClipContact, elapsed / StrikeContactTime)
                    : Mathf.Lerp(ClipContact, 1f, (elapsed - StrikeContactTime) / (StrikeDuration - StrikeContactTime));
                _strikeMotion.SetTime(progress * _strikeClip.length);
            }
            // The arm blends back to the low, procedural grip in LateUpdate.
            _strikeMixer.SetInputWeight(1, _strikeWeight > 0f ? 1f : 0f);
        }

        void LateUpdate()
        {
            if (_arm == null || _elbow == null || _hand == null)
                return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null)
                    _pose[i] = _bones[i].localRotation;
            _posed = true;
            if (_retreatStarted && _retreatTime < 0.85f)
            {
                _retreatTime += dt;
                float progress = Mathf.Clamp01(_retreatTime / 0.85f);
                transform.position = Vector3.Lerp(_retreatFrom, _retreatTo, Mathf.SmoothStep(0f, 1f, progress));
                Vector3 offset = _retreatTo - _retreatFrom;
                Step(_rightThigh, _rightKnee, _rightFoot, _rightFootStart, offset,
                    Mathf.InverseLerp(0f, 0.56f, progress));
                Step(_leftThigh, _leftKnee, _leftFoot, _leftFootStart, offset,
                    Mathf.InverseLerp(0.44f, 1f, progress));
            }
            if (_crowbar != null)
                PoseCrowbar(dt);
        }

        void PoseCrowbar(float dt)
        {
            if (_threatening && _threatTarget != null)
            {
                Vector3 facing = Vector3.ProjectOnPlane(_threatTarget.position - transform.position, Vector3.up);
                if (facing.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), 125f * dt);
            }

            _surrender = Mathf.MoveTowards(_surrender, _surrendered ? 1f : 0f, dt / 0.85f);
            float hide = Mathf.SmoothStep(0f, 1f, _surrender);
            Vector3 shaft = Vector3.Slerp(new Vector3(0.12f, -0.54f, 0.83f).normalized,
                new Vector3(0.07f, -0.95f, -0.30f).normalized, hide);
            Quaternion barRotation = transform.rotation *
                Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.left, shaft), shaft);
            Quaternion wristRotation = barRotation * Quaternion.Inverse(_crowbar.localRotation);
            Vector3 palmOffset = Quaternion.Inverse(_hand.rotation) * (_crowbar.position - _hand.position);

            Vector3 low = new Vector3(0.30f, -0.17f, 0.09f);
            Vector3 hidden = new Vector3(0.18f, -0.07f, -0.26f);
            Vector3 wrist = transform.TransformPoint(Vector3.Lerp(low, hidden, hide)) - wristRotation * palmOffset;
            wrist += transform.right * (0.48f * hide * (1f - hide));
            float clipWeight = _strikeGraph.IsValid() ? _strikeWeight : 0f;
            if (clipWeight < 1f)
            {
                if (!_strikeGraph.IsValid() && _strikeWeight > 0f)
                    wrist += (transform.forward * 0.5f + transform.up * 0.25f) * _strikeWeight;

                Vector3 pole = transform.right * 0.72f - transform.forward;
                HumanoidLimbPose.Reach(_arm, _elbow, _hand, Vector3.Lerp(wrist, _hand.position, clipWeight), pole);
                _hand.rotation = Quaternion.Slerp(wristRotation, _hand.rotation, clipWeight);
            }

            // Danny's rig has one articulated finger chain for the right-hand grip.
            if (_indexBase != null)
                _indexBase.localRotation = Quaternion.AngleAxis(52f, Vector3.right);
            if (_indexMiddle != null)
                _indexMiddle.localRotation = Quaternion.AngleAxis(71f, Vector3.right);
            if (_indexTip != null)
                _indexTip.localRotation = Quaternion.AngleAxis(57f, Vector3.right);
        }

        void Step(Transform thigh, Transform knee, Transform foot, Vector3 start, Vector3 offset, float progress)
        {
            if (thigh == null || knee == null || foot == null)
                return;
            Quaternion rotation = foot.rotation;
            float lift = Mathf.Min(0.065f, offset.magnitude * 0.2f) * Mathf.Sin(progress * Mathf.PI);
            Vector3 target = start + offset * Mathf.SmoothStep(0f, 1f, progress) + transform.up * lift;
            HumanoidLimbPose.Reach(thigh, knee, foot, target, transform.forward);
            foot.rotation = rotation;
        }

        void Restore()
        {
            if (!_posed)
                return;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null)
                    _bones[i].localRotation = _pose[i];
            _posed = false;
        }

        void OnDisable()
        {
            Restore();
            if (_strikeGraph.IsValid())
                _strikeGraph.Destroy();
            if (_strikeMask != null)
                Destroy(_strikeMask);
        }
    }
}
