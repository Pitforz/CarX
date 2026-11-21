using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarX.Level
{
    /// <summary>
    /// Open-air entrance to the tunnels: a dry channel of concrete slabs (gravel floor, the inner bank clean concrete, the outer
    /// bank under slumped soil, fences on top, a culvert under a road at the far end), a concrete headwall with the tunnel mouths
    /// and an apron, the highway on top, the sky and daylight.
    /// Local frame: origin = floor level at the headwall, +Z = out of the tunnels, -Z = under the road.
    ///
    /// The channel (floor, slabs, soil, fences, grass) is built only on request («Пересоздать русло» in the inspector): after
    /// that its meshes stay as they are, so they can be painted with Polybrush (vertex colours of the ground) and edited by hand.
    /// Headwall, apron, road, sky and lights rebuild by themselves.
    ///
    /// Lighting does not touch the tunnels: everything outside lives on its own rendering layer (outsideLayer) and only the
    /// sun / sky lights of this component shine on it. Fog, exposure and vignette switch between the tunnel look and the outdoor
    /// look by the camera position (no code on the camera).
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("CarX/Canal Entrance")]
    public partial class CanalEntrance : MonoBehaviour
    {
        [Header("Связь с тоннелями")]
        [Tooltip("Граф тоннелей. Узлы «Выход наружу» с галкой Exit Open Air у стенки (локальный Z около 0) получают проёмы")]
        public TunnelNetwork network;

        [Header("Русло (после правок нажмите «Пересоздать русло»)")]
        [Tooltip("Ось русла: точки (X, Z) относительно объекта, от стенки по течению. Двигайте их ручками в окне Scene")]
        public List<Vector2> channelPath = new List<Vector2>();
        [Tooltip("Высота земли и шоссе над полом тоннелей, м")]
        [Min(3f)] public float groundHeight = 3.8f;
        [Tooltip("Ширина гравийного дна у стенки с проёмами, м")]
        [Min(4f)] public float channelWidth = 21f;
        [Tooltip("Ширина дна дальше по руслу (русло плавно сужается), м")]
        [Min(4f)] public float channelWidthFar = 14f;
        [Tooltip("Где русло начинает сужаться: расстояние от стенки вдоль русла, м")]
        [Min(0f)] public float narrowFrom = 6f;
        [Tooltip("Где сужение заканчивается, м")]
        [Min(1f)] public float narrowTo = 40f;
        [Tooltip("Длина русла до водопропуска под дорогой, м")]
        [Min(30f)] public float channelLength = 110f;
        [Tooltip("Пологость откосов: метров по горизонтали на 1 м высоты")]
        [Range(0.8f, 3f)] public float slopeRun = 1.5f;
        [Tooltip("Если смотреть из тоннеля: справа чистый бетон, слева плиты под землёй. Сейчас галка снята: бетон слева, на внутренней стороне поворота")]
        public bool concreteOnRight = false;

        [Header("Плиты откосов")]
        [Tooltip("Длина плиты вдоль русла (посередине откоса), м")]
        [Range(1f, 8f)] public float slabLength = 3f;
        [Tooltip("Рядов плит по высоте откоса")]
        [Range(1, 4)] public int slabRows = 2;
        [Tooltip("Шов между плитами, м")]
        [Range(0.004f, 0.05f)] public float slabGap = 0.015f;
        [Tooltip("Фаска по краю плиты, м")]
        [Range(0f, 0.05f)] public float slabChamfer = 0.02f;
        [Tooltip("Плиты чуть перекошены: насколько углы выступают, м")]
        [Range(0f, 0.03f)] public float slabTilt = 0.008f;

        [Header("Земля на плитах (сторона без чистого бетона)")]
        [Tooltip("Сколько откоса покрыто землёй: 0 голые плиты, 1 почти весь")]
        [Range(0f, 1f)] public float soilCover = 0.55f;
        [Tooltip("Толщина земли, м")]
        [Range(0.1f, 0.8f)] public float soilDepth = 0.4f;
        [Tooltip("Оползней земли с бровки (языки до дна)")]
        [Range(0, 20)] public int slumps = 7;
        [Tooltip("Как далеко оползни выползают на дно, м")]
        [Range(0f, 5f)] public float fanReach = 2.5f;
        [Tooltip("Ширина земли наверху за бровкой, м")]
        [Min(1f)] public float soilTop = 8f;

        [Header("Дно (цвета вершин: красный крупный гравий, зелёный ил, синий сухая трава)")]
        [Tooltip("Мелкая неровность гравия, м")]
        [Range(0f, 0.3f)] public float gravelNoise = 0.06f;
        [Tooltip("Крупные пологие бугры на дне, м")]
        [Range(0f, 1f)] public float floorBumps = 0.2f;
        [Tooltip("Крупный гравий вдоль струи воды и пятнами")]
        [Range(0f, 1f)] public float coarseGravel = 0.5f;
        [Tooltip("Наносы ила: у внутреннего берега поворота, в углах у стенки, у подножия откосов, редкие кучки мусора")]
        [Range(0f, 1f)] public float siltDeposits = 0.6f;
        [Tooltip("Сухая жёлтая трава на наносах")]
        [Range(0f, 1f)] public float dryGrass = 0.5f;
        [Tooltip("Пучков травы на 1 м² там, где синяя краска полная (кнопка «Трава по покраске»)")]
        [Range(0f, 20f)] public float tuftDensity = 5f;

        [Header("Заборы из сетки-рабицы")]
        public bool fences = true;
        [Range(1f, 3f)] public float fenceHeight = 1.8f;
        [Tooltip("Наклон столбов, градусы")]
        [Range(0f, 10f)] public float fenceLean = 4f;
        [Tooltip("Доля пролётов с отогнутой или сорванной сеткой")]
        [Range(0f, 1f)] public float fenceDamage = 0f;
        [Tooltip("Забор и на стене водопропуска в дальнем конце русла")]
        public bool culvertFence = false;

        [Header("Водопропуск под дорогой в конце русла")]
        [Range(0, 6)] public int culvertBarrels = 3;
        [Min(1f)] public float culvertWidth = 3f;
        [Min(1f)] public float culvertHeight = 2.4f;

        [Header("Где может ходить игрок")]
        [Tooltip("Невидимые стенки: игрок не уходит от тоннелей дальше Area Depth и не поднимается на откосы")]
        public bool limitArea = true;
        [Tooltip("Как далеко от стенки с проёмами можно отойти (вдоль русла), м")]
        [Min(3f)] public float areaDepth = 15f;
        [Tooltip("Насколько стенка заходит на откос от его подножия, м")]
        [Range(0f, 3f)] public float boundaryInset = 0.4f;
        [Min(2f)] public float boundaryHeight = 6f;

        [Header("Деревья (кнопка «Посадить деревья» ставит их в «Снаружи: объекты»)")]
        public GameObject palmPrefab;
        public GameObject tallPalmPrefab;
        public GameObject treePrefab;
        public GameObject shrubPrefab;
        public int seed = 3;
        [Tooltip("Ближе этого (вдоль русла от стенки) деревья не сажаются, м")]
        public float treesFrom = 25f;
        [Range(0, 40)] public int palms = 6;
        [Range(0, 40)] public int tallPalms = 12;
        [Range(0, 40)] public int trees = 6;
        [Range(0, 200)] public int shrubs = 30;

        [Header("Стенка над проёмами и шоссе")]
        [Tooltip("Высота бетонного бортика над дорогой")]
        public float parapetHeight = 0.9f;
        [Min(0.6f)] public float wallThickness = 0.8f;
        [Tooltip("Запас бетона вокруг проёма")]
        [Min(0.05f)] public float openingMargin = 0.35f;
        [Tooltip("Длина бетонного фартука перед проёмами, м")]
        [Min(0.6f)] public float apronLength = 1.6f;
        [Tooltip("Брать бетон тоннеля из библиотеки типов для торцевой стены. Выкл = как плиты русла (тоннельный материал металлический, на солнце темнеет)")]
        public bool tunnelConcrete = false;
        [Min(6f)] public float roadWidth = 24f;
        [Min(0f)] public float roadSetback = 1.5f;
        [Range(1, 10)] public int lanes = 6;
        [Tooltip("Длина шоссе в каждую сторону от русла, м")]
        [Min(10f)] public float roadExtent = 60f;

        [Header("Материалы")]
        public Material concrete;
        public Material asphalt, roadLine, ground;
        [Tooltip("Земля и гравий (шейдер CarX/CanalGround, слои по цветам вершин)")]
        public Material groundBlend;
        [Tooltip("Три оттенка плит, плиты получают их вперемешку")]
        public Material[] slabs = new Material[3];
        [Tooltip("Тёмный герметик в швах")]
        public Material sealant;
        public Material chainLink, fenceSteel, grassTuft;
        [Tooltip("Темнота внутри водопропуска")]
        public Material dark;
        [Tooltip("Материал неба (шейдер CarX/SkyDome)")]
        public Material skyMaterial;

        [Header("Свет снаружи (на тоннель не действует)")]
        [Tooltip("Номер Rendering Layer для всего, что снаружи (не 4: это слой стен тоннеля). Тоннели на Default, поэтому солнце их не освещает")]
        [Range(3, 31)] public int outsideLayer = 3;
        public float sunYaw = 25f;
        [Tooltip("Высота солнца: 89 = в зените")]
        [Range(3f, 89f)] public float sunPitch = 89f;
        public Color sunColor = new Color(1f, 0.96f, 0.9f);
        [Min(0f)] public float sunIntensity = 22f;
        public Color skyFillColor = new Color(0.72f, 0.82f, 1f);
        [Min(0f)] public float skyFill = 7f;
        [Tooltip("С какой высоты светит небо (подсветка без теней), градусы. Ниже = светлее отвесные стенки")]
        [Range(10f, 90f)] public float skyFillPitch = 30f;
        [Tooltip("Вторая подсветка неба с противоположной стороны, доля от первой. 0 = одна сторона заметно темнее")]
        [Range(0f, 1f)] public float skyFillBalance = 0.6f;
        public Color bounceColor = new Color(0.85f, 0.8f, 0.72f);
        [Tooltip("Свет, отражённый от светлого гравия: подсвечивает отвесные стены и низ крон")]
        [Min(0f)] public float bounce = 20f;
        [Tooltip("Наклон отражённого света над горизонтом, градусы")]
        [Range(0f, 80f)] public float bouncePitch = 15f;

        [Header("Небо (яркость в HDR)")]
        [ColorUsage(false, true)] public Color skyZenith = new Color(4.5f, 9f, 20f);
        [ColorUsage(false, true)] public Color skyHorizon = new Color(16f, 18f, 20.5f);
        [ColorUsage(false, true)] public Color skyBelow = new Color(12f, 12.5f, 13f);
        [ColorUsage(false, true)] public Color sunDiscColor = new Color(80f, 76f, 70f);
        [Tooltip("Сколько неба закрыто облаками: 0 ясно, 1 сплошь")]
        [Range(0f, 1f)] public float cloudCover = 0.25f;
        [Tooltip("Размер облаков: больше = крупнее")]
        [Range(0.2f, 4f)] public float cloudSize = 1f;
        [Tooltip("Скорость ветра (облака плывут в игре и в окне Game)")]
        [Range(0f, 0.05f)] public float cloudSpeed = 0.004f;
        [ColorUsage(false, true)] public Color cloudLit = new Color(24f, 24f, 25f);
        [ColorUsage(false, true)] public Color cloudShade = new Color(13f, 14f, 16f);

        [Header("Переход снаружи - внутрь (по положению камеры)")]
        [Tooltip("Экспозиция снаружи, EV. Внутри у выхода работает адаптация глаза из библиотеки типов")]
        public float outsideExposure = -4.3f;
        [Tooltip("Затемнение по краям кадра снаружи (внутри тоннеля своё, из TunnelPostFX)")]
        [Range(0f, 1f)] public float outsideVignette = 0f;
        [Tooltip("Свечение ярких мест снаружи (Bloom), 0 = нет. Порог подстраивается под экспозицию, иначе всё снаружи светится молочной дымкой")]
        [Range(0f, 1f)] public float outsideBloom = 0.15f;
        [Tooltip("Дымка снаружи (0 = нет)")]
        [Range(0f, 0.05f)] public float outsideFogDensity = 0f;
        [ColorUsage(false, true)] public Color outsideFogColor = new Color(12f, 12.5f, 13f);
        [Min(0.2f)] public float blendInside = 1.5f;
        [Min(0.2f)] public float blendOutside = 4f;

        public const string RootName = "_Canal";
        public uint OutsideMask { get { return 1u << Mathf.Clamp(outsideLayer, 0, 31); } }

        public struct Mouth { public float x, halfW, height; public List<Vector2> outline; }

        [System.NonSerialized] int builtSignature;
        [System.NonSerialized] double nextCheck;
        [System.NonSerialized] float areaCx;
        [System.NonSerialized] readonly List<Mesh> meshes = new List<Mesh>();

        // ------------------------------------------------------------------ lifecycle

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
            HookCameras();
            builtSignature = 0;
            Rebuild();
        }

        void OnDisable()
        {
            all.Remove(this);
            if (all.Count == 0) ResetAtmosphere();
        }

        void OnDestroy() { FreeMeshes(); }

        void OnValidate() { builtSignature = 0; pathDirty = true; }

        void Update()
        {
            if (Application.isPlaying) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (builtSignature != 0 && now < nextCheck) return;
            nextCheck = now + 0.3;
            Rebuild();
        }

        int Signature()
        {
            unchecked
            {
                int h = JsonUtility.ToJson(this).GetHashCode();
                foreach (var m in Mouths()) h = h * 31 + Mathf.RoundToInt(m.x * 100f) * 7 + Mathf.RoundToInt(m.halfW * 100f) * 13 + Mathf.RoundToInt(m.height * 100f);
                h = h * 31 + transform.localToWorldMatrix.GetHashCode();
                if (network != null && network.library != null && network.library.materials != null && network.library.materials.concrete != null)
                    h = h * 31 + network.library.materials.concrete.GetHashCode();
                return h == 0 ? 1 : h;
            }
        }

        public void ForceRebuild() { builtSignature = 0; pathDirty = true; Rebuild(); }

        /// <summary>Headwall, apron, road, sky, lights and the walls of the play area (the channel is separate: <see cref="BuildChannel"/>).</summary>
        public void Rebuild()
        {
            int sig = Signature();
            if (sig == builtSignature) return;
            builtSignature = sig;
            pathDirty = true;
            FreeMeshes();

            var root = transform.Find(RootName);
            if (root == null)
            {
                var go = new GameObject(RootName);
                go.transform.SetParent(transform, false);
                root = go.transform;
            }
            // left-overs of the first prototype (mesh terrain and plants)
            foreach (var old in new[] { "Terrain", "Plants" })
            {
                var t = root.Find(old);
                if (t != null) DestroyImmediate(t.gameObject);
            }

            var mouths = Mouths();
            areaCx = MouthsCentre();
            var wallMat = WallMaterial();

            Part(root, "Headwall", BuildHeadwall(mouths), new[] { wallMat }, true);
            Part(root, "Road", BuildTop(), new[] { ground, asphalt, roadLine }, true);
            BuildSky(root);
            BuildLights(root);
            BuildBounds(root);
        }

        void FreeMeshes()
        {
            foreach (var m in meshes) if (m != null) DestroyImmediate(m);
            meshes.Clear();
        }

        Mesh Keep(Mesh m) { m.hideFlags = HideFlags.DontSave; meshes.Add(m); return m; }

        void Part(Transform root, string name, Mesh mesh, Material[] mats, bool collider)
        {
            var t = Child(root, name);
            var mf = Get<MeshFilter>(t);
            var mr = Get<MeshRenderer>(t);
            mf.sharedMesh = mesh;
            mr.sharedMaterials = mats;
            mr.renderingLayerMask = OutsideMask;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var mc = t.GetComponent<MeshCollider>();
            if (collider)
            {
                if (mc == null) mc = t.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = null;
                mc.sharedMesh = mesh;
            }
            else if (mc != null) DestroyImmediate(mc);
        }

        // ------------------------------------------------------------------ tunnel mouths

        public List<Mouth> Mouths()
        {
            var list = new List<Mouth>();
            if (network == null) return list;
            foreach (var n in network.nodes)
            {
                if (n.kind != GraphNodeKind.Exit || !n.exitOpenAir) continue;
                var edges = network.EdgesOf(n.id);
                if (edges.Count != 1) continue;
                var type = network.TypeOf(edges[0]);
                if (type == null || type.section == null) continue;
                var lp = transform.InverseTransformPoint(network.World(n.position));
                if (Mathf.Abs(lp.z) > 1.5f || Mathf.Abs(lp.y) > 0.5f) continue;
                List<int> kind; int ce;
                var outline = type.section.BaseOutline(out kind, out ce);
                var m = new Mouth { x = lp.x, outline = outline };
                foreach (var p in outline) { m.halfW = Mathf.Max(m.halfW, Mathf.Abs(p.x)); m.height = Mathf.Max(m.height, p.y); }
                list.Add(m);
            }
            list.Sort((a, b) => a.x.CompareTo(b.x));
            return list;
        }

        /// <summary>Centre of the mouths along the wall (local X).</summary>
        public float MouthsCentre()
        {
            var mouths = Mouths();
            if (mouths.Count == 0) return 0f;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var m in mouths) { lo = Mathf.Min(lo, m.x - m.halfW); hi = Mathf.Max(hi, m.x + m.halfW); }
            return (lo + hi) * 0.5f;
        }

        /// <summary>Default course: leaves the wall at 35° to the left, then turns further left.</summary>

        /// <summary>Default course: 8 m straight out of the wall, a 90° turn to the left (radius 18 m), then straight on.</summary>
        public void ResetPath()
        {
            float cx = MouthsCentre();
            const float straight = 8f, R = 18f;
            channelPath = new List<Vector2> { new Vector2(cx, 0f), new Vector2(cx, straight) };
            var centre = new Vector2(cx - R, straight);
            for (int a = 15; a <= 90; a += 15)
            {
                float r = a * Mathf.Deg2Rad;
                channelPath.Add(centre + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * R);
            }
            var end = centre + new Vector2(0f, R);
            channelPath.Add(end + new Vector2(-30f, 0f));
            channelPath.Add(end + new Vector2(-90f, 0f));
            channelPath.Add(end + new Vector2(-160f, 0f));
            pathDirty = true;
        }

        // ------------------------------------------------------------------ channel course

        [System.NonSerialized] bool pathDirty = true;
        [System.NonSerialized] readonly List<Vector2> pathPts = new List<Vector2>();
        [System.NonSerialized] readonly List<Vector2> pathTan = new List<Vector2>();
        [System.NonSerialized] readonly List<float> pathDist = new List<float>();
        [System.NonSerialized] readonly List<float> pathK = new List<float>();

        void SamplePath()
        {
            if (!pathDirty && pathPts.Count > 1) return;
            pathDirty = false;
            pathPts.Clear();
            pathTan.Clear();
            pathDist.Clear();
            var ctl = new List<Vector2>(channelPath);
            if (ctl.Count == 0) ctl.Add(Vector2.zero);
            if (ctl.Count == 1) ctl.Add(ctl[0] + Vector2.up * 50f);
            // straight on behind the first point and past the last one
            Vector2 d0 = (ctl[1] - ctl[0]).normalized, d1 = (ctl[ctl.Count - 1] - ctl[ctl.Count - 2]).normalized;
            ctl.Insert(0, ctl[0] - d0 * 30f);
            ctl.Add(ctl[ctl.Count - 1] + d1 * 600f);
            for (int i = 0; i < ctl.Count - 1; i++)
            {
                if (i == 0 || i == ctl.Count - 2) { pathPts.Add(ctl[i]); continue; }   // straight extensions: one segment each
                Vector2 p0 = ctl[Mathf.Max(0, i - 1)], p1 = ctl[i], p2 = ctl[i + 1], p3 = ctl[Mathf.Min(ctl.Count - 1, i + 2)];
                float len = (p2 - p1).magnitude;
                int n = Mathf.Clamp(Mathf.CeilToInt(len / 0.75f), 1, 400);
                for (int k = 0; k < n; k++) pathPts.Add(CatmullRom(p0, p1, p2, p3, (float)k / n));
            }
            pathPts.Add(ctl[ctl.Count - 1]);
            float acc = -30f;
            pathDist.Add(acc);
            for (int i = 1; i < pathPts.Count; i++) { acc += (pathPts[i] - pathPts[i - 1]).magnitude; pathDist.Add(acc); }
            // smooth tangents at the samples, so offset lines (banks) have no kinks at the sample points
            for (int i = 0; i < pathPts.Count; i++)
            {
                Vector2 a = pathPts[Mathf.Max(0, i - 1)], b = pathPts[Mathf.Min(pathPts.Count - 1, i + 1)];
                pathTan.Add((b - a).normalized);
            }
            // curvature per sample (over about 2 m), widened to the strongest value nearby; PointAt keeps offsets inside the bend radius
            pathK.Clear();
            int pn = pathPts.Count;
            var raw = new float[pn];
            for (int i = 0; i < pn; i++)
            {
                Vector2 a = pathPts[Mathf.Max(0, i - 3)], b = pathPts[i], c = pathPts[Mathf.Min(pn - 1, i + 3)];
                Vector2 ab = b - a, bc = c - b;
                float den = ab.magnitude * bc.magnitude * (c - a).magnitude;
                raw[i] = den < 1e-9f ? 0f : 2f * (ab.x * bc.y - ab.y * bc.x) / den;
            }
            var widened = new float[pn];
            for (int i = 0; i < pn; i++)
            {
                float best = 0f;
                for (int j = Mathf.Max(0, i - 6); j <= Mathf.Min(pn - 1, i + 6); j++) if (Mathf.Abs(raw[j]) > Mathf.Abs(best)) best = raw[j];
                widened[i] = best;
            }
            for (int i = 0; i < pn; i++)   // and averaged, so the inner bank narrows gradually
            {
                float sum = 0f;
                int j0 = Mathf.Max(0, i - 6), j1 = Mathf.Min(pn - 1, i + 6);
                for (int j = j0; j <= j1; j++) sum += widened[j];
                pathK.Add(sum / (j1 - j0 + 1));
            }
        }

        const float BendLimit = 0.95f, BendKnee = 0.75f;

        /// <summary>Offset towards the centre of a bend, softly kept under the bend radius, so banks never fold over themselves.</summary>
        static float InsideBend(float u, float k)
        {
            float lim = BendLimit / Mathf.Abs(k), a = Mathf.Abs(u) / lim;
            if (a <= BendKnee) return u;
            float x = Mathf.Min((a - BendKnee) / (1f - BendKnee), 10f), e = Mathf.Exp(2f * x);
            return Mathf.Sign(u) * lim * (BendKnee + (1f - BendKnee) * (e - 1f) / (e + 1f));
        }

        /// <summary>Centripetal Catmull-Rom: no loops or overshoot where neighbouring control points are spaced very differently.</summary>
        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t0 = 0f, t1 = t0 + Knot(p0, p1), t2 = t1 + Knot(p1, p2), t3 = t2 + Knot(p2, p3);
            float tt = Mathf.Lerp(t1, t2, t);
            Vector2 a1 = Lerp2(p0, p1, t0, t1, tt), a2 = Lerp2(p1, p2, t1, t2, tt), a3 = Lerp2(p2, p3, t2, t3, tt);
            Vector2 b1 = Lerp2(a1, a2, t0, t2, tt), b2 = Lerp2(a2, a3, t1, t3, tt);
            return Lerp2(b1, b2, t1, t2, tt);
        }

        static float Knot(Vector2 a, Vector2 b) { return Mathf.Sqrt(Mathf.Max(1e-4f, (b - a).magnitude)); }
        static Vector2 Lerp2(Vector2 a, Vector2 b, float ta, float tb, float t) { return a + (b - a) * ((t - ta) / (tb - ta)); }

        /// <summary>Distance along the channel (s, from the wall) and signed distance across it (u, + = right bank looking downstream).</summary>
        public void Project(Vector2 p, out float s, out float u)
        {
            SamplePath();
            float best = float.MaxValue;
            s = 0f; u = 0f;
            for (int i = 0; i < pathPts.Count - 1; i++)
            {
                Vector2 a = pathPts[i], b = pathPts[i + 1], ab = b - a;
                float l2 = ab.sqrMagnitude;
                if (l2 < 1e-8f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                Vector2 q = a + ab * t;
                float d = (p - q).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    s = Mathf.Lerp(pathDist[i], pathDist[i + 1], t);
                    Vector2 right = new Vector2(ab.y, -ab.x);
                    u = Mathf.Sqrt(d) * (Vector2.Dot(p - a, right) >= 0f ? 1f : -1f);
                }
            }
        }

        /// <summary>Point of the channel frame (local XZ): s along the axis from the wall, u across (+ = right bank looking downstream).</summary>
        public Vector2 PointAt(float s, float u)
        {
            SamplePath();
            int lo = 0, hi = pathDist.Count - 2;   // binary search: the segment with pathDist[i] <= s
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (pathDist[mid] <= s) lo = mid; else hi = mid - 1;
            }
            int i = lo;
            float t = Mathf.InverseLerp(pathDist[i], pathDist[i + 1], s);
            Vector2 dir = Vector2.Lerp(pathTan[i], pathTan[i + 1], t).normalized;
            float k = Mathf.Lerp(pathK[i], pathK[i + 1], t);
            if (u * k < 0f) u = InsideBend(u, k);
            return Vector2.Lerp(pathPts[i], pathPts[i + 1], t) + new Vector2(dir.y, -dir.x) * u;
        }

        /// <summary>Direction of the axis at s (local XZ).</summary>
        public Vector2 DirAt(float s)
        {
            return (PointAt(s + 0.5f, 0f) - PointAt(s - 0.5f, 0f)).normalized;
        }

        /// <summary>Half width of the gravel floor at s.</summary>
        public float HalfWAt(float s) { return Mathf.Lerp(channelWidth, channelWidthFar, Smooth(narrowFrom, narrowTo, s)) * 0.5f; }
        /// <summary>Distance from the axis to the top edge of a slope at s.</summary>
        public float CrestAt(float s) { return HalfWAt(s) + Run; }
        float Run { get { return groundHeight * slopeRun; } }
        /// <summary>+1 if the clean concrete bank is the right one (u &gt; 0).</summary>
        public float ConcreteSign { get { return concreteOnRight ? 1f : -1f; } }

        /// <summary>Signed curvature of the axis, 1/m (+ = turns left).</summary>
        public float CurvatureAt(float s)
        {
            Vector2 a = PointAt(s - 2f, 0f), b = PointAt(s, 0f), c = PointAt(s + 2f, 0f);
            Vector2 d1 = (b - a).normalized, d2 = (c - b).normalized;
            return (d1.x * d2.y - d1.y * d2.x) * 0.5f;
        }

        float Noise(float x, float z, float f, float off)
        {
            return Mathf.PerlinNoise(x * f + off + seed * 3.17f, z * f + off * 1.7f + seed * 1.31f) * 2f - 1f;
        }

        float Noise01(float x, float z, float f, float off) { return Noise(x, z, f, off) * 0.5f + 0.5f; }

        // ------------------------------------------------------------------ headwall with openings, apron

        Mesh BuildHeadwall(List<Mouth> mouths)
        {
            var kit = new MeshKit(1);
            float G = groundHeight, top = G + parapetHeight, y0 = -0.5f, zf = 0.02f, zb = -wallThickness;
            // the wall spans the channel from crest to crest
            float xl, xr;
            WallSpan(out xl, out xr);

            float cur = xl;
            foreach (var m in mouths)
            {
                float rx0 = Mathf.Max(cur, m.x - m.halfW - openingMargin), rx1 = m.x + m.halfW + openingMargin;
                float ry1 = Mathf.Min(top - 0.1f, m.height + openingMargin);
                if (rx0 > cur + 1e-3f) Front(kit, cur, rx0, y0, top, zf);
                Front(kit, rx0, rx1, ry1, top, zf);
                var outline = new List<Vector2>();
                foreach (var p in m.outline) outline.Add(new Vector2(m.x + p.x, p.y));
                var rect = new List<Vector2> { new Vector2(rx0, y0), new Vector2(rx1, y0), new Vector2(rx1, ry1), new Vector2(rx0, ry1) };
                var tris = MeshKit.RingFill(new Vector2(m.x, m.height * 0.5f), outline, rect);
                for (int i = 0; i + 2 < tris.Count; i += 3)
                    kit.Tri(0, P(tris[i], zf), P(tris[i + 1], zf), P(tris[i + 2], zf), UV(tris[i]), UV(tris[i + 1]), UV(tris[i + 2]), Vector3.forward);
                cur = rx1;
            }
            if (xr > cur + 1e-3f) Front(kit, cur, xr, y0, top, zf);

            kit.Quad(0, new Vector3(xl, G - 0.1f, zb), new Vector3(xr, G - 0.1f, zb), new Vector3(xr, top, zb), new Vector3(xl, top, zb),
                     UV(new Vector2(xl, G)), UV(new Vector2(xr, G)), UV(new Vector2(xr, top)), UV(new Vector2(xl, top)), Vector3.back);
            kit.Quad(0, new Vector3(xl, top, zb), new Vector3(xr, top, zb), new Vector3(xr, top, zf), new Vector3(xl, top, zf),
                     UV(new Vector2(xl, 0f)), UV(new Vector2(xr, 0f)), UV(new Vector2(xr, wallThickness)), UV(new Vector2(xl, wallThickness)), Vector3.up);
            for (int e = 0; e < 2; e++)
            {
                float x = e == 0 ? xl : xr;
                kit.Quad(0, new Vector3(x, y0, zb), new Vector3(x, y0, zf), new Vector3(x, top, zf), new Vector3(x, top, zb),
                         UV(new Vector2(0f, y0)), UV(new Vector2(wallThickness, y0)), UV(new Vector2(wallThickness, top)), UV(new Vector2(0f, top)),
                         e == 0 ? Vector3.left : Vector3.right);
            }
            // apron: a concrete slab in front of the mouths, flush with the tunnel floor
            float ay = 0.005f, al = Mathf.Max(0.6f, apronLength);
            float ax0 = xl, ax1 = xr;
            if (mouths.Count > 0) { ax0 = mouths[0].x - mouths[0].halfW - 1.5f; ax1 = mouths[mouths.Count - 1].x + mouths[mouths.Count - 1].halfW + 1.5f; }
            kit.Quad(0, new Vector3(ax0, ay, zf), new Vector3(ax1, ay, zf), new Vector3(ax1, ay, al), new Vector3(ax0, ay, al),
                     UV(new Vector2(ax0, 0f)), UV(new Vector2(ax1, 0f)), UV(new Vector2(ax1, al)), UV(new Vector2(ax0, al)), Vector3.up);
            kit.Quad(0, new Vector3(ax0, -0.35f, al), new Vector3(ax1, -0.35f, al), new Vector3(ax1, ay, al), new Vector3(ax0, ay, al),
                     UV(new Vector2(ax0, -0.35f)), UV(new Vector2(ax1, -0.35f)), UV(new Vector2(ax1, 0f)), UV(new Vector2(ax0, 0f)), Vector3.forward);
            for (int e = 0; e < 2; e++)
            {
                float x = e == 0 ? ax0 : ax1;
                kit.Quad(0, new Vector3(x, -0.35f, zf), new Vector3(x, -0.35f, al), new Vector3(x, ay, al), new Vector3(x, ay, zf),
                         UV(new Vector2(0f, -0.35f)), UV(new Vector2(al, -0.35f)), UV(new Vector2(al, 0f)), UV(new Vector2(0f, 0f)),
                         e == 0 ? Vector3.left : Vector3.right);
            }
            var mesh = new Mesh { name = "CanalHeadwall" };
            kit.Fill(mesh);
            return Keep(mesh);
        }

        /// <summary>UV in metres / 1.83 (one tunnel slab), like the tunnel walls.</summary>
        static Vector2 UV(Vector2 p) { return p / 1.83f; }

        static Vector3 P(Vector2 p, float z) { return new Vector3(p.x, p.y, z); }

        static void Front(MeshKit kit, float x0, float x1, float y0, float y1, float z)
        {
            if (x1 - x0 < 1e-3f || y1 - y0 < 1e-3f) return;
            kit.Quad(0, new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z),
                     UV(new Vector2(x0, y0)), UV(new Vector2(x1, y0)), UV(new Vector2(x1, y1)), UV(new Vector2(x0, y1)), Vector3.forward);
        }

        // ------------------------------------------------------------------ road and verges behind the headwall

        Mesh BuildTop()
        {
            var kit = new MeshKit(3);
            float G = groundHeight;
            float xa, xb;
            WallSpan(out xa, out xb);
            float x0 = xa - roadExtent, x1 = xb + roadExtent;
            float z0 = -wallThickness - roadSetback, z1 = z0 - roadWidth;
            // verges: between the wall and the road, and a strip beyond the road
            Flat(kit, 0, x0, x1, z0, -wallThickness + 0.05f, G - 0.01f, 0.25f);
            Flat(kit, 0, x0, x1, z1 - 8f, z1, G - 0.01f, 0.25f);
            Flat(kit, 1, x0, x1, z1, z0, G + 0.02f, 0.25f);
            float lw = roadWidth / Mathf.Max(1, lanes);
            for (int k = 0; k <= lanes; k++)
            {
                float z = z0 - k * lw;
                if (k == 0) z -= 0.4f; else if (k == lanes) z += 0.4f;
                bool edge = k == 0 || k == lanes;
                bool middle = lanes % 2 == 0 && k == lanes / 2;
                if (edge) Flat(kit, 2, x0, x1, z - 0.08f, z + 0.08f, G + 0.03f, 1f);
                else if (middle) { Flat(kit, 2, x0, x1, z - 0.25f, z - 0.1f, G + 0.03f, 1f); Flat(kit, 2, x0, x1, z + 0.1f, z + 0.25f, G + 0.03f, 1f); }
                else for (float x = x0; x < x1 - 3f; x += 12f) Flat(kit, 2, x, x + 3f, z - 0.07f, z + 0.07f, G + 0.03f, 1f);
            }
            var mesh = new Mesh { name = "CanalTop" };
            kit.Fill(mesh);
            return Keep(mesh);
        }

        static void Flat(MeshKit kit, int sub, float x0, float x1, float z0, float z1, float y, float uvScale)
        {
            if (z0 > z1) { float t = z0; z0 = z1; z1 = t; }
            kit.Quad(sub, new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1),
                     new Vector2(x0, z0) * uvScale, new Vector2(x1, z0) * uvScale, new Vector2(x1, z1) * uvScale, new Vector2(x0, z1) * uvScale, Vector3.up);
        }

        /// <summary>Extent of the headwall along X: from crest to crest of the channel, at least over all mouths.</summary>
        public void WallSpan(out float xl, out float xr)
        {
            Vector2 a = PointAt(0f, -CrestAt(0f)), b = PointAt(0f, CrestAt(0f));
            xl = Mathf.Min(a.x, b.x);
            xr = Mathf.Max(a.x, b.x);
            foreach (var m in Mouths()) { xl = Mathf.Min(xl, m.x - m.halfW - openingMargin); xr = Mathf.Max(xr, m.x + m.halfW + openingMargin); }
            xl -= 1.5f; xr += 1.5f;
        }

        // ------------------------------------------------------------------ invisible walls around the play area

        /// <summary>Points of one boundary line (along the toe of one slope) from the wall to areaDepth along the channel.</summary>
        List<Vector2> ToeLine(float side)
        {
            var pts = new List<Vector2>();
            int n = Mathf.Max(2, Mathf.CeilToInt(areaDepth + wallThickness));
            for (int i = 0; i <= n; i++)
            {
                float s = Mathf.Lerp(-wallThickness, areaDepth, (float)i / n);
                pts.Add(PointAt(s, side * (HalfWAt(s) + boundaryInset)));
            }
            return pts;
        }

        void BuildBounds(Transform root)
        {
            var b = root.Find("Bounds");
            if (b != null) DestroyImmediate(b.gameObject);
            if (!limitArea) return;
            var go = new GameObject("Bounds") { layer = 2 };   // Ignore Raycast: invisible to aiming and interaction
            go.transform.SetParent(root, false);
            var left = ToeLine(-1f);
            var right = ToeLine(1f);
            if (left.Count < 2 || right.Count < 2) return;
            var line = new List<Vector2>(left);
            for (int i = right.Count - 1; i >= 0; i--) line.Add(right[i]);   // ...across the channel at areaDepth, back along the other toe
            float y0 = -1f, hgt = boundaryHeight + 1f;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector2 a = line[i], c = line[i + 1];
                float len = (c - a).magnitude;
                if (len < 0.05f) continue;
                var seg = new GameObject("Wall" + i) { layer = 2 };
                seg.transform.SetParent(go.transform, false);
                var mid = (a + c) * 0.5f;
                seg.transform.localPosition = new Vector3(mid.x, y0 + hgt * 0.5f, mid.y);
                seg.transform.localRotation = Quaternion.LookRotation(new Vector3(c.x - a.x, 0f, c.y - a.y));
                var box = seg.AddComponent<BoxCollider>();
                box.size = new Vector3(0.3f, hgt, len + 0.3f);
            }
        }

        // ------------------------------------------------------------------ sky and lights

        Vector3 SunForward { get { return transform.rotation * (Quaternion.Euler(sunPitch, sunYaw + 180f, 0f) * Vector3.forward); } }

        void BuildSky(Transform root)
        {
            var t = Child(root, "Sky");
            var mf = Get<MeshFilter>(t);
            var mr = Get<MeshRenderer>(t);
            var m = new Mesh { name = "CanalSkyDome" };
            var v = new List<Vector3>();
            var tris = new List<int>();
            const int lat = 16, lon = 32;
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j <= lon; j++)
                {
                    float th = Mathf.PI * i / lat, ph = Mathf.PI * 2f * j / lon;
                    v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)));
                }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j, b = a + 1, c = a + lon + 1, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            m.SetVertices(v);
            m.SetTriangles(tris, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            mf.sharedMesh = Keep(m);
            mr.sharedMaterial = skyMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.renderingLayerMask = OutsideMask;
            var mpb = new MaterialPropertyBlock();
            mpb.SetVector("_Zenith", (Vector4)skyZenith);
            mpb.SetVector("_Horizon", (Vector4)skyHorizon);
            mpb.SetVector("_Ground", (Vector4)skyBelow);
            mpb.SetVector("_SunColor", (Vector4)sunDiscColor);
            mpb.SetVector("_SunDir", -SunForward);
            mpb.SetVector("_CloudLit", (Vector4)cloudLit);
            mpb.SetVector("_CloudShade", (Vector4)cloudShade);
            mpb.SetVector("_Clouds", new Vector4(cloudCover, 1f / Mathf.Max(0.05f, cloudSize), cloudSpeed, seed * 7.31f));
            mr.SetPropertyBlock(mpb);
        }

        void BuildLights(Transform root)
        {
            Dir(root, "Sun", Quaternion.LookRotation(SunForward), sunColor, sunIntensity, true);
            Dir(root, "SkyFill", transform.rotation * Quaternion.Euler(skyFillPitch, sunYaw + 200f, 0f), skyFillColor, skyFill, false);
            Dir(root, "SkyFill 2", transform.rotation * Quaternion.Euler(skyFillPitch, sunYaw + 20f, 0f), skyFillColor, skyFill * skyFillBalance, false);
            Dir(root, "Bounce", transform.rotation * Quaternion.Euler(-bouncePitch, sunYaw + 180f, 0f), bounceColor, bounce * 0.6f, false);
            Dir(root, "Bounce 2", transform.rotation * Quaternion.Euler(-bouncePitch, sunYaw, 0f), bounceColor, bounce * 0.4f, false);
        }

        void Dir(Transform root, string name, Quaternion rot, Color color, float intensity, bool shadows)
        {
            var t = Child(root, name);
            t.rotation = rot;
            var l = Get<Light>(t);
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.renderingLayerMask = (int)OutsideMask;
            l.enabled = intensity > 0.0001f;
            var data = l.GetUniversalAdditionalLightData();
            if (data != null)
            {
                data.renderingLayers = OutsideMask;
                data.customShadowLayers = false;
            }
        }

        static T Get<T>(Transform t) where T : Component
        {
            var c = t.GetComponent<T>();
            if (c == null) c = t.gameObject.AddComponent<T>();
            return c;
        }

        static Transform Child(Transform root, string n)
        {
            var t = root.Find(n);
            if (t != null) return t;
            var go = new GameObject(n);
            go.transform.SetParent(root, false);
            return go.transform;
        }

        // ------------------------------------------------------------------ outdoor / tunnel switch by camera position

        static readonly List<CanalEntrance> all = new List<CanalEntrance>();
        static bool hooked;
        static Volume volume;
        static VolumeProfile profile;
        static ColorAdjustments adjust;
        static Vignette vignette;
        static Bloom bloom;

        /// <summary>1 = the camera is outside (in the canal or above ground), 0 = in the tunnels.</summary>
        public float Outside(Vector3 worldPos)
        {
            var p = transform.InverseTransformPoint(worldPos);
            float above = Smooth(groundHeight - 1.2f, groundHeight + 0.3f, p.y);
            bool inArea = Mathf.Abs(p.x - areaCx) < 250f && p.z < 250f && p.y > -1f;
            float canal = inArea ? Smooth(-blendInside, blendOutside, p.z) : 0f;
            return Mathf.Max(above, canal);
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static void HookCameras()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }

        static void InsideFog(out float density, out Color color)
        {
            var lib = TunnelTypeLibrary.Current;
            if (lib != null && lib.applyAtmosphere) { density = lib.fogDensity; color = lib.fogColor; }
            else { density = 0.045f; color = new Color(0.005f, 0.0055f, 0.006f); }
        }

        static void BeforeCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (all.Count == 0) return;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            CanalEntrance best = null;
            float t = 0f;
            foreach (var e in all)
            {
                if (e == null || !e.isActiveAndEnabled) continue;
                float o = e.Outside(cam.transform.position);
                if (best == null || o > t) { best = e; t = o; }
            }
            if (best == null) return;
