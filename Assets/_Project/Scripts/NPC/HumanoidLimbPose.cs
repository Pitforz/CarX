using UnityEngine;

namespace CarX.NPC
{
    internal static class HumanoidLimbPose
    {
        internal static void Reach(Transform joint, Transform elbowJoint, Transform tip, Vector3 target, Vector3 pole,
            float weight = 1f)
        {
            Vector3 origin = joint.position;
            float upper = Vector3.Distance(origin, elbowJoint.position);
            float lower = Vector3.Distance(elbowJoint.position, tip.position);
            Vector3 offset = target - origin;
            if (offset.sqrMagnitude < 0.000001f || upper < 0.002f || lower < 0.002f || weight <= 0f)
                return;

            Quaternion startJoint = joint.localRotation;
            Quaternion startElbow = elbowJoint.localRotation;
            Vector3 direction = offset.normalized;
            float distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(upper - lower) + 0.001f, upper + lower - 0.001f);
            float along = (upper * upper + distance * distance - lower * lower) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction);
            if (bend.sqrMagnitude < 0.000001f)
                bend = Vector3.ProjectOnPlane(elbowJoint.position - origin, direction);
            if (bend.sqrMagnitude < 0.000001f)
                bend = Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right);
            Vector3 elbow = origin + direction * along + bend.normalized * height;
            Vector3 hand = origin + direction * distance;
            joint.rotation = Quaternion.FromToRotation(elbowJoint.position - origin, elbow - origin) * joint.rotation;
            elbowJoint.rotation = Quaternion.FromToRotation(tip.position - elbowJoint.position, hand - elbowJoint.position) *
                elbowJoint.rotation;
            joint.localRotation = Quaternion.Slerp(startJoint, joint.localRotation, weight);
            elbowJoint.localRotation = Quaternion.Slerp(startElbow, elbowJoint.localRotation, weight);
        }
    }
}
