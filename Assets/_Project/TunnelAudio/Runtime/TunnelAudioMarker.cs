using UnityEngine;

namespace CarX.TunnelAudio
{
    /// <summary>Scene label for a generated sound point. The point itself is the object this sits on.</summary>
    [DisallowMultipleComponent]
    public sealed class TunnelAudioMarker : MonoBehaviour
    {
        public string label;
        public Color color = new Color(1f, 0.85f, 0.2f, 1f);
        public float radius = 0.35f;

        void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
