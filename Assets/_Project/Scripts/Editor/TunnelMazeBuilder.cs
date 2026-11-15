using System.Collections.Generic;
using CarX.Gameplay;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>Demo: random maze of junction chambers joined by curved tunnels of different sections, with rises to the surface.</summary>
    public static class TunnelMazeBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/TunnelMaze_Demo.unity";

        [MenuItem("CarX/Tunnels/Build Random Maze Demo", priority = 20)]
        static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(Random.Range(1, 99999));
        }

        /// <summary>Entry point for automation: saves open scenes, builds and saves the demo scene.</summary>
        public static string BuildSilent(int seed)
        {
            EditorSceneManager.SaveOpenScenes();
            return Build(seed);
        }

        static float Range(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

        static string Build(int seed)
        {
            var mats = TunnelAssets.Get(false);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TunnelAssets.ApplyAtmosphere();
            var rnd = new System.Random(seed);
            const int N = 5;
            const float D = 20f, S = 4.8f, H = 3.3f;
            float[] levels = { 0f, -0.35f, -0.7f };

            var level = new float[N, N];
            for (int x = 0; x < N; x++)
                for (int z = 0; z < N; z++)
                    level[x, z] = levels[rnd.Next(levels.Length)];
            level[0, 0] = 0f;

            // maze: depth-first spanning tree plus a few loops
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            var edges = new List<int[]>();
            var seen = new bool[N, N];
            var stack = new Stack<Vector2Int>();
            stack.Push(Vector2Int.zero);
            seen[0, 0] = true;
            while (stack.Count > 0)
            {
                var c = stack.Peek();
                var options = new List<Vector2Int>();
                foreach (var d in dirs)
                {
                    var nb = c + d;
                    if (nb.x >= 0 && nb.x < N && nb.y >= 0 && nb.y < N && !seen[nb.x, nb.y]) options.Add(nb);
                }
                if (options.Count == 0) { stack.Pop(); continue; }
                var next = options[rnd.Next(options.Count)];
                seen[next.x, next.y] = true;
                edges.Add(new[] { c.x, c.y, next.x, next.y });
                stack.Push(next);
            }
            int loops = 0, guard = 0;
            while (loops < 4 && guard++ < 300)
            {
                int x = rnd.Next(N), z = rnd.Next(N);
                var d = dirs[rnd.Next(4)];
                int x2 = x + d.x, z2 = z + d.y;
                if (x2 < 0 || x2 >= N || z2 < 0 || z2 >= N) continue;
                bool exists = false;
                foreach (var e in edges)
                    if ((e[0] == x && e[1] == z && e[2] == x2 && e[3] == z2) || (e[0] == x2 && e[1] == z2 && e[2] == x && e[3] == z)) { exists = true; break; }
                if (exists) continue;
                edges.Add(new[] { x, z, x2, z2 });
                loops++;
            }

            var root = new GameObject("TunnelMaze");
            var chambers = new TunnelJunction[N, N];
            int shaftCount = 0;
            for (int x = 0; x < N; x++)
                for (int z = 0; z < N; z++)
                {
                    var go = new GameObject("Chamber_" + x + "_" + z);
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = new Vector3(x * D, level[x, z], z * D);
                    var j = go.AddComponent<TunnelJunction>();
                    j.sizeX = S;
                    j.sizeZ = S;
                    j.height = H;
                    j.materials = mats;
                    j.seed = rnd.Next(1, 99999);
                    if (!(x == 0 && z == 0) && rnd.NextDouble() < 0.22)
                    {
                        j.hasShaft = true;
                        j.shaft = ShaftSpec.Preset(ShaftType.GrateSquare, 0f);
                        j.shaft.width = 1.3f;
                        j.shaft.height = 2.4f;
                        shaftCount++;
                    }
                    chambers[x, z] = j;
                }

            Vector3 firstDir = Vector3.forward;
            var shaftTypes = new[] { ShaftType.GrateLong, ShaftType.GrateLong, ShaftType.GrateSquare, ShaftType.Manhole, ShaftType.Manhole };
            for (int idx = 0; idx < edges.Count; idx++)
            {
                var e = edges[idx];
                var A = chambers[e[0], e[1]];
                var B = chambers[e[2], e[3]];
                Vector3 pa = A.transform.position, pb = B.transform.position;
                var dir = new Vector3(e[2] - e[0], 0f, e[3] - e[1]);
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                Vector3 p0 = pa + dir * (S * 0.5f);
                Vector3 pn = pb - dir * (S * 0.5f);
                Vector3 p1 = p0 + dir * 2f;
                Vector3 pn1 = pn - dir * 2f;
                var pts = new List<Vector3> { p0, p1 };
                int mids = rnd.Next(1, 3);
                for (int m = 1; m <= mids; m++)
                {
                    float t = (float)m / (mids + 1);
                    Vector3 q = Vector3.Lerp(p1, pn1, t) + side * Range(rnd, -1.1f, 1.1f);
                    pts.Add(q);
                }
                pts.Add(pn1);
                pts.Add(pn);

                var pgo = new GameObject("Tunnel_" + idx);
                pgo.transform.SetParent(root.transform, false);
                pgo.transform.position = p0;
                for (int i = 0; i < pts.Count; i++)
                {
                    var pt = new GameObject("Point_" + i);
                    pt.transform.SetParent(pgo.transform, false);
                    pt.transform.position = pts[i];
                }
                var path = pgo.AddComponent<TunnelPath>();
                path.materials = mats;
                path.section = RandomSection(rnd, idx == 0);
                path.straightEnds = true;
                path.startAtWall = true;
                path.endAtWall = true;
                path.seed = rnd.Next(1, 99999);
                bool canShaft = path.section.profile != TunnelProfile.Round;
                if (canShaft && (idx == 0 || rnd.NextDouble() < 0.42))
                {
                    var type = idx == 0 ? ShaftType.GrateLong : shaftTypes[rnd.Next(shaftTypes.Length)];
                    float span = Vector3.Distance(p0, pn);
                    float dist = idx == 0 ? 7f : span * Range(rnd, 0.35f, 0.65f);
                    var spec = ShaftSpec.Preset(type, dist);
                    if (type == ShaftType.Manhole && rnd.NextDouble() < 0.5) spec.lateral = -10f;
                    path.shafts.Add(spec);
                    shaftCount++;
                }
                A.SetSide(TunnelJunction.SideOf(dir), path);
                B.SetSide(TunnelJunction.SideOf(-dir), path);
                path.Rebuild(true);
                TunnelDressing.Scatter(path, rnd.Next(1, 99999), false);
                if (idx == 0) firstDir = dir;
            }
            foreach (var j in chambers) j.Rebuild(true);

            Vector3 spawn = chambers[0, 0].transform.position + Vector3.up * 0.05f - firstDir * 0.8f;
            SpawnPlayer(spawn, firstDir);

            EditorSceneManager.SaveScene(scene, ScenePath);
            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.pivot = spawn + firstDir * 7f + Vector3.up * 1.4f;
                view.rotation = Quaternion.LookRotation(firstDir + Vector3.down * 0.05f);
                view.size = 3f;
                view.Repaint();
            }
            string msg = "CarX maze: seed " + seed + ", chambers " + (N * N) + ", corridors " + edges.Count + ", shafts " + shaftCount;
            Debug.Log(msg);
            return msg;
        }

        static TunnelSection RandomSection(System.Random rnd, bool main)
        {
            var s = new TunnelSection();
            double r = rnd.NextDouble();
            if (main || r < 0.35) { s.profile = TunnelProfile.Octagon; s.width = 3f; s.height = 2.4f; s.chamfer = 0.3f; }
            else if (r < 0.58) { s.profile = TunnelProfile.BoxWithCurbs; s.width = 3.2f; s.height = 2.5f; s.curbWidth = 0.5f; s.curbHeight = 0.16f; }
            else if (r < 0.74) { s.profile = TunnelProfile.Box; s.width = 2.4f; s.height = 2.4f; }
            else if (r < 0.88) { s.profile = TunnelProfile.Box; s.width = 3.4f; s.height = 2.1f; }
            else { s.profile = TunnelProfile.Round; s.diameter = 2.2f; s.silt = 0.22f; }
            return s;
        }

        public static GameObject SpawnPlayer(Vector3 pos, Vector3 dir)
        {
            string prefabPath = null;
            foreach (var g in AssetDatabase.FindAssets("NestedParent_Unpack t:Prefab"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("FirstPersonController")) { prefabPath = p; break; }
            }
            GameObject player = null;
            Camera cam = null;
            if (prefabPath != null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var cc = player.GetComponentInChildren<CharacterController>(true);
                if (cc != null)
                {
                    cc.transform.position = pos;
                    cc.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                }
                else player.transform.position = pos;
                cam = player.GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    cam.transform.position = pos + Vector3.up * 1.4f;
                    cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                }
            }
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera");
                cgo.tag = "MainCamera";
                cam = cgo.AddComponent<Camera>();
                cgo.transform.position = pos + Vector3.up * 1.6f;
                cgo.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                player = cgo;
            }
            TunnelAssets.SetupCamera(cam);
            cam.nearClipPlane = 0.05f;
            AddFlashlight(cam.transform);
            return player;
        }

        public static Light AddFlashlight(Transform parent)
        {
            var fl = new GameObject("Flashlight");
            fl.transform.SetParent(parent, false);
            fl.transform.localPosition = new Vector3(0.28f, -0.3f, 0.1f);   // in the hand: light from the side keeps the wall relief
            var light = fl.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 28f;
            light.spotAngle = 62f;
            light.innerSpotAngle = 20f;
            light.intensity = 16f;
            light.color = new Color(1f, 0.94f, 0.84f);
            light.shadows = LightShadows.Soft;
            light.shadowNearPlane = 0.1f;
            var cookie = TunnelAssets.Cookie();
            if (cookie != null) light.cookie = cookie;
            fl.AddComponent<Flashlight>();
            return light;
        }
    }
}
