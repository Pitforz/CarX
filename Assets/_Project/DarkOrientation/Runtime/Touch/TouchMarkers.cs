using UnityEngine;

namespace CarX.DarkOrientation
{
    /// <summary>
    /// Marks a trigger collider the hands may hold on to (normally triggers are ignored).
    /// The module adds these to Humanoid NPCs at runtime; NPC prefabs can also carry their own,
    /// then the module adds nothing to that NPC.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CarX/Dark Orientation/Touch Proxy")]
    public sealed class DarkTouchProxy : MonoBehaviour
    {
        [Tooltip("Создан модулем во время игры и будет удалён, когда режим выключится")]
        public bool generated;
    }

    /// <summary>The hands never touch colliders on this object or its children.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CarX/Dark Orientation/Touch Ignore")]
    public sealed class DarkTouchIgnore : MonoBehaviour { }

    /// <summary>Put into a scene to stop the mode from installing itself there.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CarX/Dark Orientation/Disable In This Scene")]
    public sealed class DarkOrientationOptOut : MonoBehaviour { }

    public readonly struct TouchInfo
    {
        public readonly Collider collider;
        public readonly Vector3 point;
        public readonly Vector3 normal;

        public TouchInfo(Collider collider, Vector3 point, Vector3 normal)
        {
            this.collider = collider;
            this.point = point;
            this.normal = normal;
        }
    }

    /// <summary>A touch point stored in the collider's local space, so it follows moving objects and animated bones.</summary>
    internal struct TouchAnchor
    {
        public Collider collider;
        public Vector3 localPoint;
        public Vector3 localNormal;

        public bool IsValid => collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
        public Vector3 Point => collider.transform.TransformPoint(localPoint);
        public Vector3 Normal => collider.transform.TransformDirection(localNormal).normalized;

        public static TouchAnchor Create(Collider collider, Vector3 point, Vector3 normal)
        {
            var t = collider.transform;
            return new TouchAnchor
            {
                collider = collider,
                localPoint = t.InverseTransformPoint(point),
                localNormal = t.InverseTransformDirection(normal),
            };
        }
    }
}
