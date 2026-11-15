using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarX.DarkOrientation.EditorTools
{
    /// <summary>Builds a dark test room: corridor walls, a box, a pipe with a concave mesh collider and a moving panel.</summary>
    public static class DarkOrientationTestSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/DarkOrientation/Scenes/DarkOrientation_Test.unity";
        const string PlayerPrefab = "Assets/Starter Assets/Runtime/FirstPersonController/Prefabs/NestedParent_Unpack.prefab";

        [MenuItem("CarX/Dark Orientation/Build Test Scene", priority = 40)]
        static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.012f, 0.013f, 0.015f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.05f;
            RenderSettings.fogColor = new Color(0.005f, 0.0055f, 0.006f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f;

            var root = new GameObject("Test Room").transform;
            Block(root, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(8f, 0.2f, 14f));
            Block(root, "Ceiling", new Vector3(0f, 2.6f, 0f), new Vector3(8f, 0.2f, 14f));
            Block(root, "Wall West", new Vector3(-1.3f, 1.25f, 0f), new Vector3(0.2f, 2.5f, 14f));
            Block(root, "Wall East", new Vector3(1.3f, 1.25f, 0f), new Vector3(0.2f, 2.5f, 14f));
            Block(root, "Wall North", new Vector3(0f, 1.25f, 6f), new Vector3(2.6f, 2.5f, 0.2f));
            Block(root, "Wall South", new Vector3(0f, 1.25f, -6f), new Vector3(2.6f, 2.5f, 0.2f));
            Block(root, "Box", new Vector3(0.75f, 0.45f, 1.5f), new Vector3(0.6f, 0.9f, 0.6f));
            Block(root, "Wall Corner", new Vector3(-0.85f, 1.25f, 3f), new Vector3(0.7f, 2.5f, 0.2f));

            var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pipe.name = "Pipe (concave collider)";
            pipe.transform.SetParent(root, false);
            pipe.transform.SetPositionAndRotation(new Vector3(0f, 1.25f, 4.4f), Quaternion.Euler(0f, 0f, 90f));
            pipe.transform.localScale = new Vector3(0.25f, 1.3f, 0.25f);
            Object.DestroyImmediate(pipe.GetComponent<Collider>());
            var mc = pipe.AddComponent<MeshCollider>();
            mc.convex = false;

            var panel = Block(root, "Moving Panel", new Vector3(-0.4f, 1.3f, -0.6f), new Vector3(0.8f, 0.8f, 0.06f));
            panel.isStatic = false;
            var mover = panel.AddComponent<DarkOrientationTestMover>();
            mover.axis = Vector3.right;
            mover.amplitude = 0.5f;
            mover.period = 6f;

            var lightGo = new GameObject("Dim Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.position = new Vector3(0f, 2.2f, 5f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 6f;
            light.intensity = 0.6f;
            light.color = new Color(0.8f, 0.85f, 1f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            if (prefab != null)
            {
                var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                player.transform.position = new Vector3(0f, 0f, -2.5f);
                foreach (var cam in player.GetComponentsInChildren<Camera>(true))
                {
                    var data = cam.GetUniversalAdditionalCameraData();
                    data.renderPostProcessing = true;
                    data.requiresDepthTexture = true;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = Color.black;
                }
            }
            else
            {
                Debug.LogWarning("CarX DarkOrientation: player prefab not found at " + PlayerPrefab);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("CarX DarkOrientation: test scene saved to " + ScenePath);
        }

        static GameObject Block(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;
            go.isStatic = true;
            return go;
        }
    }
}
