using System.Collections.Generic;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(180)]
    public sealed class CombatHumanoidAnimation : MonoBehaviour
    {
        static readonly int MoveSpeed = Animator.StringToHash("MoveSpeed");
        static readonly int Armed = Animator.StringToHash("Armed");
        static readonly int ArmedMoveX = Animator.StringToHash("ArmedMoveX");
        static readonly int ArmedMoveY = Animator.StringToHash("ArmedMoveY");
        static readonly Vector3 HandGrip = new(-0.028f, 0.012f, 0.065f);
        static readonly Vector3 PresentationGrip = new(-0.028f, -0.034f, -0.077f);
        internal const float DefaultAimRaiseTime = 0.24f;
        const float FallDuration = 0.68f;

        Animator _animator;
        Transform _actor, _head, _hips, _spine, _chest, _leftThigh, _leftShin, _leftFoot, _rightThigh, _rightShin, _rightFoot;
        SphereCollider _headHitbox;
        Arm _rightArm, _leftArm;
        Transform _pistol, _slide;
        Transform[] _poseBones;
        Quaternion[] _animatedPose;
        Vector3 _restPosition, _previousFeet, _pistolReleasePoint, _headOffset;
        Quaternion _pistolReleaseRotation;
        Quaternion _restRotation;
        Vector3 _fallAxis;
        Quaternion _headLookFrame;
        Quaternion _weaponAim = Quaternion.identity;
        Vector3? _aimTarget;
        Pose? _stagedGrip;
        Vector3 _lookTarget;
        Vector3 _stagedGripOffset;
        Vector2 _armedVelocity;
        float _speed, _aimWeight, _relaxedWeaponWeight, _fallTime;
        float _aimRaiseTime = DefaultAimRaiseTime;
        float _gripWeight, _gripClosure, _presentationWeight, _coverLean, _lookWeight;
        float _idlePhase, _idleWeight;
        float _lastShot = float.NegativeInfinity;
        float _lastHit = float.NegativeInfinity;
        bool _poseApplied, _falling, _settled, _relaxWeapon, _keepIdleStance, _backpedaling, _looking;

        internal Transform Muzzle { get; private set; }
        internal Vector3 HeadPosition => _head.TransformPoint(_headOffset);
        internal Pose HeldPistolPose => new(_pistol.position, _pistol.rotation);
        internal Pose LoweredPistolPose
        {
            get
            {
                if (!_keepIdleStance)
                    return new Pose(_rightArm.Upper.position - transform.up * 0.52f +
                        transform.right * 0.08f + transform.forward * 0.10f,
                        transform.rotation * Quaternion.Euler(72f, 0f, -12f));

                Quaternion grip = _rightArm.Hand.rotation * _rightArm.HandFrame;
                Vector3 forward = grip * Vector3.forward;
                Vector3 lowered = Vector3.RotateTowards(forward, -transform.up, 38f * Mathf.Deg2Rad, 0f);
                grip = Quaternion.FromToRotation(forward, lowered) * grip;
                return new Pose(_rightArm.Hand.position + grip * HandGrip, grip);
            }
        }

        internal bool Bind(Transform actor, SphereCollider headHitbox, GameObject pistolPrefab,
            RuntimeAnimatorController locomotion)
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null || _animator.avatar == null || !_animator.avatar.isValid || !_animator.isHuman)
                return false;

            _rightArm = new Arm(_animator, right: true);
            _leftArm = new Arm(_animator, right: false);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = _animator.GetBoneTransform(HumanBodyBones.UpperChest) ??
                _animator.GetBoneTransform(HumanBodyBones.Chest) ?? _hips;
            _leftThigh = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _leftShin = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rightThigh = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _rightShin = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (!_rightArm.IsValid || !_leftArm.IsValid || _head == null || _hips == null ||
                _leftThigh == null || _leftShin == null || _leftFoot == null ||
                _rightThigh == null || _rightShin == null || _rightFoot == null)
                return false;

            _actor = actor;
            _headHitbox = headHitbox;
            _headOffset = _head.InverseTransformPoint(_head.position + actor.up * 0.11f + actor.forward * 0.025f);
            _headLookFrame = Quaternion.Inverse(_head.rotation) * actor.rotation;
            _restPosition = transform.localPosition;
            _restRotation = transform.localRotation;
            _previousFeet = actor.position;
            _idlePhase = (GetEntityId().GetHashCode() & 255) * 0.074f;
            var poseBones = new List<Transform> { _head, _chest, _leftThigh, _leftShin, _rightThigh, _rightShin };
            if (_spine != null && _spine != _chest)
                poseBones.Add(_spine);
            _rightArm.CollectBones(poseBones);
            _leftArm.CollectBones(poseBones);
            _poseBones = poseBones.ToArray();
            _animatedPose = new Quaternion[_poseBones.Length];

            _animator.runtimeAnimatorController = locomotion;
            _animator.applyRootMotion = false;
            _animator.fireEvents = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _pistol = Instantiate(pistolPrefab, _rightArm.Hand, false).transform;
            AttachPistolToHand();
            _slide = _pistol.Find("Slide");
            Muzzle = _pistol.Find("Muzzle");
            if (Muzzle == null)
                return false;
            SetWeaponVisible(false);
            SyncHead();
            return true;
        }

        internal void SetWeaponVisible(bool visible)
        {
            _pistol.gameObject.SetActive(visible);
            UpdateArmedAnimation();
        }

        void UpdateArmedAnimation() =>
            _animator.SetBool(Armed, _pistol.gameObject.activeSelf && !_keepIdleStance && !_stagedGrip.HasValue);

        internal void SetCoverLean(float lean) => _coverLean = lean;

        internal void KeepIdleStanceWithPistol()
        {
            _keepIdleStance = true;
            UpdateArmedAnimation();
        }

        internal void LookAt(Vector3? point)
        {
            _looking = point.HasValue;
            if (point is Vector3 target)
                _lookTarget = target;
        }

        internal void SetBackpedaling(bool active) => _backpedaling = active;

        internal void StageGrip(Pose pose, float weight, float closure, float presentation = 0f)
        {
            if (!_stagedGrip.HasValue)
                _animator.SetBool(Armed, false);

            _stagedGrip = pose;
            _gripWeight = weight;
            _gripClosure = closure;
            _presentationWeight = presentation;
            _stagedGripOffset = Vector3.Lerp(HandGrip, PresentationGrip, presentation);
        }

        internal void ClearStagedGrip(bool holdAtSide = false)
        {
            if (!_stagedGrip.HasValue)
                return;

            _stagedGrip = null;
            AttachPistolToHand();
            if (holdAtSide)
            {
                TrackAim(null, relaxed: true);
                _aimWeight = 0f;
                _relaxedWeaponWeight = 1f;
            }
            UpdateArmedAnimation();
        }

        internal void PassPistolTo(CombatHumanoidAnimation receiver)
        {
            (_pistol, receiver._pistol) = (receiver._pistol, _pistol);
            (_slide, receiver._slide) = (receiver._slide, _slide);
            (Muzzle, receiver.Muzzle) = (receiver.Muzzle, Muzzle);
            receiver._pistol.SetParent(receiver._rightArm.Hand, true);
            receiver.SetWeaponVisible(true);
            AttachPistolToHand();
            SetWeaponVisible(false);
        }

        void AttachPistolToHand()
        {
            _pistol.SetParent(_rightArm.Hand, false);
            _pistol.SetLocalPositionAndRotation(_rightArm.HandFrame * HandGrip, _rightArm.HandFrame);
        }

        internal void TrackAim(Vector3? point, bool relaxed = false, float raiseDuration = DefaultAimRaiseTime)
        {
            _aimTarget = point;
            _relaxWeapon = relaxed;
            _aimRaiseTime = raiseDuration;
        }

        internal void SettleAimPose() => _aimWeight = _aimTarget.HasValue ? 1f : 0f;

        internal void Recoil() => _lastShot = Time.time;

        internal void Flinch() => _lastHit = Time.time;

        void Update() => RestorePose();

        void LateUpdate()
        {
            if (_actor == null || _settled)
                return;

            if (_falling)
            {
                PoseFall();
                return;
            }

            float dt = Time.deltaTime;
            Vector3 delta = Vector3.ProjectOnPlane(_actor.position - _previousFeet, _actor.up);
            _previousFeet = _actor.position;
            if (dt > 0f)
            {
                Vector3 velocity = delta.sqrMagnitude < 1.5f
                    ? _actor.InverseTransformDirection(delta) / dt : Vector3.zero;
                float response = 1f - Mathf.Exp(-13f * dt);
                float speed = Mathf.Min(velocity.magnitude, 5.4f);
                _speed = Mathf.Lerp(_speed, _backpedaling ? -speed : speed, response);
                var localMotion = Vector2.ClampMagnitude(new Vector2(velocity.x, velocity.z), 5.4f);
                _armedVelocity = Vector2.Lerp(_armedVelocity, localMotion, response);
                _animator.SetFloat(MoveSpeed, _speed);
                _animator.SetFloat(ArmedMoveX, _armedVelocity.x);
                _animator.SetFloat(ArmedMoveY, _armedVelocity.y);
            }

            float impact = MotionPulse(Time.time - _lastHit, 0.045f, 0.30f);
            _lookWeight = Mathf.MoveTowards(_lookWeight, _looking ? 1f : 0f, dt / 0.22f);
            bool fitForIdle = !_stagedGrip.HasValue && !_aimTarget.HasValue && _coverLean == 0f;
            float idle = fitForIdle ? 1f - Mathf.InverseLerp(0.08f, 0.85f, Mathf.Abs(_speed)) : 0f;
            _idleWeight = Mathf.MoveTowards(_idleWeight, idle, dt * 4f);
            if (_pistol.gameObject.activeSelf || _stagedGrip.HasValue || impact > 0f ||
                _coverLean != 0f || _lookWeight > 0f || _idleWeight > 0f)
            {
                SavePose();
                if (_idleWeight > 0f)
                    PoseIdle();
                PoseCoverLean();
                Rotate(_chest, 13f * impact);
                Rotate(_head, -9f * impact);
                if (_stagedGrip is Pose grip)
                    PoseStagedGrip(grip);
                else if (_pistol.gameObject.activeSelf)
                    PoseWeapon(dt);
                if (_lookWeight > 0f)
                    PoseLook();
            }
            SyncHead();
        }

        void PoseIdle()
        {
            float time = Time.time;
            float breath = Mathf.Sin(time * 1.66f + _idlePhase) * 0.72f * _idleWeight;
            Rotate(_chest, breath);
            if (_lookWeight <= 0f)
            {
                float glance = Mathf.Sin(time * 0.72f + _idlePhase);
                float yaw = glance * Mathf.Abs(glance) * 3.8f * _idleWeight;
                _head.rotation = Quaternion.AngleAxis(yaw, transform.up) * _head.rotation;
            }
        }

        void PoseCoverLean()
        {
            if (_coverLean == 0f)
                return;

            Vector3 forward = transform.forward;
            if (_spine != null && _spine != _chest)
                _spine.rotation = Quaternion.AngleAxis(-6f * _coverLean, forward) * _spine.rotation;
            _chest.rotation = Quaternion.AngleAxis(-5f * _coverLean, forward) * _chest.rotation;
            _head.rotation = Quaternion.AngleAxis(7f * _coverLean, forward) * _head.rotation;
        }

        void PoseStagedGrip(Pose grip)
        {
            Vector3 handPosition = grip.position - grip.rotation * _stagedGripOffset;
            Quaternion localGrip = _rightArm.HandFrame;
            if (_presentationWeight > 0f)
            {
                Vector3 reach = handPosition - _rightArm.Upper.position;
                if (reach.sqrMagnitude > 0.0001f)
                {
                    Quaternion relaxedHand = Quaternion.LookRotation(reach, transform.forward);
                    Quaternion presentGrip = Quaternion.Inverse(relaxedHand) * grip.rotation;
                    localGrip *= Quaternion.Slerp(Quaternion.identity, presentGrip, _presentationWeight);
                }
            }
            if (_keepIdleStance && _pistol.gameObject.activeSelf)
                _rightArm.Hand.rotation = LoweredPistolPose.rotation * Quaternion.Inverse(localGrip);

            Quaternion wrist = grip.rotation * Quaternion.Inverse(localGrip);
            _rightArm.Place(handPosition, wrist, transform.right * 0.72f - transform.up, _gripWeight);
            _rightArm.CloseHand(_gripClosure, triggerHand: false);
            _pistol.SetLocalPositionAndRotation(localGrip * _stagedGripOffset, localGrip);
        }

        void PoseLook()
        {
            Vector3 direction = _lookTarget - HeadPosition;
            if (direction.sqrMagnitude < 0.01f)
                return;

            Quaternion animated = _head.rotation * _headLookFrame;
            Quaternion target = Quaternion.LookRotation(direction, transform.up);
            _head.rotation = Quaternion.RotateTowards(animated, target, 16f * _lookWeight) *
                Quaternion.Inverse(_headLookFrame);
        }

        void SyncHead()
        {
            if (_headHitbox != null && _headHitbox.enabled)
                _headHitbox.transform.position = HeadPosition;
        }

        void PoseWeapon(float dt)
        {
            float weight = Mathf.SmoothStep(0f, 1f, _aimWeight = Mathf.MoveTowards(_aimWeight,
                _aimTarget.HasValue ? 1f : 0f, dt / (_aimTarget.HasValue ? _aimRaiseTime : 0.22f)));
            _relaxedWeaponWeight = Mathf.MoveTowards(_relaxedWeaponWeight, _relaxWeapon ? 1f : 0f, dt / 0.25f);
            float relaxed = (1f - weight) * Mathf.SmoothStep(0f, 1f, _relaxedWeaponWeight);
            float armWeight = _keepIdleStance ? weight : weight + relaxed;
            float lower = !_keepIdleStance && armWeight > 0f ? relaxed / armWeight : 0f;
            float recoil = MotionPulse(Time.time - _lastShot, 0.018f, 0.20f);
            Vector3 up = transform.up;
            Vector3 side = transform.right;
            Vector3 forward = transform.forward;
            Vector3 shoulders = (_rightArm.Upper.position + _leftArm.Upper.position) * 0.5f;
            Vector3 grip = shoulders + forward * 0.49f + side * 0.012f + up * 0.02f;
            Vector3 direction = (_aimTarget ?? (grip + forward)) - grip;
            if (direction.sqrMagnitude < 0.0001f)
                direction = forward;
            Quaternion targetAim = Quaternion.RotateTowards(Quaternion.identity,
                Quaternion.LookRotation(transform.InverseTransformDirection(direction), Vector3.up), 48f);
            _weaponAim = weight < 0.005f ? targetAim :
                Quaternion.Slerp(_weaponAim, targetAim, 1f - Mathf.Exp(-25f * dt));
            Quaternion aim = transform.rotation * _weaponAim;
            aim *= Quaternion.Euler(-7.5f * recoil, 0f, 0f);
            grip -= forward * (0.048f * recoil);
            Pose lowered = LoweredPistolPose;
            if (_keepIdleStance)
            {
                _rightArm.Hand.rotation = lowered.rotation * Quaternion.Inverse(_pistol.localRotation);
            }
            else
            {
                grip = Vector3.Lerp(grip, lowered.position, lower);
                aim = Quaternion.Slerp(aim, lowered.rotation, lower);
            }
            Quaternion rightWrist = aim * Quaternion.Inverse(_pistol.localRotation);
            _rightArm.Place(grip - rightWrist * _pistol.localPosition, rightWrist, side * 0.72f - up, armWeight);
            Quaternion support = _pistol.rotation * Quaternion.Euler(0f, 16f, -12f) *
                Quaternion.Inverse(_leftArm.HandFrame);
            Vector3 supportGrip = _pistol.TransformPoint(new Vector3(-0.063f, -0.026f, -0.060f));
            Vector3 relaxedHand = _leftArm.Upper.position - up * 0.54f - side * 0.05f + forward * 0.065f;
            Quaternion relaxedWrist = transform.rotation * Quaternion.Euler(70f, 0f, 75f) *
                Quaternion.Inverse(_leftArm.HandFrame);
            float supportWeight = _aimTarget.HasValue && _aimRaiseTime > DefaultAimRaiseTime
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 1f, _aimWeight))
                : armWeight;
            _leftArm.Place(Vector3.Lerp(supportGrip, relaxedHand, lower),
                Quaternion.Slerp(support, relaxedWrist, lower), -side * 0.8f - up, supportWeight);
            _rightArm.CloseHand(1f, triggerHand: true);
            _leftArm.CloseHand(weight, triggerHand: false);
            _head.rotation = Quaternion.AngleAxis((1.5f * recoil + 3f) * weight, side) * _head.rotation;
            if (_slide != null)
                _slide.localPosition = Vector3.back * (0.031f * recoil * recoil);
        }

        static float MotionPulse(float elapsed, float peak, float duration) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, peak, elapsed)) *
            Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(peak, duration, elapsed));

        internal void Fall(bool immediately, Vector3? shotOrigin = null)
        {
            if (_falling || _settled)
                return;

            if (_pistol.gameObject.activeSelf)
            {
                if (!_poseApplied)
                    PoseWeapon(0f);
                _pistol.SetParent(_actor, true);
                _pistolReleasePoint = _pistol.localPosition;
                _pistolReleaseRotation = _pistol.localRotation;
            }
            SavePose();
            _animator.enabled = false;
            Vector3 fallDirection = immediately
                ? Vector3.forward
                : shotOrigin.HasValue
                    ? _actor.InverseTransformDirection(_actor.position - shotOrigin.Value)
                    : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.back;
            fallDirection.y = 0f;
            if (fallDirection.sqrMagnitude < 0.001f)
                fallDirection = Vector3.back;
            _fallAxis = Vector3.Cross(Vector3.up, fallDirection.normalized);
            _fallTime = immediately ? FallDuration : 0f;
            _falling = true;
            PoseFall();
        }

        void PoseFall()
        {
            _fallTime = Mathf.Min(FallDuration, _fallTime + Time.deltaTime);
            RestorePose();
            SavePose();
            float t = _fallTime / FallDuration;
            float roll = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.055f, 0.96f, t));
            float knees = MotionPulse(t, 0.42f, 0.98f) * 0.72f + roll * 0.28f;
            float shoulderDrop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.85f, t));
            transform.SetLocalPositionAndRotation(_restPosition - Vector3.up * (0.17f * knees),
                Quaternion.AngleAxis(88f * roll, _fallAxis) * _restRotation);

            Rotate(_leftThigh, -26f * knees);
            Rotate(_rightThigh, -42f * knees);
            Rotate(_leftShin, 45f * knees);
            Rotate(_rightShin, 59f * knees);
            Rotate(_chest, 17f * shoulderDrop);
            Rotate(_head, -13f * roll + 5f * knees);
            _rightArm.Place(transform.TransformPoint(new Vector3(0.42f, 0.83f, 0.13f)),
                _rightArm.Hand.rotation, transform.right, roll);
            _leftArm.Place(transform.TransformPoint(new Vector3(-0.36f, 0.93f, 0.19f)),
                _leftArm.Hand.rotation, -transform.right, roll);

            float bottom = Mathf.Min(HeadPosition.y - 0.115f, _hips.position.y - 0.15f, _chest.position.y - 0.17f,
                _leftFoot.position.y - 0.055f, _rightFoot.position.y - 0.055f,
                _rightArm.Hand.position.y - 0.04f, _leftArm.Hand.position.y - 0.04f);
            transform.position += Vector3.up * Mathf.Max(0f, _actor.position.y - bottom) * roll;
            if (_pistol.gameObject.activeSelf)
            {
                float drop = Mathf.InverseLerp(0.08f, 0.76f, t);
                _pistol.SetLocalPositionAndRotation(Vector3.Lerp(_pistolReleasePoint, new Vector3(0.39f, 0.044f, 0.29f), drop * drop),
                    Quaternion.Slerp(_pistolReleaseRotation, Quaternion.Euler(0f, 112f, 90f), drop));
                if (_slide != null)
                    _slide.localPosition = Vector3.zero;
            }
            if (_fallTime >= FallDuration)
            {
                _poseApplied = false;
                _settled = true;
            }
        }

        void Rotate(Transform bone, float angle) =>
            bone.rotation = Quaternion.AngleAxis(angle, transform.right) * bone.rotation;

        void SavePose()
        {
            for (int i = 0; i < _poseBones.Length; i++)
                _animatedPose[i] = _poseBones[i].localRotation;
            _poseApplied = true;
        }

        void RestorePose()
        {
            if (!_poseApplied)
                return;
            for (int i = 0; i < _poseBones.Length; i++)
                _poseBones[i].localRotation = _animatedPose[i];
            _poseApplied = false;
        }

        sealed class Arm
        {
            internal Transform Upper { get; }
            internal Transform Hand { get; }
            internal Quaternion HandFrame { get; }
            readonly Transform _forearm;
            readonly List<Finger> _fingers = new();
            internal bool IsValid => Upper != null && _forearm != null && Hand != null;

            internal Arm(Animator animator, bool right)
            {
                Upper = animator.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
                _forearm = animator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
                Hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                if (!IsValid)
                    return;

                var index = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
                var middle = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                var pinky = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
                Vector3 forward = middle != null ? middle.position - Hand.position : Hand.position - _forearm.position;
                Vector3 up = index != null && pinky != null ? index.position - pinky.position : animator.transform.up;
                Quaternion frame = Quaternion.LookRotation(forward, up);
                HandFrame = Quaternion.Inverse(Hand.rotation) * frame;
                Vector3 inward = frame * (right ? Vector3.left : Vector3.right);
                int firstFinger = (int)(right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
                for (int i = 0; i < 15; i++)
                {
                    var bone = animator.GetBoneTransform((HumanBodyBones)(firstFinger + i));
                    if (bone != null && bone.childCount > 0)
                        _fingers.Add(new Finger(bone, inward, i / 3, i % 3));
                }
            }

            internal void CollectBones(List<Transform> bones)
            {
                bones.Add(Upper);
                bones.Add(_forearm);
                bones.Add(Hand);
                foreach (Finger finger in _fingers)
                    bones.Add(finger.Bone);
            }

            internal void Place(Vector3 position, Quaternion rotation, Vector3 pole, float weight)
            {
                Quaternion start = Hand.rotation;
                HumanoidLimbPose.Reach(Upper, _forearm, Hand, position, pole, weight);
                Hand.rotation = Quaternion.Slerp(start, rotation, weight);
            }

            internal void CloseHand(float weight, bool triggerHand)
            {
                foreach (Finger finger in _fingers)
                    finger.Curl(weight, triggerHand);
            }
        }

        readonly struct Finger
        {
            internal Transform Bone { get; }
            readonly Quaternion _rest;
            readonly Vector3 _axis;
            readonly int _digit, _joint;

            internal Finger(Transform bone, Vector3 inward, int digit, int joint)
            {
                Bone = bone;
                _rest = bone.localRotation;
                _axis = bone.InverseTransformDirection(Vector3.Cross(bone.GetChild(0).position - bone.position, inward).normalized);
                _digit = digit;
                _joint = joint;
            }

            internal void Curl(float weight, bool triggerHand)
            {
                float angle = _digit == 0 ? 16f : triggerHand && _digit == 1 ? 28f : _joint == 0 ? 49f : 67f;
                Bone.localRotation = Quaternion.Slerp(Bone.localRotation, _rest * Quaternion.AngleAxis(angle, _axis), weight);
            }
        }
    }
}
