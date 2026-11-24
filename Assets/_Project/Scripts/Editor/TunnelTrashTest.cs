using System.Collections.Generic;
using System.Text;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarX.EditorTools
{
    /// <summary>Bridge test for the trash tool: call CarX.EditorTools.TunnelTrashTest.Run, then .Close.
    /// Builds a throw-away additive scene (open scenes are not touched).</summary>
    public static class TunnelTrashTest
    {
        static Scene scene;
        const string ScenePath = "Assets/_Project/Scenes/Test/TrashTest.unity";
        static readonly Vector3 Origin = new Vector3(0f, -400f, 0f);

        public static string Run()
        {
            TunnelTrash.Report();
            Close();
            // an empty scene file opened additively (NewScene refuses while an untitled scene is open)
            if (!System.IO.File.Exists(ScenePath))
            {
                System.IO.File.WriteAllText(ScenePath, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
                AssetDatabase.ImportAsset(ScenePath);
            }
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var go = new GameObject("TrashTestNetwork");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = Origin;
            var net = go.AddComponent<TunnelNetwork>();
            net.library = TunnelGraphAssets.GetLibrary();
            var T = net.library.types[0];
            // dead end - straight - bend - room, and a branch
            var n0 = net.AddNode(new Vector3(0, 0, 0));
            var n1 = net.AddNode(new Vector3(24, 0, 0));
            var n2 = net.AddNode(new Vector3(44, 0, 10));
            var n3 = net.AddNode(new Vector3(64, 0, 10));
            n3.kind = GraphNodeKind.Room;
            n3.roomCorners = GraphNode.DefaultRoom();
            var e01 = net.AddEdge(n0.id, n1.id, T);
            var e12 = net.AddEdge(n1.id, n2.id, T);
            var e23 = net.AddEdge(n2.id, n3.id, T);
            e01.trash = 5; e01.trashSeed = 11;
            e12.trash = 3; e12.trashSeed = 12;
            n3.trash = 5; n3.trashSeed = 13;
            net.Layout();
            net.Build();
            var sb = new StringBuilder("trash test: ");
            var root = go.transform.Find(TunnelTrash.RootName);
            if (root == null) return "FAIL no root (ready " + TunnelTrash.Ready + ")";
            var before = Snapshot(root);
            sb.Append("objects " + before.Count);
            int bad = 0;
            foreach (var kv in before)
            {
                var p = net.Local(kv.Value);
                GraphEdge e; float d;
                bool ok = net.RoomAt(p, 0.6f) != null || net.ClosestOnTunnels(p, 0.6f, out e, out d);
                if (!ok) { bad++; if (bad < 5) sb.Append("\n  outside: " + kv.Key + " " + p); }
            }
            sb.Append(", outside " + bad);
            int cols = 0, boxes = 0, found = 0, objs = 0;
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) if (c.enabled) cols++;
            foreach (var g in root.GetComponentsInChildren<TunnelTrashGroup>())
            {
                boxes += g.steps.Count;
                foreach (Transform it in g.transform)
                {
                    objs++;
                    var rs = it.GetComponentsInChildren<Renderer>();
                    var pos = rs.Length > 0 ? rs[0].bounds.center : it.position;
                    if (g.BoxAt(new Vector3(pos.x, net.World(Vector3.zero).y + 0.05f, pos.z)) >= 0 || g.BoxAt(it.position) >= 0) found++;
                }
            }
            sb.Append(", enabled colliders " + cols + ", step boxes " + boxes + ", items inside a box " + found + "/" + objs);
            // same data -> same result
            Object.DestroyImmediate(root.gameObject);
            TunnelTrash.Sync(net);
            root = go.transform.Find(TunnelTrash.RootName);
            var again = Snapshot(root);
            sb.Append(", rebuild same " + Same(before, again));
            // one more spot keeps the earlier ones
            e01.trash = 6;
            TunnelTrash.Sync(net);
            var plus = Snapshot(root);
            int kept = 0;
            foreach (var kv in before) if (plus.ContainsKey(kv.Key) && (plus[kv.Key] - kv.Value).sqrMagnitude < 1e-6f) kept++;
            sb.Append(", +1 spot: " + plus.Count + " objects, kept " + kept + "/" + before.Count);
            // new seed moves them
            e01.trashSeed = 99;
            TunnelTrash.Sync(net);
            sb.Append(", new seed changed " + !Same(plus, Snapshot(root)));
            // move the room: its trash follows
            var roomGroup = root.Find("Trash_" + n3.id);
            var r0 = roomGroup.GetChild(0).position;
            n3.position += new Vector3(0, 0, 4);
            net.MarkDirty(); net.Layout(); net.Build();
            var r1 = root.Find("Trash_" + n3.id).GetChild(0).position;
            sb.Append(", room moved by " + (r1 - r0).ToString("0.00"));
            // remove all
            e01.trash = 0; e12.trash = 0; n3.trash = 0;
            TunnelTrash.Sync(net);
            sb.Append(", cleared " + (go.transform.Find(TunnelTrash.RootName) == null));
            // back for the picture
            e01.trash = 6; e01.trashSeed = 11; e12.trash = 3; n3.trash = 5;
            TunnelTrash.Sync(net);
            var a = net.World(n0.position);
            foreach (var lp in new[] { a + new Vector3(3f, 2f, 0f), net.World(n3.position) + Vector3.up * 2.5f, net.World(n1.position) + new Vector3(-6f, 2f, 0f) })
            {
                var lgo = new GameObject("_TestLight");
                SceneManager.MoveGameObjectToScene(lgo, scene);
                lgo.transform.position = lp;
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point; l.range = 14f; l.intensity = 6f;
            }
            sb.Append("\n  shot from dead end: shotf trash_deadend " + (a.x + 9f) + " " + (a.y + 1.6f) + " " + a.z + " -90 10");
            sb.Append("\n  shot room: shotf trash_room " + net.World(n3.position).x + " " + (net.World(n3.position).y + 6f) + " " + (net.World(n3.position).z - 7f) + " 0 40");
            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        public static string Close()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "play mode: closes after it";
            var sc = SceneManager.GetSceneByPath(ScenePath);
            if (sc.IsValid() && sc.isLoaded) EditorSceneManager.CloseScene(sc, true);
            if (System.IO.File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
            return "closed";
        }

        // the test scene never outlives a session: closed when play mode ends or the editor reloads
        [InitializeOnLoadMethod]
        static void AutoClose()
        {
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += () => Close(); };
            EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode && System.IO.File.Exists(ScenePath)) Close(); };
        }


        static Dictionary<string, Vector3> Snapshot(Transform root)
        {
            var d = new Dictionary<string, Vector3>();
            if (root == null) return d;
            foreach (Transform g in root)
                for (int i = 0; i < g.childCount; i++) d[g.name + "/" + i + "/" + g.GetChild(i).name] = g.GetChild(i).position;
            return d;
        }

        static bool Same(Dictionary<string, Vector3> a, Dictionary<string, Vector3> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a) { Vector3 v; if (!b.TryGetValue(kv.Key, out v) || (v - kv.Value).sqrMagnitude > 1e-6f) return false; }
            return true;
        }
    }
}
