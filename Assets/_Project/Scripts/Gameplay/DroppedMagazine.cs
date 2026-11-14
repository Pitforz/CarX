using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class DroppedMagazine : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        internal static readonly Color RestingColor = new Color32(151, 125, 89, 255);
        static readonly Color HighlightColor = new Color32(255, 209, 113, 255);
        Material _material;

        internal int Rounds { get; private set; }

        internal static void Spawn(Vector3 position, int rounds, Shader shader, Scene scene,
            Collider[] playerColliders)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Dropped Pistol Magazine";
            if (cube.scene != scene)
                SceneManager.MoveGameObjectToScene(cube, scene);
            cube.transform.SetPositionAndRotation(position, Quaternion.identity);
            cube.transform.localScale = Vector3.one * 0.16f;

            var pickup = cube.AddComponent<DroppedMagazine>();
            pickup.Rounds = rounds;
            pickup._material = new Material(shader) { name = "Dropped Magazine (Runtime)" };
            cube.GetComponent<Renderer>().sharedMaterial = pickup._material;
            pickup.SetHighlighted(false);

            var body = cube.AddComponent<Rigidbody>();
            body.mass = 0.15f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var collider = cube.GetComponent<Collider>();
            foreach (var playerCollider in playerColliders)
            {
                if (playerCollider.enabled && playerCollider.gameObject.activeInHierarchy)
                    Physics.IgnoreCollision(collider, playerCollider);
            }
        }

        internal void SetHighlighted(bool highlighted)
        {
            _material.SetColor(BaseColor, highlighted ? HighlightColor : RestingColor);
        }

        void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }
    }
}
