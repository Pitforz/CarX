using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DoorKeyPickup : MonoBehaviour
    {
        [SerializeField] Sprite _icon;

        internal Sprite Icon => _icon;
        internal Mesh Mesh => GetComponent<MeshFilter>().sharedMesh;
        internal Material Material => GetComponent<MeshRenderer>().sharedMaterial;
    }
}
