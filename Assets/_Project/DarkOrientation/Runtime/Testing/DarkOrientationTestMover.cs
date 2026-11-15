using UnityEngine;

namespace CarX.DarkOrientation
{
    /// <summary>Test helper: slides an object back and forth so a held hand can be checked on moving geometry.</summary>
    [AddComponentMenu("CarX/Dark Orientation/Test Mover")]
    public sealed class DarkOrientationTestMover : MonoBehaviour
    {
        public Vector3 axis = Vector3.right;
        [Min(0f)] public float amplitude = 0.6f;
        [Min(0.1f)] public float period = 6f;

        Vector3 _origin;

        void Start() => _origin = transform.position;

        void Update()
        {
            float phase = Mathf.Sin(Time.time * Mathf.PI * 2f / period);
            transform.position = _origin + axis.normalized * (amplitude * phase);
        }
    }
}
