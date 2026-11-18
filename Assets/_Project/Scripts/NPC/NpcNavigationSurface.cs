using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class NpcNavigationSurface : MonoBehaviour
    {
        NavMeshSurface _surface;

        void Start()
        {
            // TunnelPath and TunnelJunction build their colliders in OnEnable.
            Physics.SyncTransforms();
            _surface = gameObject.AddComponent<NavMeshSurface>();
            _surface.collectObjects = CollectObjects.Children;
            _surface.layerMask = Physics.DefaultRaycastLayers;
            _surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            _surface.overrideVoxelSize = true;
            _surface.voxelSize = 0.10f;
            _surface.BuildNavMesh();
        }

        void OnDestroy()
        {
            if (_surface == null)
                return;

            _surface.RemoveData();
            if (_surface.navMeshData != null)
                Destroy(_surface.navMeshData);
            Destroy(_surface);
        }
    }
}
