using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>
    /// The channel itself, built on request (<see cref="BuildChannel"/>): gravel floor, concrete slabs on both slopes, soil over
    /// the slabs of one bank with slumps down to the floor, ground on top, chain-link fences, a culvert at the far end, dry grass.
    /// Ground pieces use the CarX/CanalGround shader: vertex colour black = fine gravel, red = coarse gravel, green = silt,
    /// blue = dry grass. After painting them (Polybrush) press «Трава по покраске» to regrow the grass tufts.
    /// </summary>
    public partial class CanalEntrance
    {
        public const string ChannelRootName = "Русло";
        const float Chunk = 10f;

        delegate float Sampler(float s, float u, Vector2 xz, out Color col);

        struct Lobe { public float s, half, reach, thick; }

        /// <summary>Builds the channel from scratch under the child «Русло» (the old one with hand edits is replaced).</summary>
        public Transform BuildChannel()
        {
            pathDirty = true;
            var old = transform.Find(ChannelRootName);
            if (old != null) DestroyImmediate(old.gameObject);
            var root = new GameObject(ChannelRootName).transform;
            root.SetParent(transform, false);
            var rnd = new System.Random(seed * 7919 + 13);
            float L = channelLength;

            var lobes = MakeLobes(rnd);
            var floorRoot = Group(root, "Дно");
            var soilRoot = Group(root, "Земля на плитах");
            int k = 0;
            for (float s0 = -wallThickness; s0 < L + 0.5f - 0.01f; s0 += Chunk, k++)
            {
                float s1 = Mathf.Min(s0 + Chunk, L + 0.5f);
                Piece(floorRoot, "Дно " + k, FloorMesh(s0, s1), new[] { groundBlend }, true, true);
                Piece(soilRoot, "Земля " + k, SoilMesh(s0, s1, lobes), new[] { groundBlend }, true, true);
            }
            var slabMats = new[] { SlabMat(0), SlabMat(1), SlabMat(2), sealant };
            Piece(root, "Плиты: чистый бетон", SlabMesh(ConcreteSign, rnd), slabMats, true, true);
            Piece(root, "Плиты под землёй", SlabMesh(-ConcreteSign, rnd), slabMats, true, true);
            Piece(root, "Земля наверху за бетоном", TopMesh(ConcreteSign), new[] { groundBlend }, true, true);
            Piece(root, "Водопропуск", CulvertMesh(), new[] { WallMaterial(), dark }, true, true);
            if (fences) BuildFences(root, rnd);
            GrowGrass();
            return root;
        }

        /// <summary>Floor, both slab banks and the ground behind the clean bank again (soil, fences, hand-placed objects stay); grass regrows.</summary>
        public Transform RebuildFloorAndSlabs()
        {
            pathDirty = true;
            var root = transform.Find(ChannelRootName);
            if (root == null) return null;
            var rnd = new System.Random(seed * 7919 + 13);
            MakeLobes(rnd);   // same random sequence as a full build
            foreach (var n in new[] { "Дно", "Плиты: чистый бетон", "Плиты под землёй", "Земля наверху за бетоном" })
            {
                var t = root.Find(n);
                if (t != null) DestroyImmediate(t.gameObject);
            }
            float L = channelLength;
            var floorRoot = Group(root, "Дно");
            floorRoot.SetSiblingIndex(0);
            int k = 0;
            for (float s0 = -wallThickness; s0 < L + 0.5f - 0.01f; s0 += Chunk, k++)
                Piece(floorRoot, "Дно " + k, FloorMesh(s0, Mathf.Min(s0 + Chunk, L + 0.5f)), new[] { groundBlend }, true, true);
            var slabMats = new[] { SlabMat(0), SlabMat(1), SlabMat(2), sealant };
            Piece(root, "Плиты: чистый бетон", SlabMesh(ConcreteSign, rnd), slabMats, true, true);
            Piece(root, "Плиты под землёй", SlabMesh(-ConcreteSign, rnd), slabMats, true, true);
            Piece(root, "Земля наверху за бетоном", TopMesh(ConcreteSign), new[] { groundBlend }, true, true);
            GrowGrass();
            return root;
        }

        Material SlabMat(int i) { return slabs != null && slabs.Length > i && slabs[i] != null ? slabs[i] : concrete; }

        Material WallMaterial()
        {
            if (tunnelConcrete && network != null && network.library != null && network.library.materials != null && network.library.materials.concrete != null)
                return network.library.materials.concrete;
            return SlabMat(0);
        }

        static float R(System.Random r) { return (float)r.NextDouble(); }
        static float Sqr(float x) { return x * x; }

        static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        Transform Piece(Transform parent, string name, Mesh mesh, Material[] mats, bool collider, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.renderingLayerMask = OutsideMask;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go.transform;
        }

        /// <summary>Mesh under construction: positions, normals, uv, vertex colours, several sub-meshes.</summary>
        public class Geo
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int>[] t;

            public Geo(int subs)
            {
                t = new List<int>[subs];
                for (int i = 0; i < subs; i++) t[i] = new List<int>();
            }

            public int Add(Vector3 p, Vector3 nn, Vector2 u, Color col) { v.Add(p); n.Add(nn); uv.Add(u); c.Add(col); return v.Count - 1; }

            /// <summary>Quad a b c d (around); the side towards <paramref name="facing"/> is the front.</summary>
            public void Quad(int sub, int a, int b, int cc, int d, Vector3 facing)
            {
                var cr = Vector3.Cross(v[b] - v[a], v[cc] - v[a]) + Vector3.Cross(v[cc] - v[a], v[d] - v[a]);
                var l = t[sub];
                if (Vector3.Dot(cr, facing) >= 0f) { l.Add(a); l.Add(b); l.Add(cc); l.Add(a); l.Add(cc); l.Add(d); }
                else { l.Add(a); l.Add(cc); l.Add(b); l.Add(a); l.Add(d); l.Add(cc); }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.SetColors(c);
                m.subMeshCount = t.Length;
                for (int i = 0; i < t.Length; i++) m.SetTriangles(t[i], i);
                if (v.Count > 0) m.RecalculateTangents();
                m.RecalculateBounds();
                return m;
            }
        }

        // ------------------------------------------------------------------ ground surfaces (floor, soil)

        /// <summary>Grid over (s, u) with normals from neighbour samples, so 10 m pieces join without a shading seam.</summary>
        Mesh Surface(string name, float s0, float s1, float step, int nu, System.Func<float, int, float> uAt, Sampler f)
        {
            int ns = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / step));
            int W = nu + 3;
            var P = new Vector3[W * (ns + 3)];
            var C = new Color[P.Length];
            for (int i = -1; i <= ns + 1; i++)
            {
                float s = s0 + (s1 - s0) * i / ns;
                for (int j = -1; j <= nu + 1; j++)
                {
                    float u = uAt(s, j);
                    var xz = PointAt(s, u);
                    Color col;
                    float y = f(s, u, xz, out col);
                    int id = (i + 1) * W + j + 1;
                    P[id] = new Vector3(xz.x, y, xz.y);
                    C[id] = col;
                }
            }
            var g = new Geo(1);
            for (int i = 0; i <= ns; i++)
                for (int j = 0; j <= nu; j++)
                {
                    int id = (i + 1) * W + j + 1;
                    var nrm = Vector3.Cross(P[id + W] - P[id - W], P[id + 1] - P[id - 1]).normalized;
                    if (nrm.y < 0f) nrm = -nrm;
                    g.Add(P[id], nrm, new Vector2(P[id].x, P[id].z), C[id]);
                }
            for (int i = 0; i < ns; i++)
                for (int j = 0; j < nu; j++)
                {
                    int a = i * (nu + 1) + j;
                    g.Quad(0, a, a + nu + 1, a + nu + 2, a + 1, Vector3.up);
                }
            return g.ToMesh(name);
        }

        const float FloorUnder = 2.5f;     // m of floor hidden under the slabs
        const float SlabMaxTurn = 0.35f;   // rad: in bends a slab turns at most this much, so its straight edges stay on the curve

        Mesh FloorMesh(float s0, float s1)
        {
            // the floor runs FloorUnder metres under the slabs on both sides and dips below them there, so no gap shows at the toe
            int nu = Mathf.CeilToInt((Mathf.Max(channelWidth, channelWidthFar) + 2f * FloorUnder) / 0.5f);
            Sampler f = (float s, float u, Vector2 xz, out Color col) =>
            {
                float y = FloorAt(s, u, xz, out col), w = Mathf.Abs(u) - HalfWAt(s);
                return w > 0f ? Mathf.Min(y, groundHeight * w / Run - 0.12f) : y;
            };
            return Surface("CanalFloor", s0, s1, 0.5f, nu, (s, j) => { float hw = HalfWAt(s) + FloorUnder; return Mathf.Lerp(-hw, hw, (float)j / nu); }, f);
        }

        /// <summary>Offset of the low-flow line from the axis: wanders a little, pushed to the outer bank in bends.</summary>
        float Thalweg(float s)
        {
            float k = Mathf.Clamp(CurvatureAt(s + 4f) * 25f, -1f, 1f);
            return HalfWAt(s) * (0.3f * (Mathf.PerlinNoise(s * 0.025f + seed * 1.7f, 7.7f) * 2f - 1f) + 0.4f * k);
        }

        /// <summary>Floor height and layer colours (r coarse gravel, g silt, b dry grass; black = fine gravel).</summary>
        float FloorAt(float s, float u, Vector2 xz, out Color col)
        {
            float x = xz.x, z = xz.y, hw = HalfWAt(s), au = Mathf.Abs(u);
            float k = Mathf.Clamp(CurvatureAt(s) * 25f, -1f, 1f);
            float toward = k >= 0f ? -u : u;                      // + towards the inner bank of the bend
            float bend = Mathf.Abs(k) * Smooth(5f, 0.5f, hw - toward) * (0.6f + 0.4f * Noise01(x, z, 0.15f, 3f));
            float corner = Smooth(5f, 1f, s) * Smooth(hw - 5f, hw - 0.5f, au);
            float toe = 0.35f * Smooth(hw - 1.2f, hw - 0.1f, au);
            float drift = Smooth(0.66f, 0.8f, Noise01(x, z, 0.09f, 91f)) * (0.5f + 0.5f * Noise01(x, z, 0.5f, 17f));
            float silt = siltDeposits * Mathf.Clamp01(Mathf.Max(Mathf.Max(bend, corner), Mathf.Max(toe, drift)) * 1.3f);
            float flow = Smooth(2.2f, 0.3f, Mathf.Abs(u - Thalweg(s)));
            float coarse = coarseGravel * Mathf.Clamp01(flow * 0.9f + 0.7f * Smooth(0.55f, 0.75f, Noise01(x, z, 0.07f, 23f))) * (1f - silt);
            float grassFrac = Mathf.Clamp01(dryGrass * 1.6f) * Smooth(0.35f, 0.65f, Noise01(x, z, 0.3f, 113f)) * (1f - flow);
            col = new Color(coarse, silt * (1f - grassFrac), silt * grassFrac, 1f);

            float n = Noise(x, z, 0.9f, 11f) * 0.5f + Noise(x, z, 0.21f, 37f);
            float fade = Mathf.Clamp01(s / 2f);
            return -0.03f + (gravelNoise * n + floorBumps * Noise(x, z, 0.045f, 53f) - 0.1f * flow + 0.18f * silt + 0.04f * coarse) * fade;
        }

        List<Lobe> MakeLobes(System.Random rnd)
        {
            var list = new List<Lobe>();
            for (int i = 0; i < slumps; i++)
                list.Add(new Lobe
                {
                    s = i == 0 ? 9f : Mathf.Lerp(3f, channelLength - 4f, R(rnd)),
                    half = 2f + 2.5f * R(rnd),
                    reach = i == 0 ? 1.3f : 0.4f + R(rnd),
                    thick = soilDepth * (0.6f + 0.5f * R(rnd)),
                });
            return list;
        }

        /// <summary>Thickness of the slumped soil at s, w (w = distance from the toe, negative on the floor).</summary>
        float LobeAt(List<Lobe> lobes, float s, float w)
        {
            float down = Run - w, best = 0f;
            if (down <= 0f) return 0f;
            foreach (var l in lobes)
            {
                float reach = Mathf.Min(l.reach * Run, Run + fanReach);
                if (down >= reach) continue;
                float spread = 1f + Mathf.Max(0f, down - Run) / Mathf.Max(0.5f, fanReach) * 0.6f;   // the fan widens on the floor
                float a = 1f - Sqr((s - l.s) / (l.half * spread));
                if (a <= 0f) continue;
                float t = down / reach;
                best = Mathf.Max(best, l.thick * Mathf.Sqrt(a) * Smooth(0f, 0.25f, t) * Smooth(1f, 0.7f, t) * (0.5f + 0.5f * t));
            }
            return best;
        }

        Mesh SoilMesh(float s0, float s1, List<Lobe> lobes)
        {
            float side = -ConcreteSign, run = Run, fan = fanReach + 0.4f;
            int nf = Mathf.Max(1, Mathf.CeilToInt(fan / 0.5f)), nsl = Mathf.CeilToInt(run / 0.45f), nt = Mathf.Max(1, Mathf.CeilToInt(soilTop / 0.8f));
            int nu = nf + nsl + nt;
            System.Func<int, float> W = j =>
            {
                if (j <= nf) return Mathf.Lerp(-fan, 0f, (float)j / nf);
                if (j <= nf + nsl) return run * (j - nf) / nsl;
                return run + soilTop * (j - nf - nsl) / nt;
            };
            Sampler f = (float s, float u, Vector2 xz, out Color col) =>
            {
                float x = xz.x, z = xz.y, w = Mathf.Abs(u) - HalfWAt(s), G = groundHeight;
                Color dummy;
                float baseY = w < 0f ? FloorAt(s, u, xz, out dummy) : w < run ? G * w / run : G;
                float topT = 0.13f * (0.8f + 0.4f * Noise01(x, z, 0.4f, 301f));
                float lobe = LobeAt(lobes, s, w), T;
                if (w >= run) T = topT;
                else if (w >= 0f)
                {
                    float patch = soilDepth * 0.6f * Smooth(1f - soilCover - 0.08f, 1f - soilCover + 0.08f, Noise01(x, z, 0.13f, 211f)) * (0.6f + 0.4f * Noise01(x, z, 0.6f, 223f));
                    float band = topT * Smooth(run - 1.4f, run, w);   // soil spilling over the top edge
                    T = Mathf.Max(Mathf.Max(patch, band), lobe);
                }
                else T = lobe;
                float gn = Noise01(x, z, 0.35f, 241f);
                float b = w >= run ? 0.45f + 0.4f * gn : w >= 0f ? (0.15f + 0.45f * gn) * Smooth(0.05f, 0.25f, T) : 0.12f * gn;
                b = T < 0.06f ? 0f : Mathf.Clamp01(b * dryGrass * 1.6f);
                col = new Color(0f, 1f - b, b, 1f);
                return baseY + T - 0.05f;   // no soil: 5 cm under the slabs / gravel, out of sight
            };
            return Surface("CanalSoil", s0, s1, 0.5f, nu, (s, j) => side * (HalfWAt(s) + W(j)), f);
        }

        // ------------------------------------------------------------------ slabs

        Vector3 SlopePoint(float side, float s, float w)
        {
            var xz = PointAt(s, side * (HalfWAt(s) + w));
            return new Vector3(xz.x, groundHeight * w / Run, xz.y);
        }

        Mesh SlabMesh(float side, System.Random rnd)
        {
            var g = new Geo(4);
            float run = Run, ext = 0.6f;
            var joints = SlabJoints(side);
            int rows = Mathf.Max(1, slabRows);
            for (int i = 0; i + 1 < joints.Count; i++)
                for (int r = 0; r < rows; r++)
                {
                    float wa = r == 0 ? -ext : run * r / rows, wb = run * (r + 1) / rows;
                    Slab(g, SlopePoint(side, joints[i], wa), SlopePoint(side, joints[i + 1], wa),
                            SlopePoint(side, joints[i + 1], wb), SlopePoint(side, joints[i], wb), rnd);
                }
            // one continuous backing under all slabs (shared corners), so no light or void shows through the joints
            var down = Vector3.down * 0.05f;
            int b0 = g.v.Count, nw = rows + 1;
            foreach (float s in joints)
                for (int r = 0; r <= rows; r++)
                {
                    var p = SlopePoint(side, s, r == 0 ? -ext : run * r / rows) + down;
                    g.Add(p, Vector3.up, new Vector2(p.x, p.z), Color.white);
                }
            for (int i = 0; i + 1 < joints.Count; i++)
                for (int r = 0; r < rows; r++)
                {
                    int a = b0 + i * nw + r;
                    g.Quad(3, a, a + nw, a + nw + 1, a + 1, Vector3.up);
                }
            return g.ToMesh("CanalSlabs");
        }

        /// <summary>Distances along the channel of the joints between slabs of one bank (also the corners of the crest line).</summary>
        List<float> SlabJoints(float side)
        {
            float run = Run, s0 = -wallThickness, s1 = channelLength + 0.4f;
            var joints = new List<float> { s0 };
            float s = s0;
            while (s < s1 - 0.01f)
            {
                float uMid = side * (HalfWAt(s) + run * 0.5f);
                float stretch = (PointAt(s + 0.5f, uMid) - PointAt(s - 0.5f, uMid)).magnitude;
                float ds = slabLength / Mathf.Max(0.2f, stretch);
                float k = Mathf.Max(Mathf.Abs(CurvatureAt(s)), Mathf.Max(Mathf.Abs(CurvatureAt(s + ds * 0.5f)), Mathf.Abs(CurvatureAt(s + ds))));
                if (k > 1e-4f) ds = Mathf.Min(ds, SlabMaxTurn / k);   // short wedge slabs in tight bends
                s = Mathf.Min(s + Mathf.Max(0.3f, ds), s1);
                if (s1 - s < slabLength * 0.3f) s = s1;   // no sliver at the end
                joints.Add(s);
            }
            return joints;
        }

        /// <summary>One slab A B C D (A, B at the bottom): flat top with a chamfer, a gap to its neighbours, sealant under the gap.</summary>
        void Slab(Geo g, Vector3 A, Vector3 B, Vector3 C, Vector3 D, System.Random rnd)
        {
            var N = Vector3.Cross(C - A, D - B).normalized;
            if (N.y < 0f) N = -N;
            float lb = Vector3.Distance(A, B), lt = Vector3.Distance(D, C), ll = Vector3.Distance(A, D), lr = Vector3.Distance(B, C);
            if (lb < 0.05f || lt < 0.05f || ll < 0.05f || lr < 0.05f) return;
            System.Func<float, Vector3[]> ring = d => new[]
            {
                A + (B - A) * (d / lb) + (D - A) * (d / ll),
                B + (A - B) * (d / lb) + (C - B) * (d / lr),
                C + (D - C) * (d / lt) + (B - C) * (d / lr),
                D + (C - D) * (d / lt) + (A - D) * (d / ll),
            };
            float half = slabGap * 0.5f, ch = slabChamfer;
            var o = ring(half);
            var inn = ring(half + ch);
            float ax = R(rnd) - 0.5f, ay = R(rnd) - 0.5f;
            float[] sx = { -1f, 1f, 1f, -1f }, sy = { -1f, -1f, 1f, 1f };
            for (int k = 0; k < 4; k++)
            {
                float tilt = slabTilt * (0.5f + 0.5f * (sx[k] * ax + sy[k] * ay));
                o[k] += N * (tilt - ch);
                inn[k] += N * tilt;
            }
            int sub = rnd.Next(3);
            // slab texture frame: metres along the bottom edge and up the slope, random offset per slab
            var e1 = (B - A).normalized;
            var e2 = (D - A) - e1 * Vector3.Dot(D - A, e1);
            e2.Normalize();
            var off = new Vector2(R(rnd) * 4f, R(rnd) * 4f);
            System.Func<Vector3, Vector2> UV = p => new Vector2(Vector3.Dot(p - A, e1), Vector3.Dot(p - A, e2)) + off;
            var white = Color.white;

            var topN = Vector3.Cross(inn[2] - inn[0], inn[3] - inn[1]).normalized;
            if (Vector3.Dot(topN, N) < 0f) topN = -topN;
            int t0 = g.v.Count;
            for (int k = 0; k < 4; k++) g.Add(inn[k], topN, UV(inn[k]), white);
            g.Quad(sub, t0, t0 + 1, t0 + 2, t0 + 3, N);

            var centre = (inn[0] + inn[1] + inn[2] + inn[3]) * 0.25f;
            for (int k = 0; k < 4; k++)
            {
                int k1 = (k + 1) % 4;
                var mid = (o[k] + o[k1]) * 0.5f;
                var outward = mid - centre;
                outward -= N * Vector3.Dot(outward, N);
                var facing = (N + outward.normalized).normalized;
                int c0 = g.v.Count;
                g.Add(inn[k], facing, UV(inn[k]), white);
                g.Add(inn[k1], facing, UV(inn[k1]), white);
                g.Add(o[k1], facing, UV(o[k1]), white);
                g.Add(o[k], facing, UV(o[k]), white);
                g.Quad(sub, c0, c0 + 1, c0 + 2, c0 + 3, facing);
            }

            // sealant: the whole cell, under the lowest edge of the chamfers
            float drop = ch + 0.006f;
            int s0 = g.v.Count;
            foreach (var p in new[] { A, B, C, D }) g.Add(p - N * drop, N, UV(p), white);
            g.Quad(3, s0, s0 + 1, s0 + 2, s0 + 3, N);
        }

        // ------------------------------------------------------------------ ground on top of the clean concrete bank

        /// <summary>Flat ground beyond the crest of one bank: the crest line, then back to the wall line (a convex area for the default course).</summary>
        Mesh TopMesh(float side)
        {
            // the crest line goes through the slab joints, so the ground meets the straight top edges of the slabs exactly
            var joints = SlabJoints(side);
            float G = groundHeight, L = joints[joints.Count - 1], s0 = joints[0];
            var poly = new List<Vector2>();
            foreach (float s in joints) poly.Add(PointAt(s, side * CrestAt(s)));
            var e = PointAt(L, side * (CrestAt(L) + 12f));
            poly.Add(e);
            if (e.y > -wallThickness) poly.Add(new Vector2(e.x, -wallThickness));
            poly.Add(PointAt(s0, side * (CrestAt(0f) + 12f)));
            for (int i = poly.Count - 1; i > 0; i--) if ((poly[i] - poly[i - 1]).sqrMagnitude < 1e-4f) poly.RemoveAt(i);
            var g = new Geo(1);
            var col = new Color(0f, 0.55f, 0.45f, 1f);
            foreach (var p in poly) g.Add(new Vector3(p.x, G, p.y), Vector3.up, p, col);
            Triangulate(poly, g.t[0]);   // ear clipping: the area is not convex inside a bend
            return g.ToMesh("CanalTopGround");
        }

        /// <summary>Ear clipping of a simple polygon (XZ); triangles face up.</summary>
        static void Triangulate(List<Vector2> poly, List<int> tris)
        {
            int n = poly.Count;
            if (n < 3) return;
            float area = 0f;
            for (int i = 0; i < n; i++) { var a = poly[i]; var b = poly[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
            var idx = new List<int>();
            for (int i = 0; i < n; i++) idx.Add(area > 0f ? i : n - 1 - i);   // counter-clockwise in (x, z)
            System.Func<Vector2, Vector2, Vector2, float> cross = (a, b, c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            int guard = 0;
            while (idx.Count > 3 && guard++ < n * n)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                    if (cross(a, b, c) <= 1e-6f) continue;   // reflex corner
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        var p = poly[j];
                        if (cross(a, b, p) >= 0f && cross(b, c, p) >= 0f && cross(c, a, p) >= 0f) { inside = true; break; }
                    }
                    if (inside) continue;
                    tris.Add(ia); tris.Add(ic); tris.Add(ib);   // (x, z) counter-clockwise -> clockwise seen from above = front face up
                    idx.RemoveAt(i);
                    cut = true;
                    break;
                }
                if (!cut) break;
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[2]); tris.Add(idx[1]); }
        }

        // ------------------------------------------------------------------ culvert at the far end

        void CulvertFrame(out Vector3 o, out Vector3 fwd, out Vector3 right, out float uMax)
        {
            float L = channelLength;
            var p = PointAt(L, 0f);
            var d = DirAt(L);
            o = new Vector3(p.x, 0f, p.y);
            fwd = new Vector3(d.x, 0f, d.y);
            right = new Vector3(d.y, 0f, -d.x);
            uMax = CrestAt(L) + Mathf.Max(soilTop, 8f);
        }

        Mesh CulvertMesh()
        {
            var g = new Geo(2);
            Vector3 O, F, Rt;
            float uMax;
            CulvertFrame(out O, out F, out Rt, out uMax);
            float G = groundHeight, top = G + parapetHeight, y0 = -0.5f, th = wallThickness, depth = 14f;
            int n = culvertBarrels;
            float w = culvertWidth, h = Mathf.Min(culvertHeight, G - 0.4f), pier = 0.6f;
            float total = n * w + Mathf.Max(0, n - 1) * pier;
            System.Func<float, float, float, Vector3> Q = (u, y, d) => O + Rt * u + F * d + Vector3.up * y;
            var white = Color.white;
            System.Action<int, Vector3, Vector3, Vector3, Vector3, Vector3> quad = (sub, a, b, c, d, facing) =>
            {
                int i = g.v.Count;
                var nrm = facing.normalized;
                foreach (var p in new[] { a, b, c, d })
                {
                    var local = p - O;
                    var uv = Mathf.Abs(nrm.y) > 0.7f ? new Vector2(Vector3.Dot(local, Rt), Vector3.Dot(local, F)) : new Vector2(Vector3.Dot(local, Rt) + Vector3.Dot(local, F), local.y);
                    g.Add(p, nrm, uv / 1.83f, white);
                }
                g.Quad(sub, i, i + 1, i + 2, i + 3, facing);
            };
            // front face around the openings
            quad(0, Q(-uMax, h, 0f), Q(uMax, h, 0f), Q(uMax, top, 0f), Q(-uMax, top, 0f), -F);
            float cur = -uMax;
            for (int i = 0; i <= n; i++)
            {
                float nxt = i < n ? -total * 0.5f + i * (w + pier) : uMax;
                if (nxt > cur + 1e-3f) quad(0, Q(cur, y0, 0f), Q(nxt, y0, 0f), Q(nxt, h, 0f), Q(cur, h, 0f), -F);
                if (i < n)
                {
                    float a = nxt, b = nxt + w;
                    quad(0, Q(a, y0, 0f), Q(b, y0, 0f), Q(b, 0f, 0f), Q(a, 0f, 0f), -F);
                    // the barrel: concrete lip, then darkness
                    const float lip = 1.2f;
                    quad(0, Q(a, 0f, 0f), Q(b, 0f, 0f), Q(b, 0f, lip), Q(a, 0f, lip), Vector3.up);
                    quad(0, Q(a, h, 0f), Q(b, h, 0f), Q(b, h, lip), Q(a, h, lip), Vector3.down);
                    quad(0, Q(a, 0f, 0f), Q(a, h, 0f), Q(a, h, lip), Q(a, 0f, lip), Rt);
                    quad(0, Q(b, 0f, 0f), Q(b, h, 0f), Q(b, h, lip), Q(b, 0f, lip), -Rt);
                    quad(1, Q(a, 0f, lip), Q(b, 0f, lip), Q(b, 0f, depth), Q(a, 0f, depth), Vector3.up);
                    quad(1, Q(a, h, lip), Q(b, h, lip), Q(b, h, depth), Q(a, h, depth), Vector3.down);
                    quad(1, Q(a, 0f, lip), Q(a, h, lip), Q(a, h, depth), Q(a, 0f, depth), Rt);
                    quad(1, Q(b, 0f, lip), Q(b, h, lip), Q(b, h, depth), Q(b, 0f, depth), -Rt);
                    quad(1, Q(a, 0f, depth), Q(b, 0f, depth), Q(b, h, depth), Q(a, h, depth), -F);
                    cur = b;
                }
            }
            // top, back and ends of the wall
            quad(0, Q(-uMax, top, 0f), Q(uMax, top, 0f), Q(uMax, top, th), Q(-uMax, top, th), Vector3.up);
            quad(0, Q(-uMax, G - 0.2f, th), Q(uMax, G - 0.2f, th), Q(uMax, top, th), Q(-uMax, top, th), F);
            quad(0, Q(-uMax, y0, 0f), Q(-uMax, top, 0f), Q(-uMax, top, th), Q(-uMax, y0, th), -Rt);
            quad(0, Q(uMax, y0, 0f), Q(uMax, top, 0f), Q(uMax, top, th), Q(uMax, y0, th), Rt);
            // ground over the barrels
            quad(0, Q(-uMax, G - 0.02f, th), Q(uMax, G - 0.02f, th), Q(uMax, G - 0.02f, depth), Q(-uMax, G - 0.02f, depth), Vector3.up);
            return g.ToMesh("CanalCulvert");
        }

        // ------------------------------------------------------------------ chain-link fences

        void BuildFences(Transform root, System.Random rnd)
        {
            var posts = new MeshKit(1);
            var net = new Geo(1);
            float G = groundHeight, L = channelLength;
            foreach (float side in new[] { 1f, -1f })
            {
                bool soil = side != ConcreteSign;
                float off = soil ? 1.2f : 0.8f;
                var line = new List<Vector3>();
                float s = 0.4f;
                while (s < L - 0.2f)
                {
                    float u = side * (CrestAt(s) + off);
                    var xz = PointAt(s, u);
                    line.Add(new Vector3(xz.x, G + (soil ? 0.08f : 0f), xz.y));
                    float stretch = (PointAt(s + 0.5f, u) - PointAt(s - 0.5f, u)).magnitude;
                    s += 3f / Mathf.Max(0.2f, stretch);
                }
                Fence(posts, net, line, rnd);
            }
            // on the parapet over the mouths
            float xl, xr;
            WallSpan(out xl, out xr);
            Fence(posts, net, Straight(new Vector3(xl + 0.2f, G + parapetHeight, -wallThickness * 0.5f), new Vector3(xr - 0.2f, G + parapetHeight, -wallThickness * 0.5f)), rnd);
            // on the culvert wall
            if (culvertFence)
            {
                Vector3 O, F, Rt;
                float uMax;
                CulvertFrame(out O, out F, out Rt, out uMax);
                var cy = Vector3.up * (G + parapetHeight) + F * (wallThickness * 0.5f);
                Fence(posts, net, Straight(O + cy - Rt * (uMax - 0.2f), O + cy + Rt * (uMax - 0.2f)), rnd);
            }

            Piece(root, "Заборы: сетка", net.ToMesh("CanalFenceNet"), new[] { chainLink }, false, true);
            var postMesh = posts.ToMesh("CanalFencePosts");
            postMesh.hideFlags = HideFlags.None;   // kept in the scene / channel asset
            Piece(root, "Заборы: столбы", postMesh, new[] { fenceSteel }, false, true);
        }

        /// <summary>Only the fences again (floor, soil paint and hand edits stay). Returns the channel root or null.</summary>
        public Transform RebuildFences()
        {
            var root = transform.Find(ChannelRootName);
            if (root == null) return null;
            for (int i = root.childCount - 1; i >= 0; i--)
                if (root.GetChild(i).name.StartsWith("Заборы")) DestroyImmediate(root.GetChild(i).gameObject);
            if (fences) BuildFences(root, new System.Random(seed * 7919 + 101));
            return root;
        }

        static List<Vector3> Straight(Vector3 a, Vector3 b)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / 3f));
            var l = new List<Vector3>();
            for (int i = 0; i <= n; i++) l.Add(Vector3.Lerp(a, b, (float)i / n));
            return l;
        }

        /// <summary>Posts leaning a little, a sagging top rail, chain-link between them; some spans bent out or torn away.</summary>
        void Fence(MeshKit posts, Geo net, List<Vector3> line, System.Random rnd)
        {
            if (line.Count < 2) return;
            float H = fenceHeight, lean = fenceLean * Mathf.Deg2Rad;
            var tops = new Vector3[line.Count];
            for (int i = 0; i < line.Count; i++)
            {
                var along = line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                along.y = 0f;
                along.Normalize();
                var across = new Vector3(along.z, 0f, -along.x);
                var up = (Vector3.up + across * ((R(rnd) * 2f - 1f) * lean) + along * ((R(rnd) * 2f - 1f) * lean * 0.4f)).normalized;
                tops[i] = line[i] + up * H;
                posts.Tube(0, line[i] - Vector3.up * 0.4f, tops[i] + up * 0.05f, 0.03f, 0.028f, 6, true, 1f);
            }
            float dist = 0f;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector3 a = line[i], b = line[i + 1], ta = tops[i], tb = tops[i + 1];
                float len = Vector3.Distance(a, b);
                var tm = (ta + tb) * 0.5f - Vector3.up * (0.02f + 0.06f * R(rnd));
                posts.Tube(0, ta, tm, 0.02f, 0.02f, 5, true, 1f);
                posts.Tube(0, tm, tb, 0.02f, 0.02f, 5, true, 1f);
                float r = R(rnd);
                if (r < fenceDamage * 0.2f) { dist += len; continue; }   // the mesh is gone from this span
                bool bent = r < fenceDamage;
                var along = b - a;
                along.y = 0f;
                along.Normalize();
                var across = new Vector3(along.z, 0f, -along.x) * (R(rnd) < 0.5f ? 1f : -1f);
                var grid = new Vector3[9];
                for (int col = 0; col < 3; col++)
                {
                    float t = col * 0.5f;
                    var bot = Vector3.Lerp(a, b, t) + Vector3.up * 0.03f;
                    var topP = (col == 1 ? tm : Vector3.Lerp(ta, tb, t)) - Vector3.up * 0.04f;
                    if (bent && col == 1) bot += across * 0.35f + Vector3.up * 0.3f;
                    grid[col] = bot;
                    grid[3 + col] = Vector3.Lerp(bot, topP, 0.5f) + (col == 1 ? across * 0.04f : Vector3.zero);
                    grid[6 + col] = topP;
                }
                var nrm = (Vector3.up * 1.5f + across).normalized;   // thin wires: lit like the ground on both sides
                int i0 = net.v.Count;
                for (int k = 0; k < 9; k++)
                {
                    int col = k % 3;
                    float uu = (dist + len * col * 0.5f) / 0.4f, vv = Vector3.Distance(grid[col], grid[k]) / 0.4f;
                    net.Add(grid[k], nrm, new Vector2(uu, vv), Color.white);
                }
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 2; col++)
                    {
                        int q = i0 + row * 3 + col;
                        net.Quad(0, q, q + 1, q + 4, q + 3, across);
                    }
                dist += len;
            }
        }

        // ------------------------------------------------------------------ dry grass tufts from the blue vertex colour

        /// <summary>Regrows the grass tufts from the blue vertex colour of the ground pieces (also after Polybrush painting).</summary>
        public void GrowGrass()
        {
            var root = transform.Find(ChannelRootName);
            if (root == null) return;
            var old = root.Find("Трава");
            if (old != null) DestroyImmediate(old.gameObject);
            var grassRoot = Group(root, "Трава");
            if (grassTuft == null || tuftDensity <= 0f) return;
            var rnd = new System.Random(seed * 31 + 5);
            int k = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.transform.IsChildOf(grassRoot) || mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || groundBlend == null || mr.sharedMaterial != groundBlend) continue;
                var mesh = mf.sharedMesh;
                var extra = mr.additionalVertexStreams;
                var cols = extra != null && extra.vertexCount == mesh.vertexCount && extra.colors.Length == mesh.vertexCount ? extra.colors : mesh.colors;
                if (cols == null || cols.Length != mesh.vertexCount) continue;
                var verts = mesh.vertices;
                var tris = mesh.triangles;
                var toLocal = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var g = new Geo(1);
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    float bl = (cols[tris[i]].b + cols[tris[i + 1]].b + cols[tris[i + 2]].b) / 3f;
                    if (bl < 0.05f) continue;
                    Vector3 a = toLocal.MultiplyPoint3x4(verts[tris[i]]), b = toLocal.MultiplyPoint3x4(verts[tris[i + 1]]), c = toLocal.MultiplyPoint3x4(verts[tris[i + 2]]);
                    float expect = Vector3.Cross(b - a, c - a).magnitude * 0.5f * bl * tuftDensity;
                    int count = (int)expect + (R(rnd) < expect - (int)expect ? 1 : 0);
                    for (int j = 0; j < count; j++)
                    {
                        float r1 = R(rnd), r2 = R(rnd);
                        if (r1 + r2 > 1f) { r1 = 1f - r1; r2 = 1f - r2; }
                        Tuft(g, a + (b - a) * r1 + (c - a) * r2, (0.2f + 0.4f * R(rnd)) * (0.6f + 0.6f * bl), rnd);
                    }
                }
                if (g.v.Count == 0) continue;
                Piece(grassRoot, "Трава " + (k++), g.ToMesh("CanalGrass"), new[] { grassTuft }, false, false);
            }
        }

        static void Tuft(Geo g, Vector3 p, float h, System.Random rnd)
        {
            float w = h * 1.2f, yaw = R(rnd) * Mathf.PI;
            var lean = new Vector3(R(rnd) - 0.5f, 0f, R(rnd) - 0.5f) * (0.3f * h);
            for (int q = 0; q < 2; q++)
            {
                float ang = yaw + q * Mathf.PI * 0.5f;
                var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (w * 0.5f);
                var down = Vector3.up * 0.03f;
                int i = g.Add(p - d - down, Vector3.up, new Vector2(0f, 0f), Color.white);
                g.Add(p + d - down, Vector3.up, new Vector2(1f, 0f), Color.white);
                g.Add(p + d + Vector3.up * h + lean, Vector3.up, new Vector2(1f, 1f), Color.white);
                g.Add(p - d + Vector3.up * h + lean, Vector3.up, new Vector2(0f, 1f), Color.white);
                g.Quad(0, i, i + 1, i + 2, i + 3, new Vector3(-d.z, 0f, d.x));
            }
        }
    }
}
