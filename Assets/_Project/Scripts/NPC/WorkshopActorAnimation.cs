using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class WorkshopActorAnimation : MonoBehaviour
    {
        [SerializeField] Animator _animator;
        [SerializeField] Transform _crowbar;
        [SerializeField, Min(0.1f)] float _raiseDuration = 1.05f;
        Transform _arm, _elbow, _hand;
        Transform _threatTarget;
        Transform _indexBase, _indexMiddle, _indexTip;
        Transform _leftThigh, _leftKnee, _leftFoot, _rightThigh, _rightKnee, _rightFoot;
        Transform[] _bones;
        Quaternion[] _pose;
        bool _posed, _threatening, _surrendered, _retreatStarted;
        float _threat, _surrender, _retreatTime;
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

        internal void Threaten(Transform target)
        {
            _threatTarget = target;
            _threatening = true;
        }

        internal void Surrender()
        {
            _threatening = false;
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

        internal void CancelThreat() => _threatening = false;

        void Update() => Restore();

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

            // Blend out of the current lift, even when the pistol interrupts it halfway.
            if (!_surrendered)
                _threat = Mathf.MoveTowards(_threat, _threatening ? 1f : 0f, dt / Mathf.Max(0.1f, _raiseDuration));
            _surrender = Mathf.MoveTowards(_surrender, _surrendered ? 1f : 0f, dt / 0.85f);
            float lift = Mathf.SmoothStep(0f, 1f, _threat);
            float turn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.09f, 1f, _threat));
            float hide = Mathf.SmoothStep(0f, 1f, _surrender);

            // Keep the upper arm against the torso. Only the forearm comes forward.
            float upperLength = Vector3.Distance(_arm.position, _elbow.position);
            float forearmLength = Vector3.Distance(_elbow.position, _hand.position);
            Vector3 readyWrist = _arm.position - transform.up * (upperLength + 0.38f * forearmLength) +
                transform.forward * (0.92f * forearmLength) + transform.right * 0.018f;
            Vector3 towardPlayer = _threatTarget != null
                ? transform.InverseTransformDirection(_threatTarget.position - transform.up * 0.23f - readyWrist)
                : Vector3.forward;
            Vector3 pointed = Vector3.RotateTowards(Vector3.forward, towardPlayer.normalized,
                30f * Mathf.Deg2Rad, 0f);
            float presentation = turn * (1f - hide);
            Vector3 shaft = Vector3.Slerp(new Vector3(0.12f, -0.54f, 0.83f).normalized, pointed, presentation);
            shaft = Vector3.Slerp(shaft, new Vector3(0.07f, -0.95f, -0.30f).normalized, hide);
            Vector3 face = Vector3.Slerp(Vector3.left, Vector3.up, presentation);
            Quaternion barRotation = transform.rotation *
                Quaternion.LookRotation(Vector3.ProjectOnPlane(face, shaft), shaft);
            Quaternion wristRotation = barRotation * Quaternion.Inverse(_crowbar.localRotation);
            Vector3 palmOffset = Quaternion.Inverse(_hand.rotation) * (_crowbar.position - _hand.position);

            Vector3 lowWrist = transform.TransformPoint(new Vector3(0.30f, -0.17f, 0.09f)) -
                wristRotation * palmOffset;
            Vector3 hiddenWrist = transform.TransformPoint(new Vector3(0.18f, -0.07f, -0.26f)) -
                wristRotation * palmOffset;
            Vector3 wrist = Vector3.Lerp(Vector3.Lerp(lowWrist, readyWrist, lift), hiddenWrist, hide);
            wrist += transform.right * (0.48f * hide * (1f - hide));
            Vector3 pole = Vector3.Lerp(transform.right * 0.72f - transform.forward,
                -transform.up + transform.right * 0.08f, lift * (1f - hide));
            HumanoidLimbPose.Reach(_arm, _elbow, _hand, wrist, pole);
            _hand.rotation = wristRotation;

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

        void OnDisable() => Restore();
    }
}
