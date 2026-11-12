using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>
    /// Builds a precast-slab storm tunnel along its child points (any child whose name does not start with "_").
    /// Slabs follow the path at small angles; joints open as wedges with soil behind them and sand in the floor.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, SelectionBase]
    [AddComponentMenu("CarX/Tunnel Path")]
    public class TunnelPath : MonoBehaviour
    {
        [Header("Сечение тоннеля")]
        public TunnelSection section = new TunnelSection();

        [Header("Плиты и стыки")]
        [Tooltip("Длина одной бетонной плиты вдоль хода, м. Короче плиты = плавнее поворот и уже клиновые щели")]
        [Range(0.5f, 6f)] public float segmentLength = 2f;
        [Tooltip("Толщина стенки плиты, видна в торцах стыков, м")]
        [Range(0.08f, 0.6f)] public float slabThickness = 0.25f;
        [Tooltip("Зазор стыка с внутренней стороны поворота, м")]
        [Range(0f, 0.1f)] public float minJointGap = 0.025f;
        [Tooltip("Насколько просела замытая песком щель в полу, м")]
        [Range(0f, 0.15f)] public float jointSink = 0.04f;
        [Tooltip("Фаска на кромках плит, м")]
        [Range(0f, 0.06f)] public float edgeBevel = 0.02f;
        [Tooltip("Высота грязной полосы (след воды) на стенах, м. 0 = без полосы")]
        [Range(0f, 1.5f)] public float dirtLine = 0.9f;

        [Header("Путь (точки = дочерние объекты Point)")]
        [Tooltip("Плавная кривая через точки. Выключено = ломаная")]
        public bool smooth = true;
        [Tooltip("Первая и последняя плиты идут строго по первой и последней паре точек (для стыковки с камерами). Нужно 4+ точки")]
        public bool straightEnds = true;
        [Tooltip("Начало упирается в стену камеры: без торца")]
        public bool startAtWall;
        [Tooltip("Конец упирается в стену камеры: без торца")]
        public bool endAtWall;
        [Tooltip("Заглушить начало бетонной стенкой (тупик)")]
        public bool capStart;
        [Tooltip("Заглушить конец бетонной стенкой (тупик)")]
        public bool capEnd;
        [Tooltip("Случайное зерно: поворот текстур на плитах")]
        public int seed = 1;

        [Header("Вода в канале (для короба с бордюрами)")]
        public bool channelWater = true;
        [Range(0.01f, 0.14f)] public float waterDepth = 0.05f;

        [Header("Подъёмы наверх: ливнёвки и люки")]
        public List<ShaftSpec> shafts = new List<ShaftSpec>();

        [Header("Материалы (пусто = стандартные)")]
        public TunnelMaterials materials;

        [SerializeField, HideInInspector] List<GameObject> shaftObjects = new List<GameObject>();
        [SerializeField, HideInInspector] List<int> shaftHashes = new List<int>();

        public const string GeneratedName = "_Generated", WaterName = "_Water", DressingName = "_Dressing";

        public struct Slab
        {
            public Vector3 a, b, fwd, right, up;
            public float length, tStart, tEnd, startDistance;
            public Vector3 Point(float s, float x, float y) { return a + fwd * s + right * x + up * y; }
            public Vector3 Dir(Vector2 v) { return right * v.x + up * v.y; }
        }

        struct Hole
        {
            public int spec;
            public ShaftOpening op;
            public float cx, cz;
        }

        [NonSerialized] public readonly List<Slab> slabs = new List<Slab>();
        [NonSerialized] public float totalLength;
        [NonSerialized] public int buildVersion;
        /// <summary>Per shaft spec: centre of its ceiling opening in local space (NaN when not placed).</summary>
        [NonSerialized] public readonly List<Vector3> shaftCentres = new List<Vector3>();
        [NonSerialized] public string warning;
        [NonSerialized] bool dirty = true;
        [NonSerialized] Mesh mesh, waterMesh;

        // ------------------------------------------------------------------ lifecycle

        void OnEnable()
        {
            Rebuild(true);
        }

        void OnValidate()
        {
            dirty = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void Update()
        {
            if (!dirty)
            {
                foreach (Transform c in transform)
                    if (c.hasChanged && !c.name.StartsWith("_")) { dirty = true; break; }
            }
            if (dirty) Rebuild(false);
        }

        void OnDestroy()
        {
            if (mesh != null) DestroyImmediate(mesh);
            if (waterMesh != null) DestroyImmediate(waterMesh);
        }

        public void MarkDirty() { dirty = true; }

        public List<Transform> Points()
        {
            var list = new List<Transform>();
            foreach (Transform c in transform)
                if (!c.name.StartsWith("_")) list.Add(c);
            return list;
        }

        public Transform GetChild(string name, bool create)
        {
            var t = transform.Find(name);
            if (t == null && create)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                t = go.transform;
            }
            return t;
        }

        // ------------------------------------------------------------------ rebuild

        public void Rebuild(bool forceShafts)
        {
            dirty = false;
            warning = null;
            var pts = Points();
            foreach (var p in pts) p.hasChanged = false;
            var mats = TunnelMaterials.Resolve(materials);

            slabs.Clear();
            totalLength = 0f;
            if (pts.Count >= 2)
            {
                var local = new List<Vector3>(pts.Count);
                foreach (var p in pts) local.Add(transform.InverseTransformPoint(p.position));
                BuildSlabs(local);
            }
            else warning = "Нужно минимум 2 точки (дочерние объекты).";

            var prof = section.Build(dirtLine);
            var holes = PlanShafts(prof);

            // tunnel body
            var kit = new MeshKit(5);
            BuildTunnel(kit, prof, holes);
            var gen = GetChild(GeneratedName, true).gameObject;
            if (mesh == null) mesh = new Mesh { name = name + "_Tunnel", hideFlags = HideFlags.DontSave };
            kit.Fill(mesh);
            var mf = gen.GetComponent<MeshFilter>();
            if (mf == null) mf = gen.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = gen.GetComponent<MeshRenderer>();
            if (mr == null) mr = gen.AddComponent<MeshRenderer>();
            if (mats != null) mr.sharedMaterials = mats.TunnelSet();
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var mc = gen.GetComponent<MeshCollider>();
            if (mc == null) mc = gen.AddComponent<MeshCollider>();
            mc.sharedMesh = null;
            if (kit.VertexCount > 0) mc.sharedMesh = mesh;

            BuildWater(mats);
            SyncShafts(holes, mats, forceShafts);
            SnapDressing(mc);
            buildVersion++;
        }

        // ------------------------------------------------------------------ path sampling

        void BuildSlabs(List<Vector3> p)
        {
            var q = SamplePath(p);
            float dist = 0f;
            Vector3 lastRight = Vector3.right;
            for (int i = 0; i + 1 < q.Count; i++)
            {
                Vector3 a = q[i], b = q[i + 1];
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 0.05f) continue;
                Vector3 fwd = d / len;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                if (right.sqrMagnitude < 1e-4f) right = lastRight; else right.Normalize();
                Vector3 up = Vector3.Cross(fwd, right).normalized;
                lastRight = right;
                slabs.Add(new Slab { a = a, b = b, fwd = fwd, right = right, up = up, length = len, startDistance = dist });
                dist += len;
            }
            totalLength = dist;

            List<int> kind; int ceil;
            var outline = section.BaseOutline(out kind, out ceil);
            for (int k = 1; k < slabs.Count; k++)
            {
                var s0 = slabs[k - 1];
                var s1 = slabs[k];
                float t = minJointGap * 0.5f;
                float ang = Vector3.Angle(s0.fwd, s1.fwd) * Mathf.Deg2Rad;
                if (ang > 1e-4f)
                {
                    Vector3 n = (s1.fwd - s0.fwd).normalized;
                    float nx = Vector3.Dot(n, s0.right), ny = Vector3.Dot(n, s0.up);
                    float e = 0f;
                    foreach (var v in outline) e = Mathf.Max(e, v.x * nx + v.y * ny);
                    t += e * Mathf.Tan(Mathf.Min(ang, 1.2f) * 0.5f);
                }
                t = Mathf.Min(t, 0.45f * Mathf.Min(s0.length, s1.length));
                s0.tEnd = t;
                s1.tStart = t;
                slabs[k - 1] = s0;
                slabs[k] = s1;
            }
        }

        List<Vector3> SamplePath(List<Vector3> p)
        {
            var q = new List<Vector3>();
            int n = p.Count;
            float L = Mathf.Max(0.5f, segmentLength);
            if (!smooth || n == 2)
            {
                for (int i = 0; i + 1 < n; i++) AddStraight(q, p[i], p[i + 1], L);
                q.Add(p[n - 1]);
                return q;
            }
            if (straightEnds && n >= 4)
            {
                AddStraight(q, p[0], p[1], L);
                AddResampled(q, Dense(p, 1, n - 2), L);
                AddStraight(q, p[n - 2], p[n - 1], L);
            }
            else AddResampled(q, Dense(p, 0, n - 1), L);
            q.Add(p[n - 1]);
            return q;
        }

        static void AddStraight(List<Vector3> q, Vector3 a, Vector3 b, float L)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / L));
            for (int k = 0; k < count; k++) q.Add(Vector3.Lerp(a, b, (float)k / count));
        }

        static void AddResampled(List<Vector3> q, List<Vector3> dense, float L)
        {
            if (dense.Count < 2) return;
            var cum = new float[dense.Count];
            for (int i = 1; i < dense.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(dense[i - 1], dense[i]);
            float total = cum[dense.Count - 1];
            if (total < 1e-3f) return;
            int count = Mathf.Max(1, Mathf.RoundToInt(total / L));
            int seg = 0;
            for (int k = 0; k < count; k++)
            {
                float target = total * k / count;
                while (seg < dense.Count - 2 && cum[seg + 1] < target) seg++;
                float span = cum[seg + 1] - cum[seg];
                float t = span > 1e-6f ? (target - cum[seg]) / span : 0f;
                q.Add(Vector3.Lerp(dense[seg], dense[seg + 1], t));
            }
        }

        List<Vector3> Dense(List<Vector3> p, int i0, int i1)
        {
            var d = new List<Vector3>();
            for (int i = i0; i < i1; i++)
            {
                Vector3 a = p[i], b = p[i + 1];
                float len = Vector3.Distance(a, b);
                Vector3 ta = Tangent(p, i) * len, tb = Tangent(p, i + 1) * len;
                int steps = Mathf.Clamp(Mathf.CeilToInt(len / 0.1f), 4, 400);
                for (int s = 0; s < steps; s++) d.Add(Hermite(a, ta, b, tb, (float)s / steps));
            }
            d.Add(p[i1]);
            return d;
        }

        static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * m0 + (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * m1;
        }

        Vector3 Tangent(List<Vector3> p, int i)
        {
            int n = p.Count;
            if (straightEnds && n >= 4)
            {
                if (i <= 1) return (p[1] - p[0]).normalized;
                if (i >= n - 2) return (p[n - 1] - p[n - 2]).normalized;
            }
            if (i == 0) return (p[1] - p[0]).normalized;
            if (i == n - 1) return (p[n - 1] - p[n - 2]).normalized;
            return (p[i + 1] - p[i - 1]).normalized;
        }

        // ------------------------------------------------------------------ queries

        public int SlabAt(float distance)
        {
            if (slabs.Count == 0) return -1;
            for (int k = 0; k < slabs.Count; k++)
                if (distance < slabs[k].startDistance + slabs[k].length) return k;
            return slabs.Count - 1;
        }

        /// <summary>Distance along the path closest to a world position.</summary>
        public float ClosestDistance(Vector3 worldPos)
        {
            Vector3 p = transform.InverseTransformPoint(worldPos);
            float best = float.MaxValue, bestD = 0f;
            foreach (var s in slabs)
            {
                float t = Mathf.Clamp(Vector3.Dot(p - s.a, s.fwd), 0f, s.length);
                Vector3 c = s.a + s.fwd * t;
                Vector3 off = p - c;
                off -= s.up * Vector3.Dot(off, s.up) * 0.7f;
                float d = off.sqrMagnitude;
                if (d < best) { best = d; bestD = s.startDistance + t; }
            }
            return bestD;
        }

        /// <summary>Local position on the floor centre line at a distance along the path.</summary>
        public bool PointAtDistance(float distance, out Vector3 local, out Slab slab)
        {
            local = Vector3.zero;
            slab = default(Slab);
            int k = SlabAt(distance);
            if (k < 0) return false;
            slab = slabs[k];
            local = slab.Point(Mathf.Clamp(distance - slab.startDistance, 0f, slab.length), 0f, 0f);
            return true;
        }

        // ------------------------------------------------------------------ shafts planning

        Dictionary<int, Hole> PlanShafts(Profile prof)
        {
            var res = new Dictionary<int, Hole>();
            shaftCentres.Clear();
            if (shafts == null) return res;
            for (int i = 0; i < shafts.Count; i++) shaftCentres.Add(new Vector3(float.NaN, 0, 0));
            if (shafts.Count == 0 || slabs.Count == 0) return res;
            float xMin, xMax, yCeil;
            if (!section.FlatCeiling(out xMin, out xMax, out yCeil) || prof.ceilingEdge < 0)
            {
                warning = "У круглой трубы нет плоского потолка: подъёмы наверх не строятся.";
                return res;
            }
            for (int i = 0; i < shafts.Count; i++)
            {
                var s = shafts[i];
                if (s == null) continue;
                int k = SlabAt(s.distance);
                if (k < 0) continue;
                if (res.ContainsKey(k))
                {
                    int alt = k + 1 < slabs.Count && !res.ContainsKey(k + 1) ? k + 1 : (k > 0 && !res.ContainsKey(k - 1) ? k - 1 : -1);
                    if (alt < 0) { warning = "Подъём №" + (i + 1) + " пропущен: на этой плите уже есть подъём."; continue; }
                    k = alt;
                }
                var sl = slabs[k];
                float zLo = sl.tStart + edgeBevel + 0.08f, zHi = sl.length - sl.tEnd - edgeBevel - 0.08f;
                if (zHi - zLo < 0.25f) { warning = "Подъём №" + (i + 1) + " не влезает в плиту: увеличьте длину плиты."; continue; }
                float cx;
                var op = TunnelShaftBuilder.MakeOpening(s, xMin, xMax, (zHi - zLo) * 0.5f, out cx);
                float cz = Mathf.Clamp(s.distance - sl.startDistance, zLo + op.HalfZ, zHi - op.HalfZ);
                res[k] = new Hole { spec = i, op = op, cx = cx, cz = cz };
                shaftCentres[i] = sl.Point(cz, cx, yCeil);
            }
            return res;
        }

        // ------------------------------------------------------------------ meshing

        static Vector3 P(Slab sl, Profile prof, int j, float s, float expand)
        {
            Vector2 v = prof.Offset(j, expand);
            return sl.a + sl.fwd * s + sl.right * v.x + sl.up * v.y;
        }

        void BuildTunnel(MeshKit kit, Profile prof, Dictionary<int, Hole> holes)
        {
            if (slabs.Count == 0) return;
            int n = prof.Count;
            int last = slabs.Count - 1;
            float thick = Mathf.Max(0.05f, slabThickness);
            var rnd = new System.Random(seed * 7919 + 13);
            for (int k = 0; k <= last; k++)
            {
                var sl = slabs[k];
                bool wallStart = k == 0 && startAtWall, wallEnd = k == last && endAtWall;
                float s0 = sl.tStart, s1 = sl.length - sl.tEnd;
                float bev = Mathf.Min(edgeBevel, (s1 - s0) * 0.2f);
                bool flip = rnd.NextDouble() < 0.5;
                float vOff = (float)rnd.NextDouble();

                var ringS = new List<float>(4);
                var ringE = new List<float>(4);
                if (wallStart) { ringS.Add(s0); ringE.Add(0f); }
                else { ringS.Add(s0); ringE.Add(bev); ringS.Add(s0 + bev); ringE.Add(0f); }
                if (wallEnd) { ringS.Add(s1); ringE.Add(0f); }
                else { ringS.Add(s1 - bev); ringE.Add(0f); ringS.Add(s1); ringE.Add(bev); }
                int mainPair = wallStart ? 0 : 1;
                Hole hole;
                bool hasHole = holes.TryGetValue(k, out hole) && prof.ceilingEdge >= 0;

                for (int r = 0; r + 1 < ringS.Count; r++)
                {
                    float sa = ringS[r], sb = ringS[r + 1], ea = ringE[r], eb = ringE[r + 1];
                    if (sb - sa < 1e-4f) continue;
                    for (int e = 0; e < n; e++)
                    {
                        if (hasHole && r == mainPair && e == prof.ceilingEdge)
                        {
                            CeilingWithHole(kit, sl, prof, hole, sa, sb, flip, vOff);
                            continue;
                        }
                        int j0 = e, j1 = (e + 1) % n;
                        Vector3 A = P(sl, prof, j0, sa, ea), B = P(sl, prof, j1, sa, ea);
                        Vector3 C = P(sl, prof, j1, sb, eb), D = P(sl, prof, j0, sb, eb);
                        int sub = prof.edgeSub[e];
                        Vector2 uA, uB, uC, uD;
                        if (sub == 1)
                        {
                            float va = (sl.startDistance + sa) * 0.5f, vb = (sl.startDistance + sb) * 0.5f;
                            uA = new Vector2(prof.points[j0].x * 0.5f, va); uB = new Vector2(prof.points[j1].x * 0.5f, va);
                            uC = new Vector2(prof.points[j1].x * 0.5f, vb); uD = new Vector2(prof.points[j0].x * 0.5f, vb);
                        }
                        else
                        {
                            float ua = sa / sl.length, ub = sb / sl.length;
                            if (flip) { ua = 1f - ua; ub = 1f - ub; }
                            float v0, v1;
                            if (sub == 4) { v0 = prof.points[j0].y * 0.5f; v1 = prof.points[j1].y * 0.5f; }
                            else { v0 = prof.perim[e] * 0.5f + vOff; v1 = prof.perim[e + 1] * 0.5f + vOff; }
                            uA = new Vector2(ua, v0); uB = new Vector2(ua, v1); uC = new Vector2(ub, v1); uD = new Vector2(ub, v0);
                        }
                        if (prof.smooth && sub != 1)
                        {
                            Vector3 n0 = sl.Dir(prof.smoothNormal[j0]), n1 = sl.Dir(prof.smoothNormal[j1]);
                            kit.QuadN(sub, A, B, C, D, uA, uB, uC, uD, n0, n1, n1, n0);
                        }
                        else kit.Quad(sub, A, B, C, D, uA, uB, uC, uD, sl.Dir(prof.EdgeInward(e)));
                    }
                }
                if (!wallStart) Lip(kit, sl, prof, s0, bev, thick, -sl.fwd, vOff);
                if (!wallEnd) Lip(kit, sl, prof, s1, bev, thick, sl.fwd, vOff);
                if (k == 0 && capStart && !startAtWall) Cap(kit, sl, prof, s0 - 0.01f, thick, sl.fwd);
                if (k == last && capEnd && !endAtWall) Cap(kit, sl, prof, s1 + 0.01f, thick, -sl.fwd);
            }
            for (int k = 1; k <= last; k++) JointFill(kit, prof, slabs[k - 1], slabs[k], thick);
        }

        void CeilingWithHole(MeshKit kit, Slab sl, Profile prof, Hole hole, float sa, float sb, bool flip, float vOff)
        {
            int e = prof.ceilingEdge;
            Vector2 pa = prof.points[e], pb = prof.points[(e + 1) % prof.Count];
            float xMin = Mathf.Min(pa.x, pb.x), xMax = Mathf.Max(pa.x, pb.x), y = pa.y;
            var outer = new List<Vector2> { new Vector2(xMin, sa), new Vector2(xMax, sa), new Vector2(xMax, sb), new Vector2(xMin, sb) };
            var c = new Vector2(hole.cx, hole.cz);
            var tris = MeshKit.RingFill(c, hole.op.Polygon(c), outer);
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                Vector2 a = tris[t], b = tris[t + 1], d = tris[t + 2];
                kit.Tri(0, sl.Point(a.y, a.x, y), sl.Point(b.y, b.x, y), sl.Point(d.y, d.x, y),
                        CeilUV(a, sl, prof, pa, flip, vOff), CeilUV(b, sl, prof, pa, flip, vOff), CeilUV(d, sl, prof, pa, flip, vOff), -sl.up);
            }
        }

        static Vector2 CeilUV(Vector2 v, Slab sl, Profile prof, Vector2 pa, bool flip, float vOff)
        {
            float u = v.y / sl.length;
            if (flip) u = 1f - u;
            return new Vector2(u, (prof.perim[prof.ceilingEdge] + Mathf.Abs(v.x - pa.x)) * 0.5f + vOff);
        }

        static void Lip(MeshKit kit, Slab sl, Profile prof, float s, float bev, float thick, Vector3 facing, float vOff)
        {
            int n = prof.Count;
            for (int e = 0; e < n; e++)
            {
                int j0 = e, j1 = (e + 1) % n;
                Vector3 A = P(sl, prof, j0, s, bev), B = P(sl, prof, j1, s, bev);
                Vector3 C = P(sl, prof, j1, s, thick), D = P(sl, prof, j0, s, thick);
                float u0 = prof.perim[e] * 0.5f + vOff, u1 = prof.perim[e + 1] * 0.5f + vOff;
                kit.Quad(0, A, B, C, D, new Vector2(u0, bev * 0.5f), new Vector2(u1, bev * 0.5f), new Vector2(u1, thick * 0.5f), new Vector2(u0, thick * 0.5f), facing);
            }
        }

        static void Cap(MeshKit kit, Slab sl, Profile prof, float s, float thick, Vector3 facing)
        {
            var ring = new List<Vector2>(prof.Count);
            for (int j = 0; j < prof.Count; j++) ring.Add(prof.Offset(j, thick));
            var tris = MeshKit.FanFill(prof.centre, ring);
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                Vector2 a = tris[t], b = tris[t + 1], c = tris[t + 2];
                kit.Tri(0, sl.Point(s, a.x, a.y), sl.Point(s, b.x, b.y), sl.Point(s, c.x, c.y), a * 0.5f, b * 0.5f, c * 0.5f, facing);
            }
        }

        void JointFill(MeshKit kit, Profile prof, Slab A, Slab B, float thick)
        {
            int n = prof.Count;
            float sEnd = A.length - A.tEnd, sStart = B.tStart;
            float depth = thick * 0.6f;
            float va = (A.startDistance + sEnd - 0.12f) / 1.5f, vb = (B.startDistance + sStart + 0.12f) / 1.5f;
            for (int e = 0; e < n; e++)
            {
                int j0 = e, j1 = (e + 1) % n;
                Vector3 a0 = P(A, prof, j0, sEnd - 0.12f, depth), a1 = P(A, prof, j1, sEnd - 0.12f, depth);
                Vector3 b0 = P(B, prof, j0, sStart + 0.12f, depth), b1 = P(B, prof, j1, sStart + 0.12f, depth);
                float p0 = prof.perim[e] / 1.5f, p1 = prof.perim[e + 1] / 1.5f;
                kit.Quad(2, a0, a1, b1, b0, new Vector2(p0, va), new Vector2(p1, va), new Vector2(p1, vb), new Vector2(p0, vb), A.Dir(prof.EdgeInward(e)));
            }
            // washed-in sand in the floor joint
            for (int e = 0; e < n; e++)
            {
                if (prof.edgeSub[e] != 1) continue;
                Vector2 p0 = prof.points[e], p1 = prof.points[(e + 1) % n];
                Vector2 dir = (p1 - p0).normalized;
                Vector2 down = prof.edgeOut[e];
                Vector2 q0 = p0 - dir * 0.06f + down * jointSink, q1 = p1 + dir * 0.06f + down * jointSink;
                Vector3 a0 = A.Point(sEnd - 0.06f, q0.x, q0.y), a1 = A.Point(sEnd - 0.06f, q1.x, q1.y);
                Vector3 b0 = B.Point(sStart + 0.06f, q0.x, q0.y), b1 = B.Point(sStart + 0.06f, q1.x, q1.y);
                float da = (A.startDistance + sEnd - 0.06f) / 1.2f, db = (B.startDistance + sStart + 0.06f) / 1.2f;
                kit.Quad(3, a0, a1, b1, b0, new Vector2(q0.x / 1.2f, da), new Vector2(q1.x / 1.2f, da), new Vector2(q1.x / 1.2f, db), new Vector2(q0.x / 1.2f, db), A.Dir(-down));
            }
        }

        void BuildWater(TunnelMaterials mats)
        {
            var t = transform.Find(WaterName);
            bool want = channelWater && section.profile == TunnelProfile.BoxWithCurbs && slabs.Count > 0 && mats != null && mats.water != null;
            if (!want)
            {
                if (t != null) Kill(t.gameObject);
                return;
            }
            var go = t != null ? t.gameObject : GetChild(WaterName, true).gameObject;
            var kit = new MeshKit(1);
            float half = section.FloorHalfWidth - 0.002f;
            float y = Mathf.Min(waterDepth, section.curbHeight - 0.01f);
            int last = slabs.Count - 1;
            for (int k = 0; k <= last; k++)
            {
                var sl = slabs[k];
                float s0 = k == 0 ? sl.tStart : 0f, s1 = k == last ? sl.length - sl.tEnd : sl.length;
                float d0 = (sl.startDistance + s0) * 0.5f, d1 = (sl.startDistance + s1) * 0.5f;
                kit.Quad(0, sl.Point(s0, -half, y), sl.Point(s0, half, y), sl.Point(s1, half, y), sl.Point(s1, -half, y),
                         new Vector2(-half * 0.5f, d0), new Vector2(half * 0.5f, d0), new Vector2(half * 0.5f, d1), new Vector2(-half * 0.5f, d1), sl.up);
            }
            if (waterMesh == null) waterMesh = new Mesh { name = name + "_Water", hideFlags = HideFlags.DontSave };
            kit.Fill(waterMesh);
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = waterMesh;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mats.water;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ------------------------------------------------------------------ shafts

        void SyncShafts(Dictionary<int, Hole> holes, TunnelMaterials mats, bool force)
        {
            int count = shafts != null ? shafts.Count : 0;
            while (shaftObjects.Count < count) { shaftObjects.Add(null); shaftHashes.Add(0); }
            while (shaftHashes.Count < shaftObjects.Count) shaftHashes.Add(0);
            for (int i = shaftObjects.Count - 1; i >= count; i--)
            {
                if (shaftObjects[i] != null) Kill(shaftObjects[i]);
                shaftObjects.RemoveAt(i);
                shaftHashes.RemoveAt(i);
            }
            var used = new bool[count];
            float xMin, xMax, ceilY;
            section.FlatCeiling(out xMin, out xMax, out ceilY);
            var sec = section;
            foreach (var kv in holes)
            {
                var h = kv.Value;
                var sl = slabs[kv.Key];
                var spec = shafts[h.spec];
                used[h.spec] = true;
                int hash;
                unchecked
                {
                    hash = spec.Hash() * 31 + h.op.Hash();
                    hash = hash * 31 + h.cx.GetHashCode();
                    hash = hash * 31 + section.Hash();
                    hash = hash * 31 + (mats != null ? mats.GetHashCode() : 0);
                }
                var go = shaftObjects[h.spec];
                if (go == null || force || shaftHashes[h.spec] != hash)
                {
                    if (go != null) Kill(go);
                    go = TunnelShaftBuilder.Build(transform, "_Shaft_" + h.spec, Vector3.zero, Quaternion.identity, ceilY, h.cx, h.op, spec,
                                                  y => sec.WallXAt(y), x => sec.FloorHeightAt(x), mats);
                    shaftObjects[h.spec] = go;
                    shaftHashes[h.spec] = hash;
                }
                go.transform.localPosition = sl.Point(h.cz, h.cx, 0f);
                go.transform.localRotation = Quaternion.LookRotation(sl.fwd, sl.up);
            }
            for (int i = 0; i < count; i++)
                if (!used[i] && shaftObjects[i] != null) { Kill(shaftObjects[i]); shaftObjects[i] = null; }
            var orphans = new List<GameObject>();
            foreach (Transform c in transform)
                if (c.name.StartsWith("_Shaft_") && !shaftObjects.Contains(c.gameObject)) orphans.Add(c.gameObject);
            foreach (var o in orphans) Kill(o);
        }

        void SnapDressing(MeshCollider col)
        {
            var d = transform.Find(DressingName);
            if (d == null || col == null || col.sharedMesh == null) return;
            foreach (Transform c in d)
            {
                var pos = c.position;
                RaycastHit hit;
                if (col.Raycast(new Ray(pos + Vector3.up * 1.2f, Vector3.down), out hit, 3f))
                {
                    var dressing = c.GetComponent<IFloorDressing>();
                    float offset = dressing != null ? dressing.FloorOffset : 0f;
                    c.position = new Vector3(pos.x, hit.point.y + offset, pos.z);
                }
            }
        }

        static void Kill(GameObject g)
        {
            if (g == null) return;
            if (Application.isPlaying) Destroy(g); else DestroyImmediate(g);
        }
    }

    /// <summary>Objects that sit on the tunnel floor and follow it after rebuilds.</summary>
    public interface IFloorDressing
    {
        float FloorOffset { get; }
    }
}