#if UNITY_EDITOR
            // Scene view with its lighting toggle off: no outdoor exposure (it would turn the unlit view black), no fog
            if (cam.cameraType == CameraType.SceneView)
                foreach (UnityEditor.SceneView sv in UnityEditor.SceneView.sceneViews)
                    if (sv != null && sv.camera == cam && !sv.sceneLighting)
                    {
                        RenderSettings.fogDensity = 0f;
                        SetFogGlobals();
                        var fv = GetVolume();
                        adjust.postExposure.Override(0f);
                        vignette.intensity.Override(0f);
                        bloom.intensity.Override(0f);
                        fv.weight = 1f;
                        return;
                    }
#endif
            float d; Color c;
            InsideFog(out d, out c);
            RenderSettings.fog = true;
            RenderSettings.fogDensity = Mathf.Lerp(d, best.outsideFogDensity, t);
            RenderSettings.fogColor = Color.Lerp(c, best.outsideFogColor, t);
            SetFogGlobals();
            var v = GetVolume();
            adjust.postExposure.Override(best.outsideExposure);
            vignette.intensity.Override(best.outsideVignette);
            // the tunnel profile blooms everything above 1; outside the scene is ~20x brighter before exposure
            bloom.threshold.Override(Mathf.Pow(2f, -best.outsideExposure) * 1.2f);
            bloom.intensity.Override(best.outsideBloom);
            v.weight = t;
        }

        /// <summary>The engine may read RenderSettings fog once per frame: set the shader values for this camera directly.</summary>
        static void SetFogGlobals()
        {
            float d = RenderSettings.fogDensity;
            Shader.SetGlobalVector("unity_FogParams", new Vector4(d * 1.2011224f, d * 1.4426950f, 0f, 0f));
            Shader.SetGlobalColor("unity_FogColor", RenderSettings.fogColor.linear);
        }

        static void ResetAtmosphere()
        {
            float d; Color c;
            InsideFog(out d, out c);
            RenderSettings.fogDensity = d;
            RenderSettings.fogColor = c;
            if (volume != null) volume.weight = 0f;
        }

        static Volume GetVolume()
        {
            if (volume == null)
            {
                foreach (var old in Resources.FindObjectsOfTypeAll<Volume>())
                    if (old != null && old.gameObject.name == "_CarX_Outdoor") DestroyImmediate(old.gameObject);
                var go = new GameObject("_CarX_Outdoor") { hideFlags = HideFlags.HideAndDontSave };
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 25f;
            }
            if (profile == null || adjust == null || vignette == null || bloom == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.hideFlags = HideFlags.HideAndDontSave;
                adjust = profile.Add<ColorAdjustments>(false);
                vignette = profile.Add<Vignette>(false);
                bloom = profile.Add<Bloom>(false);
            }
            if (volume.sharedProfile != profile) volume.sharedProfile = profile;
            return volume;
        }
    }
}
