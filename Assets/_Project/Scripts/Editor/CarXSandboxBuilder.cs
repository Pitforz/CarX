// Editor-only helper: menu "CarX > Build Test Tunnel" builds a simple tunnel
// with a first-person player into Assets/_Project/Scenes/Sandbox.unity.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class CarXSandboxBuilder
{
    const string ScenePath = "Assets/_Project/Scenes/Sandbox.unity";
    const string MaterialFolder = "Assets/_Project/Materials";

    const float Width = 4f;      // tunnel inner width
    const float Height = 3f;     // tunnel inner height
    const float Length = 40f;    // main tunnel length
    const float Wall = 0.3f;     // wall thickness

    [MenuItem("CarX/Build Test Tunnel")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Dark ambient so the tunnel feels underground.
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.08f, 0.08f, 0.1f);
        RenderSettings.skybox = null;

        var concrete = GetMaterial("Concrete_Test", new Color(0.45f, 0.44f, 0.42f));
        var floorMat = GetMaterial("Floor_Test", new Color(0.25f, 0.24f, 0.22f));

        var root = new GameObject("TestTunnel").transform;

        // Main tunnel along +Z, centred on X = 0, from Z = -Length/2 to +Length/2.
        float halfL = Length / 2f;
        Box("Floor", root, new Vector3(0, -Wall / 2, 0), new Vector3(Width + 2 * Wall, Wall, Length), floorMat);
        Box("Ceiling", root, new Vector3(0, Height + Wall / 2, 0), new Vector3(Width + 2 * Wall, Wall, Length), concrete);
        Box("Wall_Left", root, new Vector3(-(Width + Wall) / 2, Height / 2, 0), new Vector3(Wall, Height, Length), concrete);
        // Right wall has a 4 m gap at Z = 8..12 for a side branch.
        float gapStart = 8f, gapEnd = 12f;
        float seg1Len = gapStart + halfL;            // from -halfL to gapStart
        float seg2Len = halfL - gapEnd;              // from gapEnd to +halfL
        Box("Wall_Right_A", root, new Vector3((Width + Wall) / 2, Height / 2, -halfL + seg1Len / 2), new Vector3(Wall, Height, seg1Len), concrete);
        Box("Wall_Right_B", root, new Vector3((Width + Wall) / 2, Height / 2, gapEnd + seg2Len / 2), new Vector3(Wall, Height, seg2Len), concrete);
        // End caps.
        Box("Wall_End_South", root, new Vector3(0, Height / 2, -halfL - Wall / 2), new Vector3(Width + 2 * Wall, Height, Wall), concrete);
        Box("Wall_End_North", root, new Vector3(0, Height / 2, halfL + Wall / 2), new Vector3(Width + 2 * Wall, Height, Wall), concrete);

        // Side branch going +X, 12 m long, ends in a dead end.
        float branchLen = 12f;
        float bx = Width / 2 + Wall + branchLen / 2;
        float bz = (gapStart + gapEnd) / 2;
        float bw = gapEnd - gapStart;
        Box("Branch_Floor", root, new Vector3(bx, -Wall / 2, bz), new Vector3(branchLen, Wall, bw + 2 * Wall), floorMat);
        Box("Branch_Ceiling", root, new Vector3(bx, Height + Wall / 2, bz), new Vector3(branchLen, Wall, bw + 2 * Wall), concrete);
        Box("Branch_Wall_S", root, new Vector3(bx, Height / 2, gapStart - Wall / 2), new Vector3(branchLen, Height, Wall), concrete);
        Box("Branch_Wall_N", root, new Vector3(bx, Height / 2, gapEnd + Wall / 2), new Vector3(branchLen, Height, Wall), concrete);
        Box("Branch_Wall_End", root, new Vector3(bx + branchLen / 2 + Wall / 2, Height / 2, bz), new Vector3(Wall, Height, bw + 2 * Wall), concrete);

        // A few obstacles to walk around.
        Box("Crate_1", root, new Vector3(-1.2f, 0.4f, -5f), new Vector3(0.8f, 0.8f, 0.8f), floorMat);
        Box("Crate_2", root, new Vector3(1.1f, 0.5f, 3f), new Vector3(1f, 1f, 1f), floorMat);

        // Weak lamps along the tunnel: some light, lots of shadow.
        var lights = new GameObject("Lights").transform;
        for (float z = -halfL + 5f; z < halfL; z += 12f)
            PointLight(lights, new Vector3(0, Height - 0.3f, z), 7f, 1.5f, new Color(1f, 0.85f, 0.6f));
        PointLight(lights, new Vector3(bx + branchLen / 2 - 1f, Height - 0.3f, bz), 6f, 1.2f, new Color(0.6f, 0.8f, 1f));

        // Player from Starter Assets (camera + capsule + follow camera already linked).
        var player = SpawnPlayer(new Vector3(0, 0.1f, -halfL + 2f));

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        if (player != null) Selection.activeGameObject = player;

        Debug.Log(player != null
            ? "CarX: test tunnel built in " + ScenePath + ". Press Play, walk with WASD + mouse, Esc frees the cursor."
            : "CarX: tunnel built, but Starter Assets player prefab (NestedParent_Unpack) was not found. Import Starter Assets first.");
    }

    static GameObject SpawnPlayer(Vector3 position)
    {
        string[] guids = AssetDatabase.FindAssets("NestedParent_Unpack t:Prefab");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Contains("FirstPersonController")) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = "Player_FPS";
            go.transform.position = position;
            return go;
        }
        return null;
    }

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        return go;
    }

    static void PointLight(Transform parent, Vector3 pos, float range, float intensity, Color color)
    {
        var go = new GameObject("Lamp");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = color;
        l.shadows = LightShadows.Soft;
    }

    static Material GetMaterial(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        mat.color = color;
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/_Project", "Materials");
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
