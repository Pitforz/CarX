// Diagnostics for dark materials / inverted normals. Read-only: changes nothing.
// Bridge: call CarX.EditorTools.MaterialDiag.Run  -> Logs/material_diag.txt
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CarX.EditorTools
{
    public static class MaterialDiag
    {
        static string PathOf(Transform t)
        {
            var s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        static float Cull(Material m)
        {
            foreach (var p in new[] { "_Cull", "_CullMode", "_OpaqueCullMode", "_CullModeForward" })
                if (m.HasProperty(p)) return m.GetFloat(p);
            return -1;
        }

        // fraction of triangles whose winding normal disagrees with vertex normals
        static float FlippedFraction(Mesh mesh, Dictionary<Mesh, float> cache)
        {
            if (cache.TryGetValue(mesh, out var f)) return f;
            f = -1;
            try
            {
                using (var arr = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    var d = arr[0];
                    if (d.HasVertexAttribute(VertexAttribute.Normal))
                    {
                        var v = new Unity.Collections.NativeArray<Vector3>(d.vertexCount, Unity.Collections.Allocator.Temp);
                        var n = new Unity.Collections.NativeArray<Vector3>(d.vertexCount, Unity.Collections.Allocator.Temp);
                        d.GetVertices(v); d.GetNormals(n);
                        int bad = 0, all = 0;
                        for (int sm = 0; sm < d.subMeshCount; sm++)
                        {
                            if (d.GetSubMesh(sm).topology != MeshTopology.Triangles) continue;
                            var tri = new Unity.Collections.NativeArray<int>(d.GetSubMesh(sm).indexCount, Unity.Collections.Allocator.Temp);
                            d.GetIndices(tri, sm);
                            for (int i = 0; i + 2 < tri.Length; i += 3)
                            {
                                int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                                var fn = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                                if (fn.sqrMagnitude < 1e-12f) continue;
                                all++;
                                if (Vector3.Dot(fn, n[a] + n[b] + n[c]) < 0) bad++;
                            }
                            tri.Dispose();
                        }
                        v.Dispose(); n.Dispose();
                        f = all > 0 ? (float)bad / all : -1;
                    }
                }
            }
            catch (System.Exception) { f = -2; }
            cache[mesh] = f;
            return f;
        }

        public static void Run()
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            sb.AppendLine("scene " + scene.path + "  ambientMode " + RenderSettings.ambientMode + " ambientLight " + RenderSettings.ambientLight + " ambientIntensity " + RenderSettings.ambientIntensity + " reflIntensity " + RenderSettings.reflectionIntensity);
            sb.AppendLine("roots: " + string.Join(", ", scene.GetRootGameObjects().Select(g => g.name + (g.activeSelf ? "" : "(off)"))));
            var cache = new Dictionary<Mesh, float>();
            var negScale = new List<string>();
            var cullOdd = new SortedDictionary<string, int>();
            var flipped = new SortedDictionary<string, string>();
            var matUse = new SortedDictionary<string, int>();
            var noUv2 = new SortedDictionary<string, int>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != scene) continue;
                var t = r.transform;
                if (t.localToWorldMatrix.determinant < 0) negScale.Add(PathOf(t));
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string key = m.name + " [" + (m.shader ? m.shader.name : "null") + "]";
                    matUse[key] = matUse.TryGetValue(key, out var k) ? k + 1 : 1;
                    float c = Cull(m);
                    if (c == 0 || c == 1) { var ck = key + " cull=" + c; cullOdd[ck] = cullOdd.TryGetValue(ck, out var q) ? q + 1 : 1; }
                }
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf ? mf.sharedMesh : (r is SkinnedMeshRenderer sk ? sk.sharedMesh : null);
                if (mesh == null) continue;
                var ff = FlippedFraction(mesh, cache);
                if (ff > 0.3f) flipped[PathOf(t)] = mesh.name + " flipped " + ff.ToString("0.00") + (t.localToWorldMatrix.determinant < 0 ? " +NEGSCALE" : "");
                bool gi = GameObjectUtility.GetStaticEditorFlags(r.gameObject).HasFlag(StaticEditorFlags.ContributeGI);
                if (gi && r is MeshRenderer mr && mr.receiveGI == ReceiveGI.Lightmaps && !mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                    noUv2[mesh.name] = noUv2.TryGetValue(mesh.name, out var u) ? u + 1 : 1;
            }
            sb.AppendLine("\n== negative scale (mirrored) " + negScale.Count);
            foreach (var s in negScale.Take(300)) sb.AppendLine("  " + s);
            sb.AppendLine("\n== materials with cull Off(0)/Front(1)");
            foreach (var kv in cullOdd) sb.AppendLine("  " + kv.Key + " x" + kv.Value);
            sb.AppendLine("\n== meshes whose winding disagrees with normals (>30% tris)");
            foreach (var kv in flipped) sb.AppendLine("  " + kv.Key + " : " + kv.Value);
            sb.AppendLine("\n== meshes not checked: " + cache.Count(kv => kv.Value < 0) + " of " + cache.Count);
            sb.AppendLine("\n== lightmapped static meshes without UV2 ");
            foreach (var kv in noUv2) sb.AppendLine("  " + kv.Key + " x" + kv.Value);
            sb.AppendLine("\n== material usage");
            foreach (var kv in matUse) sb.AppendLine("  " + kv.Key + " x" + kv.Value);
            File.WriteAllText("Logs/material_diag.txt", sb.ToString());
            Debug.Log("CarX MaterialDiag done -> Logs/material_diag.txt");
        }
    }
}
