using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(400)]
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
        Transform _carryCamera;

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
            _body.interpolation = RigidbodyInterpolation.None;
            _body.isKinematic = true;
            _body.detectCollisions = false;
            _collider.enabled = false;
            _carryCamera = camera;
            Vector3 scale = transform.lossyScale;
            foreach (var renderer in GetComponentsInChildren<Renderer>())
                renderer.allowOcclusionWhenDynamic = false;
            transform.SetParent(camera, false);
            Vector3 cameraScale = camera.lossyScale;
            transform.localScale = new Vector3(scale.x / cameraScale.x, scale.y / cameraScale.y, scale.z / cameraScale.z);
            UpdateCarryPose();
        }

        void LateUpdate()
        {
            if (_carryCamera != null)
                UpdateCarryPose();
        }

        void UpdateCarryPose() => transform.SetLocalPositionAndRotation(
            new Vector3(0f, -0.25f, 0.55f), Quaternion.Euler(8f, -9f, 0f));

        internal void PutDown(Vector3 position, float yaw, Collider[] playerColliders)
        {
            _carryCamera = null;
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _collider.enabled = true;
            foreach (var playerCollider in playerColliders)
                if (playerCollider != _collider && playerCollider.enabled && playerCollider.gameObject.activeInHierarchy)
                    Physics.IgnoreCollision(_collider, playerCollider);
            _body.isKinematic = false;
            _body.detectCollisions = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }
}
