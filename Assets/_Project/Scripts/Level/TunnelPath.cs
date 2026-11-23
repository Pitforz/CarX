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
        [Tooltip("Ширина шва между плитами, м (одинаковая по всему кругу, и на поворотах)")]
        [Range(0f, 0.03f)] public float minJointGap = 0.006f;
        [Tooltip("На сколько шов в полу ниже пола, м (не больше 5 мм: пол остаётся одной плоскостью)")]
        [Range(0f, 0.005f)] public float jointSink = 0.001f;
        [Tooltip("Фаска на кромках плит на стенах и потолке, м (на полу фаски нет)")]
        [Range(0f, 0.03f)] public float edgeBevel = 0.006f;
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
        [Tooltip("Без швов между плитами: стыки срезаны под углом, текстура непрерывная")]
        public bool seamless;
        [Tooltip("Случайное зерно: поворот текстур на плитах")]
        public int seed = 1;

        [Header("Вода в канале (для короба с бордюрами)")]
        public bool channelWater = true;
        [Range(0.01f, 0.14f)] public float waterDepth = 0.05f;

        [Header("Подъёмы наверх: ливнёвки и люки")]
        public List<ShaftSpec> shafts = new List<ShaftSpec>();

        [Header("Материалы (пусто = стандартные)")]
        public TunnelMaterials materials;

        /// <summary>Set by TunnelNetwork: ready-made slab joints (local). When present, child points are ignored.</summary>
        [HideInInspector] public List<Vector3> drivenSamples = new List<Vector3>();
        [HideInInspector] public int drivenSignature;
        public bool IsDriven { get { return drivenSamples != null && drivenSamples.Count >= 2; } }
        /// <summary>Set by TunnelNetwork: holes in the side walls where smaller tunnels join this one.</summary>
        [HideInInspector] public List<SideOpening> openings = new List<SideOpening>();
        /// <summary>Set by TunnelNetwork: sharp corners (elbows), joined with a mitre cut instead of a joint.</summary>
        [HideInInspector] public List<Vector3> drivenMiters = new List<Vector3>();
        /// <summary>Set by TunnelNetwork: the start/end is cut along a wall with this normal (zero = square end).</summary>
        [HideInInspector] public Vector3 startPlane, endPlane;
        /// <summary>Set by TunnelNetwork: opening in the end wall (profile coordinates), where another tunnel type goes on.</summary>
        [HideInInspector] public List<Vector2> capHoleStart = new List<Vector2>(), capHoleEnd = new List<Vector2>();
        /// <summary>Set by TunnelNetwork: the start/end enters the side wall of a bigger tunnel through this opening (profile coordinates).
        /// The reveal says how far the walls go on past the end at each height (x = height, y = depth) to meet a leaning main wall.</summary>
        [HideInInspector] public List<Vector2> startOpening = new List<Vector2>(), endOpening = new List<Vector2>();
        [HideInInspector] public List<Vector2> startReveal = new List<Vector2>(), endReveal = new List<Vector2>();

        /// <summary>World positions of the first and last point of the tunnel axis.</summary>
        public bool EndsWorld(out Vector3 a, out Vector3 b)
        {
            a = b = Vector3.zero;
            if (IsDriven)
            {
                a = transform.TransformPoint(drivenSamples[0]);
                b = transform.TransformPoint(drivenSamples[drivenSamples.Count - 1]);
                return true;
            }
            var pts = Points();
            if (pts.Count < 2) return false;
            a = pts[0].position;
            b = pts[pts.Count - 1].position;
            return true;
        }

        [SerializeField, HideInInspector] List<GameObject> shaftObjects = new List<GameObject>();
        [SerializeField, HideInInspector] List<int> shaftHashes = new List<int>();

        public const string GeneratedName = "_Generated", WaterName = "_Water", DressingName = "_Dressing";

        public struct Slab
        {
            public Vector3 a, b, fwd, right, up;
            public float length, tStart, tEnd, startDistance;
            public Vector3 mStart, mEnd;   // mitre plane normals at the ends (zero = square end)
            public bool gapStart, gapEnd;  // mitred joint with a thin seam (bevel and joint fill stay), not a closed mitre
            public Vector3 Point(float s, float x, float y) { return a + fwd * s + right * x + up * y; }
            /// <summary>Like Point, but near a mitred end the cross-section is cut parallel to the mitre plane
            /// (start half of the slab: start mitre, end half: end mitre), so a seam has the same width all round.</summary>
            public Vector3 MPoint(float s, float x, float y)
            {
                var o = right * x + up * y;
                var X = a + fwd * s + o;
                if (s <= length * 0.5f) { if (mStart != Vector3.zero) X -= fwd * (Vector3.Dot(o, mStart) / Vector3.Dot(fwd, mStart)); }
                else if (mEnd != Vector3.zero) X -= fwd * (Vector3.Dot(o, mEnd) / Vector3.Dot(fwd, mEnd));
                return X;
            }
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
        [NonSerialized] Mesh mesh, waterMesh, shellMesh;
        [NonSerialized] TunnelSkin.Part skinPart;
        [NonSerialized] int variants;

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
            if (shellMesh != null) DestroyImmediate(shellMesh);
            TunnelSkin.Remove(skinPart);
            if (skinPart != null && skinPart.mesh != null) DestroyImmediate(skinPart.mesh);
        }

        void OnDisable() { TunnelSkin.Remove(skinPart); }

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
            if (IsDriven)
            {
                BuildSlabsFromSamples(new List<Vector3>(drivenSamples));
                CutEnds();
            }
            else if (pts.Count >= 2)
            {
                var local = new List<Vector3>(pts.Count);
                foreach (var p in pts) local.Add(transform.InverseTransformPoint(p.position));
                BuildSlabs(local);
            }
            else warning = "Нужно минимум 2 точки (дочерние объекты).";

            var prof = section.Build(dirtLine);
            var holes = PlanShafts(prof);

            // tunnel body (sub-meshes 5+ = wall variants, one per slab, picked at random)
            variants = mats != null ? mats.VariantCount : 0;
            var kit = new MeshKit(5 + variants);
            BuildTunnel(kit, prof, holes);
            var gen = GetChild(GeneratedName, true).gameObject;
            if (mesh == null) mesh = new Mesh { name = name + "_Tunnel", hideFlags = HideFlags.DontSave };
            kit.Fill(mesh);
            var mf = gen.GetComponent<MeshFilter>();
            if (mf == null) mf = gen.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = gen.GetComponent<MeshRenderer>();
            if (mr == null) mr = gen.AddComponent<MeshRenderer>();
            if (mats != null) mr.sharedMaterials = mats.TunnelSetWithVariants();
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            TunnelGI.Setup(mr);   // baked bounce light from probe volumes (lighting thread)
            var mc = gen.GetComponent<MeshCollider>();
            if (mc == null) mc = gen.AddComponent<MeshCollider>();
            mc.sharedMesh = null;
            if (kit.VertexCount > 0) mc.sharedMesh = mesh;
            BuildShadowShell(prof, holes, mats);

            BuildWater(mats);
            SyncShafts(holes, mats, forceShafts);
            SnapDressing(mc);
            buildVersion++;
        }

        // ------------------------------------------------------------------ path sampling

        void BuildSlabs(List<Vector3> p)
        {
            BuildSlabsFromSamples(SamplePath(p));
        }

        void BuildSlabsFromSamples(List<Vector3> q)
        {
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
                if (seamless || IsMiter(s0.b))
                {
                    var nm = s0.fwd + s1.fwd;
                    if (nm.sqrMagnitude > 1e-6f)
                    {
                        s0.mEnd = s1.mStart = nm.normalized;
                        s0.tEnd = s1.tStart = 0f;
                        slabs[k - 1] = s0;
                        slabs[k] = s1;
                        continue;
                    }
                }
                float t = Mathf.Max(0.001f, minJointGap) * 0.5f;
                float ang = Vector3.Angle(s0.fwd, s1.fwd) * Mathf.Deg2Rad;
                float e = 0f;
                if (ang > 1e-4f)
                {
                    Vector3 n = (s1.fwd - s0.fwd).normalized;
                    float nx = Vector3.Dot(n, s0.right), ny = Vector3.Dot(n, s0.up);
                    foreach (var v in outline) e = Mathf.Max(e, v.x * nx + v.y * ny);
                }
                // regular joint: both slab ends cut parallel to the bisector plane, so the seam is equally thin
                // on the inside and the outside of a bend (a square cut leaves a wedge there)
                var bis = s0.fwd + s1.fwd;
                float shear = ang < 1.2f ? e * Mathf.Tan(ang * 0.5f) / Mathf.Cos(ang * 0.5f) : float.MaxValue;
                if (bis.sqrMagnitude > 1e-6f && t + shear + edgeBevel < 0.4f * Mathf.Min(s0.length, s1.length))
                {
                    s0.mEnd = s1.mStart = bis.normalized;
                    s0.gapEnd = s1.gapStart = true;
                    s0.tEnd = s1.tStart = t;
                    slabs[k - 1] = s0;
                    slabs[k] = s1;
                    continue;
                }
                if (ang > 1e-4f) t += e * Mathf.Tan(Mathf.Min(ang, 1.2f) * 0.5f);
                t = Mathf.Min(t, 0.45f * Mathf.Min(s0.length, s1.length));
                s0.tEnd = t;
                s1.tStart = t;
                slabs[k - 1] = s0;
                slabs[k] = s1;
            }
        }

        void CutEnds()
        {
            if (!IsDriven || slabs.Count == 0) return;
            if (startPlane != Vector3.zero)
            {
                var s = slabs[0];
                s.mStart = startPlane.normalized;
                s.gapStart = false;
                s.tStart = 0f;
                slabs[0] = s;
            }
            if (endPlane != Vector3.zero)
            {
                var s = slabs[slabs.Count - 1];
                s.mEnd = endPlane.normalized;
                s.gapEnd = false;
                s.tEnd = 0f;
                slabs[slabs.Count - 1] = s;
            }
        }

        bool IsMiter(Vector3 p)
        {
            if (drivenMiters == null || !IsDriven) return false;
            foreach (var m in drivenMiters) if ((m - p).sqrMagnitude < 1e-4f) return true;
            return false;
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
            bool hasCeil = section.FlatCeiling(out xMin, out xMax, out yCeil) && prof.ceilingEdge >= 0;
            bool skippedCeil = false;
            for (int i = 0; i < shafts.Count; i++)
            {
                var s = shafts[i];
                if (s == null) continue;
                int k = SlabAt(s.distance);
                if (k < 0) continue;
                var sl = slabs[k];
                if (!ShaftSpec.IsCeilingRise(s.type))
                {
                    float along = Mathf.Clamp(s.distance - sl.startDistance, 0.05f, Mathf.Max(0.05f, sl.length - 0.05f));
                    shaftCentres[i] = sl.Point(along, 0f, section.Height * 0.5f);
                    continue;
                }
                if (!hasCeil) { skippedCeil = true; continue; }
                if (res.ContainsKey(k))
                {
                    int alt = k + 1 < slabs.Count && !res.ContainsKey(k + 1) ? k + 1 : (k > 0 && !res.ContainsKey(k - 1) ? k - 1 : -1);
                    if (alt < 0) { warning = "Подъём №" + (i + 1) + " пропущен: на этой плите уже есть подъём."; continue; }
                    k = alt;
                    sl = slabs[k];
                }
                float zLo = sl.tStart + edgeBevel + 0.08f, zHi = sl.length - sl.tEnd - edgeBevel - 0.08f;
                if (zHi - zLo < 0.25f) { warning = "Подъём №" + (i + 1) + " не влезает в плиту: увеличьте длину плиты."; continue; }
                float cx;
                var op = TunnelShaftBuilder.MakeOpening(s, xMin, xMax, (zHi - zLo) * 0.5f, out cx);
                float cz = Mathf.Clamp(s.distance - sl.startDistance, zLo + op.HalfZ, zHi - op.HalfZ);
                res[k] = new Hole { spec = i, op = op, cx = cx, cz = cz };
                shaftCentres[i] = sl.Point(cz, cx, yCeil);
            }
            if (skippedCeil && string.IsNullOrEmpty(warning))
                warning = "У круглой трубы нет плоского потолка: подъёмы наверх не строятся.";
            return res;
        }

        // ------------------------------------------------------------------ meshing

        static Vector3 P(Slab sl, Profile prof, int j, float s, float expand)
        {
            Vector2 v = prof.Offset(j, expand);
            return sl.MPoint(s, v.x, v.y);
        }

        /// <summary>Sloping faces next to the floor (the lower bevels of an octagon): their joints are made like the floor's,
        /// flat and filled, so they read as a faint line, not a shadowed groove.</summary>
        const bool flatLowerBevels = false;

        static bool[] LowEdges(Profile prof)
        {
            int n = prof.Count;
            var low = new bool[n];
            // 2026-11-22: the lower bevels get the same joint as the upper ones again (user's call), so none is «low» now
            if (!flatLowerBevels) return low;
            for (int e = 0; e < n; e++)
            {
                if (prof.edgeSub[e] == 1) continue;
                bool nextToFloor = prof.edgeSub[(e + n - 1) % n] == 1 || prof.edgeSub[(e + 1) % n] == 1;
                low[e] = nextToFloor && prof.edgeOut[e].y < -0.3f;
            }
            return low;
        }

        void BuildTunnel(MeshKit kit, Profile prof, Dictionary<int, Hole> holes)
        {
            if (slabs.Count == 0) return;
            var low = LowEdges(prof);
            int n = prof.Count;
            int last = slabs.Count - 1;
            float thick = Mathf.Max(0.05f, slabThickness);
            var rnd = new System.Random(seed * 7919 + 13);
            var vrnd = new System.Random(seed * 104729 + 71);   // separate stream: old tunnels keep their texture layout
            var cuts = PlanCuts();
            for (int k = 0; k <= last; k++)
            {
                var sl = slabs[k];
                List<Cut> slabCuts;
                cuts.TryGetValue(k, out slabCuts);
                bool wallStart = (k == 0 && startAtWall) || (sl.mStart != Vector3.zero && !sl.gapStart);
                bool wallEnd = (k == last && endAtWall) || (sl.mEnd != Vector3.zero && !sl.gapEnd);
                // at a joint between slabs the floor runs flat right up to the seam: no chamfer, no groove
                bool flatS = !wallStart && k > 0, flatE = !wallEnd && k < last;
                float s0 = sl.tStart, s1 = sl.length - sl.tEnd;
                float bev = Mathf.Min(edgeBevel, (s1 - s0) * 0.2f);
                bool flip = rnd.NextDouble() < 0.5;
                float vOff = (float)rnd.NextDouble();
                float uOff = vOff * 7.31f % 1f;
                // wall material of this slab: the base concrete or one of its lighter/darker variants
                int wallSub = 0;
                if (variants > 0) { int pick = vrnd.Next(variants + 1); wallSub = pick == 0 ? 0 : 4 + pick; }

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
                            CeilingWithHole(kit, sl, prof, hole, sa, sb, flip, vOff, wallSub, Mathf.Max(0.5f, segmentLength));
                            continue;
                        }
                        int j0 = e, j1 = (e + 1) % n;
                        int sub = prof.edgeSub[e];
                        int matSub = sub == 0 ? wallSub : sub;
                        float eA = ea, eB = eb;
                        if (sub == 1 || low[e])
                        {
                            if (flatS && r == 0) eA = 0f;
                            if (flatE && r + 2 == ringS.Count) eB = 0f;
                        }
                        Vector3 A = P(sl, prof, j0, sa, eA), B = P(sl, prof, j1, sa, eA);
                        Vector3 C = P(sl, prof, j1, sb, eB), D = P(sl, prof, j0, sb, eB);
                        Vector2 uA, uB, uC, uD;
                        if (sub == 1)
                        {
                            // world-planar: the floor texture runs on unbroken from one tunnel into the next
                            uA = FloorUV(A); uB = FloorUV(B); uC = FloorUV(C); uD = FloorUV(D);
                        }
                        else
                        {
                            // metres along the tunnel / nominal slab length: the same texture density on every slab,
                            // short or long (slabs shortened to fit a tunnel no longer squeeze the texture)
                            float tile = Mathf.Max(0.5f, segmentLength);
                            float ua = sa / tile + uOff, ub = sb / tile + uOff;
                            if (seamless) { ua = (sl.startDistance + sa) / tile; ub = (sl.startDistance + sb) / tile; }
                            else if (flip) { ua = -ua; ub = -ub; }
                            float v0, v1;
                            if (sub == 4) { v0 = prof.points[j0].y * 0.5f; v1 = prof.points[j1].y * 0.5f; }
                            else { v0 = prof.perim[e] * 0.5f + vOff; v1 = prof.perim[e + 1] * 0.5f + vOff; }
                            uA = new Vector2(ua, v0); uB = new Vector2(ua, v1); uC = new Vector2(ub, v1); uD = new Vector2(ub, v0);
                        }
                        if (slabCuts != null)
                        {
                            Vector2 inw = prof.EdgeInward(e);
                            List<List<Vector2>> hit = null;
                            foreach (var cut in slabCuts)
                                if (cut.side * inw.x < -0.05f) (hit ??= new List<List<Vector2>>()).Add(cut.poly);
                            if (hit != null)
                            {
                                QuadCut(kit, matSub, sl, A, B, C, D, uA, uB, uC, uD, sl.Dir(inw), hit);
                                continue;
                            }
                        }
                        if (prof.smooth && sub != 1)
                        {
                            Vector3 n0 = sl.Dir(prof.smoothNormal[j0]), n1 = sl.Dir(prof.smoothNormal[j1]);
                            kit.QuadN(matSub, A, B, C, D, uA, uB, uC, uD, n0, n1, n1, n0);
                        }
                        else kit.Quad(matSub, A, B, C, D, uA, uB, uC, uD, sl.Dir(prof.EdgeInward(e)));
                    }
                }
                if (!wallStart) Lip(kit, sl, prof, s0, bev, thick, -sl.fwd, vOff, flatS, wallSub, low);
                if (!wallEnd) Lip(kit, sl, prof, s1, bev, thick, sl.fwd, vOff, flatE, wallSub, low);
                // where a flat floor edge meets a chamfered wall edge: close the tiny corner between them
                if ((flatS || flatE) && bev > 1e-4f)
                    for (int j = 0; j < n; j++)
                    {
                        int jp = (j + n - 1) % n;
                        if ((prof.edgeSub[jp] == 1 || low[jp]) == (prof.edgeSub[j] == 1 || low[j])) continue;
                        if (flatS) Tri2(kit, P(sl, prof, j, s0, 0f), P(sl, prof, j, s0 + bev, 0f), P(sl, prof, j, s0, bev));
                        if (flatE) Tri2(kit, P(sl, prof, j, s1, 0f), P(sl, prof, j, s1 - bev, 0f), P(sl, prof, j, s1, bev));
                    }
                // dead end: a wall just past the rim; a wall with an opening (meets another tunnel): exactly at the end
                EndWalls(kit, prof, thick, k == 0, k == last);
                if (k == 0 && startReveal != null && startReveal.Count >= 2) Reveal(kit, sl, s0, -1f, startOpening, startReveal);
                if (k == last && endReveal != null && endReveal.Count >= 2) Reveal(kit, sl, s1, 1f, endOpening, endReveal);
            }
            for (int k = 1; k <= last; k++)
                if (slabs[k].mStart == Vector3.zero || slabs[k].gapStart) JointFill(kit, prof, slabs[k - 1], slabs[k], thick);
        }

        /// <summary>Light-tight skin behind the walls (inside the concrete, half a slab out): a closed, matt black sleeve.
        /// Through any slit between faces (slab joints, seams with other pieces) the eye meets black, never the lit world
        /// outside, and it casts shadows, so light from outside the tunnel cannot get in either.
        /// It overlaps across the slab joints; it is open only exactly at the shaft holes and at openings into other tunnels.</summary>
        void BuildShadowShell(Profile prof, Dictionary<int, Hole> holes, TunnelMaterials mats)
        {
            var kit = new MeshKit(1);
            int n = prof.Count;
            float off = Mathf.Max(0.05f, slabThickness) * 0.5f;
            const float ext = 0.04f;   // overlap across a joint: the skins of two slabs never leave a slit between them
            var cuts = PlanCuts();
            int last = slabs.Count - 1;
            // dead ends: the skin runs on past the end wall and is closed behind it
            bool deadS = capStart && !startAtWall && !(capHoleStart != null && capHoleStart.Count >= 3);
            bool deadE = capEnd && !endAtWall && !(capHoleEnd != null && capHoleEnd.Count >= 3);
            float behind = 0.01f + off;
            // the floor plate: a wide flat sheet under the floor skin, carried on past both ends of every slab, so nothing
            // under the tunnel (seams at the floor, the lower corners, joins with other pieces) ever shows the world below
            float pxMin = float.MaxValue, pxMax = float.MinValue, pyMin = float.MaxValue;
            for (int j = 0; j < n; j++) { var v = prof.points[j]; pxMin = Mathf.Min(pxMin, v.x); pxMax = Mathf.Max(pxMax, v.x); pyMin = Mathf.Min(pyMin, v.y); }
            float plateY = pyMin - off - 0.01f;
            pxMin -= off * 2f; pxMax += off * 2f;
            for (int k = 0; k <= last; k++)
            {
                var sl = slabs[k];
                // open ends (another tunnel, a chamber, a T-join) overlap the neighbour too; whatever reaches into its air
                // is trimmed away by TunnelSkin, so nothing of it shows
                float s0 = sl.tStart - (k > 0 ? ext : (deadS ? behind : EndOverlap));
                float s1 = sl.length - sl.tEnd + (k < last ? ext : (deadE ? behind : EndOverlap));
                if (k == 0 && deadS) Cap(kit, sl, prof, sl.tStart - behind, off * 1.2f, sl.fwd, null);
                if (k == last && deadE) Cap(kit, sl, prof, sl.length - sl.tEnd + behind, off * 1.2f, -sl.fwd, null);
                {
                    float pa = sl.tStart - off, pb = sl.length - sl.tEnd + off;
                    kit.Quad(0, sl.MPoint(pa, pxMin, plateY), sl.MPoint(pa, pxMax, plateY), sl.MPoint(pb, pxMax, plateY), sl.MPoint(pb, pxMin, plateY),
                             Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, sl.up);
                }
                List<Cut> sc;
                cuts.TryGetValue(k, out sc);
                Hole hole;
                bool hasHole = holes.TryGetValue(k, out hole) && prof.ceilingEdge >= 0;
                for (int e = 0; e < n; e++)
                {
                    int j1 = (e + 1) % n;
                    if (hasHole && e == prof.ceilingEdge)
                    {
                        SkinCeilingWithHole(kit, sl, prof, hole, s0, s1, off);
                        continue;
                    }
                    Vector3 A = P(sl, prof, e, s0, off), B = P(sl, prof, j1, s0, off);
                    Vector3 C = P(sl, prof, j1, s1, off), D = P(sl, prof, e, s1, off);
                    Vector2 inw = prof.EdgeInward(e);
                    var nrm = sl.Dir(inw);
                    // side openings (T-joins) are not cut out here: the branch's air trims the skin exactly, with no gap round it
                    List<List<Vector2>> hit = null;
                    // faces look into the tunnel: the shadow normal bias moves them outwards, away from the walls
                    if (hit != null) QuadCut(kit, 0, sl, A, B, C, D, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, nrm, hit);
                    else kit.Quad(0, A, B, C, D, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, nrm);
                }
            }
            var go = GetChild("_ShadowShell", true).gameObject;
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            TunnelMaterials.ApplySkin(mr, mats);

            // the skin is trimmed against the air of every piece (this tunnel, its shafts, its neighbours): never seen from inside
            if (skinPart == null) skinPart = new TunnelSkin.Part();
            skinPart.owner = transform;
            skinPart.filter = mf;
            TunnelSkin.TakeRaw(skinPart, kit);
            skinPart.volumes.Clear();
            var poly = new Vector2[n];
            for (int j = 0; j < n; j++) poly[j] = prof.points[j];
            float ceilY = prof.ceilingEdge >= 0 ? prof.points[prof.ceilingEdge].y : 0f;
            for (int k = 0; k <= last; k++)
            {
                var sl = slabs[k];
                // the air of the slab: its profile swept along it, reaching past a mitred end as far as the mitre does,
                // and past an open end of the tunnel (into a reveal, a doorway, a chamber) by a little more than the skin's offset
                float w0 = sl.tStart, w1 = sl.length - sl.tEnd;
                float shS = 0f, shE = 0f;
                for (int j = 0; j < n; j++)
                {
                    var o = sl.right * poly[j].x + sl.up * poly[j].y;
                    if (sl.mStart != Vector3.zero) { float d = Vector3.Dot(sl.fwd, sl.mStart); if (Mathf.Abs(d) > 0.2f) shS = Mathf.Max(shS, Vector3.Dot(o, sl.mStart) / d); }
                    if (sl.mEnd != Vector3.zero) { float d = Vector3.Dot(sl.fwd, sl.mEnd); if (Mathf.Abs(d) > 0.2f) shE = Mathf.Max(shE, -Vector3.Dot(o, sl.mEnd) / d); }
                }
                w0 -= shS + 0.01f;
                w1 += shE + 0.01f;
                if (k == 0 && !deadS) w0 -= OpenEndReach(startReveal, off);
                if (k == last && !deadE) w1 += OpenEndReach(endReveal, off);
                skinPart.volumes.Add(new TunnelSkin.Volume { o = sl.a, u = sl.right, v = sl.up, w = sl.fwd, poly = poly, w0 = w0, w1 = w1 });
                Hole hole;
                if (holes.TryGetValue(k, out hole) && prof.ceilingEdge >= 0)
                    skinPart.volumes.Add(new TunnelSkin.Volume
                    {
                        o = sl.a, u = sl.right, v = sl.fwd, w = sl.up,
                        poly = hole.op.Polygon(new Vector2(hole.cx, hole.cz)).ToArray(), w0 = ceilY - 0.3f, w1 = ceilY + 8f
                    });
            }
            TunnelSkin.Submit(skinPart);
        }

        const float EndOverlap = 0.08f;

        static float OpenEndReach(List<Vector2> reveal, float off)
        {
            float r = off + 0.12f;
            if (reveal != null) foreach (var v in reveal) r = Mathf.Max(r, v.y + 0.05f);
            return r;
        }

        /// <summary>The skin's ceiling over a slab with a shaft: a ring round the opening (the shaft walls pass through it).</summary>
        static void SkinCeilingWithHole(MeshKit kit, Slab sl, Profile prof, Hole hole, float sa, float sb, float off)
        {
            int e = prof.ceilingEdge;
            Vector2 pa = prof.Offset(e, off), pb = prof.Offset((e + 1) % prof.Count, off);
            float xMin = Mathf.Min(pa.x, pb.x), xMax = Mathf.Max(pa.x, pb.x), y = pa.y;
            var outer = new List<Vector2> { new Vector2(xMin, sa), new Vector2(xMax, sa), new Vector2(xMax, sb), new Vector2(xMin, sb) };
            var c = new Vector2(hole.cx, hole.cz);
            var tris = MeshKit.RingFill(c, hole.op.Polygon(c), outer);
            for (int t = 0; t + 2 < tris.Count; t += 3)
                kit.Tri(0, CeilPoint(sl, tris[t], y, sa, sb), CeilPoint(sl, tris[t + 1], y, sa, sb), CeilPoint(sl, tris[t + 2], y, sa, sb),
                        Vector2.zero, Vector2.zero, Vector2.zero, -sl.up);
        }

        /// <summary>A convex polygon pushed outwards from its centre by d.</summary>
        static List<Vector2> Grow(List<Vector2> poly, float d)
        {
            var c = Vector2.zero;
            foreach (var v in poly) c += v;
            c /= Mathf.Max(1, poly.Count);
            var res = new List<Vector2>(poly.Count);
            foreach (var v in poly)
            {
                var dir = v - c;
                float len = dir.magnitude;
                res.Add(len > 1e-5f ? v + dir / len * d : v);
            }
            return res;
        }

        Vector2 FloorUV(Vector3 local)
        {
            var w = transform.TransformPoint(local);
            return new Vector2(w.x * 0.5f, w.z * 0.5f);
        }

        static void Tri2(MeshKit kit, Vector3 a, Vector3 b, Vector3 c)
        {
            var nrm = Vector3.Cross(b - a, c - a);
            if (nrm.sqrMagnitude < 1e-14f) return;
            Vector2 u = Vector2.zero, v = new Vector2(0.01f, 0f), w = new Vector2(0f, 0.01f);
            kit.Tri(0, a, b, c, u, v, w, nrm);
            kit.Tri(0, a, b, c, u, v, w, -nrm);
        }

        void CeilingWithHole(MeshKit kit, Slab sl, Profile prof, Hole hole, float sa, float sb, bool flip, float vOff, int sub, float tile)
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
                kit.Tri(sub, CeilPoint(sl, a, y, sa, sb), CeilPoint(sl, b, y, sa, sb), CeilPoint(sl, d, y, sa, sb),
                        CeilUV(a, sl, prof, pa, flip, vOff, tile), CeilUV(b, sl, prof, pa, flip, vOff, tile), CeilUV(d, sl, prof, pa, flip, vOff, tile), -sl.up);
            }
        }

        /// <summary>Rim corners follow the slab's cut ends (like the rest of the ring); the opening stays as planned.</summary>
        static Vector3 CeilPoint(Slab sl, Vector2 v, float y, float sa, float sb)
        {
            return v.y <= sa + 1e-4f || v.y >= sb - 1e-4f ? sl.MPoint(v.y, v.x, y) : sl.Point(v.y, v.x, y);
        }

        static Vector2 CeilUV(Vector2 v, Slab sl, Profile prof, Vector2 pa, bool flip, float vOff, float tile)
        {
            float u = v.y / tile + vOff * 7.31f % 1f;
            if (flip) u = -u;
            return new Vector2(u, (prof.perim[prof.ceilingEdge] + Mathf.Abs(v.x - pa.x)) * 0.5f + vOff);
        }

        static void Lip(MeshKit kit, Slab sl, Profile prof, float s, float bev, float thick, Vector3 facing, float vOff, bool flatFloor, int sub, bool[] low)
        {
            int n = prof.Count;
            for (int e = 0; e < n; e++)
            {
                int j0 = e, j1 = (e + 1) % n;
                // floor and lower bevels: the end face starts a few cm under the surface (the seam fill covers it).
                // Flush with the floor, its edge shadowed the next slab under the grazing exit light (a band at every joint)
                float e0 = flatFloor && (prof.edgeSub[e] == 1 || low[e]) ? Mathf.Min(0.03f, thick * 0.5f) : bev;
                Vector3 A = P(sl, prof, j0, s, e0), B = P(sl, prof, j1, s, e0);
                Vector3 C = P(sl, prof, j1, s, thick), D = P(sl, prof, j0, s, thick);
                float u0 = prof.perim[e] * 0.5f + vOff, u1 = prof.perim[e + 1] * 0.5f + vOff;
                kit.Quad(sub, A, B, C, D, new Vector2(u0, e0 * 0.5f), new Vector2(u1, e0 * 0.5f), new Vector2(u1, thick * 0.5f), new Vector2(u0, thick * 0.5f), facing);
            }
        }

        /// <summary>End walls of the first and/or last slab: a dead end (a wall just past the rim) or
        /// a wall with an opening (meets another tunnel: exactly at the end).</summary>
        void EndWalls(MeshKit kit, Profile prof, float thick, bool first, bool lastSlab)
        {
            if (slabs.Count == 0) return;
            bool holeS = capHoleStart != null && capHoleStart.Count >= 3, holeE = capHoleEnd != null && capHoleEnd.Count >= 3;
            // (a wall with an opening covers only the inside of the tunnel: nothing of it lies on the other tunnel's walls)
            if (first && capStart && (!startAtWall || holeS))
            {
                var sl = slabs[0];
                float s0 = sl.tStart;
                Cap(kit, sl, prof, startAtWall ? s0 : s0 - 0.01f, startAtWall ? 0f : thick, sl.fwd, capHoleStart);
            }
            if (lastSlab && capEnd && (!endAtWall || holeE))
            {
                var sl = slabs[slabs.Count - 1];
                float s1 = sl.length - sl.tEnd;
                Cap(kit, sl, prof, endAtWall ? s1 : s1 + 0.01f, endAtWall ? 0f : thick, -sl.fwd, capHoleEnd);
            }
        }

        /// <summary>The end walls exactly as in the visible mesh, as world-space triangles (3 points each, facing into the tunnel).
        /// The play-mode collision shell copies them: the player ignores the visible mesh and would walk through a dead end.</summary>
        public void EndWallTriangles(List<Vector3> world)
        {
            if (slabs.Count == 0 || section == null || (!capStart && !capEnd)) return;
            var kit = new MeshKit(1);
            EndWalls(kit, section.Build(dirtLine), Mathf.Max(0.05f, slabThickness), true, true);
            foreach (int i in kit.tris[0]) world.Add(transform.TransformPoint(kit.verts[i]));
        }

        static void Cap(MeshKit kit, Slab sl, Profile prof, float s, float thick, Vector3 facing, List<Vector2> hole)
        {
            var ring = new List<Vector2>(prof.Count);
            for (int j = 0; j < prof.Count; j++) ring.Add(prof.Offset(j, thick));
            List<Vector2> tris;
            if (hole != null && hole.Count >= 3)
            {
                // wall with an opening: fill between the opening and the outer edge
                Vector2 c = Vector2.zero;
                foreach (var v in hole) c += v;
                tris = MeshKit.RingFill(c / hole.Count, hole, ring);
            }
            else tris = MeshKit.FanFill(prof.centre, ring);
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                Vector2 a = tris[t], b = tris[t + 1], c = tris[t + 2];
                kit.Tri(0, sl.MPoint(s, a.x, a.y), sl.MPoint(s, b.x, b.y), sl.MPoint(s, c.x, c.y), a * 0.5f, b * 0.5f, c * 0.5f, facing);
            }
        }

        /// <summary>Walls of this tunnel carried on past its end (dir -1 = before the start, +1 = after the end) by the reveal depth,
        /// so they meet a bigger tunnel's leaning wall exactly: no gap, no part sticking into it.</summary>
        void Reveal(MeshKit kit, Slab sl, float s, float dir, List<Vector2> opening, List<Vector2> reveal)
        {
            if (opening == null || opening.Count < 3) return;
            float line = dirtLine > 0.01f ? dirtLine : -1f;
            var cuts = new List<float>();
            foreach (var r in reveal) cuts.Add(r.x);
            if (line > 0f) cuts.Add(line);
            float perim = 0f;
            for (int i = 0; i < opening.Count; i++)
            {
                Vector2 p = opening[i], q = opening[(i + 1) % opening.Count];
                float len = Vector2.Distance(p, q);
                if (len < 1e-4f) continue;
                // split the edge where the depth changes slope and at the dirt line
                var ts = new List<float> { 0f, 1f };
                if (Mathf.Abs(q.y - p.y) > 1e-4f)
                    foreach (var y in cuts)
                    {
                        float t = (y - p.y) / (q.y - p.y);
                        if (t > 1e-4f && t < 1f - 1e-4f) ts.Add(t);
                    }
                ts.Sort();
                bool floor = Mathf.Abs(p.y) < 1e-3f && Mathf.Abs(q.y) < 1e-3f;
                Vector2 inw = new Vector2(p.y - q.y, q.x - p.x).normalized;   // into the opening (outline is counter-clockwise)
                for (int k = 0; k + 1 < ts.Count; k++)
                {
                    Vector2 a = Vector2.Lerp(p, q, ts[k]), b = Vector2.Lerp(p, q, ts[k + 1]);
                    float ea = Depth(reveal, a.y), eb = Depth(reveal, b.y);
                    if (ea < 1e-3f && eb < 1e-3f) continue;
                    Vector3 A = sl.Point(s, a.x, a.y), B = sl.Point(s, b.x, b.y);
                    Vector3 C = sl.Point(s + dir * eb, b.x, b.y), D = sl.Point(s + dir * ea, a.x, a.y);
                    float pa = (perim + len * ts[k]) * 0.5f, pb = (perim + len * ts[k + 1]) * 0.5f;
                    int sub = floor ? 1 : (line > 0f && Mathf.Max(a.y, b.y) <= line + 1e-3f ? 4 : 0);
                    Vector2 uA, uB, uC, uD;
                    if (floor) { uA = FloorUV(A); uB = FloorUV(B); uC = FloorUV(C); uD = FloorUV(D); }
                    else if (sub == 4) { uA = new Vector2(0f, a.y * 0.5f); uB = new Vector2(0f, b.y * 0.5f); uC = new Vector2(eb * 0.5f, b.y * 0.5f); uD = new Vector2(ea * 0.5f, a.y * 0.5f); }
                    else { uA = new Vector2(0f, pa); uB = new Vector2(0f, pb); uC = new Vector2(eb * 0.5f, pb); uD = new Vector2(ea * 0.5f, pa); }
                    if (ea < 1e-3f) kit.Tri(sub, A, B, C, uA, uB, uC, sl.Dir(inw));
                    else if (eb < 1e-3f) kit.Tri(sub, A, B, D, uA, uB, uD, sl.Dir(inw));
                    else kit.Quad(sub, A, B, C, D, uA, uB, uC, uD, sl.Dir(inw));
                }
                perim += len;
            }
        }

        static float Depth(List<Vector2> reveal, float y)
        {
            if (y <= reveal[0].x) return reveal[0].y;
            for (int i = 0; i + 1 < reveal.Count; i++)
                if (y <= reveal[i + 1].x)
                {
                    float span = reveal[i + 1].x - reveal[i].x;
                    return span > 1e-6f ? Mathf.Lerp(reveal[i].y, reveal[i + 1].y, (y - reveal[i].x) / span) : reveal[i + 1].y;
                }
            return reveal[reveal.Count - 1].y;
        }

        void JointFill(MeshKit kit, Profile prof, Slab A, Slab B, float thick)
        {
            int n = prof.Count;
            var low = LowEdges(prof);
            float sEnd = A.length - A.tEnd, sStart = B.tStart;
            float gap = Vector3.Distance(A.Point(sEnd, 0f, 0f), B.Point(sStart, 0f, 0f));
            // grey soil just behind the seam: shallow, so the seam reads as a thin line, not a dark slot
            float depth = Mathf.Min(thick * 0.6f, Mathf.Max(0.015f, gap * 2f + edgeBevel * 2f));
            float ia = Mathf.Min(0.06f, A.length * 0.3f), ib = Mathf.Min(0.06f, B.length * 0.3f);
            float va = (A.startDistance + sEnd - ia) / 1.5f, vb = (B.startDistance + sStart + ib) / 1.5f;
            for (int e = 0; e < n; e++)
            {
                if (prof.edgeSub[e] == 1 || low[e]) continue;   // floor and lower bevel seams are filled flush below
                int j0 = e, j1 = (e + 1) % n;
                Vector3 a0 = P(A, prof, j0, sEnd - ia, depth), a1 = P(A, prof, j1, sEnd - ia, depth);
                Vector3 b0 = P(B, prof, j0, sStart + ib, depth), b1 = P(B, prof, j1, sStart + ib, depth);
                float p0 = prof.perim[e] / 1.5f, p1 = prof.perim[e + 1] / 1.5f;
                kit.Quad(2, a0, a1, b1, b0, new Vector2(p0, va), new Vector2(p1, va), new Vector2(p1, vb), new Vector2(p0, vb), A.Dir(prof.EdgeInward(e)));
            }
            // floor: the seam is filled with the same floor surface, at most a few mm lower, so the floor stays one plane
            // flush: a sunk strip showed as a thin dark line at grazing angles (the eye slipped under the next slab's edge)
            const float sink = 0f;
            for (int e = 0; e < n; e++)
            {
                if (prof.edgeSub[e] != 1) continue;
                Vector2 p0 = prof.points[e], p1 = prof.points[(e + 1) % n];
                Vector2 down = prof.edgeOut[e];
                Vector2 q0 = p0 + down * sink, q1 = p1 + down * sink;
                Vector3 a0 = A.MPoint(sEnd, q0.x, q0.y), a1 = A.MPoint(sEnd, q1.x, q1.y);
                Vector3 b0 = B.MPoint(sStart, q0.x, q0.y), b1 = B.MPoint(sStart, q1.x, q1.y);
                kit.Quad(1, a0, a1, b1, b0, FloorUV(a0), FloorUV(a1), FloorUV(b1), FloorUV(b0), A.Dir(-down));
            }
            // lower bevels: same as the floor, wall concrete a hair lower, a faint ring instead of a shadowed groove
            float lowSink = Mathf.Clamp(jointSink * 2f, 0.0005f, 0.005f);
            float tile = Mathf.Max(0.5f, segmentLength);
            float uu = (A.startDistance + sEnd) / tile;
            for (int e = 0; e < n; e++)
            {
                if (!low[e]) continue;
                Vector2 p0 = prof.points[e], p1 = prof.points[(e + 1) % n];
                Vector2 o = prof.edgeOut[e];
                Vector2 q0 = p0 + o * lowSink, q1 = p1 + o * lowSink;
                Vector3 a0 = A.MPoint(sEnd, q0.x, q0.y), a1 = A.MPoint(sEnd, q1.x, q1.y);
                Vector3 b0 = B.MPoint(sStart, q0.x, q0.y), b1 = B.MPoint(sStart, q1.x, q1.y);
                float v0 = prof.perim[e] * 0.5f, v1 = prof.perim[e + 1] * 0.5f;
                kit.Quad(0, a0, a1, b1, b0, new Vector2(uu, v0), new Vector2(uu, v1), new Vector2(uu + 0.01f, v1), new Vector2(uu + 0.01f, v0), A.Dir(-o));
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
                kit.Quad(0, sl.MPoint(s0, -half, y), sl.MPoint(s0, half, y), sl.MPoint(s1, half, y), sl.MPoint(s1, -half, y),
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

        // ------------------------------------------------------------------ side openings (tunnel-to-tunnel joins)

        struct Cut
        {
            public int side;
            public List<Vector2> poly;   // slab space: x = along the slab from its start, y = height; convex, CCW
        }

        Dictionary<int, List<Cut>> PlanCuts()
        {
            var res = new Dictionary<int, List<Cut>>();
            if (openings == null) return res;
            foreach (var op in openings)
            {
                if (op == null || op.outline == null || op.outline.Count < 3) continue;
                float half = 0f;
                foreach (var v in op.outline) half = Mathf.Max(half, Mathf.Abs(v.x));
                for (int k = 0; k < slabs.Count; k++)
                {
                    var sl = slabs[k];
                    if (sl.startDistance > op.distance + half || sl.startDistance + sl.length < op.distance - half) continue;
                    float c = op.distance - sl.startDistance;
                    var pts = new List<Vector2>(op.outline.Count);
                    foreach (var v in op.outline) pts.Add(new Vector2(c + v.x, v.y));
                    List<Cut> list;
                    if (!res.TryGetValue(k, out list)) { list = new List<Cut>(); res[k] = list; }
                    foreach (var piece in MeshKit.ConvexPieces(pts))
                        list.Add(new Cut { side = op.side >= 0 ? 1 : -1, poly = piece });
                }
            }
            return res;
        }

        public string DebugCuts()
        {
            var cuts = PlanCuts();
            var prof = section.Build(dirtLine);
            string r = name + ": openings " + (openings != null ? openings.Count : -1) + ", slabs " + slabs.Count + ", cut slabs " + cuts.Count;
            foreach (var kv in cuts)
                foreach (var c in kv.Value)
                {
                    r += "\n   slab " + kv.Key + " len " + slabs[kv.Key].length.ToString("0.00") + " side " + c.side + " poly";
                    foreach (var v in c.poly) r += " " + v.ToString("0.00");
                }
            r += "\n   inward:";
            for (int e = 0; e < prof.Count; e++) r += " " + prof.EdgeInward(e).ToString("0.0");
            if (openings != null) foreach (var o in openings) r += "\n   op d=" + o.distance.ToString("0.00") + " side " + o.side;
            return r;
        }

        static List<Vector2> Hull(List<Vector2> p)
        {
            var pts = new List<Vector2>(p);
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    var q = pass == 0 ? pts[i] : pts[pts.Count - 1 - i];
                    while (h.Count >= start + 2 && MeshKit.Cross2(h[h.Count - 1] - h[h.Count - 2], q - h[h.Count - 2]) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(q);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;   // counter-clockwise
        }

        static Vector2 To2(Slab sl, Vector3 p) { var q = p - sl.a; return new Vector2(Vector3.Dot(q, sl.fwd), Vector3.Dot(q, sl.up)); }

        static Vector3 From2(Slab sl, Vector2 v, Vector3 onPlane, Vector3 N, float nr)
        {
            Vector3 b = sl.a + sl.fwd * v.x + sl.up * v.y;
            return b + sl.right * (Vector3.Dot(N, onPlane - b) / nr);
        }

        static bool Bary(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 w)
        {
            float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(den) < 1e-9f) { w = new Vector3(1f, 0f, 0f); return false; }
            float l1 = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / den;
            float l2 = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / den;
            w = new Vector3(l1, l2, 1f - l1 - l2);
            return l1 >= -1e-3f && l2 >= -1e-3f && w.z >= -1e-3f;
        }

        static List<Vector2> ClipHalf(List<Vector2> poly, Vector2 h0, Vector2 h1, bool keepInside)
        {
            var res = new List<Vector2>();
            Vector2 e = h1 - h0;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % poly.Count];
                float sp = MeshKit.Cross2(e, p - h0), sq = MeshKit.Cross2(e, q - h0);
                if (!keepInside) { sp = -sp; sq = -sq; }
                bool pin = sp >= 0f, qin = sq >= 0f;
                if (pin) res.Add(p);
                if (pin != qin) res.Add(Vector2.Lerp(p, q, sp / (sp - sq)));
            }
            return res;
        }

        static float Area2(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) a += MeshKit.Cross2(p[i], p[(i + 1) % p.Count]);
            return Mathf.Abs(a) * 0.5f;
        }

        /// <summary>Convex polygon minus a convex CCW hole, as convex pieces.</summary>
        static void Subtract(List<Vector2> P, List<Vector2> H, List<List<Vector2>> output)
        {
            var rest = P;
            for (int i = 0; i < H.Count && rest.Count >= 3; i++)
            {
                Vector2 h0 = H[i], h1 = H[(i + 1) % H.Count];
                var outside = ClipHalf(rest, h0, h1, false);
                if (outside.Count >= 3 && Area2(outside) > 1e-6f) output.Add(outside);
                rest = ClipHalf(rest, h0, h1, true);
            }
        }

        static void QuadCut(MeshKit kit, int sub, Slab sl, Vector3 A, Vector3 B, Vector3 C, Vector3 D,
                            Vector2 uA, Vector2 uB, Vector2 uC, Vector2 uD, Vector3 facing, List<List<Vector2>> holes)
        {
            Vector3 N = Vector3.Cross(C - A, D - B);
            if (N.sqrMagnitude < 1e-12f) return;
            N.Normalize();
            float nr = Vector3.Dot(N, sl.right);
            if (Mathf.Abs(nr) < 0.05f) { kit.Quad(sub, A, B, C, D, uA, uB, uC, uD, facing); return; }
            Vector2 a = To2(sl, A), b = To2(sl, B), c = To2(sl, C), d = To2(sl, D);
            var pieces = new List<List<Vector2>> { new List<Vector2> { a, b, c, d } };
            foreach (var h in holes)
            {
                var next = new List<List<Vector2>>();
                foreach (var p in pieces) Subtract(p, h, next);
                pieces = next;
            }
            foreach (var p in pieces)
            {
                var w3 = new Vector3[p.Count];
                var uv = new Vector2[p.Count];
                for (int i = 0; i < p.Count; i++)
                {
                    w3[i] = From2(sl, p[i], A, N, nr);
                    Vector3 w;
                    if (Bary(p[i], a, b, c, out w)) uv[i] = uA * w.x + uB * w.y + uC * w.z;
                    else { Bary(p[i], a, c, d, out w); uv[i] = uA * w.x + uC * w.y + uD * w.z; }
                }
                for (int i = 1; i + 1 < p.Count; i++) kit.Tri(sub, w3[0], w3[i], w3[i + 1], uv[0], uv[i], uv[i + 1], facing);
            }
        }

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
                    hash = hash * 31 + TunnelTypeLibrary.Current.DaylightHash();
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
            {
                if (used[i]) continue;
                var spec = shafts[i];
                if (spec == null || ShaftSpec.IsCeilingRise(spec.type)) continue;
                int k = SlabAt(spec.distance);
                if (k < 0) continue;
                var sl = slabs[k];
                float along = Mathf.Clamp(spec.distance - sl.startDistance, 0.05f, Mathf.Max(0.05f, sl.length - 0.05f));
                used[i] = true;
                int hash;
                unchecked
                {
                    hash = spec.Hash() * 31 + section.Hash();
                    hash = hash * 31 + (mats != null ? mats.GetHashCode() : 0);
                }
                var go = shaftObjects[i];
                if (go == null || force || shaftHashes[i] != hash)
                {
                    if (go != null) Kill(go);
                    go = TunnelShaftBuilder.BuildPipeGrate(transform, "_Shaft_" + i, Vector3.zero, Quaternion.identity, section, mats);
                    shaftObjects[i] = go;
                    shaftHashes[i] = hash;
                }
                go.transform.localPosition = sl.Point(along, 0f, 0f);
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
    /// <summary>Hole in a tunnel side wall: outline in (offset along the tunnel, height above its floor).</summary>
    [Serializable]
    public class SideOpening
    {
        public float distance;
        [Tooltip("+1 = правая стена, -1 = левая")] public int side = 1;
        public List<Vector2> outline = new List<Vector2>();
    }

    public interface IFloorDressing
    {
        float FloorOffset { get; }
    }
}
