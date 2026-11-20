using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [AddComponentMenu("CarX/Player Status Zone")]
    public sealed class PlayerStatusZone : MonoBehaviour
    {
        [SerializeField] Bounds _bounds = new(Vector3.zero, Vector3.one);

        internal bool Contains(Vector3 worldPosition) =>
            isActiveAndEnabled && _bounds.Contains(transform.InverseTransformPoint(worldPosition));

        void OnDrawGizmosSelected()
        {
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Color oldColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(_bounds.center, _bounds.size);
            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;
        }
    }
}
