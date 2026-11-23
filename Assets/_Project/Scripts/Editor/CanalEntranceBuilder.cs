using System.Collections.Generic;
using System.IO;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>
    /// Test scene «вход с каналом»: highway, four tunnel mouths in a concrete headwall (main 12×8 ft octagon + three 10×6 ft);
    /// the smaller ones run parallel, then join the main one under the road; the main tunnel then turns aside to the link point
    /// where the maze will be attached. Outside: a dry channel of concrete slabs that turns left, soil, fences, palms, daylight.
    /// Bridge: call CarX.EditorTools.CanalEntranceBuilder.BuildSilent | UpgradeToSlabs | Shot name x y z yaw pitch | Tune field value | Probe
    /// </summary>
    public static partial class CanalEntranceBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Test/CanalEntrance_Test.unity";
        const string MatDir = "Assets/_Project/Materials/Canal";
        const string TexDir = "Assets/_Project/Textures/Canal";
        const string ModelDir = "Assets/_Project/Models/Canal";
        const string PrefabDir = "Assets/_Project/Prefabs/Canal";
        const string BigType = "Assets/_Project/Tunnels/Types/Octagon.asset";      // «Вегас 12×8 фт»
        const string SmallType = "Assets/_Project/Tunnels/Types/Vegas10x6.asset";  // 10×6 фт

        [MenuItem("CarX/Canal/Тестовая сцена: вход с каналом")]
        static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(BuildSilent());
        }

        [MenuItem("CarX/Canal/Добавить вход с каналом в открытую сцену")]
        static void AddToScene()
        {
            var net = Object.FindAnyObjectByType<TunnelNetwork>();
            var go = new GameObject("CanalEntrance");
            Undo.RegisterCreatedObjectUndo(go, "Вход с каналом");
            var c = go.AddComponent<CanalEntrance>();
            c.network = net;
            AssignAssets(c);
            c.ResetPath();
            Selection.activeGameObject = go;
            Debug.Log("CarX canal: added. Поставьте объект так, чтобы проёмы (узлы-выходы с галкой Exit Open Air) были на его линии Z = 0, затем «Пересоздать русло»");
        }

        [MenuItem("CarX/Canal/Добавить щиты, знаки и ЛЭП в открытую сцену")]
        static void PropsMenu()
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) { EditorUtility.DisplayDialog("Канал", "В сцене нет CanalEntrance.", "Ок"); return; }
            var root = PlaceProps(c);
            Undo.RegisterCreatedObjectUndo(root, "Объекты снаружи");
            Selection.activeGameObject = root;
        }

        public static string BuildSilent()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    return "CarX canal: an open scene has unsaved changes, save it first (scene not built)";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TunnelAssets.ApplyAtmosphere();
            var lib = TunnelGraphAssets.GetLibrary();
            var big = AssetDatabase.LoadAssetAtPath<TunnelType>(BigType);
            var small = AssetDatabase.LoadAssetAtPath<TunnelType>(SmallType);
            if (big == null || small == null) return "CarX canal: tunnel types not found (" + BigType + ", " + SmallType + ")";

            var netGo = new GameObject("TunnelNetwork");
            var net = netGo.AddComponent<TunnelNetwork>();
            net.library = lib;
            net.puddles = false;

            // main tunnel: mouth at the wall, three junctions under the road, then it turns aside to the maze link point
            var m0 = Exit(net, new Vector3(0f, 0f, 0f));
            var j1 = net.AddNode(new Vector3(0f, 0f, -16f));
            var j2 = net.AddNode(new Vector3(0f, 0f, -23f));
            var j3 = net.AddNode(new Vector3(0f, 0f, -30f));
            var bend = net.AddNode(new Vector3(0f, 0f, -37f));
            var link = net.AddNode(new Vector3(-21f, 0f, -49f));
            net.AddEdge(m0.id, j1.id, big);
            net.AddEdge(j1.id, j2.id, big);
            net.AddEdge(j2.id, j3.id, big);
            net.AddEdge(j3.id, bend.id, big);
            net.AddEdge(bend.id, link.id, big);
            // smaller tunnels: straight from the wall, then at ~30-35° into the side wall of the main one
            Side(net, small, new Vector3(5.5f, 0f, 0f), new Vector3(5.5f, 0f, -7f), j1);
            Side(net, small, new Vector3(-5.5f, 0f, 0f), new Vector3(-5.5f, 0f, -14f), j2);
            Side(net, small, new Vector3(11f, 0f, 0f), new Vector3(11f, 0f, -12f), j3);
            net.Build();

            var linkGo = new GameObject("→ Лабиринт (точка стыковки)");
            linkGo.transform.position = net.World(link.position);

            var canalGo = new GameObject("CanalEntrance");
            var canal = canalGo.AddComponent<CanalEntrance>();
            canal.network = net;
            AssignAssets(canal);
            canal.ResetPath();
            canal.ForceRebuild();
            RebuildChannel(canal);

            PlaceProps(canal);
            PlantTrees(canal);

            // player on the channel floor, inside the walkable area, looking at the mouths
            var p = canal.PointAt(Mathf.Min(12f, canal.areaDepth * 0.6f), 0f);
            var q = canal.PointAt(0f, 0f);
            var dir = new Vector3(q.x - p.x, 0f, q.y - p.y).normalized;
            var player = TunnelMazeBuilder.SpawnPlayer(new Vector3(p.x, 0.3f, p.y), dir);
            var cam = player != null ? player.GetComponentInChildren<Camera>(true) : null;
            if (cam != null) { cam.farClipPlane = Mathf.Max(cam.farClipPlane, 1500f); EditorUtility.SetDirty(cam); }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            return "CarX canal: test scene built, " + net.nodes.Count + " nodes, issues " + net.issues.Count + " -> " + ScenePath;
        }

        static GraphNode Exit(TunnelNetwork net, Vector3 p)
        {
            var n = net.AddNode(p);
            n.kind = GraphNodeKind.Exit;
            n.exitBrightness = 0.35f;   // open air: daylight enters from the sky, not a floodlight
            n.exitOpenAir = true;
            return n;
        }

        static void Side(TunnelNetwork net, TunnelType type, Vector3 mouth, Vector3 bend, GraphNode join)
        {
            var a = Exit(net, mouth);
            var b = net.AddNode(bend);
            net.AddEdge(a.id, b.id, type);
            net.AddEdge(b.id, join.id, type);
        }

        // ------------------------------------------------------------------ channel (slabs, floor, soil, fences, grass)

        /// <summary>Builds the channel again and keeps its meshes in «&lt;scene&gt;_Channel.asset» next to the scene.</summary>
        public static void RebuildChannel(CanalEntrance c)
        {
            if (c.groundBlend == null || c.chainLink == null || c.grassTuft == null || c.dark == null) AssignAssets(c);
            string path = ChannelAssetPath(c);
            var root = c.BuildChannel();
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            bool first = true;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null || AssetDatabase.Contains(m)) continue;
                if (first) { AssetDatabase.CreateAsset(m, path); first = false; }
                else AssetDatabase.AddObjectToAsset(m, path);
            }
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(c);
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        /// <summary>Grass tufts again from the blue vertex colour (after painting the ground with Polybrush).</summary>
        public static void RegrowGrass(CanalEntrance c)
        {
            string path = ChannelAssetPath(c);
            var oldGrass = new List<Object>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Mesh && o.name == "CanalGrass" && !AssetDatabase.IsMainAsset(o)) oldGrass.Add(o);
            c.GrowGrass();
            foreach (var o in oldGrass) { AssetDatabase.RemoveObjectFromAsset(o); Object.DestroyImmediate(o, true); }
            var root = c.transform.Find(CanalEntrance.ChannelRootName + "/Трава");
            if (root != null && AssetDatabase.LoadMainAssetAtPath(path) != null)
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && !AssetDatabase.Contains(mf.sharedMesh)) AssetDatabase.AddObjectToAsset(mf.sharedMesh, path);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        static string ChannelAssetPath(CanalEntrance c)
        {
            string sp = c.gameObject.scene.path;
            if (string.IsNullOrEmpty(sp)) sp = ScenePath;
            return Path.ChangeExtension(sp, null) + "_Channel.asset";
        }

        /// <summary>
        /// v3 scene (Unity Terrain) -> slab channel, in place, keeping the objects placed by hand: backs the scene up, removes the
        /// terrain, sets the new course, sky and sun, builds the channel, moves objects that would stand inside it, plants trees.
        /// </summary>
        /// <summary>Zenith sun: brighter vertical walls (two sky fills + ground bounce). Bridge entry point.</summary>
        public static string TuneZenithLight()
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) return "no canal";
            Undo.RecordObject(c, "Zenith light");
            c.skyFill = 7f; c.skyFillPitch = 30f; c.skyFillBalance = 0.6f; c.bounce = 20f; c.bouncePitch = 15f;
            c.tunnelConcrete = false;
            EditorUtility.SetDirty(c);
            c.ForceRebuild();
            EditorSceneManager.SaveScene(c.gameObject.scene);
            return "zenith light set";
        }

        /// <summary>Bridge: set a float/bool/colour field on the canal and rebuild, e.g. "SetField skyFill 20" or "SetField skyLightSide 5.5,5.2,4.8".</summary>
        public static string SetField(string name, string value)
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) return "no canal";
            var f = typeof(CanalEntrance).GetField(name);
            if (f == null) return "no field " + name;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Undo.RecordObject(c, "Set " + name);
            if (f.FieldType == typeof(bool)) f.SetValue(c, value == "1" || value == "true");
            else if (f.FieldType == typeof(int)) f.SetValue(c, int.Parse(value));
            else if (f.FieldType == typeof(Color))
            {
                var p = value.Split(',');
                if (p.Length < 3) return "colour needs r,g,b";
                f.SetValue(c, new Color(float.Parse(p[0], inv), float.Parse(p[1], inv), float.Parse(p[2], inv), p.Length > 3 ? float.Parse(p[3], inv) : 1f));
            }
            else f.SetValue(c, float.Parse(value, inv));
            EditorUtility.SetDirty(c);
            c.ForceRebuild();
            return name + " = " + f.GetValue(c);
        }

        /// <summary>Bridge: list lights and the active pipeline.</summary>
        public static string Lights()
        {
            var sb = new System.Text.StringBuilder();
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            sb.Append("rp=" + (rp != null ? rp.name : "null") + " ambient=" + RenderSettings.ambientMode + " " + RenderSettings.ambientLight + " sky=" + RenderSettings.ambientSkyColor + " sun=" + (RenderSettings.sun != null ? RenderSettings.sun.name : "null"));
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional)
                    sb.Append(" | " + l.name + " en=" + (l.enabled && l.gameObject.activeInHierarchy) + " i=" + l.intensity + " mask=" + l.renderingLayerMask + " fwd=" + l.transform.forward + " mode=" + l.lightmapBakeType);
            return sb.ToString();
        }

        public static string UpgradeToSlabs()
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null)
            {
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) return "CarX canal: unsaved scene open, save it first";
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                c = Object.FindAnyObjectByType<CanalEntrance>();
                if (c == null) return "CarX canal: no CanalEntrance in " + ScenePath;
            }
            var scene = c.gameObject.scene;
            string backup = Path.ChangeExtension(scene.path, null) + "_v3backup.unity";
            if (!string.IsNullOrEmpty(scene.path) && AssetDatabase.LoadMainAssetAtPath(backup) == null)
                AssetDatabase.CopyAsset(scene.path, backup);

            // the terrain object goes (its data asset stays for the backup scene)
            foreach (var t in Terrain.activeTerrains)
                if (t != null && t.gameObject.scene == scene) Object.DestroyImmediate(t.gameObject);

            c.channelWidth = 21f; c.channelWidthFar = 14f; c.narrowFrom = 6f; c.narrowTo = 40f; c.channelLength = 110f;
            c.slopeRun = 1.5f; c.concreteOnRight = false; c.areaDepth = 15f; c.floorBumps = 0.2f;
            c.sunPitch = 89f; c.sunColor = new Color(1f, 0.96f, 0.9f); c.skyFillColor = new Color(0.72f, 0.82f, 1f); c.skyFillPitch = 30f; c.skyFill = 7f; c.skyFillBalance = 0.6f; c.bounce = 20f; c.bouncePitch = 15f;
            c.bounceColor = new Color(0.85f, 0.8f, 0.72f);
            c.skyZenith = new Color(4.5f, 9f, 20f); c.skyHorizon = new Color(16f, 18f, 20.5f); c.skyBelow = new Color(12f, 12.5f, 13f);
            c.sunDiscColor = new Color(80f, 76f, 70f); c.cloudCover = 0.25f;
            c.cloudLit = new Color(24f, 24f, 25f); c.cloudShade = new Color(13f, 14f, 16f); c.outsideFogColor = new Color(12f, 12.5f, 13f);
            AssignAssets(c);
            c.ResetPath();
            c.ForceRebuild();
            RebuildChannel(c);

            var props = GameObject.Find(PropsRootName);
            int moved = props != null ? MoveOutOfChannel(c, props.transform) : 0;
            if (props == null) props = PlaceProps(c);
            PlantTrees(c);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "CarX canal: upgraded to slabs, backup " + backup + ", objects moved out of the channel: " + moved;
        }

        /// <summary>Objects (by their root under the props root) that stand inside the channel go out over the crest of the same bank.</summary>
        public static int MoveOutOfChannel(CanalEntrance c, Transform props)
        {
            int moved = 0;
            for (int i = 0; i < props.childCount; i++)
            {
                var t = props.GetChild(i);
                if (t.GetComponent<PowerLine>() != null || t.name == TreesRootName) continue;
                var lp = c.transform.InverseTransformPoint(t.position);
                float s, u;
                c.Project(new Vector2(lp.x, lp.z), out s, out u);
                if (s < -0.5f || s > c.channelLength + 1f) continue;
                float crest = c.CrestAt(s);
                if (Mathf.Abs(u) > crest + 0.6f) continue;
                var p = c.PointAt(s, Mathf.Sign(u == 0f ? 1f : u) * (crest + 1.5f));
                Undo.RecordObject(t, "Объекты из русла");
                t.position = c.transform.TransformPoint(new Vector3(p.x, c.groundHeight, p.y));
                moved++;
            }
            return moved;
        }

        public const string TreesRootName = "Деревья (двигайте, копируйте, удаляйте)";

        /// <summary>Palms, trees and shrubs on top of the banks and behind the road, far from the walkable area (replaces the old ones).</summary>
        public static void PlantTrees(CanalEntrance c)
        {
            if (PackAvailable() && PlantPackTrees(c) > 0) return; // trees from Assets/TreePackVol.1
            var props = GameObject.Find(PropsRootName);
            if (props == null) props = PlaceProps(c);
            var old = props.transform.Find(TreesRootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(TreesRootName);
            Undo.RegisterCreatedObjectUndo(root, "Деревья");
            root.transform.SetParent(props.transform, false);
            if (c.palmPrefab == null || c.tallPalmPrefab == null || c.treePrefab == null || c.shrubPrefab == null) AssignAssets(c);
            var rnd = new System.Random(c.seed * 977 + 1);
            System.Func<float> R = () => (float)rnd.NextDouble();
            float G = c.groundHeight, from = Mathf.Max(c.treesFrom, c.areaDepth + 5f), L = c.channelLength - 4f, cs = c.ConcreteSign;
            System.Action<GameObject, float, float, float> put = (prefab, s, u, scale) =>
            {
                var p = c.PointAt(s, u);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                go.transform.localPosition = new Vector3(p.x, G, p.y);
                go.transform.localRotation = Quaternion.Euler(0f, R() * 360f, 0f);
                go.transform.localScale = Vector3.one * scale;
            };
            // a tree right at the edge of the soil bank, leaning over the slabs
            put(c.treePrefab, from + 12f, -cs * (c.CrestAt(from + 12f) + 0.7f), 1.1f);
            for (int i = 0; i < c.trees; i++)
            {
                float s = Mathf.Lerp(from, L, R());
                put(c.treePrefab, s, -cs * (c.CrestAt(s) + 2f + R() * 5f), 0.8f + 0.4f * R());
            }
            for (int i = 0; i < c.palms; i++)
            {
                float s = Mathf.Lerp(from, L, R()), side = R() < 0.6f ? -cs : cs;
                put(c.palmPrefab, s, side * (c.CrestAt(s) + 2.5f + R() * 5f), 0.85f + 0.3f * R());
            }
            // tall fan palms: on both banks, and a row behind the road (seen over the headwall)
            float xl, xr;
            c.WallSpan(out xl, out xr);
            float behind = -c.wallThickness - c.roadSetback - c.roadWidth - 4f;
            for (int i = 0; i < c.tallPalms; i++)
            {
                float scale = 0.85f + 0.35f * R();
                if (i % 3 == 2)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(c.tallPalmPrefab, root.transform);
                    go.transform.localPosition = new Vector3(Mathf.Lerp(xl - 25f, xr + 25f, R()), G, behind - R() * 3f);
                    go.transform.localRotation = Quaternion.Euler(0f, R() * 360f, 0f);
                    go.transform.localScale = Vector3.one * scale;
                    continue;
                }
                float s = Mathf.Lerp(from, L, R()), side = R() < 0.5f ? -cs : cs;
                put(c.tallPalmPrefab, s, side * (c.CrestAt(s) + 3f + R() * 6f), scale);
            }
            for (int i = 0; i < c.shrubs; i++)
            {
                float s = Mathf.Lerp(from - 4f, L, R()), side = R() < 0.65f ? -cs : cs;
                put(c.shrubPrefab, s, side * (c.CrestAt(s) + 0.6f + R() * 4f), 0.6f + 0.6f * R());
            }
            var oo = props.GetComponent<OutdoorObjects>();
            if (oo != null) oo.Apply();
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        // ------------------------------------------------------------------ bridge helpers

        public static string Tune(string field, float value)
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) return "no canal";
            var f = typeof(CanalEntrance).GetField(field);
            if (f == null) return "no field " + field;
            if (f.FieldType == typeof(int)) f.SetValue(c, Mathf.RoundToInt(value)); else f.SetValue(c, value);
            c.ForceRebuild();
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            return field + " = " + value;
        }

        /// <summary>HDR screenshot (rendered twice, fog/exposure follow the camera with one frame delay).</summary>
        public static string Shot(string name, float x, float y, float z, float yaw, float pitch)
        {
            var m = typeof(TunnelLookDev).GetMethod("Shot", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var args = new object[] { name, new Vector3(x, y, z), yaw, pitch, 70f, false };
            m.Invoke(null, args);
            m.Invoke(null, args);
            return "shot " + name;
        }

        public static string Probe()
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) return "no canal";
            var sb = new System.Text.StringBuilder();
            sb.Append("fog " + RenderSettings.fogDensity + " " + RenderSettings.fogColor);
            var ch = c.transform.Find(CanalEntrance.ChannelRootName);
            sb.Append(" | channel " + (ch != null ? ch.GetComponentsInChildren<MeshFilter>().Length + " pieces" : "none"));
            foreach (var r in c.GetComponentsInChildren<MeshRenderer>(true)) sb.Append(" | " + r.name + " layers=" + r.renderingLayerMask + " mat0=" + (r.sharedMaterial != null ? r.sharedMaterial.name : "null"));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ assets (made once; edit them freely, rebuilding does not overwrite)

        public static void AssignAssets(CanalEntrance c)
        {
            c.concrete = Lit("Canal_Concrete", new Color(0.66f, 0.64f, 0.60f), 0.1f, null, 1f);
            c.asphalt = Lit("Canal_Asphalt", new Color(0.20f, 0.20f, 0.21f), 0.15f, Tex("Asphalt", TexKind.Sand, new Color(0.22f, 0.22f, 0.23f)), 0.5f);
            c.ground = Lit("Canal_Ground", new Color(1f, 1f, 1f), 0.05f, Tex("Sand", TexKind.Sand, new Color(0.74f, 0.66f, 0.52f)), 1f / 6f);
            c.roadLine = Lit("Canal_RoadLine", new Color(0.85f, 0.85f, 0.82f), 0.2f, null, 1f);
            c.skyMaterial = Mat("Canal_Sky", Shader.Find("CarX/SkyDome"));
            c.groundBlend = GroundBlend();
            Texture2D slabA, slabN;
            CanalTextures.GetSlab(false, out slabA, out slabN);
            c.slabs = new[]
            {
                SlabLit("Canal_Slab_A", new Color(0.86f, 0.85f, 0.82f), slabA, slabN),
                SlabLit("Canal_Slab_B", new Color(0.76f, 0.75f, 0.72f), slabA, slabN),
                SlabLit("Canal_Slab_C", new Color(0.93f, 0.91f, 0.86f), slabA, slabN),
            };
            c.sealant = Lit("Canal_Sealant", new Color(0.12f, 0.12f, 0.12f), 0.2f, null, 1f);
            c.chainLink = Cutout("Canal_ChainLink", CanalTextures.ChainLink(false), new Color(0.9f, 0.9f, 0.9f), 0.35f);
            c.grassTuft = Cutout("Canal_GrassTuft", CanalTextures.GrassAtlas(false), Color.white, 0.05f);
            c.fenceSteel = Steel();
            c.dark = Lit("Canal_CulvertDark", new Color(0.035f, 0.035f, 0.035f), 0.1f, null, 1f);
            c.palmPrefab = PalmPrefab();
            c.tallPalmPrefab = TallPalmPrefab();
            c.treePrefab = TreePrefab();
            c.shrubPrefab = ShrubPrefab();
            EditorUtility.SetDirty(c);
        }

        static Material Mat(string name, Shader shader)
        {
            Directory.CreateDirectory(MatDir);
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material Lit(string name, Color color, float smooth, Texture2D tex, float tiling, bool twoSided = false)
        {
            string path = MatDir + "/" + name + ".mat";
            bool fresh = AssetDatabase.LoadAssetAtPath<Material>(path) == null;
            var m = Mat(name, Shader.Find("Universal Render Pipeline/Lit"));
            if (!fresh) return m;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                m.SetTextureScale("_BaseMap", Vector2.one * tiling);
            }
            if (twoSided) { m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; }
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        /// <summary>Slab concrete: UV of the slabs are in metres, the texture covers 4 m.</summary>
        static Material SlabLit(string name, Color tint, Texture2D albedo, Texture2D normal)
        {
            string path = MatDir + "/" + name + ".mat";
            bool fresh = AssetDatabase.LoadAssetAtPath<Material>(path) == null;
            var m = Lit(name, tint, 0.12f, albedo, 0.25f);
            if (!fresh) return m;
            m.SetTexture("_BumpMap", normal);
            m.SetTextureScale("_BumpMap", Vector2.one * 0.25f);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Alpha-tested, two-sided URP Lit (chain-link, grass).</summary>
        static Material Cutout(string name, Texture2D tex, Color color, float smooth)
        {
            string path = MatDir + "/" + name + ".mat";
            bool fresh = AssetDatabase.LoadAssetAtPath<Material>(path) == null;
            var m = Mat(name, Shader.Find("Universal Render Pipeline/Lit"));
            if (!fresh) return m;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);
            m.doubleSidedGI = true;
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        /// <summary>Ground of the channel: shader CarX/CanalGround with our four layers (a layer you gave your own texture is left alone).</summary>
        static Material GroundBlend()
        {
            var m = Mat("Canal_GroundBlend", Shader.Find("CarX/CanalGround"));
            var kinds = new[] { CanalTextures.GroundKind.FineGravel, CanalTextures.GroundKind.CoarseGravel, CanalTextures.GroundKind.Silt, CanalTextures.GroundKind.DryGrass };
            var tiles = new[] { 2.5f, 3f, 4f, 2f };
            for (int i = 0; i < 4; i++)
            {
                var cur = m.GetTexture("_L" + i);
                if (cur != null && !AssetDatabase.GetAssetPath(cur).StartsWith(TexDir + "/Canal_")) continue;
                Texture2D a, n;
                CanalTextures.GetGround(kinds[i], false, out a, out n);
                if (cur == null) m.SetFloat("_T" + i, tiles[i]);
                m.SetTexture("_L" + i, a);
                m.SetTexture("_N" + i, n);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("CarX/Canal/Пересоздать текстуры-заглушки")]
        static void RegenerateTexturesMenu()
        {
            if (!EditorUtility.DisplayDialog("Текстуры канала", "Текстуры-заглушки земли, плит, сетки, травы, щита и знаков будут сгенерированы заново (файлы Canal_*.png в Textures/Canal). Свои текстуры, поставленные в материалы, не тронутся. Продолжить?", "Да", "Отмена")) return;
            Debug.Log(RegenerateTextures());
        }

        public static string RegenerateTextures()
        {
            Texture2D a, n;
            foreach (CanalTextures.GroundKind k in System.Enum.GetValues(typeof(CanalTextures.GroundKind)))
                CanalTextures.GetGround(k, true, out a, out n);
            CanalTextures.GetSlab(true, out a, out n);
            CanalTextures.ChainLink(true);
            CanalTextures.GrassTuft(true);
            CanalTextures.BillboardAd(true);
            CanalTextures.WarningSign(true);
            CanalTextures.Plate(true);
            AssetDatabase.SaveAssets();
            return "CarX canal: placeholder textures regenerated";
        }

        // ------------------------------------------------------------------ outdoor props: billboards, warning signs, power line

        public const string PropsRootName = "Снаружи: объекты (свои кладите сюда)";

        /// <summary>Billboards, warning signs and a power line along the road, under one root that lights its children with the sun.</summary>
        public static GameObject PlaceProps(CanalEntrance c)
        {
            var root = new GameObject(PropsRootName);
            var oo = root.AddComponent<OutdoorObjects>();
            oo.outsideLayer = c.outsideLayer;
            root.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation);
            float G = c.groundHeight, cs = c.ConcreteSign;
            float roadFar = -c.wallThickness - c.roadSetback - c.roadWidth;
            float cx = c.MouthsCentre();
            System.Func<Vector2, Vector3> At = v => new Vector3(v.x, G, v.y);
            System.Func<Vector3, Vector3, Quaternion> Face = (from, to) => { var d = to - from; d.y = 0f; return Quaternion.LookRotation(d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward); };
            var view = At(c.PointAt(10f, 0f));

            // power line along the far side of the road
            var line = new GameObject("ЛЭП (двигайте и копируйте опоры, провода следуют)");
            line.transform.SetParent(root.transform, false);
            var pl = line.AddComponent<PowerLine>();
            pl.wireMaterial = Lit("Canal_Wire", new Color(0.07f, 0.07f, 0.07f), 0.3f, null, 1f);
            var pole = PowerPolePrefab();
            int k = 0;
            for (float x = -270f; x <= 270f; x += 45f)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pole, line.transform);
                go.name = "Опора " + (++k);
                go.transform.localPosition = new Vector3(cx + x, G, roadFar - 3.5f);
                go.transform.localRotation = Quaternion.Euler(0f, 90f + (k % 3 - 1) * 1.5f, (k % 2 == 0 ? 0.8f : -0.6f));
            }

            // billboards: one behind the road (seen above the headwall), one on the inner bank of the bend
            var bb = BillboardPrefab();
            var b1 = (GameObject)PrefabUtility.InstantiatePrefab(bb, root.transform);
            b1.name = "Рекламный щит 1";
            b1.transform.localPosition = new Vector3(cx + 16f, G, roadFar - 16f);
            b1.transform.localRotation = Face(b1.transform.localPosition, view);
            var b2 = (GameObject)PrefabUtility.InstantiatePrefab(bb, root.transform);
            b2.name = "Рекламный щит 2";
            b2.transform.localPosition = At(c.PointAt(34f, cs * (c.CrestAt(34f) + 7f)));
            b2.transform.localRotation = Face(b2.transform.localPosition, view);

            // warning signs at the top of the slopes, facing the channel
            var sign = WarningSignPrefab();
            var spots = new[] { c.PointAt(3f, cs * (c.CrestAt(3f) + 1.2f)), c.PointAt(5f, -cs * (c.CrestAt(5f) + 2f)), c.PointAt(24f, -cs * (c.CrestAt(24f) + 2f)) };
            for (int i = 0; i < spots.Length; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(sign, root.transform);
                go.name = "Знак " + (i + 1);
                go.transform.localPosition = At(spots[i]);
                go.transform.localRotation = Face(go.transform.localPosition, At(c.PointAt(10f, 0f)));
            }
            oo.Apply();
            return root;
        }

        static GameObject SavePrefab(string name, Mesh mesh, Material[] mats, System.Action<GameObject> extra)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.renderingLayerMask = 1u << 3;
            extra?.Invoke(go);
            Directory.CreateDirectory(PrefabDir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/" + name + ".prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static Material Steel() { return Lit("Canal_Steel", new Color(0.45f, 0.46f, 0.47f), 0.35f, null, 1f); }

        static GameObject PowerPolePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/PowerPole.prefab");
            if (existing != null) return existing;
            var kit = new MeshKit(3);
            const float H = 11f, arm = 10.2f;
            kit.Tube(0, new Vector3(0f, -0.6f, 0f), new Vector3(0f, H, 0f), 0.15f, 0.11f, 10, true, 1f);
            kit.Box(0, new Vector3(0f, arm, 0f), new Vector3(1.25f, 0f, 0f), new Vector3(0f, 0.06f, 0f), new Vector3(0f, 0f, 0.05f), 1f);
            kit.Tube(1, new Vector3(0f, arm - 0.8f, 0.06f), new Vector3(-0.7f, arm - 0.04f, 0.06f), 0.015f, 0.015f, 4, true, 1f);
            kit.Tube(1, new Vector3(0f, arm - 0.8f, 0.06f), new Vector3(0.7f, arm - 0.04f, 0.06f), 0.015f, 0.015f, 4, true, 1f);
            float[] xs = { -1.1f, -0.45f, 1.1f };
            foreach (var x in xs)
            {
                kit.Tube(2, new Vector3(x, arm + 0.06f, 0f), new Vector3(x, arm + 0.3f, 0f), 0.035f, 0.03f, 8, true, 1f);
                kit.Tube(2, new Vector3(x, arm + 0.12f, 0f), new Vector3(x, arm + 0.15f, 0f), 0.07f, 0.07f, 8, true, 1f);
                kit.Tube(2, new Vector3(x, arm + 0.2f, 0f), new Vector3(x, arm + 0.23f, 0f), 0.065f, 0.065f, 8, true, 1f);
            }
            var mesh = SaveMesh(kit.ToMesh("PowerPole"), "PowerPole");
            var wood = Lit("Canal_PoleWood", new Color(0.36f, 0.30f, 0.24f), 0.05f, null, 1f);
            var ins = Lit("Canal_Insulator", new Color(0.72f, 0.72f, 0.68f), 0.6f, null, 1f);
            return SavePrefab("PowerPole", mesh, new[] { wood, Steel(), ins }, go =>
            {
                for (int i = 0; i < xs.Length; i++)
                {
                    var a = new GameObject("Attach" + i);
                    a.transform.SetParent(go.transform, false);
                    a.transform.localPosition = new Vector3(xs[i], arm + 0.32f, 0f);
                }
                var col = go.AddComponent<CapsuleCollider>();
                col.radius = 0.15f; col.height = H + 0.6f; col.center = new Vector3(0f, (H - 0.6f) * 0.5f, 0f);
            });
        }

        static GameObject BillboardPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Billboard.prefab");
            if (existing != null) return existing;
            var kit = new MeshKit(2);
            const float post = 10f, w = 7.3f, h = 4.3f;
            foreach (var x in new[] { -3.8f, 3.8f })
                kit.Box(0, new Vector3(x, (post + 0.3f - 0.6f) * 0.5f, -0.2f), new Vector3(0.2f, 0f, 0f), new Vector3(0f, (post + 0.3f + 0.6f) * 0.5f, 0f), new Vector3(0f, 0f, 0.2f), 1f);
            kit.Box(0, new Vector3(0f, post + h * 0.5f, -0.18f), new Vector3(w + 0.1f, 0f, 0f), new Vector3(0f, h * 0.5f + 0.1f, 0f), new Vector3(0f, 0f, 0.17f), 1f);
            kit.Box(0, new Vector3(0f, post - 0.25f, 0.55f), new Vector3(w, 0f, 0f), new Vector3(0f, 0.03f, 0f), new Vector3(0f, 0f, 0.55f), 1f);
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 4.6f;
                kit.Tube(0, new Vector3(x, post - 0.22f, 1.05f), new Vector3(x, post + 0.25f, 1.35f), 0.03f, 0.03f, 5, true, 1f);
                kit.Box(0, new Vector3(x, post + 0.3f, 1.4f), new Vector3(0.25f, 0f, 0f), new Vector3(0f, 0.08f, 0f), new Vector3(0f, 0f, 0.15f), 1f);
            }
            kit.Quad(1, new Vector3(-w, post, 0.005f), new Vector3(w, post, 0.005f), new Vector3(w, post + h, 0.005f), new Vector3(-w, post + h, 0.005f),
                     new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector3.forward);
            var mesh = SaveMesh(kit.ToMesh("Billboard"), "Billboard");
            var face = Lit("Canal_BillboardFace", Color.white, 0.25f, CanalTextures.BillboardAd(false), 1f);
            return SavePrefab("Billboard", mesh, new[] { Steel(), face }, go =>
            {
                foreach (var x in new[] { -3.8f, 3.8f })
                {
                    var b = go.AddComponent<BoxCollider>();
                    b.center = new Vector3(x, post * 0.5f, -0.2f); b.size = new Vector3(0.4f, post, 0.4f);
                }
            });
        }

        static GameObject WarningSignPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/WarningSign.prefab");
            if (existing != null) return existing;
            var kit = new MeshKit(3);
            kit.Tube(0, new Vector3(0f, -0.4f, -0.04f), new Vector3(0f, 2.65f, -0.04f), 0.03f, 0.03f, 8, true, 1f);
            const float cy = 2.2f, r = 0.43f, z = 0.0f;
            Vector3 top = new Vector3(0f, cy + r, z), right = new Vector3(r, cy, z), bottom = new Vector3(0f, cy - r, z), left = new Vector3(-r, cy, z);
            kit.Quad(1, left, bottom, right, top, new Vector2(0f, 0.5f), new Vector2(0.5f, 0f), new Vector2(1f, 0.5f), new Vector2(0.5f, 1f), Vector3.forward);
            var off = new Vector3(0f, 0f, -0.006f);
            kit.Quad(0, left + off, bottom + off, right + off, top + off, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.back);
            const float pw = 0.32f, py0 = 1.4f, py1 = 1.84f;
            kit.Quad(2, new Vector3(-pw, py0, z), new Vector3(pw, py0, z), new Vector3(pw, py1, z), new Vector3(-pw, py1, z),
                     new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector3.forward);
            kit.Quad(0, new Vector3(-pw, py0, z - 0.006f), new Vector3(pw, py0, z - 0.006f), new Vector3(pw, py1, z - 0.006f), new Vector3(-pw, py1, z - 0.006f),
                     Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.back);
            var mesh = SaveMesh(kit.ToMesh("WarningSign"), "WarningSign");
            var warn = Lit("Canal_WarningSign", Color.white, 0.35f, CanalTextures.WarningSign(false), 1f);
            var plate = Lit("Canal_SignPlate", Color.white, 0.35f, CanalTextures.Plate(false), 1f);
            return SavePrefab("WarningSign", mesh, new[] { Steel(), warn, plate }, go =>
            {
                var col = go.AddComponent<CapsuleCollider>();
                col.radius = 0.05f; col.height = 2.6f; col.center = new Vector3(0f, 1.2f, -0.04f);
            });
        }

        // ------------------------------------------------------------------ palm and shrub

        static GameObject PalmPrefab()
        {
            string path = PrefabDir + "/Palm.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var mesh = SaveMesh(PalmMesh(), "Palm");
            var trunk = Lit("Canal_PalmTrunk", new Color(0.46f, 0.39f, 0.31f), 0.05f, null, 1f);
            var leaf = Lit("Canal_PalmLeaf", new Color(0.30f, 0.38f, 0.17f), 0.1f, null, 1f, true);
            var go = new GameObject("Palm");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { trunk, leaf };
            mr.renderingLayerMask = 1u << 3;
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.3f; col.height = 8f; col.center = new Vector3(0.3f, 4f, 0f);
            Directory.CreateDirectory(PrefabDir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject ShrubPrefab()
        {
            string path = PrefabDir + "/DryShrub.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var kit = new MeshKit(1);
            var rnd = new System.Random(11);
            for (int b = 0; b < 3; b++)
            {
                var c = new Vector3(((float)rnd.NextDouble() - 0.5f) * 0.8f, 0.3f, ((float)rnd.NextDouble() - 0.5f) * 0.8f);
                float r = 0.5f + (float)rnd.NextDouble() * 0.3f;
                Blob(kit, 0, c, new Vector3(r, r * 0.65f, r), rnd);
            }
            var mesh = SaveMesh(kit.ToMesh("DryShrub"), "DryShrub");
            var mat = Lit("Canal_Brush", new Color(0.46f, 0.42f, 0.26f), 0.05f, null, 1f);
            var go = new GameObject("DryShrub");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.renderingLayerMask = 1u << 3;
            Directory.CreateDirectory(PrefabDir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Tall thin fan palm (the Las Vegas street palm): ~15 m trunk, compact crown, a skirt of dead fronds.</summary>
        static GameObject TallPalmPrefab()
        {
            string path = PrefabDir + "/PalmTall.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var kit = new MeshKit(3);
            const float H = 15f, lean = 0.5f;
            const int segs = 14;
            System.Func<float, Vector3> C = t => new Vector3(lean * t * t, H * t, 0f);
            for (int i = 0; i < segs; i++)
            {
                float t0 = (float)i / segs, t1 = (float)(i + 1) / segs;
                float r0 = Mathf.Lerp(0.34f, 0.2f, Mathf.Sqrt(t0)), r1 = Mathf.Lerp(0.34f, 0.2f, Mathf.Sqrt(t1));
                kit.Tube(0, C(t0), C(t1), r0, r1, 8, true, 1f);
            }
            var top = C(1f);
            // skirt of dry fronds hanging under the crown
            kit.Tube(2, top + Vector3.down * 2.8f, top + Vector3.down * 0.9f, 0.42f, 0.62f, 10, true, 1f);
            kit.Tube(2, top + Vector3.down * 0.9f, top + Vector3.down * 0.2f, 0.62f, 0.4f, 10, true, 1f);
            var rnd = new System.Random(17);
            const int fronds = 16, n = 6;
            for (int f = 0; f < fronds; f++)
            {
                float a = f * Mathf.PI * 2f / fronds + ((float)rnd.NextDouble() - 0.5f) * 0.3f;
                float L = 1.9f + (float)rnd.NextDouble() * 0.7f;
                float rise = 0.5f + (float)rnd.NextDouble() * 0.6f, droop = 0.5f + (float)rnd.NextDouble() * 0.6f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, d).normalized;
                Vector3 prevC = top, prevL = top, prevR = top;
                for (int k = 1; k <= n; k++)
                {
                    float t = (float)k / n;
                    var c = top + d * (L * t) + Vector3.up * (L * (rise * t - droop * t * t));
                    float w = 0.75f * Mathf.Pow(Mathf.Clamp01(t * 1.3f), 0.8f) * (t > 0.85f ? 0.6f : 1f);   // fan leaf: widest at the end
                    var l = c - side * w;
                    var r = c + side * w;
                    kit.Quad(1, prevC, prevL, l, c, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
                    kit.Quad(1, prevR, prevC, c, r, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
                    prevC = c; prevL = l; prevR = r;
                }
            }
            var mesh = SaveMesh(kit.ToMesh("PalmTall"), "PalmTall");
            var trunk = Lit("Canal_PalmTrunk", new Color(0.46f, 0.39f, 0.31f), 0.05f, null, 1f);
            var leaf = Lit("Canal_PalmLeafFan", new Color(0.33f, 0.42f, 0.20f), 0.15f, null, 1f, true);
            var skirt = Lit("Canal_PalmSkirt", new Color(0.45f, 0.36f, 0.24f), 0.05f, null, 1f);
            return SavePrefab("PalmTall", mesh, new[] { trunk, leaf, skirt }, go =>
            {
                var col = go.AddComponent<CapsuleCollider>();
                col.radius = 0.3f; col.height = H; col.center = new Vector3(0.2f, H * 0.5f, 0f);
            });
        }

        /// <summary>Broadleaf desert tree (mesquite / ash look): short trunk, three limbs, a dark olive crown of lumps.</summary>
        static GameObject TreePrefab()
        {
            string path = PrefabDir + "/Tree.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var kit = new MeshKit(2);
            var rnd = new System.Random(23);
            System.Func<float> R = () => (float)rnd.NextDouble();
            var fork = new Vector3(0.1f, 2.6f, 0f);
            kit.Tube(0, new Vector3(0f, -0.3f, 0f), fork, 0.24f, 0.17f, 9, true, 1f);
            for (int b = 0; b < 3; b++)
            {
                float a = b * Mathf.PI * 2f / 3f + R() * 0.6f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var end = fork + d * (1.4f + R() * 0.8f) + Vector3.up * (1.8f + R() * 1.0f);
                kit.Tube(0, fork, end, 0.13f, 0.07f, 7, true, 1f);
                for (int k = 0; k < 3; k++)
                {
                    var c = end + new Vector3(R() - 0.5f, R() * 0.8f, R() - 0.5f) * 1.6f;
                    float r = 1.1f + R() * 0.6f;
                    Blob(kit, 1, c, new Vector3(r, r * 0.7f, r), rnd);
                }
            }
            Blob(kit, 1, fork + Vector3.up * 3.6f, new Vector3(1.6f, 1.1f, 1.6f), rnd);
            var mesh = SaveMesh(kit.ToMesh("Tree"), "Tree");
            var bark = Lit("Canal_TreeBark", new Color(0.33f, 0.29f, 0.25f), 0.05f, null, 1f);
            var leaves = Lit("Canal_TreeLeaves", new Color(0.24f, 0.31f, 0.15f), 0.1f, null, 1f);
            return SavePrefab("Tree", mesh, new[] { bark, leaves }, go =>
            {
                var col = go.AddComponent<CapsuleCollider>();
                col.radius = 0.25f; col.height = 3f; col.center = new Vector3(0f, 1.3f, 0f);
            });
        }

        static Mesh SaveMesh(Mesh m, string name)
        {
            Directory.CreateDirectory(ModelDir);
            m.hideFlags = HideFlags.None;
            m.name = name;
            AssetDatabase.CreateAsset(m, ModelDir + "/" + name + ".asset");
            return m;
        }

        /// <summary>Palm: slightly bent trunk ~8 m, crown of drooping fronds (sub-mesh 0 trunk, 1 leaves).</summary>
        static Mesh PalmMesh()
        {
            var kit = new MeshKit(2);
            const float H = 8f, lean = 0.7f;
            const int segs = 10;
            System.Func<float, Vector3> C = t => new Vector3(lean * t * t, H * t, 0f);
            for (int i = 0; i < segs; i++)
            {
                float t0 = (float)i / segs, t1 = (float)(i + 1) / segs;
                float r0 = Mathf.Lerp(0.3f, 0.17f, t0) * (i % 2 == 0 ? 1f : 0.93f), r1 = Mathf.Lerp(0.3f, 0.17f, t1);
                kit.Tube(0, C(t0), C(t1), r0, r1, 8, true, 1f);
            }
            var top = C(1f);
            var rnd = new System.Random(7);
            const int fronds = 11, n = 9;
            for (int f = 0; f < fronds; f++)
            {
                float a = f * Mathf.PI * 2f / fronds + ((float)rnd.NextDouble() - 0.5f) * 0.4f;
                float L = 3.2f + (float)rnd.NextDouble() * 0.8f;
                float rise = 0.35f + (float)rnd.NextDouble() * 0.45f, droop = 0.9f + (float)rnd.NextDouble() * 0.5f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, d).normalized;
                Vector3 prevC = top, prevL = top, prevR = top;
                for (int k = 1; k <= n; k++)
                {
                    float t = (float)k / n;
                    var c = top + d * (L * t) + Vector3.up * (L * (rise * t - droop * t * t));
                    float w = 0.6f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.1f))), 0.6f);
                    var l = c - side * w + Vector3.down * 0.25f * w;
                    var r = c + side * w + Vector3.down * 0.25f * w;
                    kit.Quad(1, prevC, prevL, l, c, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
                    kit.Quad(1, prevR, prevC, c, r, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
                    prevC = c; prevL = l; prevR = r;
                }
            }
            return kit.ToMesh("Palm");
        }

        static void Blob(MeshKit kit, int sub, Vector3 c, Vector3 r, System.Random rnd)
        {
            const int lat = 6, lon = 9;
            float off = (float)rnd.NextDouble() * 100f;
            var pts = new Vector3[lat + 1, lon];
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    float th = Mathf.PI * i / lat, ph = Mathf.PI * 2f * j / lon;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    float k = 1f + 0.25f * (Mathf.PerlinNoise(off + d.x * 1.7f + d.y, off + d.z * 1.7f - d.y) - 0.5f) * 2f;
                    if (i == 0 || i == lat) k = 1f;
                    pts[i, j] = c + Vector3.Scale(d, r) * k;
                }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int j1 = (j + 1) % lon;
                    Vector3 a = pts[i, j], b = pts[i, j1], d2 = pts[i + 1, j1], e = pts[i + 1, j];
                    var mid = (a + b + d2 + e) * 0.25f;
                    if (i == 0) kit.Tri(sub, a, d2, e, Vector2.zero, Vector2.right, Vector2.up, mid - c);
                    else if (i == lat - 1) kit.Tri(sub, a, b, e, Vector2.zero, Vector2.right, Vector2.up, mid - c);
                    else kit.Quad(sub, a, b, d2, e, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, mid - c);
                }
        }

        // ------------------------------------------------------------------ procedural tileable textures (placeholders: swap for real ones)

        enum TexKind { Gravel, Riprap, Sand, Dirt }

        static Texture2D Tex(string name, TexKind kind, Color baseColor)
        {
            string path = TexDir + "/Canal_" + name + ".png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            const int N = 512;
            var px = new Color32[N * N];
            var rnd = new System.Random(name.GetHashCode());
            int cells = kind == TexKind.Gravel ? 48 : kind == TexKind.Riprap ? 7 : 8;
            var jitter = new Vector2[cells, cells];
            var tone = new float[cells, cells];
            for (int i = 0; i < cells; i++)
                for (int j = 0; j < cells; j++)
                {
                    jitter[i, j] = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
                    tone[i, j] = (float)rnd.NextDouble();
                }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N;
                    float f1, f2, t;
                    Voronoi(u * cells, v * cells, cells, jitter, tone, out f1, out f2, out t);
                    float low = PNoise(u * 4f, v * 4f, 4, 1) * 0.6f + PNoise(u * 16f, v * 16f, 16, 2) * 0.4f;
                    float fine = PNoise(u * 128f, v * 128f, 128, 3);
                    Color c;
                    switch (kind)
                    {
                        case TexKind.Gravel:
                        {
                            float stone = Mathf.Clamp01((f2 - f1) * 6f);
                            float k = 0.8f + (t - 0.5f) * 0.45f + (low - 0.5f) * 0.25f + (fine - 0.5f) * 0.1f;
                            k *= Mathf.Lerp(0.7f, 1f, stone);
                            var tint = Color.Lerp(new Color(1f, 0.97f, 0.92f), new Color(0.9f, 0.92f, 0.95f), t);
                            c = baseColor * tint * k;
                            break;
                        }
                        case TexKind.Riprap:
                        {
                            float gap = Mathf.Clamp01((f2 - f1) * 3.5f);
                            float k = (0.75f + (t - 0.5f) * 0.5f + (fine - 0.5f) * 0.15f) * Mathf.Lerp(0.25f, 1f, Mathf.Sqrt(gap));
                            // bulge: lighter in the middle of a stone
                            k *= Mathf.Lerp(0.85f, 1.1f, Mathf.Clamp01(1f - f1 * 1.6f));
                            var tint = Color.Lerp(new Color(1.05f, 0.98f, 0.9f), new Color(0.88f, 0.9f, 0.95f), t);
                            c = baseColor * tint * k;
                            // wire mesh: thin diamond lines
                            float m1 = Mathf.Abs(Mathf.Repeat((u + v) * 10f, 1f) - 0.5f), m2 = Mathf.Abs(Mathf.Repeat((u - v) * 10f, 1f) - 0.5f);
                            float wire = Mathf.Max(Smooth(0.47f, 0.5f, m1), Smooth(0.47f, 0.5f, m2));
                            c = Color.Lerp(c, new Color(0.42f, 0.42f, 0.42f), wire * 0.6f);
                            break;
                        }
                        case TexKind.Sand:
                        {
                            float k = 0.92f + (low - 0.5f) * 0.18f + (fine - 0.5f) * 0.12f;
                            c = baseColor * k;
                            break;
                        }
                        default:
                        {
                            float k = 0.85f + (low - 0.5f) * 0.4f + (fine - 0.5f) * 0.15f + (t - 0.5f) * 0.1f;
                            c = baseColor * k;
                            break;
                        }
                    }
                    px[y * N + x] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
                }
            var t2 = new Texture2D(N, N, TextureFormat.RGBA32, true);
            t2.SetPixels32(px);
            t2.Apply();
            Directory.CreateDirectory(TexDir);
            File.WriteAllBytes(path, t2.EncodeToPNG());
            Object.DestroyImmediate(t2);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.sRGBTexture = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Tileable Voronoi: distances to the nearest and second nearest feature point, tone of the nearest cell.</summary>
        static void Voronoi(float x, float y, int n, Vector2[,] jitter, float[,] tone, out float f1, out float f2, out float t)
        {
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            f1 = f2 = 9f; t = 0f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = cx + dx, gy = cy + dy;
                    int wx = ((gx % n) + n) % n, wy = ((gy % n) + n) % n;
                    var p = new Vector2(gx + jitter[wx, wy].x, gy + jitter[wx, wy].y);
                    float d = Vector2.Distance(p, new Vector2(x, y));
                    if (d < f1) { f2 = f1; f1 = d; t = tone[wx, wy]; }
                    else if (d < f2) f2 = d;
                }
        }

        /// <summary>Tileable value noise with the given period (in lattice cells).</summary>
        static float PNoise(float x, float y, int period, int salt)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0, y0, period, salt), b = Hash(x0 + 1, y0, period, salt), c = Hash(x0, y0 + 1, period, salt), d = Hash(x0 + 1, y0 + 1, period, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash(int x, int y, int period, int salt)
        {
            x = ((x % period) + period) % period; y = ((y % period) + period) % period;
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
