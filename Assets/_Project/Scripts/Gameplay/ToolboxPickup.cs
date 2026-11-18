using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody), typeof(MeshRenderer))]
    public sealed class ToolboxPickup : MonoBehaviour
    {
        internal const float DropClearanceRadius = 0.29f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color HighlightColor = new Color32(248, 98, 65, 255);

        BoxCollider _collider;
        Rigidbody _body;
        MeshRenderer _mesh;
        MaterialPropertyBlock _properties;

        internal bool PickupAllowed { get; set; } = true;

        void Awake()
        {
            _collider = GetComponent<BoxCollider>();
            _body = GetComponent<Rigidbody>();
            _mesh = GetComponent<MeshRenderer>();
            _properties = new MaterialPropertyBlock();
        }

        internal void SetHighlighted(bool highlighted)
        {
            _mesh.GetPropertyBlock(_properties);
            if (highlighted)
                _properties.SetColor(BaseColor, HighlightColor);
            else
                _properties.Clear();
            _mesh.SetPropertyBlock(_properties);
        }

        internal void Carry(Transform camera)
        {
            _body.isKinematic = true;
            _collider.enabled = false;
            transform.SetParent(camera, false);
            transform.SetLocalPositionAndRotation(new Vector3(0f, -0.34f, 0.64f), Quaternion.Euler(8f, -9f, 0f));
        }

        internal void PutDown(Vector3 position, float yaw, Collider[] playerColliders)
        {
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _collider.enabled = true;
            foreach (var playerCollider in playerColliders)
                if (playerCollider != _collider && playerCollider.enabled && playerCollider.gameObject.activeInHierarchy)
                    Physics.IgnoreCollision(_collider, playerCollider);
            _body.isKinematic = false;
        }
    }
}
