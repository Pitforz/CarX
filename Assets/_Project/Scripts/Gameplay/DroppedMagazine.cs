using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class DroppedMagazine : MonoBehaviour
    {
        internal static readonly Color RestingColor = new Color32(151, 125, 89, 255);

        [SerializeField] Material _restingMaterial;
        [SerializeField] Material _highlightMaterial;
        [SerializeField] GameObject[] _visibleRounds;

        Renderer[] _renderers;

        internal int Rounds { get; private set; }

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        internal static void Spawn(DroppedMagazine prefab, Vector3 position, int rounds, Scene scene,
            Collider[] playerColliders)
        {
            var pickup = Instantiate(prefab, position, Quaternion.Euler(24f, 0f, 18f));
            if (pickup.gameObject.scene != scene)
                SceneManager.MoveGameObjectToScene(pickup.gameObject, scene);
            pickup.Rounds = rounds;
            for (int i = 0; i < pickup._visibleRounds.Length; i++)
                pickup._visibleRounds[i].SetActive(i < rounds);

            var collider = pickup.GetComponent<BoxCollider>();
            foreach (var playerCollider in playerColliders)
            {
                if (playerCollider.enabled && playerCollider.gameObject.activeInHierarchy)
                    Physics.IgnoreCollision(collider, playerCollider);
            }
        }

        internal void SetHighlighted(bool highlighted)
        {
            var material = highlighted ? _highlightMaterial : _restingMaterial;
            foreach (var renderer in _renderers)
                renderer.sharedMaterial = material;
        }
    }
}
