using System.Collections.Generic;
using UnityEngine;

namespace CarX.DarkOrientation
{
    /// <summary>
    /// NPCs have no colliders, so while the mode is on, Humanoid characters near the player get trigger capsules
    /// on their bones. Proxies live on the Ignore Raycast layer and are triggers: player movement, shots, the flashlight
    /// and everything else using default raycast layers never see them. Nothing is saved into scenes or prefabs.
    /// </summary>
    internal sealed class HumanoidTouchProxyBuilder
    {
        public const int ProxyLayer = 2;   // Ignore Raycast

        readonly List<GameObject> _created = new();
        readonly HashSet<Animator> _handled = new();
        float _nextScan;

        public int Count => _created.Count;

        public void Tick(Transform body, Vector3 playerPosition, float range, float time)
        {
            if (time < _nextScan) return;
            _nextScan = time + 1f;
            float rangeSq = range * range;
            foreach (var animator in Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude))
            {
                if (animator == null || !animator.isHuman || !animator.isActiveAndEnabled || _handled.Contains(animator)) continue;
                if (body != null && (animator.transform.IsChildOf(body) || body.IsChildOf(animator.transform))) continue;
                if ((animator.transform.position - playerPosition).sqrMagnitude > rangeSq) continue;
                _handled.Add(animator);
                if (HasOwnColliders(animator)) continue;
                Build(animator);
            }
        }

        public void Clear()
        {
            foreach (var go in _created)
                if (go != null) Object.Destroy(go);
            _created.Clear();
            _handled.Clear();
            _nextScan = 0f;
        }

        static bool HasOwnColliders(Animator animator)
        {
            foreach (var c in animator.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger || c.GetComponent<DarkTouchProxy>() != null) return true;
            return false;
        }

        void Build(Animator a)
        {
            float s = Mathf.Max(0.5f, a.humanScale);
            var chest = a.GetBoneTransform(HumanBodyBones.UpperChest) != null ? HumanBodyBones.UpperChest : HumanBodyBones.Chest;
            Capsule(a, HumanBodyBones.Hips, chest, 0.13f * s);
            Capsule(a, chest, HumanBodyBones.Neck, 0.13f * s);
            Capsule(a, HumanBodyBones.Neck, HumanBodyBones.Head, 0.055f * s);
            Head(a, 0.1f * s);
            foreach (bool left in new[] { true, false })
            {
                Capsule(a, left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm,
                    left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, 0.055f * s);
                Capsule(a, left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm,
                    left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, 0.045f * s);
                Capsule(a, left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand,
                    left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal, 0.04f * s);
                Capsule(a, left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg,
                    left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, 0.08f * s);
                Capsule(a, left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg,
                    left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, 0.06f * s);
            }
        }

        void Capsule(Animator a, HumanBodyBones from, HumanBodyBones to, float radius)
        {
            var t0 = a.GetBoneTransform(from);
            var t1 = a.GetBoneTransform(to);
            if (t0 == null || t1 == null) return;
            Vector3 p0 = t0.position, p1 = t1.position;
            float length = Vector3.Distance(p0, p1);
            if (length < 0.01f) return;
            var go = NewProxy(t0, "DarkTouchProxy " + from);
            go.transform.SetPositionAndRotation((p0 + p1) * 0.5f, Quaternion.FromToRotation(Vector3.up, p1 - p0));
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.direction = 1;
            float lossy = Mathf.Max(1e-4f, go.transform.lossyScale.y);
            capsule.radius = radius / lossy;
            capsule.height = (length + radius) / lossy;
        }

        void Head(Animator a, float radius)
        {
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            var go = NewProxy(head, "DarkTouchProxy Head");
            go.transform.SetPositionAndRotation(head.position + a.transform.up * radius * 0.9f, a.transform.rotation);
            var sphere = go.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = radius / Mathf.Max(1e-4f, go.transform.lossyScale.y);
        }

        GameObject NewProxy(Transform bone, string name)
        {
            var go = new GameObject(name) { layer = ProxyLayer };
            go.transform.SetParent(bone, false);
            go.AddComponent<DarkTouchProxy>().generated = true;
            _created.Add(go);
            return go;
        }
    }
}
