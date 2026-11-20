using System.Collections.Generic;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    [AddComponentMenu("CarX/Bandit Cover")]
    public sealed class BanditCover : MonoBehaviour
    {
        const float HideClearance = 0.6f;
        const float PeekClearance = 0.85f;

        static readonly HashSet<BanditCover> ActiveCovers = new();
        internal static IEnumerable<BanditCover> Active => ActiveCovers;

        BoxCollider _obstacle;
        BanditPistol _occupant;

        void OnEnable()
        {
            _obstacle = GetComponent<BoxCollider>();
            ActiveCovers.Add(this);
        }

        void OnDisable()
        {
            ActiveCovers.Remove(this);
            _occupant = null;
        }

        internal bool IsAvailable => _occupant == null && _obstacle.enabled && !_obstacle.isTrigger;

        internal bool IsReservedBy(BanditPistol bandit) => isActiveAndEnabled && _obstacle.enabled && _occupant == bandit;

        internal void Reserve(BanditPistol bandit) => _occupant = bandit;

        internal void Release(BanditPistol bandit)
        {
            if (_occupant == bandit)
                _occupant = null;
        }

        internal void GetPositions(Vector3 threat, bool lean, out Vector3 hide, out Vector3 left, out Vector3 right)
        {
            Vector3 center = transform.TransformPoint(_obstacle.center);
            Vector3 back = Vector3.Dot(threat - center, transform.forward) >= 0f
                ? -transform.forward : transform.forward;
            float halfDepth = transform.TransformVector(Vector3.forward * (_obstacle.size.z * 0.5f)).magnitude;
            float halfWidth = transform.TransformVector(Vector3.right * (_obstacle.size.x * 0.5f)).magnitude;
            hide = center + back * (halfDepth + HideClearance);
            hide.y = _obstacle.bounds.min.y;
            Vector3 side = transform.right * (lean ? Mathf.Max(0.1f, halfWidth - 0.08f) : halfWidth + PeekClearance);
            left = hide - side;
            right = hide + side;
        }

        internal bool ProtectsFrom(Vector3 threat, Vector3 feet) => isActiveAndEnabled &&
            Blocks(threat, feet + Vector3.up * 0.9f) && Blocks(threat, feet + Vector3.up * 1.72f);

        bool Blocks(Vector3 from, Vector3 to)
        {
            Vector3 offset = to - from;
            float distance = offset.magnitude;
            return distance > 0.001f && _obstacle.Raycast(new Ray(from, offset / distance), out _, distance);
        }
    }
}
