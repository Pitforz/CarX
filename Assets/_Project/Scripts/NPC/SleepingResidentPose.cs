using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    public sealed class SleepingResidentPose : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] float _hipHeight = 0.13f;
        [SerializeField] bool _handOnStomach = true;
        [SerializeField, Range(-25f, 25f)] float _headTilt = 12f;
        [SerializeField, Min(1f)] float _breathsPerMinute = 12f;
        [SerializeField, Range(0f, 1f)] float _breathPhase;

        Transform _chest;
        Quaternion _chestRest;
        Vector3 _breathAxis;

        void Awake()
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.isHuman)
                return;

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (hips == null || head == null || leftFoot == null || rightFoot == null)
                return;

            animator.enabled = false;
            // The scene root is centered on the mattress, with +Z pointing toward the feet.
            Transform model = animator.transform;
            model.rotation = Quaternion.LookRotation(transform.up, -transform.forward);
            float headLength = Vector3.Distance(hips.position, head.position) + 0.14f;
            float legLength = Mathf.Max(Vector3.Distance(hips.position, leftFoot.position),
                Vector3.Distance(hips.position, rightFoot.position)) + 0.06f;
            Vector3 targetHips = transform.TransformPoint(new Vector3(0f, _hipHeight, (headLength - legLength) * 0.5f));
            model.position += targetHips - hips.position;

            PoseLeg(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, leftFoot, -0.055f, 0.97f);
            PoseLeg(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, rightFoot, 0.04f, 0.99f);

            Vector3 localHips = transform.InverseTransformPoint(hips.position);
            PoseArm(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                localHips + new Vector3(-0.27f, -0.02f, 0.12f), -1f);
            Vector3 rightHand = _handOnStomach ? new Vector3(0.10f, 0.13f, -0.17f) : new Vector3(0.27f, 0f, 0.04f);
            PoseArm(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                localHips + rightHand, 1f);

            head.rotation = Quaternion.AngleAxis(_headTilt, transform.forward) * head.rotation;
            CloseEyes();

            _chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
            if (_chest != null)
            {
                _chestRest = _chest.localRotation;
                _breathAxis = _chest.parent.InverseTransformDirection(transform.right);
            }
        }

        void PoseLeg(Animator animator, HumanBodyBones upperBone, HumanBodyBones lowerBone, Transform foot,
            float footOffset, float extension)
        {
            var upper = animator.GetBoneTransform(upperBone);
            var lower = animator.GetBoneTransform(lowerBone);
            if (upper == null || lower == null)
                return;

            float length = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, foot.position);
            Vector3 target = transform.InverseTransformPoint(upper.position);
            target += new Vector3(footOffset, 0f, length * extension);
            target.y = _hipHeight * 0.55f;
            HumanoidLimbPose.Reach(upper, lower, foot, transform.TransformPoint(target), transform.up);
        }

        void PoseArm(Animator animator, HumanBodyBones upperBone, HumanBodyBones lowerBone, HumanBodyBones handBone,
            Vector3 handTarget, float side)
        {
            var upper = animator.GetBoneTransform(upperBone);
            var lower = animator.GetBoneTransform(lowerBone);
            var hand = animator.GetBoneTransform(handBone);
            if (upper == null || lower == null || hand == null)
                return;

            Vector3 elbowBend = transform.right * side - transform.up * 0.18f;
            HumanoidLimbPose.Reach(upper, lower, hand, transform.TransformPoint(handTarget), elbowBend);
        }

        void CloseEyes()
        {
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null)
                    continue;

                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string shape = mesh.GetBlendShapeName(i);
                    if (shape.EndsWith("Blink", System.StringComparison.OrdinalIgnoreCase) ||
                        shape.StartsWith("EyeBlink", System.StringComparison.OrdinalIgnoreCase))
                        renderer.SetBlendShapeWeight(i, 100f);
                }
            }
        }

        void LateUpdate()
        {
            if (_chest == null)
                return;

            float phase = (Time.time * _breathsPerMinute / 60f + _breathPhase) * 2f * Mathf.PI;
            float breath = 0.45f * (1f - Mathf.Cos(phase));
            _chest.localRotation = Quaternion.AngleAxis(breath, _breathAxis) * _chestRest;
        }
    }
}
