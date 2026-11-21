using CarX.NPC;
using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DoorKeyPickup : MonoBehaviour
    {
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly Color FocusGlow = new Color(0.17f, 0.087f, 0.025f);

        [SerializeField] Sprite _icon;
        DeniDialogue _owner;
        MeshRenderer _renderer;
        MaterialPropertyBlock _properties;

        internal Sprite Icon => _icon;
        internal Mesh Mesh => GetComponent<MeshFilter>().sharedMesh;
        internal Material Material => _renderer.sharedMaterial;

        void Awake() => _renderer = GetComponent<MeshRenderer>();

        internal void Reveal(DeniDialogue owner)
        {
            _owner = owner;
            var position = transform.position;
            float nearest = float.PositiveInfinity;
            // Use the floor at the marker so the workshop can also be placed on another map.
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * 0.2f, Vector3.down, 0.95f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= nearest || hit.normal.y < 0.8f || hit.transform.IsChildOf(owner.transform))
                    continue;

                nearest = hit.distance;
                position.y = hit.point.y - Mathf.Min(0f, Mesh.bounds.min.y) + 0.003f;
            }
            transform.position = position;
            gameObject.SetActive(true);
        }

        internal bool TryCollect(PlayerInventory inventory) =>
            _owner != null && _owner.TryTakeKey(this, inventory);

        internal void SetHighlighted(bool highlighted)
        {
            if (!highlighted && _properties == null)
                return;

            _properties ??= new MaterialPropertyBlock();
            if (highlighted)
                _properties.SetColor(EmissionColor, FocusGlow);
            _renderer.SetPropertyBlock(highlighted ? _properties : null);
        }
    }
}
