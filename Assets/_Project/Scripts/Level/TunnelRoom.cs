using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>Doorway in a room wall: wall index, centre along the wall (m from its first corner) and the tunnel outline (x across, y up from the floor).</summary>
    [Serializable]
    public class RoomDoor
    {
        public int wall;
        public float along;
        public List<Vector2> outline = new List<Vector2>();
    }

    /// <summary>
    /// Concrete room with a polygonal floor plan (built by TunnelNetwork from a Room node).
    /// Walls get doorways shaped like the tunnels; columns go from floor to ceiling.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, SelectionBase]
    [AddComponentMenu("CarX/Tunnel Room")]
    public class TunnelRoom : MonoBehaviour
    {
        [Tooltip("Углы комнаты (X, Z), против часовой стрелки если смотреть сверху")]
        public List<Vector2> corners = new List<Vector2>();
        [Min(1f)] public float height = 3f;
        public List<RoomDoor> doors = new List<RoomDoor>();
        public List<ColumnSpec> columns = new List<ColumnSpec>();
        [Range(0f, 1.5f)] public float dirtLine = 0.9f;
        public int seed = 1;
        public TunnelMaterials materials;
        [HideInInspector] public int drivenSignature;

        [NonSerialized] Mesh mesh, skinMesh;
        const float SkinOffset = 0.15f;

        void OnEnable() { Rebuild(); }

        void OnDestroy()
        {
            if (skinMesh != null) DestroyImmediate(skinMesh);
            TunnelSkin.Remove(skinPart);
            if (skinPart != null && skinPart.mesh != null) DestroyImmediate(skinPart.mesh);
        }

        void OnDisable() { TunnelSkin.Remove(skinPart); }
        [NonSerialized] TunnelSkin.Part skinPart;

        public void Rebuild()
        {
            var kit = new MeshKit(5);
            var skin = new MeshKit(1);
            if (corners.Count >= 3) Emit(kit, skin);
            var gen = transform.Find("_Generated");
            if (gen == null)
            {
                gen = new GameObject("_Generated").transform;
                gen.SetParent(transform, false);
            }
            if (mesh == null) mesh = new Mesh { name = name + "_Room", hideFlags = HideFlags.DontSave };
            kit.Fill(mesh);
            var mf = gen.GetComponent<MeshFilter>();
            if (mf == null) mf = gen.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = gen.GetComponent<MeshRenderer>();
            if (mr == null) mr = gen.gameObject.AddComponent<MeshRenderer>();
            var mats = TunnelMaterials.Resolve(materials);
            if (mats != null) mr.sharedMaterials = mats.TunnelSet();
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            TunnelGI.Setup(mr);   // baked bounce light from probe volumes (lighting thread)
            var mc = gen.GetComponent<MeshCollider>();
            if (mc == null) mc = gen.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = null;
            if (kit.VertexCount > 0) mc.sharedMesh = mesh;

            // light-tight black skin a little outside the walls, floor and ceiling (see TunnelPath.BuildShadowShell)
            var sk = transform.Find("_Skin");
            if (sk == null)
            {
                sk = new GameObject("_Skin").transform;
                sk.SetParent(transform, false);
            }
            var smf = sk.GetComponent<MeshFilter>();
            if (smf == null) smf = sk.gameObject.AddComponent<MeshFilter>();
            var smr = sk.GetComponent<MeshRenderer>();
            if (smr == null) smr = sk.gameObject.AddComponent<MeshRenderer>();
            TunnelMaterials.ApplySkin(smr, mats);
            if (skinPart == null) skinPart = new TunnelSkin.Part();
            skinPart.owner = transform;
            skinPart.filter = smf;
            TunnelSkin.TakeRaw(skinPart, skin);
            skinPart.volumes.Clear();
            if (corners.Count >= 3)
                skinPart.volumes.Add(new TunnelSkin.Volume
                {
                    o = Vector3.zero, u = Vector3.right, v = Vector3.forward, w = Vector3.up, poly = corners.ToArray(), w0 = 0f, w1 = height
                });
            TunnelSkin.Submit(skinPart);
        }

        /// <summary>World-planar floor UV, the same as tunnels use, so the floor texture runs on through doorways.</summary>
        Vector2 WorldUV(Vector3 local)
        {
            var w = transform.TransformPoint(local);
            return new Vector2(w.x * 0.5f, w.z * 0.5f);
        }

        void Emit(MeshKit kit, MeshKit skin)
        {
            var poly = new List<Vector2>(corners);
            if (Area(poly) < 0f) poly.Reverse();
            float H = height;
            const float off = SkinOffset;
            float sy0 = -off, sy1 = H + off;
            var outerPoly = OffsetPoly(poly, off);
            {
                var st = EarClip(outerPoly);
                for (int t = 0; t + 2 < st.Count; t += 3)
                {
                    Vector2 a = st[t], b = st[t + 1], c = st[t + 2];
                    skin.Tri(0, new Vector3(a.x, sy0, a.y), new Vector3(b.x, sy0, b.y), new Vector3(c.x, sy0, c.y), Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                    skin.Tri(0, new Vector3(a.x, sy1, a.y), new Vector3(b.x, sy1, b.y), new Vector3(c.x, sy1, c.y), Vector2.zero, Vector2.zero, Vector2.zero, Vector3.down);
                }
            }
            var rnd = new System.Random(seed * 17 + 3);
            float line = dirtLine > 0.01f ? dirtLine : -1f;

            // floor and ceiling
            var tris = EarClip(poly);
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                Vector2 a = tris[t], b = tris[t + 1], c = tris[t + 2];
                Vector3 fa = new Vector3(a.x, 0f, a.y), fb = new Vector3(b.x, 0f, b.y), fc = new Vector3(c.x, 0f, c.y);
                kit.Tri(1, fa, fb, fc, WorldUV(fa), WorldUV(fb), WorldUV(fc), Vector3.up);
                kit.Tri(0, new Vector3(a.x, H, a.y), new Vector3(b.x, H, b.y), new Vector3(c.x, H, c.y), a * 0.5f, b * 0.5f, c * 0.5f, Vector3.down);
            }

            // walls: plain pieces between doorways, a ring around each doorway
            int n = poly.Count;
            bool flipped = Area(corners) < 0f;
            for (int w = 0; w < n; w++)
            {
                Vector2 a = poly[w], b = poly[(w + 1) % n];
                float len = Vector2.Distance(a, b);
                if (len < 1e-3f) continue;
                Vector2 u2 = (b - a) / len;
                Vector3 A = new Vector3(a.x, 0f, a.y), U = new Vector3(u2.x, 0f, u2.y);
                Vector3 inward = new Vector3(-u2.y, 0f, u2.x);
                int srcWall = flipped ? -1 : w;   // doors are given for counter-clockwise corners (TunnelNetwork always does that)
                var here = new List<RoomDoor>();
                foreach (var d in doors) if (d.wall == srcWall && d.outline.Count >= 3) here.Add(d);
                var pieces = new List<Vector2>();
                var skinPieces = new List<Vector2>();
                Vector2 sa = outerPoly[w], sb = outerPoly[(w + 1) % n];
                float shift = Vector2.Dot(a - sa, u2);          // along the skin wall = along the wall + shift
                float sLen = Vector2.Dot(sb - sa, u2);
                float sCur = 0f;
                float cur = 0f;
                here.Sort((x, y) => x.along.CompareTo(y.along));
                foreach (var d in here)
                {
                    float c = d.along;
                    var hole = new List<Vector2>(d.outline.Count);
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var v in d.outline)
                    {
                        hole.Add(new Vector2(c + v.x, v.y));
                        lo = Mathf.Min(lo, c + v.x); hi = Mathf.Max(hi, c + v.x);
                    }
                    lo = Mathf.Max(cur, lo - 0.1f);
                    hi = Mathf.Min(len, hi + 0.1f);
                    if (hi - lo < 0.05f) continue;
                    if (Area(hole) < 0f) hole.Reverse();
                    // never past the ends of the wall or above the ceiling (that is what made slanted slivers)
                    hole = MeshKit.ClipRect(hole, lo, 0f, hi, H);
                    if (hole.Count < 3 || Mathf.Abs(Area(hole)) < 0.01f) continue;
                    float top = 0f;
                    foreach (var v in hole) top = Mathf.Max(top, v.y);
                    float cx = Mathf.Clamp(c, lo + 0.01f, hi - 0.01f);
                    AddRect(pieces, cur, lo, H);
                    var rect = new List<Vector2> { new Vector2(lo, 0f), new Vector2(hi, 0f), new Vector2(hi, H), new Vector2(lo, H) };
                    pieces.AddRange(MeshKit.RingFill(new Vector2(cx, top * 0.5f), hole, rect));
                    cur = hi;
                    // the skin: the same opening exactly (the tunnel behind the door runs on through it)
                    float slo = Mathf.Max(sCur, lo + shift), shi = Mathf.Min(sLen, hi + shift);
                    if (shi - slo > 0.05f)
                    {
                        SkinRect(skinPieces, sCur, slo, sy0, sy1);
                        var sh = new List<Vector2>(hole.Count);
                        foreach (var v in hole) sh.Add(new Vector2(v.x + shift, v.y));
                        var srect = new List<Vector2> { new Vector2(slo, sy0), new Vector2(shi, sy0), new Vector2(shi, sy1), new Vector2(slo, sy1) };
                        skinPieces.AddRange(MeshKit.RingFill(new Vector2(cx + shift, top * 0.5f), sh, srect));
                        sCur = shi;
                    }
                }
                AddRect(pieces, cur, len, H);
                SkinRect(skinPieces, sCur, sLen, sy0, sy1);
                EmitWall(skin, 0, skinPieces, new Vector3(sa.x, 0f, sa.y), U, inward, 0f, 0f);
                var below = new List<Vector2>();
                var above = new List<Vector2>();
                for (int t = 0; t + 2 < pieces.Count; t += 3) MeshKit.SplitTri(pieces[t], pieces[t + 1], pieces[t + 2], line, below, above);
                float uOff = (float)rnd.NextDouble();
                EmitWall(kit, 4, below, A, U, inward, uOff, 0f);
                EmitWall(kit, 0, above, A, U, inward, uOff, (float)rnd.NextDouble());
            }

            foreach (var col in columns) col.Emit(kit, 0, 0f, H);
        }

        static void SkinRect(List<Vector2> l, float x0, float x1, float y0, float y1)
        {
            if (x1 - x0 < 1e-4f) return;
            l.Add(new Vector2(x0, y0)); l.Add(new Vector2(x1, y0)); l.Add(new Vector2(x1, y1));
            l.Add(new Vector2(x0, y0)); l.Add(new Vector2(x1, y1)); l.Add(new Vector2(x0, y1));
        }

        /// <summary>Counter-clockwise polygon pushed outwards by d (mitred corners, convex and reflex alike).</summary>
        static List<Vector2> OffsetPoly(List<Vector2> poly, float d)
        {
            int n = poly.Count;
            var res = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[(i + n - 1) % n], c = poly[i], q = poly[(i + 1) % n];
                Vector2 e0 = (c - p).normalized, e1 = (q - c).normalized;
                Vector2 n0 = new Vector2(e0.y, -e0.x), n1 = new Vector2(e1.y, -e1.x);   // outward normals
                Vector2 m = n0 + n1;
                float k = 1f + Vector2.Dot(n0, n1);
                res.Add(k > 0.1f ? c + m * (d / k) : c + n0 * d);
            }
            return res;
        }

        static void AddRect(List<Vector2> l, float x0, float x1, float H)
        {
            if (x1 - x0 < 1e-4f) return;
            l.Add(new Vector2(x0, 0f)); l.Add(new Vector2(x1, 0f)); l.Add(new Vector2(x1, H));
            l.Add(new Vector2(x0, 0f)); l.Add(new Vector2(x1, H)); l.Add(new Vector2(x0, H));
        }

        static void EmitWall(MeshKit kit, int sub, List<Vector2> tris, Vector3 A, Vector3 U, Vector3 inward, float uOff, float vOff)
        {
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                Vector2 a = tris[t], b = tris[t + 1], c = tris[t + 2];
                kit.Tri(sub, A + U * a.x + Vector3.up * a.y, A + U * b.x + Vector3.up * b.y, A + U * c.x + Vector3.up * c.y,
                        new Vector2(a.x * 0.5f + uOff, a.y * 0.5f + vOff), new Vector2(b.x * 0.5f + uOff, b.y * 0.5f + vOff),
                        new Vector2(c.x * 0.5f + uOff, c.y * 0.5f + vOff), inward);
            }
        }

        public static float Area(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) { Vector2 u = p[i], v = p[(i + 1) % p.Count]; a += u.x * v.y - v.x * u.y; }
            return a * 0.5f;
        }

        /// <summary>Ear clipping for a simple counter-clockwise polygon (concave allowed).</summary>
        public static List<Vector2> EarClip(List<Vector2> poly)
        {
            var res = new List<Vector2>();
            var idx = new List<int>();
            for (int i = 0; i < poly.Count; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool cut = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    Vector2 a = poly[idx[(k + idx.Count - 1) % idx.Count]], b = poly[idx[k]], c = poly[idx[(k + 1) % idx.Count]];
                    if (MeshKit.Cross2(b - a, c - b) <= 1e-7f) continue;   // reflex corner
                    bool inside = false;
                    for (int m = 0; m < idx.Count && !inside; m++)
                    {
                        var p = poly[idx[m]];
                        if (p == a || p == b || p == c) continue;
                        if (MeshKit.Cross2(b - a, p - a) >= 0f && MeshKit.Cross2(c - b, p - b) >= 0f && MeshKit.Cross2(a - c, p - c) >= 0f) inside = true;
                    }
                    if (inside) continue;
                    res.Add(a); res.Add(b); res.Add(c);
                    idx.RemoveAt(k);
                    cut = true;
                    break;
                }
                if (!cut) break;   // not a simple polygon: leave the rest open
            }
            if (idx.Count == 3) { res.Add(poly[idx[0]]); res.Add(poly[idx[1]]); res.Add(poly[idx[2]]); }
            return res;
        }
    }
}
