using System.Collections.Generic;
using System.IO;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarX.EditorTools
{
    /// <summary>
    /// Backward-compatibility test for tunnel types saved before a format change.
    /// Record (run once on the old code): stores every type of the library as JSON plus a geometry hash of a small test graph.
    /// Check (run on the new code): loads those old JSONs into fresh types (new fields keep their defaults, like Unity does
    /// with an old .asset file), builds the same graph and must get exactly the same geometry.
    /// </summary>
    public static class TunnelCompatProbe
    {
        const string BaselinePath = "Assets/_Project/Tunnels/Tests/TunnelCompatBaseline.txt";

        public static string Record()
        {
            var lib = TunnelGraphAssets.GetLibrary();
            var lines = new List<string>();
            var types = new List<TunnelType>();
            foreach (var t in lib.types)
            {
                if (t == null) continue;
                var copy = Object.Instantiate(t);
                copy.name = t.name;
                types.Add(copy);
                lines.Add("type\t" + t.name + "\t" + JsonUtility.ToJson(t));
            }
            var hashes = BuildAll(lib, types);
            foreach (var kv in hashes) lines.Add("hash\t" + kv.Key + "\t" + kv.Value);
            Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath));
            File.WriteAllLines(BaselinePath, lines);
            AssetDatabase.ImportAsset(BaselinePath);
            return "CarX compat baseline recorded: " + types.Count + " types, " + hashes.Count + " graphs";
        }

        public static string Check()
        {
            if (!File.Exists(BaselinePath)) return "CarX compat: no baseline " + BaselinePath;
            var lib = TunnelGraphAssets.GetLibrary();
            var types = new List<TunnelType>();
            var want = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(BaselinePath))
            {
                var p = line.Split('\t');
                if (p.Length < 3) continue;
                if (p[0] == "type")
                {
                    var t = ScriptableObject.CreateInstance<TunnelType>();
                    JsonUtility.FromJsonOverwrite(p[2], t);
                    t.name = p[1];
                    types.Add(t);
                }
                else if (p[0] == "hash") want[p[1]] = p[2];
            }
            var got = BuildAll(lib, types);
            int ok = 0, bad = 0;
            var msg = new System.Text.StringBuilder();
            foreach (var kv in want)
            {
                string g;
                if (got.TryGetValue(kv.Key, out g) && g == kv.Value) ok++;
                else { bad++; msg.Append(" [" + kv.Key + ": " + kv.Value + " -> " + g + "]"); }
            }
            return "CarX compat: old type files give the same geometry: ok " + ok + ", bad " + bad + msg;
        }

        /// <summary>Per type: a bent tunnel with a dead end and a chamber; plus one join and one type change between the first two types.</summary>
        static SortedDictionary<string, string> BuildAll(TunnelTypeLibrary lib, List<TunnelType> types)
        {
            var res = new SortedDictionary<string, string>();
            var prevActive = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                foreach (var t in types)
                {
                    var net = NewNet(lib, "Compat_" + t.name);
                    var a = net.AddNode(new Vector3(0, 0, 0));
                    var b = net.AddNode(new Vector3(0, 0, 40));
                    var c = net.AddNode(new Vector3(14, 0, 78));
                    var d = net.AddNode(new Vector3(14, 0, 110));
                    d.kind = GraphNodeKind.Chamber;
                    d.autoYaw = false;
                    var e = net.AddNode(new Vector3(14, 0, 140));
                    net.AddEdge(a.id, b.id, t);
                    net.AddEdge(b.id, c.id, t);
                    net.AddEdge(c.id, d.id, t);
                    net.AddEdge(d.id, e.id, t);
                    net.Build();
                    res[t.name] = Hash(net);
                }
                if (types.Count >= 3)
                {
                    var net = NewNet(lib, "Compat_Join");
                    var a = net.AddNode(new Vector3(0, 0, 0));
                    var m = net.AddNode(new Vector3(0, 0, 30));
                    var b = net.AddNode(new Vector3(0, 0, 60));
                    var s = net.AddNode(new Vector3(30, 0, 30));
                    net.AddEdge(a.id, m.id, types[0]);
                    net.AddEdge(m.id, b.id, types[0]);
                    net.AddEdge(m.id, s.id, types[2]);
                    var r = net.AddNode(new Vector3(0, 0, 90));
                    net.AddEdge(b.id, r.id, types[1]);
                    net.Build();
                    res["join+reducer"] = Hash(net);
                }
            }
            finally
            {
                if (prevActive.IsValid()) SceneManager.SetActiveScene(prevActive);
                EditorSceneManager.CloseScene(scene, true);
            }
            return res;
        }

        static TunnelNetwork NewNet(TunnelTypeLibrary lib, string name)
        {
            var go = new GameObject(name);
            var net = go.AddComponent<TunnelNetwork>();
            net.library = lib;
            net.puddles = false;
            return net;
        }

        /// <summary>Order-independent hash of all triangles (positions rounded to 0.1 mm), so moving triangles between sub-meshes does not count.</summary>
        static string Hash(TunnelNetwork net)
        {
            unchecked
            {
                long sum = 0;
                int tris = 0;
                foreach (var mf in net.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    var v = mesh.vertices;
                    var idx = mesh.triangles;
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        long h0 = V(mf.transform.TransformPoint(v[idx[i]])), h1 = V(mf.transform.TransformPoint(v[idx[i + 1]])), h2 = V(mf.transform.TransformPoint(v[idx[i + 2]]));
                        long lo = System.Math.Min(h0, System.Math.Min(h1, h2)), hi = System.Math.Max(h0, System.Math.Max(h1, h2));
                        long mid = h0 + h1 + h2 - lo - hi;
                        long h = lo * 1000003L + mid * 7919L + hi;
                        h ^= h >> 29; h *= 0x5DEECE66DL; h ^= h >> 31;
                        sum += h;
                        tris++;
                    }
                }
                return tris + ":" + sum.ToString("X");
            }
        }

        static long V(Vector3 p)
        {
            unchecked
            {
                long x = Mathf.RoundToInt(p.x * 10000f), y = Mathf.RoundToInt(p.y * 10000f), z = Mathf.RoundToInt(p.z * 10000f);
                long h = x * 73856093L ^ y * 19349663L ^ z * 83492791L;
                h ^= h >> 21; h *= -7046029254386353131L; h ^= h >> 27;
                return h;
            }
        }
    }
}
