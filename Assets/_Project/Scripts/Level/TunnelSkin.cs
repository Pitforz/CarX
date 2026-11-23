using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>
    /// Light-tight black skins of the tunnel pieces (TunnelPath, TunnelJunction, TunnelRoom), trimmed so that they are never
    /// seen from inside: every bit of a skin that lies in the air of any piece (a tunnel, a chamber, a room, a shaft) is cut away.
    /// Pieces hand in their raw skin and their open-space volumes; the trimming runs once all pieces are built
    /// (just before the next render), so the order in which pieces rebuild does not matter.
    /// </summary>
    public static class TunnelSkin
    {
        /// <summary>A polygon (in the u,v plane) swept along w from w0 to w1. Local to the part's owner.</summary>
        public struct Volume
        {
            public Vector3 o, u, v, w;
            public Vector2[] poly;
            public float w0, w1;
        }

        public class Part
        {
            public Transform owner;
            public MeshFilter filter;
            public Mesh mesh;
            public readonly List<Vector3> raw = new List<Vector3>();       // 3 per triangle, local to filter.transform
            public readonly List<Volume> volumes = new List<Volume>();
        }

        struct WVol
        {
            public Vector3 o, u, v, w;
            public Vector2[] poly;
            public float w0, w1;
            public Bounds box;
            public string owner;
            public Vector2[] hn;     // convex hull of poly: outward edge normals
            public float[] hd;       // and their offsets (n·x <= d inside)
        }

        const float Eps = 0.003f;     // a skin exactly on the boundary of a space stays
        const float Leaf = 0.1f;      // trimming resolution, m
        const float Cell = 4f;

        static readonly HashSet<Part> parts = new HashSet<Part>();
        static bool dirty, hooked;
        static readonly List<WVol> vols = new List<WVol>();
        static readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();

        public static void Submit(Part p)
        {
            if (p == null) return;
            parts.Add(p);
            dirty = true;
            Hook();
        }

        public static void Remove(Part p)
        {
            if (p != null && parts.Remove(p)) dirty = true;
        }

        /// <summary>Raw skin triangles of a MeshKit (sub-mesh 0) into a part.</summary>
        public static void TakeRaw(Part p, MeshKit kit)
        {
            p.raw.Clear();
            var t = kit.tris[0];
            for (int i = 0; i < t.Count; i++) p.raw.Add(kit.verts[t[i]]);
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginContextRendering += (ctx, cams) => { if (dirty) Flush(); };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterLoad() { Hook(); if (dirty) Flush(); }

        public static void Flush()
        {
            dirty = false;
            parts.RemoveWhere(p => p == null || p.owner == null || p.filter == null);
            vols.Clear();
            grid.Clear();
            foreach (var p in parts)
            {
                if (!p.owner.gameObject.activeInHierarchy) continue;
                var tr = p.owner;
                foreach (var v in p.volumes) AddVolume(tr, v);
            }
            var cand = new List<int>();
            var stamp = new int[vols.Count];
            int pass = 0;
            var outV = new List<Vector3>();
            var outN = new List<Vector3>();
            var outT = new List<int>();
            foreach (var p in parts)
            {
                outV.Clear(); outN.Clear(); outT.Clear();
                var tr = p.filter.transform;
                var raw = p.raw;
                for (int i = 0; i + 2 < raw.Count; i += 3)
                {
                    Vector3 a = tr.TransformPoint(raw[i]), b = tr.TransformPoint(raw[i + 1]), c = tr.TransformPoint(raw[i + 2]);
                    var bb = new Bounds(a, Vector3.zero); bb.Encapsulate(b); bb.Encapsulate(c);
                    cand.Clear();
                    pass++;
                    Query(bb, cand, stamp, pass);
                    Split(a, b, c, cand, 0, tr, outV, outN, outT);
                }
                if (p.mesh == null) p.mesh = new Mesh { name = p.owner.name + "_Skin", hideFlags = HideFlags.DontSave };
                p.mesh.Clear();
                p.mesh.indexFormat = outV.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                p.mesh.SetVertices(outV);
                p.mesh.SetNormals(outN);
                p.mesh.SetTriangles(outT, 0, true);
                p.filter.sharedMesh = p.mesh;
            }
        }

        static void AddVolume(Transform tr, Volume v)
        {
            var w = new WVol
            {
                o = tr.TransformPoint(v.o),
                u = tr.TransformDirection(v.u).normalized,
                v = tr.TransformDirection(v.v).normalized,
                w = tr.TransformDirection(v.w).normalized,
                poly = v.poly, w0 = v.w0, w1 = v.w1, owner = tr.name
            };
            if (w.poly == null || w.poly.Length < 3 || w.w1 - w.w0 < 2f * Eps) return;
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            foreach (var q in w.poly) { uMin = Mathf.Min(uMin, q.x); uMax = Mathf.Max(uMax, q.x); vMin = Mathf.Min(vMin, q.y); vMax = Mathf.Max(vMax, q.y); }
            var box = new Bounds(w.o + w.u * uMin + w.v * vMin + w.w * w.w0, Vector3.zero);
            for (int i = 0; i < 8; i++)
                box.Encapsulate(w.o + w.u * ((i & 1) != 0 ? uMax : uMin) + w.v * ((i & 2) != 0 ? vMax : vMin) + w.w * ((i & 4) != 0 ? w.w1 : w.w0));
            w.box = box;
            Hull(w.poly, out w.hn, out w.hd);
            int id = vols.Count;
            vols.Add(w);
            Vector3Int lo = CellOf(box.min), hi = CellOf(box.max);
            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                    for (int z = lo.z; z <= hi.z; z++)
                    {
                        long k = Key(x, y, z);
                        List<int> l;
                        if (!grid.TryGetValue(k, out l)) { l = new List<int>(); grid[k] = l; }
                        l.Add(id);
                    }
        }

        static Vector3Int CellOf(Vector3 p) { return new Vector3Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell)); }
        static long Key(int x, int y, int z) { return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF); }

        static void Query(Bounds bb, List<int> res, int[] stamp, int pass)
        {
            Vector3Int lo = CellOf(bb.min), hi = CellOf(bb.max);
            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                    for (int z = lo.z; z <= hi.z; z++)
                    {
                        List<int> l;
                        if (!grid.TryGetValue(Key(x, y, z), out l)) continue;
                        foreach (int i in l)
                        {
                            if (stamp[i] == pass) continue;
                            stamp[i] = pass;
                            if (vols[i].box.Intersects(bb)) res.Add(i);
                        }
                    }
        }

        static void Split(Vector3 a, Vector3 b, Vector3 c, List<int> cand, int depth, Transform tr,
                          List<Vector3> outV, List<Vector3> outN, List<int> outT)
        {
            var bb = new Bounds(a, Vector3.zero); bb.Encapsulate(b); bb.Encapsulate(c);
            bb.Expand(0.002f);
            List<int> here = null;
            foreach (int i in cand) if (vols[i].box.Intersects(bb) && !Separated(vols[i], a, b, c)) (here ??= new List<int>()).Add(i);
            if (here == null) { Emit(a, b, c, tr, outV, outN, outT); return; }
            float ab = (b - a).sqrMagnitude, bc = (c - b).sqrMagnitude, ca = (a - c).sqrMagnitude;
            float longest = Mathf.Max(ab, Mathf.Max(bc, ca));
            if (longest <= Leaf * Leaf || depth > 24)
            {
                var g = (a + b + c) / 3f;
                if (Inside(g, here) || Inside(Vector3.Lerp(a, g, 0.1f), here) || Inside(Vector3.Lerp(b, g, 0.1f), here) || Inside(Vector3.Lerp(c, g, 0.1f), here)) return;
                Emit(a, b, c, tr, outV, outN, outT);
                return;
            }
            // a whole triangle well inside one space: drop it at once
            if (Inside(a, here) && Inside(b, here) && Inside(c, here) && Inside((a + b + c) / 3f, here) && longest < 1f) return;
            if (ab >= bc && ab >= ca)
            {
                var m = (a + b) * 0.5f;
                Split(a, m, c, here, depth + 1, tr, outV, outN, outT);
                Split(m, b, c, here, depth + 1, tr, outV, outN, outT);
            }
            else if (bc >= ca)
            {
                var m = (b + c) * 0.5f;
                Split(a, b, m, here, depth + 1, tr, outV, outN, outT);
                Split(a, m, c, here, depth + 1, tr, outV, outN, outT);
            }
            else
            {
                var m = (c + a) * 0.5f;
                Split(a, b, m, here, depth + 1, tr, outV, outN, outT);
                Split(m, b, c, here, depth + 1, tr, outV, outN, outT);
            }
        }

        static void Emit(Vector3 a, Vector3 b, Vector3 c, Transform tr, List<Vector3> outV, List<Vector3> outN, List<int> outT)
        {
            Vector3 la = tr.InverseTransformPoint(a), lb = tr.InverseTransformPoint(b), lc = tr.InverseTransformPoint(c);
            var n = Vector3.Cross(lb - la, lc - la);
            if (n.sqrMagnitude < 1e-14f) return;
            n.Normalize();
            int i = outV.Count;
            outV.Add(la); outV.Add(lb); outV.Add(lc);
            outN.Add(n); outN.Add(n); outN.Add(n);
            outT.Add(i); outT.Add(i + 1); outT.Add(i + 2);
        }

        /// <summary>Debug: which spaces are near the point and whether it is inside them.</summary>
        public static string Probe(Vector3 p)
        {
            var sb = new System.Text.StringBuilder("probe " + p.ToString("0.00"));
            for (int i = 0; i < vols.Count; i++)
            {
                var v = vols[i];
                if (!v.box.Contains(p) && v.box.SqrDistance(p) > 1f) continue;
                var q = p - v.o;
                var x = new Vector2(Vector3.Dot(q, v.u), Vector3.Dot(q, v.v));
                float ww = Vector3.Dot(q, v.w);
                sb.Append("\n  " + v.owner + " #" + i + " uv " + x.ToString("0.00") + " w " + ww.ToString("0.00") + " [" + v.w0.ToString("0.00") + ", " + v.w1.ToString("0.00") +
                          "] inPoly " + InPoly(v.poly, x) + " edge " + EdgeDist(v.poly, x).ToString("0.00") + " polyN " + v.poly.Length);
            }
            return sb.ToString();
        }

        /// <summary>True when the triangle certainly does not reach into the volume (beyond Eps).</summary>
        static bool Separated(WVol v, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 qa = a - v.o, qb = b - v.o, qc = c - v.o;
            float wa = Vector3.Dot(qa, v.w), wb = Vector3.Dot(qb, v.w), wc = Vector3.Dot(qc, v.w);
            if (wa <= v.w0 + Eps && wb <= v.w0 + Eps && wc <= v.w0 + Eps) return true;
            if (wa >= v.w1 - Eps && wb >= v.w1 - Eps && wc >= v.w1 - Eps) return true;
            var pa = new Vector2(Vector3.Dot(qa, v.u), Vector3.Dot(qa, v.v));
            var pb = new Vector2(Vector3.Dot(qb, v.u), Vector3.Dot(qb, v.v));
            var pc = new Vector2(Vector3.Dot(qc, v.u), Vector3.Dot(qc, v.v));
            for (int i = 0; i < v.hn.Length; i++)
            {
                float d = v.hd[i] - Eps;
                if (Vector2.Dot(v.hn[i], pa) >= d && Vector2.Dot(v.hn[i], pb) >= d && Vector2.Dot(v.hn[i], pc) >= d) return true;
            }
            return false;
        }

        static void Hull(Vector2[] poly, out Vector2[] hn, out float[] hd)
        {
            var pts = new List<Vector2>(poly);
            pts.Sort((x, y) => x.x != y.x ? x.x.CompareTo(y.x) : x.y.CompareTo(y.y));
            var h = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pass == 0 ? pts[i] : pts[pts.Count - 1 - i];
                    while (h.Count >= start + 2 && MeshKit.Cross2(h[h.Count - 1] - h[h.Count - 2], p - h[h.Count - 2]) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(p);
                }
                h.RemoveAt(h.Count - 1);
            }
            // h is counter-clockwise: outward normal of edge (a -> b) is (dy, -dx)
            var nl = new List<Vector2>();
            var dl = new List<float>();
            for (int i = 0; i < h.Count; i++)
            {
                Vector2 a = h[i], b = h[(i + 1) % h.Count];
                var n = new Vector2(b.y - a.y, a.x - b.x);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                nl.Add(n);
                dl.Add(Vector2.Dot(n, a));
            }
            hn = nl.ToArray();
            hd = dl.ToArray();
        }

        static bool Inside(Vector3 p, List<int> ids)
        {
            foreach (int i in ids)
            {
                var v = vols[i];
                var q = p - v.o;
                float ww = Vector3.Dot(q, v.w);
                if (ww <= v.w0 + Eps || ww >= v.w1 - Eps) continue;
                var x = new Vector2(Vector3.Dot(q, v.u), Vector3.Dot(q, v.v));
                if (InPoly(v.poly, x) && EdgeDist(v.poly, x) > Eps) return true;
            }
            return false;
        }

        static bool InPoly(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        static float EdgeDist(Vector2[] poly, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], ab = poly[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
                best = Mathf.Min(best, (a + ab * t - p).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }
    }
}
