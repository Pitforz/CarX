using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.EditorTools
{
    // Builds a modular tunnel kit (box culvert with chamfered corners):
    // straight 4 m / 2 m, dead end, corner, T-junction, cross.
    // Pivot = centre of the cell on the floor, so modules snap on a grid of cellSize
    // and rotate in 90-degree steps. Floor uses its own material (mud), the rest is concrete.
    public static class TunnelKitGenerator
    {
        const string SettingsPath = "Assets/_Project/Settings/TunnelKitSettings.asset";
        const string MaterialDir = "Assets/_Project/Materials/Tunnel";
        const string MeshDir = "Assets/_Project/Models/TunnelKit";
        const string PrefabDir = "Assets/_Project/Prefabs/TunnelKit";
        const string DemoScenePath = "Assets/_Project/Scenes/TunnelKit_Demo.unity";

        const int SubConcrete = 0;
        const int SubFloor = 1;

        // ---------------- menu ----------------

        [MenuItem("CarX/Tunnel Kit/Generate Modules", priority = 1)]
        public static void GenerateModules()
        {
            var s = LoadOrCreateSettings();
            s.Clamp();
            EnsureMaterials(s);
            EnsureFolder(MeshDir);
            EnsureFolder(PrefabDir);

            float g = s.cellSize;
            Save(s, "Tunnel_Straight_4m", BuildStraight(s, g, false), StraightColliders(s, g, false));
            Save(s, "Tunnel_Straight_2m", BuildStraight(s, g / 2f, false), StraightColliders(s, g / 2f, false));
            Save(s, "Tunnel_DeadEnd", BuildStraight(s, g, true), StraightColliders(s, g, true));
            // Junctions: open sides given as N(+Z), E(+X), S(-Z), W(-X).
            Save(s, "Tunnel_Corner", BuildJunction(s, false, true, true, false), JunctionColliders(s, false, true, true, false));
            Save(s, "Tunnel_T", BuildJunction(s, true, true, true, false), JunctionColliders(s, true, true, true, false));
            Save(s, "Tunnel_Cross", BuildJunction(s, true, true, true, true), JunctionColliders(s, true, true, true, true));

            // Grid snapping: Ctrl+drag moves by one cell, Ctrl+rotate by 90 degrees.
            EditorSnapSettings.move = new Vector3(g, g, g);
            EditorSnapSettings.rotate = 90f;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(PrefabDir));
            Debug.Log($"CarX Tunnel Kit: 6 modules generated in {PrefabDir}. Opening {s.width} x {s.height} m, chamfer {s.chamfer} m, grid {g} m. Hold Ctrl while moving to snap to the grid.");
        }

        [MenuItem("CarX/Tunnel Kit/Build Demo Layout", priority = 2)]
        public static void BuildDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var s = LoadOrCreateSettings();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Tunnel_Straight_4m.prefab") == null)
                GenerateModules();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.07f, 0.09f);
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f; // no sky reflections underground

            float g = s.cellSize;
            var root = new GameObject("Tunnels").transform;
            Place(root, "Tunnel_DeadEnd", 0, 0, 180);
            Place(root, "Tunnel_Straight_4m", 0, 1, 0);
            Place(root, "Tunnel_T", 0, 2, 0);          // open N, E, S
            Place(root, "Tunnel_Straight_4m", 1, 2, 90);
            Place(root, "Tunnel_Corner", 2, 2, 180);   // open W, N
            Place(root, "Tunnel_Straight_4m", 2, 3, 0);
            Place(root, "Tunnel_DeadEnd", 2, 4, 0);
            Place(root, "Tunnel_Straight_4m", 0, 3, 0);
            Place(root, "Tunnel_Cross", 0, 4, 0);
            Place(root, "Tunnel_DeadEnd", -1, 4, 270);
            Place(root, "Tunnel_DeadEnd", 1, 4, 90);
            Place(root, "Tunnel_Straight_4m", 0, 5, 0);
            Place(root, "Tunnel_DeadEnd", 0, 6, 0);

            var lights = new GameObject("Lights").transform;
            float ly = s.height - 0.25f;
            Lamp(lights, new Vector3(0, ly, 1 * g));
            Lamp(lights, new Vector3(2 * g, ly, 2 * g));
            Lamp(lights, new Vector3(0, ly, 4 * g));
            Lamp(lights, new Vector3(0, ly, 6 * g));

            var player = SpawnPlayer(new Vector3(0, 0.1f, -0.5f));
            EditorSceneManager.SaveScene(scene, DemoScenePath);
            if (player != null) Selection.activeGameObject = player;
            Debug.Log("CarX Tunnel Kit: demo layout saved to " + DemoScenePath + ". Press Play to walk.");

            void Place(Transform parent, string name, int cx, int cz, float yaw)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{name}.prefab");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.SetPositionAndRotation(new Vector3(cx * g, 0, cz * g), Quaternion.Euler(0, yaw, 0));
            }
        }

        // ---------------- geometry ----------------

        // Inner cross-section, counter-clockwise looking along +Z. Segment 0 is the floor.
        static Vector2[] Profile(TunnelKitSettings s)
        {
            float w = s.width / 2f, h = s.height, c = s.chamfer;
            return new[]
            {
                new Vector2(-w + c, 0), new Vector2(w - c, 0), new Vector2(w, c), new Vector2(w, h - c),
                new Vector2(w - c, h), new Vector2(-w + c, h), new Vector2(-w, h - c), new Vector2(-w, c),
            };
        }

        static Mesh BuildStraight(TunnelKitSettings s, float length, bool deadEnd)
        {
            var mb = new MeshBuilder();
            float z0 = -length / 2f;
            float z1 = deadEnd ? length / 2f - s.wallThickness : length / 2f;
            Extrude(mb, s, Quaternion.identity, z0, z1);
            if (deadEnd)
            {
                var p = Profile(s);
                var centre = new Vector3(0, s.height / 2f, z1);
                var inside = new Vector3(0, s.height / 2f, 0);
                for (int i = 0; i < p.Length; i++)
                {
                    var a = new Vector3(p[i].x, p[i].y, z1);
                    var b = new Vector3(p[(i + 1) % p.Length].x, p[(i + 1) % p.Length].y, z1);
                    mb.Tri(SubConcrete, a, b, centre, inside);
                }
            }
            return mb.ToMesh();
        }

        static void Extrude(MeshBuilder mb, TunnelKitSettings s, Quaternion rot, float z0, float z1)
        {
            var p = Profile(s);
            var inside = rot * new Vector3(0, s.height / 2f, (z0 + z1) / 2f);
            for (int i = 0; i < p.Length; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % p.Length];
                int sub = i == 0 ? SubFloor : SubConcrete;
                mb.Quad(sub,
                    rot * new Vector3(a.x, a.y, z0), rot * new Vector3(b.x, b.y, z0),
                    rot * new Vector3(b.x, b.y, z1), rot * new Vector3(a.x, a.y, z1), inside);
            }
        }

        static Mesh BuildJunction(TunnelKitSettings s, bool n, bool e, bool so, bool w)
        {
            var mb = new MeshBuilder();
            float hw = s.width / 2f, h = s.height, c = s.chamfer, half = s.cellSize / 2f;
            var centre = new Vector3(0, h / 2f, 0);

            // Square room in the middle: floor and ceiling.
            mb.Quad(SubFloor, new Vector3(-hw, 0, -hw), new Vector3(hw, 0, -hw), new Vector3(hw, 0, hw), new Vector3(-hw, 0, hw), centre);
            mb.Quad(SubConcrete, new Vector3(-hw, h, -hw), new Vector3(hw, h, -hw), new Vector3(hw, h, hw), new Vector3(-hw, h, hw), centre);

            bool[] open = { n, e, so, w };
            for (int side = 0; side < 4; side++)
            {
                var rot = Quaternion.Euler(0, side * 90f, 0);
                if (open[side])
                {
                    // Short arm out to the cell edge with the normal tunnel profile.
                    Extrude(mb, s, rot, hw, half);
                    // Corner wedges where the chamfered profile meets the square room.
                    mb.Tri(SubConcrete, rot * new Vector3(hw - c, 0, hw), rot * new Vector3(hw, 0, hw), rot * new Vector3(hw, c, hw), centre);
                    mb.Tri(SubConcrete, rot * new Vector3(hw, h - c, hw), rot * new Vector3(hw, h, hw), rot * new Vector3(hw - c, h, hw), centre);
                    mb.Tri(SubConcrete, rot * new Vector3(-hw + c, h, hw), rot * new Vector3(-hw, h, hw), rot * new Vector3(-hw, h - c, hw), centre);
                    mb.Tri(SubConcrete, rot * new Vector3(-hw, c, hw), rot * new Vector3(-hw, 0, hw), rot * new Vector3(-hw + c, 0, hw), centre);
                }
                else
                {
                    mb.Quad(SubConcrete, rot * new Vector3(-hw, 0, hw), rot * new Vector3(hw, 0, hw),
                        rot * new Vector3(hw, h, hw), rot * new Vector3(-hw, h, hw), centre);
                }
            }
            return mb.ToMesh();
        }

        // ---------------- colliders ----------------

        struct Box { public Vector3 c, size; public Box(Vector3 c, Vector3 size) { this.c = c; this.size = size; } }

        static List<Box> StraightColliders(TunnelKitSettings s, float length, bool deadEnd)
        {
            float w = s.width, h = s.height, t = s.wallThickness;
            var list = new List<Box>
            {
                new Box(new Vector3(0, -t / 2f, 0), new Vector3(w + 2 * t, t, length)),
                new Box(new Vector3(0, h + t / 2f, 0), new Vector3(w + 2 * t, t, length)),
                new Box(new Vector3(-(w + t) / 2f, h / 2f, 0), new Vector3(t, h, length)),
                new Box(new Vector3((w + t) / 2f, h / 2f, 0), new Vector3(t, h, length)),
            };
            if (deadEnd) list.Add(new Box(new Vector3(0, h / 2f, length / 2f - t / 2f), new Vector3(w, h, t)));
            return list;
        }

        static List<Box> JunctionColliders(TunnelKitSettings s, bool n, bool e, bool so, bool w)
        {
            float hw = s.width / 2f, h = s.height, t = s.wallThickness, g = s.cellSize, half = g / 2f;
            var list = new List<Box>
            {
                new Box(new Vector3(0, -t / 2f, 0), new Vector3(g, t, g)),
                new Box(new Vector3(0, h + t / 2f, 0), new Vector3(g, t, g)),
            };
            float cs = half - hw, cc = hw + cs / 2f;
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
                list.Add(new Box(new Vector3(sx * cc, h / 2f, sz * cc), new Vector3(cs, h, cs)));
            float wt = Mathf.Min(t, cs);
            if (!n) list.Add(new Box(new Vector3(0, h / 2f, hw + wt / 2f), new Vector3(s.width, h, wt)));
            if (!so) list.Add(new Box(new Vector3(0, h / 2f, -hw - wt / 2f), new Vector3(s.width, h, wt)));
            if (!e) list.Add(new Box(new Vector3(hw + wt / 2f, h / 2f, 0), new Vector3(wt, h, s.width)));
            if (!w) list.Add(new Box(new Vector3(-hw - wt / 2f, h / 2f, 0), new Vector3(wt, h, s.width)));
            return list;
        }

        // ---------------- assets ----------------

        static void Save(TunnelKitSettings s, string name, Mesh built, List<Box> boxes)
        {
            string meshPath = $"{MeshDir}/{name}_Mesh.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = built;
                mesh.name = name;
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                // Overwrite in place so prefabs and scenes keep their references.
                mesh.Clear();
                mesh.SetVertices(built.vertices);
                mesh.subMeshCount = built.subMeshCount;
                for (int i = 0; i < built.subMeshCount; i++) mesh.SetTriangles(built.GetTriangles(i), i);
                mesh.uv = built.uv;
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
            }

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { s.concrete, s.floor };
            mr.shadowCastingMode = ShadowCastingMode.TwoSided; // blocks light from neighbouring tunnels
            foreach (var b in boxes)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = b.c;
                bc.size = b.size;
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            go.isStatic = true;

            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
        }

        static TunnelKitSettings LoadOrCreateSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<TunnelKitSettings>(SettingsPath);
            if (s != null) return s;
            EnsureFolder("Assets/_Project/Settings");
            s = ScriptableObject.CreateInstance<TunnelKitSettings>();
            AssetDatabase.CreateAsset(s, SettingsPath);
            return s;
        }

        static void EnsureMaterials(TunnelKitSettings s)
        {
            EnsureFolder(MaterialDir);
            if (s.concrete == null) s.concrete = MakeMaterial("M_Concrete", new Color(0.52f, 0.51f, 0.48f), 0.15f);
            if (s.floor == null) s.floor = MakeMaterial("M_Mud", new Color(0.24f, 0.19f, 0.13f), 0.2f);
            EditorUtility.SetDirty(s);
        }

        static Material MakeMaterial(string name, Color color, float smoothness)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            mat.color = color;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int i = path.LastIndexOf('/');
            string parent = path.Substring(0, i);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(i + 1));
        }

        static void Lamp(Transform parent, Vector3 pos)
        {
            var go = new GameObject("Lamp");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 7f;
            l.intensity = 1.5f;
            l.color = new Color(1f, 0.85f, 0.6f);
            l.shadows = LightShadows.Soft;
        }

        static GameObject SpawnPlayer(Vector3 position)
        {
            foreach (var guid in AssetDatabase.FindAssets("NestedParent_Unpack t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("FirstPersonController")) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                go.name = "Player_FPS";
                go.transform.position = position;
                return go;
            }
            return null;
        }

        // ---------------- mesh builder ----------------

        class MeshBuilder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int>[] tris = { new List<int>(), new List<int>() };

            // Adds a quad whose front side faces 'inside'.
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
            {
                var centre = (a + b + c + d) / 4f;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), inside - centre) < 0) { var tmp = b; b = d; d = tmp; }
                var nrm = Vector3.Cross(b - a, c - a).normalized;
                int i = v.Count;
                foreach (var p in new[] { a, b, c, d }) { v.Add(p); uv.Add(Project(p, nrm)); }
                tris[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
            {
                var centre = (a + b + c) / 3f;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), inside - centre) < 0) { var tmp = b; b = c; c = tmp; }
                var nrm = Vector3.Cross(b - a, c - a).normalized;
                int i = v.Count;
                foreach (var p in new[] { a, b, c }) { v.Add(p); uv.Add(Project(p, nrm)); }
                tris[sub].AddRange(new[] { i, i + 1, i + 2 });
            }

            // Box projection, 1 UV unit = 1 metre, so textures tile evenly across all modules.
            static Vector2 Project(Vector3 p, Vector3 n)
            {
                var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                if (a.y >= a.x && a.y >= a.z) return new Vector2(p.x, p.z);
                if (a.x >= a.z) return new Vector2(p.z, p.y);
                return new Vector2(p.x, p.y);
            }

            public Mesh ToMesh()
            {
                var m = new Mesh();
                m.SetVertices(v);
                m.SetUVs(0, uv);
                m.subMeshCount = 2;
                m.SetTriangles(tris[0], 0);
                m.SetTriangles(tris[1], 1);
                m.RecalculateNormals();
                m.RecalculateTangents();
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
