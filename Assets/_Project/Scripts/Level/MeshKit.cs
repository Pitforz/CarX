using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>Small helper to build multi-material meshes from quads, triangles and primitives.</summary>
    public class MeshKit
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<int>[] tris;

        public MeshKit(int subMeshes)
        {
            tris = new List<int>[subMeshes];
            for (int i = 0; i < subMeshes; i++) tris[i] = new List<int>();
        }

        public int VertexCount { get { return verts.Count; } }

        int V(Vector3 p, Vector3 n, Vector2 uv)
        {
            verts.Add(p); normals.Add(n); uvs.Add(uv);
            return verts.Count - 1;
        }

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 facing)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(n, facing) < 0)
            {
                Vector3 t = b; b = c; c = t;
                Vector2 tu = ub; ub = uc; uc = tu;
                n = -n;
            }
            n.Normalize();
            int i = V(a, n, ua); V(b, n, ub); V(c, n, uc);
            tris[sub].Add(i); tris[sub].Add(i + 1); tris[sub].Add(i + 2);
        }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 facing)
        {
            Vector3 n = Vector3.Cross(c - a, d - b);
            if (n.sqrMagnitude < 1e-14f) n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) return;
            n.Normalize();
            if (Vector3.Dot(n, facing) < 0) n = -n;
            QuadN(sub, a, b, c, d, ua, ub, uc, ud, n, n, n, n);
        }

        public void QuadN(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud,
                          Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
        {
            Vector3 geo = Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a);
            if (geo.sqrMagnitude < 1e-14f) return;
            int i = V(a, na, ua); V(b, nb, ub); V(c, nc, uc); V(d, nd, ud);
            var t = tris[sub];
            if (Vector3.Dot(geo, na + nb + nc + nd) >= 0)
            {
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }
            else
            {
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
                t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }
        }

        static Vector2 Planar(Vector3 p, Vector3 n, float scale)
        {
            Vector3 an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            if (an.y >= an.x && an.y >= an.z) return new Vector2(p.x, p.z) * scale;
            if (an.x >= an.z) return new Vector2(p.z, p.y) * scale;
            return new Vector2(p.x, p.y) * scale;
        }

        public void QuadBox(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, float uvScale)
        {
            Vector3 n = Vector3.Cross(c - a, d - b);
            if (Vector3.Dot(n, facing) < 0) n = -n;
            Quad(sub, a, b, c, d, Planar(a, n, uvScale), Planar(b, n, uvScale), Planar(c, n, uvScale), Planar(d, n, uvScale), facing);
        }

        public void Box(int sub, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, float uvScale)
        {
            Vector3[] k = new Vector3[8];
            for (int i = 0; i < 8; i++)
                k[i] = c + ((i & 1) != 0 ? ax : -ax) + ((i & 2) != 0 ? ay : -ay) + ((i & 4) != 0 ? az : -az);
            QuadBox(sub, k[0], k[2], k[6], k[4], -ax, uvScale);
            QuadBox(sub, k[1], k[3], k[7], k[5], ax, uvScale);
            QuadBox(sub, k[0], k[1], k[5], k[4], -ay, uvScale);
            QuadBox(sub, k[2], k[3], k[7], k[6], ay, uvScale);
            QuadBox(sub, k[0], k[1], k[3], k[2], -az, uvScale);
            QuadBox(sub, k[4], k[5], k[7], k[6], az, uvScale);
        }

        public void Tube(int sub, Vector3 a, Vector3 b, float ra, float rb, int sides, bool outward, float uvScale)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            axis /= len;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0);
                Vector3 d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                Vector3 n0 = outward ? d0 : -d0, n1 = outward ? d1 : -d1;
                float u0 = (float)i / sides * Mathf.PI * (ra + rb) * uvScale, u1 = (float)(i + 1) / sides * Mathf.PI * (ra + rb) * uvScale;
                QuadN(sub, a + d0 * ra, a + d1 * ra, b + d1 * rb, b + d0 * rb,
                      new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, len * uvScale), new Vector2(u0, len * uvScale),
                      n0, n1, n1, n0);
            }
        }

        public void Prism(int sub, IList<Vector2> poly, float y0, float y1, bool inward, bool smooth, float uvScale)
        {
            int n = poly.Count;
            Vector2 c = Vector2.zero;
            for (int i = 0; i < n; i++) c += poly[i];
            c /= n;
            float per = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = poly[i], p1 = poly[(i + 1) % n];
                float len = Vector2.Distance(p0, p1);
                Vector3 a = new Vector3(p0.x, y0, p0.y), b = new Vector3(p1.x, y0, p1.y);
                Vector3 c1 = new Vector3(p1.x, y1, p1.y), d = new Vector3(p0.x, y1, p0.y);
                Vector2 mid = (p0 + p1) * 0.5f;
                Vector3 face = new Vector3(c.x - mid.x, 0, c.y - mid.y);
                if (!inward) face = -face;
                Vector2 ua = new Vector2(per * uvScale, y0 * uvScale), ub = new Vector2((per + len) * uvScale, y0 * uvScale);
                Vector2 uc = new Vector2((per + len) * uvScale, y1 * uvScale), ud = new Vector2(per * uvScale, y1 * uvScale);
                if (smooth)
                {
                    Vector3 n0 = new Vector3(c.x - p0.x, 0, c.y - p0.y).normalized, n1 = new Vector3(c.x - p1.x, 0, c.y - p1.y).normalized;
                    if (!inward) { n0 = -n0; n1 = -n1; }
                    QuadN(sub, a, b, c1, d, ua, ub, uc, ud, n0, n1, n1, n0);
                }
                else Quad(sub, a, b, c1, d, ua, ub, uc, ud, face);
                per += len;
            }
        }

        public void Disc(int sub, Vector3 centre, float radius, int sides, bool up, float uvScaleToUnit)
        {
            Vector3 n = up ? Vector3.up : Vector3.down;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = centre + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                Vector3 p1 = centre + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                Tri(sub, centre, p0, p1, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f + Mathf.Cos(a0) * 0.5f * uvScaleToUnit, 0.5f + Mathf.Sin(a0) * 0.5f * uvScaleToUnit),
                    new Vector2(0.5f + Mathf.Cos(a1) * 0.5f * uvScaleToUnit, 0.5f + Mathf.Sin(a1) * 0.5f * uvScaleToUnit), n);
            }
        }

        public void Fill(Mesh m)
        {
            m.Clear();
            m.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.subMeshCount = tris.Length;
            for (int s = 0; s < tris.Length; s++) m.SetTriangles(tris[s], s, false);
            m.RecalculateBounds();
            if (verts.Count > 0) m.RecalculateTangents();
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            Fill(m);
            return m;
        }

        public static float Cross2(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

        public static List<Vector2> Circle(Vector2 c, float r, int n)
        {
            var l = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                l.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            return l;
        }

        public static List<Vector2> Rect(Vector2 c, float hw, float hl)
        {
            return new List<Vector2> { c + new Vector2(-hw, -hl), c + new Vector2(hw, -hl), c + new Vector2(hw, hl), c + new Vector2(-hw, hl) };
        }

        static float Ang(Vector2 d) { return Mathf.Repeat(Mathf.Atan2(d.y, d.x), Mathf.PI * 2f); }

        static Vector2 RayHit(Vector2 c, Vector2 dir, IList<Vector2> poly)
        {
            float best = float.MaxValue;
            Vector2 hit = c + dir;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 e0 = poly[i], e = poly[(i + 1) % n] - e0;
                float den = Cross2(dir, e);
                if (Mathf.Abs(den) < 1e-9f) continue;
                float t = Cross2(e0 - c, e) / den;
                float u = Cross2(e0 - c, dir) / den;
                if (t > 1e-6f && u >= -1e-5f && u <= 1f + 1e-5f && t < best) { best = t; hit = c + dir * t; }
            }
            return hit;
        }

        /// <summary>Triangulates the area between a star-shaped inner polygon (hole) and a convex outer polygon. Both CCW.</summary>
        public static List<Vector2> RingFill(Vector2 c, IList<Vector2> inner, IList<Vector2> outer)
        {
            var res = new List<Vector2>();
            int ni = inner.Count, no = outer.Count;
            var ao = new float[no];
            for (int k = 0; k < no; k++) ao[k] = Ang(outer[k] - c);
            var pa = new float[ni];
            var proj = new Vector2[ni];
            for (int i = 0; i < ni; i++)
            {
                pa[i] = Ang(inner[i] - c);
                proj[i] = RayHit(c, inner[i] - c, outer);
            }
            var chain = new List<Vector2>();
            var corners = new List<KeyValuePair<float, Vector2>>();
            for (int i = 0; i < ni; i++)
            {
                int j = (i + 1) % ni;
                float span = Mathf.Repeat(pa[j] - pa[i], Mathf.PI * 2f);
                corners.Clear();
                for (int k = 0; k < no; k++)
                {
                    float d = Mathf.Repeat(ao[k] - pa[i], Mathf.PI * 2f);
                    if (d > 1e-5f && d < span - 1e-5f) corners.Add(new KeyValuePair<float, Vector2>(d, outer[k]));
                }
                corners.Sort((x, y) => x.Key.CompareTo(y.Key));
                chain.Clear();
                chain.Add(proj[i]);
                foreach (var kv in corners) chain.Add(kv.Value);
                chain.Add(proj[j]);
                for (int m = 0; m < chain.Count - 1; m++) AddTri(res, inner[i], chain[m], chain[m + 1]);
                AddTri(res, inner[i], chain[chain.Count - 1], inner[j]);
            }
            return res;
        }

        static void AddTri(List<Vector2> res, Vector2 a, Vector2 b, Vector2 c)
        {
            float area = Cross2(b - a, c - a);
            if (Mathf.Abs(area) < 1e-7f) return;
            if (area < 0) { Vector2 t = b; b = c; c = t; }
            res.Add(a); res.Add(b); res.Add(c);
        }

        public static List<Vector2> FanFill(Vector2 c, IList<Vector2> poly)
        {
            var res = new List<Vector2>();
            for (int i = 0; i < poly.Count; i++) AddTri(res, c, poly[i], poly[(i + 1) % poly.Count]);
            return res;
        }

        /// <summary>Splits a 2D triangle by the horizontal line y = line; emits parts below and above.</summary>
        public static void SplitTri(Vector2 a, Vector2 b, Vector2 c, float line, List<Vector2> below, List<Vector2> above)
        {
            var pts = new[] { a, b, c };
            var lo = new List<Vector2>(4);
            var hi = new List<Vector2>(4);
            for (int i = 0; i < 3; i++)
            {
                Vector2 p = pts[i], q = pts[(i + 1) % 3];
                bool pb = p.y <= line, qb = q.y <= line;
                if (pb) lo.Add(p); else hi.Add(p);
                if (pb != qb)
                {
                    float t = (line - p.y) / (q.y - p.y);
                    Vector2 m = Vector2.Lerp(p, q, t);
                    lo.Add(m); hi.Add(m);
                }
            }
            for (int i = 1; i + 1 < lo.Count; i++) AddTri(below, lo[0], lo[i], lo[i + 1]);
            for (int i = 1; i + 1 < hi.Count; i++) AddTri(above, hi[0], hi[i], hi[i + 1]);
        }
    }
}
