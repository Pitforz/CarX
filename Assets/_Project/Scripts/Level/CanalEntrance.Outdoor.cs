using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>
    /// Outdoor side of the canal:
    ///  - which objects are "outside" (in front of the headwall or above the ground): they get the outdoor rendering layer, so the
    ///    sun lights them, and the sky light. Players and NPCs switch when they walk into a tunnel;
    ///  - the sky light: a soft light from the sky above, the horizon and the sun-warmed ground below, given to every outdoor
    ///    renderer as its own light probe (spherical harmonics). The tunnels keep their baked probes, nothing leaks inside;
    ///  - the tunnel mouths seen from outside: the daylight inside the mouths (TunnelExit lights) is dimmed while the camera is
    ///    outside, so slab joints and rubbish at the entrance are not brighter than sunlit concrete. Inside it stays as it was.
    /// </summary>
    public partial class CanalEntrance
    {
        [Header("Объекты снаружи (мусор, NPC, игрок, свои модели)")]
        [Tooltip("Всё, что стоит перед стенкой с проёмами или выше земли, само получает солнце и свет неба: класть в «Снаружи: объекты» не нужно. Игрок и NPC переключаются, когда входят в тоннель")]
        public bool autoOutdoor = true;

        [Header("Свет неба и земли (вместо плоских подсветок)")]
        [Tooltip("Вкл: мягкий свет неба сверху и тёплый отсвет нагретой земли снизу, как в жизни: солнце и тени контрастнее. Выкл: старые плоские подсветки Sky Fill и Bounce")]
        public bool realSkyLight = true;
        [Tooltip("Свет неба на всё, что смотрит вверх. Темнее = глубже тени и сильнее ощущается солнце")]
        [ColorUsage(false, true)] public Color skyLightTop = new Color(2.4f, 3.1f, 4.4f);
        [Tooltip("Свет на отвесные стены: небо у горизонта и земля")]
        [ColorUsage(false, true)] public Color skyLightSide = new Color(3.0f, 3.0f, 3.1f);
        [Tooltip("Тёплый отсвет нагретой солнцем земли на всё, что смотрит вниз: низ мусора и крон, потолок у входа")]
        [ColorUsage(false, true)] public Color groundLight = new Color(4.0f, 3.3f, 2.4f);

        [Header("Солнце как в решётках")]
        [Tooltip("Солнце снаружи того же тёплого цвета и с той же стороны, что в решётках тоннелей (CarX → Настройки света тоннелей). Выкл: берутся Sun Color, Sun Yaw и Sun Pitch выше")]
        public bool sunFromTunnels = true;
        [Tooltip("Насколько солнце снаружи берёт тёплый цвет решёток: 1 = тот же цвет (снаружи всё становится оранжевым, как на закате), 0 = белое. Глаз на улице привыкает к свету, поэтому полдень выглядит теплее при 0.4–0.6")]
        [Range(0f, 1f)] public float sunWarmth = 0.5f;

        [Header("Солнце слепит")]
        [Tooltip("Насколько слепит солнце, если смотреть в его сторону: картинка темнеет, вокруг солнца сильнее свечение. 0 = не слепит")]
        [Range(0f, 1f)] public float sunGlare = 0.6f;
        [Tooltip("Блики в объективе от солнца (лучи и отражения линз). 0 = нет")]
        [Range(0f, 2f)] public float sunFlare = 0.5f;
        [Tooltip("За сколько секунд глаз привыкает, когда отводишь взгляд от солнца или смотришь на него")]
        [Range(0.05f, 3f)] public float sunGlareTime = 0.6f;
        [Tooltip("Яркость солнечного диска. Чем ярче, тем сильнее солнце слепит: больше свечение вокруг него и ярче круги-блики в объективе. 1 = просто кружок на небе")]
        [Range(1f, 200f)] public float sunDiscBrightness = 60f;
        [Tooltip("Ореол вокруг солнца на небе (светлое кольцо у диска и мягкое свечение шире). 0 = нет")]
        [Range(0f, 3f)] public float sunHalo = 1f;

        [Header("Проёмы тоннелей, если смотреть снаружи")]
        [Tooltip("Яркость дневного света внутри проёмов, пока камера снаружи. 1 = как изнутри (рёбра плит и мусор у входа светятся), 0.1 = как в жизни: с улицы тоннель тёмный. Изнутри свет в конце тоннеля не меняется")]
        [Range(0f, 1f)] public float mouthLightOutside = 0.12f;

        [Header("Цвет картинки снаружи (в тоннелях свой)")]
        [Tooltip("Теплота картинки снаружи: + теплее, − холоднее. В тоннелях −10")]
        [Range(-60f, 60f)] public float outsideWarmth = 5f;
        [Tooltip("Насыщенность цветов снаружи. В тоннелях −28 (приглушено)")]
        [Range(-60f, 60f)] public float outsideSaturation = 3f;
        [Tooltip("Контраст снаружи. В тоннелях 18")]
        [Range(-50f, 60f)] public float outsideContrast = 12f;

        [Header("Отражение неба (стекло небоскрёбов, металл, пластик)")]
        [Tooltip("Глянцевое снаружи отражает небо, а не черноту: стекло небоскрёбов светлеет и голубеет, пакеты и бутылки блестят. Внутри тоннелей не действует")]
        public bool skyReflections = true;
        [Tooltip("Яркость отражения неба. 1 = как в жизни")]
        [Range(0f, 2f)] public float skyReflection = 1f;
        [Tooltip("Теплота неба в отражениях внутри канала (мусор, баки, пакеты, бутылки): 0 = голубое небо как есть, 1 = тёплое, как солнце. Стекло небоскрёбов не меняется")]
        [Range(0f, 1f)] public float skyReflectionWarmth = 0.5f;

        // ------------------------------------------------------------------ the sun shared with the tunnel grates

        TunnelTypeLibrary SunLibrary
        {
            get
            {
                if (!sunFromTunnels) return null;
                if (network != null && network.library != null) return network.library;
                return TunnelTypeLibrary.Current;
            }
        }

        Color SunColorNow
        {
            get
            {
                var lib = SunLibrary;
                if (lib == null) return sunColor;
                var c = Color.Lerp(Color.white, lib.grateLightColor, sunWarmth);
                c.a = 1f;
                return c;
            }
        }

        int SunSignature()
        {
            var lib = SunLibrary;
            if (lib == null) return 0;
            unchecked { return lib.grateLightColor.GetHashCode() * 31 + lib.sunElevation.GetHashCode() * 7 + lib.sunAzimuth.GetHashCode(); }
        }

        // ------------------------------------------------------------------ sky light as a light probe of its own

        [System.NonSerialized] SphericalHarmonicsL2 skySH;
        [System.NonSerialized] int skySHSig;
        [System.NonSerialized] readonly List<SphericalHarmonicsL2> shList = new List<SphericalHarmonicsL2> { new SphericalHarmonicsL2() };
        static MaterialPropertyBlock block, shBlock;
        static readonly string[] shNames = { "unity_SHAr", "unity_SHAg", "unity_SHAb", "unity_SHBr", "unity_SHBg", "unity_SHBb", "unity_SHC" };
        static readonly int[] shIds = System.Array.ConvertAll(shNames, Shader.PropertyToID);
        [System.NonSerialized] readonly Vector4[] shVectors = new Vector4[7];
        static readonly Vector3[] shDirs = { Vector3.up, Vector3.right, Vector3.down };

        int SkySignature()
        {
            unchecked { return (skyLightTop.GetHashCode() * 31 + skyLightSide.GetHashCode()) * 31 + groundLight.GetHashCode() + 1; }
        }

        /// <summary>Probe that gives "top" to surfaces facing up, "side" to walls and "bottom" to surfaces facing down.</summary>
        static SphericalHarmonicsL2 SkyProbe(Color top, Color side, Color bottom)
        {
            var parts = new SphericalHarmonicsL2[3];
            parts[0].AddAmbientLight(Color.white);
            parts[1].AddDirectionalLight(Vector3.up, Color.white, 1f);
            parts[2].AddDirectionalLight(Vector3.down, Color.white, 1f);
            var m = new float[3, 3];
            var res = new Color[3];
            for (int k = 0; k < 3; k++)
            {
                parts[k].Evaluate(shDirs, res);
                for (int i = 0; i < 3; i++) m[i, k] = res[i].r;
            }
            var sh = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++)
            {
                var x = Solve3(m, new[] { top[c], side[c], bottom[c] });
                for (int k = 0; k < 3; k++)
                    for (int q = 0; q < 9; q++) sh[c, q] += parts[k][0, q] * x[k];
            }
            return sh;
        }

        static float[] Solve3(float[,] a, float[] b)
        {
            float det = Det(a[0, 0], a[0, 1], a[0, 2], a[1, 0], a[1, 1], a[1, 2], a[2, 0], a[2, 1], a[2, 2]);
            var x = new float[3];
            if (Mathf.Abs(det) < 1e-8f) { x[0] = (b[0] + b[1] + b[2]) / 3f; return x; }
            for (int k = 0; k < 3; k++)
            {
                var t = (float[,])a.Clone();
                for (int i = 0; i < 3; i++) t[i, k] = b[i];
                x[k] = Det(t[0, 0], t[0, 1], t[0, 2], t[1, 0], t[1, 1], t[1, 2], t[2, 0], t[2, 1], t[2, 2]) / det;
            }
            return x;
        }

        static float Det(float a, float b, float c, float d, float e, float f, float g, float h, float i)
        {
            return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        }

        void RefreshSkyProbe()
        {
            int sig = SkySignature();
            if (sig == skySHSig) return;
            skySHSig = sig;
            skySH = SkyProbe(skyLightTop, skyLightSide, groundLight);
            shList[0] = skySH;
            // Unity's own packing of a probe into the 7 shader constants, read back from a scratch block
            if (shBlock == null) shBlock = new MaterialPropertyBlock();
            shBlock.Clear();
            shBlock.CopySHCoefficientArraysFrom(shList);
            for (int i = 0; i < 7; i++)
            {
                var arr = shBlock.GetVectorArray(shIds[i]);
                shVectors[i] = arr != null && arr.Length > 0 ? arr[0] : Vector4.zero;
            }
            outdoorApplied.Clear();   // push the new probe to everybody
        }

        // ------------------------------------------------------------------ the sky in reflections

        // Unity's own sky reflection is black here (no skybox: the tunnels must stay dark), so glossy things outside reflected
        // blackness. Two reflection probes hold a small cubemap of this sky: one fills the canal in front of the headwall, the
        // other everything well above the ground (towers, sign tops). Both stop short of the tunnels, so nothing changes inside.
        const string ProbeCanalName = "_CarX_SkyReflection_Canal", ProbeCityName = "_CarX_SkyReflection_City";
        const int SkyCubeSize = 64;
        [System.NonSerialized] Cubemap skyCube, canalCube;
        [System.NonSerialized] int skyCubeSig, canalCubeSig;
        [System.NonSerialized] ReflectionProbe probeCanal, probeCity;
        [System.NonSerialized] double nextProbeCheck;

        int SkyCubeSignature()
        {
            unchecked { return ((skyZenith.GetHashCode() * 31 + skyHorizon.GetHashCode()) * 31 + groundLight.GetHashCode()) * 31 + 7; }
        }

        int CanalCubeSignature()
        {
            unchecked
            {
                return (((skyLightTop.GetHashCode() * 31 + groundLight.GetHashCode()) * 31 + skyReflectionWarmth.GetHashCode()) * 31
                        + SunColorNow.GetHashCode()) * 31 + 11;
            }
        }

        void SkyReflectionTick()
        {
            if (!skyReflections || !isActiveAndEnabled) { DisableSkyReflection(); return; }
            double now = Time.realtimeSinceStartupAsDouble;
            int sig = SkyCubeSignature(), csig = CanalCubeSignature();
            if (skyCube != null && canalCube != null && sig == skyCubeSig && csig == canalCubeSig && probeCanal != null && probeCity != null
                && now < nextProbeCheck)
            {
                probeCanal.intensity = probeCity.intensity = skyReflection;
                return;
            }
            nextProbeCheck = now + 1.0;
            // the probes and their cubemaps outlive a script reload (they are engine objects): pick them up again rather than
            // destroying and recreating them, which the renderer does not like in the middle of a reload
            if (probeCanal == null) probeCanal = FindSkyProbe(ProbeCanalName);
            if (probeCity == null) probeCity = FindSkyProbe(ProbeCityName);
            if (skyCube == null)
            {
                var old = probeCity != null ? probeCity.customBakedTexture as Cubemap : null;
                if (old != null && old.name == SkyCubeName(sig)) { skyCube = old; skyCubeSig = sig; }
            }
            if (canalCube == null)
            {
                var old = probeCanal != null ? probeCanal.customBakedTexture as Cubemap : null;
                if (old != null && old.name == CanalCubeName(csig)) { canalCube = old; canalCubeSig = csig; }
            }
            Cubemap staleSky = null, staleCanal = null;
            if (skyCube == null || sig != skyCubeSig)
            {
                staleSky = skyCube;
                skyCube = MakeSkyCube(SkyCubeName(sig), skyZenith, skyHorizon, groundLight * 1.6f);
                skyCubeSig = sig;
            }
            if (canalCube == null || csig != canalCubeSig)
            {
                staleCanal = canalCube;
                Color zen, hor, below;
                CanalReflectionColors(out zen, out hor, out below);
                canalCube = MakeSkyCube(CanalCubeName(csig), zen, hor, below);
                canalCubeSig = csig;
            }
            probeCanal = SkyProbeObject(probeCanal, ProbeCanalName, canalCube);
            probeCity = SkyProbeObject(probeCity, ProbeCityName, skyCube);
            // old cubes go only after no probe holds them any more
            if (staleSky != null && staleSky != skyCube && staleSky != canalCube) DestroyNow(staleSky);
            if (staleCanal != null && staleCanal != skyCube && staleCanal != canalCube) DestroyNow(staleCanal);
            float g = groundHeight;
            // the canal: in front of the headwall (1 m clear of the mouths), from below the floor to high above the banks.
            // A probe box is aligned with the world axes, so it is used only when the canal is too.
            bool aligned = Mathf.Abs(Mathf.DeltaAngle(0f, Mathf.Round(transform.eulerAngles.y / 90f) * 90f - transform.eulerAngles.y)) < 0.5f
                           && Mathf.Abs(Mathf.DeltaAngle(0f, transform.eulerAngles.x)) < 0.5f && Mathf.Abs(Mathf.DeltaAngle(0f, transform.eulerAngles.z)) < 0.5f;
            SetProbeBox(probeCanal, aligned, LocalBoxToWorld(new Vector3(areaCx - 400f, -3f, 1f), new Vector3(areaCx + 400f, g + 60f, 800f)), 1f);
            // the city: everything 9 m above the ground (the tunnels never get that high)
            var c = transform.TransformPoint(new Vector3(areaCx, 0f, 0f));
            float y0 = transform.TransformPoint(new Vector3(0f, g + 9f, 0f)).y;
            var city = new Bounds();
            city.SetMinMax(new Vector3(c.x - 4000f, y0, c.z - 4000f), new Vector3(c.x + 4000f, y0 + 1500f, c.z + 4000f));
            SetProbeBox(probeCity, true, city, 4f);
        }

        /// <summary>
        /// What glossy things in the canal see. The sky is as bright as the light it gives (the painted sky dome is brighter, it is
        /// made for the eye: reflected, it turned bags and bins pale blue), whiter towards the horizon and tinted towards the sun by
        /// skyReflectionWarmth; below the horizon the sun-lit ground.
        /// </summary>
        void CanalReflectionColors(out Color zen, out Color hor, out Color below)
        {
            Color sun = SunColorNow;
            float sl = Luma(sun);
            Color warm = sl > 1e-4f ? sun / sl : Color.white;
            Color top = skyLightTop;
            float tl = Luma(top);
            Color whiter = Color.Lerp(top, new Color(tl, tl, tl), 0.6f) * 1.3f;
            zen = Color.Lerp(top, warm * tl, skyReflectionWarmth);
            hor = Color.Lerp(whiter, warm * Luma(whiter), skyReflectionWarmth);
            below = groundLight * 1.6f;
            zen.a = hor.a = below.a = 1f;
        }

        static float Luma(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }

        Bounds LocalBoxToWorld(Vector3 lo, Vector3 hi)
        {
            var b = new Bounds(transform.TransformPoint(lo), Vector3.zero);
            for (int i = 1; i < 8; i++)
                b.Encapsulate(transform.TransformPoint(new Vector3((i & 1) != 0 ? hi.x : lo.x, (i & 2) != 0 ? hi.y : lo.y, (i & 4) != 0 ? hi.z : lo.z)));
            return b;
        }

        ReflectionProbe FindSkyProbe(string name)
        {
            var t = transform.Find(name);
            return t != null ? t.GetComponent<ReflectionProbe>() : null;
        }

        static string SkyCubeName(int sig) { return "CarX sky reflection " + sig; }
        static string CanalCubeName(int sig) { return "CarX canal reflection " + sig; }

        ReflectionProbe SkyProbeObject(ReflectionProbe probe, string name, Cubemap cube)
        {
            if (probe == null)
            {
                var t = transform.Find(name);
                if (t == null)
                {
                    var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(transform, false);
                    t = go.transform;
                }
                probe = t.GetComponent<ReflectionProbe>();
                if (probe == null) probe = t.gameObject.AddComponent<ReflectionProbe>();
            }
            probe.mode = ReflectionProbeMode.Custom;
            if (probe.customBakedTexture != cube) probe.customBakedTexture = cube;
            probe.boxProjection = false;
            probe.importance = 1;
            probe.intensity = skyReflection;
            return probe;
        }

        static void SetProbeBox(ReflectionProbe probe, bool on, Bounds world, float blend)
        {
            probe.enabled = on;
            if (!on) return;
            probe.transform.SetPositionAndRotation(world.center, Quaternion.identity);
            probe.transform.localScale = Vector3.one;
            if (probe.transform.lossyScale != Vector3.one)
            {
                var ls = probe.transform.lossyScale;   // a scaled canal: undo it, the box is in world metres
                probe.transform.localScale = new Vector3(1f / Mathf.Max(ls.x, 1e-4f), 1f / Mathf.Max(ls.y, 1e-4f), 1f / Mathf.Max(ls.z, 1e-4f));
            }
            probe.center = Vector3.zero;
            probe.size = world.size;
            probe.blendDistance = blend;
        }

        /// <summary>Switched off or the component disabled (also before every script reload): the probes just go dark.</summary>
        void DisableSkyReflection()
        {
            if (probeCanal == null) probeCanal = FindSkyProbe(ProbeCanalName);
            if (probeCity == null) probeCity = FindSkyProbe(ProbeCityName);
            if (probeCanal != null) probeCanal.enabled = false;
            if (probeCity != null) probeCity.enabled = false;
        }

        /// <summary>The canal itself goes away: take the probes and the cubemaps with it.</summary>
        void DestroySkyReflection()
        {
            if (probeCanal == null) probeCanal = FindSkyProbe(ProbeCanalName);
            if (probeCity == null) probeCity = FindSkyProbe(ProbeCityName);
            var cubes = new List<Cubemap> { skyCube, canalCube };
            if (probeCanal != null) cubes.Add(probeCanal.customBakedTexture as Cubemap);
            if (probeCity != null) cubes.Add(probeCity.customBakedTexture as Cubemap);
            if (probeCanal != null) DestroyNow(probeCanal.gameObject);
            if (probeCity != null) DestroyNow(probeCity.gameObject);
            probeCanal = probeCity = null;
            var done = new HashSet<Cubemap>();
            foreach (var cube in cubes)
                if (cube != null && done.Add(cube) && (cube.name.StartsWith("CarX sky reflection") || cube.name.StartsWith("CarX canal reflection")))
                    DestroyNow(cube);
            skyCube = canalCube = null;
        }

        static void DestroyNow(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        /// <summary>A sky as a small HDR cubemap: horizon to zenith above, the sun-warmed ground below.</summary>
        static Cubemap MakeSkyCube(string name, Color zen, Color hor, Color below)
        {
            const int n = SkyCubeSize;
            var cube = new Cubemap(n, TextureFormat.RGBAHalf, true) { name = name, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[n * n];
            for (int f = 0; f < 6; f++)
            {
                var face = (CubemapFace)f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        Vector3 d;
                        switch (face)
                        {
                            case CubemapFace.PositiveX: d = new Vector3(1f, -v, -u); break;
                            case CubemapFace.NegativeX: d = new Vector3(-1f, -v, u); break;
                            case CubemapFace.PositiveY: d = new Vector3(u, 1f, v); break;
                            case CubemapFace.NegativeY: d = new Vector3(u, -1f, -v); break;
                            case CubemapFace.PositiveZ: d = new Vector3(u, -v, 1f); break;
                            default: d = new Vector3(-u, -v, -1f); break;
                        }
                        float h = d.normalized.y;
                        Color c = h >= 0f ? Color.Lerp(hor, zen, Mathf.Pow(h, 0.45f)) : Color.Lerp(hor, below, Mathf.Clamp01(-h * 12f));
                        c.a = 1f;
                        px[y * n + x] = c;
                    }
                cube.SetPixels(px, face, 0);
            }
            cube.Apply(true, false);
            return cube;
        }

        // ------------------------------------------------------------------ what is outside

        /// <summary>True for a point out in the open: in the canal in front of the headwall, or above the ground.</summary>
        public bool IsOutdoors(Vector3 world)
        {
            var p = transform.InverseTransformPoint(world);
            if (p.y > groundHeight - 0.4f) return Mathf.Abs(p.x - areaCx) < 4000f && Mathf.Abs(p.z) < 4000f;
            return p.z > 0.05f && p.y > -2f && Mathf.Abs(p.x - areaCx) < 500f && p.z < 1500f;
        }

        const byte KindSkip = 1, KindMover = 2, KindStatic = 3, KindAlwaysOut = 4;

        [System.NonSerialized] readonly Dictionary<Renderer, byte> kinds = new Dictionary<Renderer, byte>();
        [System.NonSerialized] readonly Dictionary<Renderer, Transform> anchors = new Dictionary<Renderer, Transform>();
        [System.NonSerialized] readonly Dictionary<Renderer, uint> moverMasks = new Dictionary<Renderer, uint>();
        [System.NonSerialized] readonly Dictionary<Renderer, int> outdoorApplied = new Dictionary<Renderer, int>();
        [System.NonSerialized] readonly List<Renderer> knownMovers = new List<Renderer>();
        [System.NonSerialized] double nextScan, nextMovers;

        byte KindOf(Renderer r)
        {
            byte k;
            if (kinds.TryGetValue(r, out k)) return k;
            k = Classify(r);
            kinds[r] = k;
            return k;
        }

        byte Classify(Renderer r)
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is BillboardRenderer || r is SpriteRenderer) return KindSkip;
            var t = r.transform;
            if (t.IsChildOf(transform))
                return t.name == "Sky" ? KindSkip : KindAlwaysOut;   // headwall, road, channel, fences, grass of this canal
            if (r.GetComponentInParent<OutdoorObjects>() != null) return KindAlwaysOut;
            // tunnel pieces keep their own light (baked probes, grates, exits)
            if (r.GetComponentInParent<TunnelPath>() != null || r.GetComponentInParent<TunnelJunction>() != null || r.GetComponentInParent<TunnelRoom>() != null
                || r.GetComponentInParent<TunnelExit>() != null || r.GetComponentInParent<TunnelBridge>() != null || r.GetComponentInParent<TunnelColumn>() != null
                || r.GetComponentInParent<CanalEntrance>() != null)
                return KindSkip;
            Transform anchor = null;
            var cc = r.GetComponentInParent<CharacterController>();
            if (cc != null) anchor = cc.transform;
            if (anchor == null) { var ag = r.GetComponentInParent<NavMeshAgent>(); if (ag != null) anchor = ag.transform; }
            if (anchor == null) { var an = r.GetComponentInParent<Animator>(); if (an != null) anchor = an.transform; }
            if (anchor == null) { var rb = r.GetComponentInParent<Rigidbody>(); if (rb != null && !rb.isKinematic) anchor = rb.transform; }
            if (anchor != null) { anchors[r] = anchor; return KindMover; }
            return KindStatic;
        }

        /// <summary>Gives a renderer the outdoor light (outdoor layer, sky probe) or takes it away.</summary>
        void SetOutdoor(Renderer r, bool outdoor, byte kind)
        {
            uint m = r.renderingLayerMask, want;
            if (kind == KindMover)
            {
                uint orig;
                if (!moverMasks.TryGetValue(r, out orig)) { orig = m & ~OutsideMask; moverMasks[r] = orig; }
                want = outdoor ? (orig | OutsideMask) & ~TunnelNetwork.TunnelRenderingLayer : orig;
            }
            else if (outdoor) want = (m | OutsideMask) & ~TunnelNetwork.TunnelRenderingLayer;
            else want = m & ~OutsideMask;
            if (want == 0) want = 1;
            if (want != m) r.renderingLayerMask = want;

            bool sky = outdoor && realSkyLight;
            if (sky)
            {
                int applied;
                if (r.lightProbeUsage != LightProbeUsage.CustomProvided) r.lightProbeUsage = LightProbeUsage.CustomProvided;
                if (outdoorApplied.TryGetValue(r, out applied) && applied == skySHSig && r.HasPropertyBlock()) return;
                if (block == null) block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                for (int i = 0; i < 7; i++) block.SetVector(shIds[i], shVectors[i]);
                r.SetPropertyBlock(block);
                outdoorApplied[r] = skySHSig;
            }
            else if (r.lightProbeUsage == LightProbeUsage.CustomProvided)
            {
                r.lightProbeUsage = kind == KindAlwaysOut ? LightProbeUsage.Off : LightProbeUsage.BlendProbes;
                outdoorApplied.Remove(r);
            }
        }

        /// <summary>Finds new renderers and sorts them; then places everything (call from Update).</summary>
        void OutdoorTick()
        {
            SkyReflectionTick();
            if (!autoOutdoor && !realSkyLight) return;
            RefreshSkyProbe();
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= nextScan)
            {
                nextScan = now + (Application.isPlaying ? 4.0 : 2.0);
                Scan();
            }
            if (now >= nextMovers)
            {
                nextMovers = now + 0.2;
                for (int i = knownMovers.Count - 1; i >= 0; i--)
                {
                    var r = knownMovers[i];
                    Transform a;
                    if (r == null || !anchors.TryGetValue(r, out a) || a == null) { knownMovers.RemoveAt(i); continue; }
                    SetOutdoor(r, autoOutdoor && IsOutdoors(a.position), KindMover);
                }
            }
        }

        /// <summary>Sorts the renderers of the scene. In Play only new ones (things do not move there); in the editor all of them,
        /// so whatever you drag out of a tunnel or into it gets the right light within a couple of seconds.</summary>
        void Scan()
        {
            var scene = gameObject.scene;
            bool playing = Application.isPlaying;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || r.gameObject.scene != scene) continue;
                bool known = kinds.ContainsKey(r);
                if (playing && known) continue;
                byte k = KindOf(r);
                if (k == KindSkip) continue;
                if (k == KindMover) { if (!known || !knownMovers.Contains(r)) knownMovers.Add(r); continue; }
                bool outdoor = k == KindAlwaysOut || (autoOutdoor ? IsOutdoors(r.bounds.center) : (r.renderingLayerMask & OutsideMask) != 0);
                SetOutdoor(r, outdoor, k);
            }
        }

        /// <summary>Forget everything sorted (new objects, moved hierarchy): the next tick sorts again.</summary>
        public void ResortOutdoor()
        {
            kinds.Clear();
            anchors.Clear();
            outdoorApplied.Clear();
            knownMovers.Clear();
            nextScan = 0;
        }

        /// <summary>Bridge / debug: sort and light everything now (normally it happens by itself every couple of seconds).</summary>
        /// <summary>Bridge / debug: tunnel renderers (tunnel rendering layer) that reach into a sky reflection box.</summary>
        public static string DebugTunnelsInReflection()
        {
            var sb = new System.Text.StringBuilder();
            int n = 0, tunnels = 0;
            foreach (var c in all)
            {
                if (c == null || c.probeCanal == null || c.probeCity == null) continue;
                Bounds a = c.probeCanal.bounds, b = c.probeCity.bounds;
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                {
                    if (r == null || (r.renderingLayerMask & TunnelNetwork.TunnelRenderingLayer) == 0) continue;
                    tunnels++;
                    var rb = r.bounds;
                    bool inA = c.probeCanal.enabled && rb.Intersects(a), inB = rb.Intersects(b);
                    if (!inA && !inB) continue;
                    n++;
                    if (n > 25) continue;
                    string path = r.transform.name;
                    for (var p = r.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                    sb.Append(" | " + (inA ? "CANAL " : "") + (inB ? "CITY " : "") + path + " min " + rb.min.ToString("F1") + " max " + rb.max.ToString("F1"));
                }
            }
            return "tunnel renderers " + tunnels + ", touching sky reflection " + n + sb;
        }

        /// <summary>Bridge / debug: the probe volumes (APV) of the scene and which renderers named like `part` reach into them.</summary>
        public static string DebugProbeVolumes(string part)
        {
            var sb = new System.Text.StringBuilder();
            var vols = Object.FindObjectsByType<ProbeVolume>(FindObjectsInactive.Exclude);
            foreach (var v in vols)
            {
                var t = v.transform;
                var b = new Bounds(t.position, Vector3.Scale(v.size, t.lossyScale));
                sb.Append(" | PV " + v.name + " mode " + v.mode + " min " + b.min.ToString("F1") + " max " + b.max.ToString("F1"));
            }
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || string.IsNullOrEmpty(part) || !r.name.Contains(part)) continue;
                var rb = r.bounds;
                sb.Append(" || " + r.name + " min " + rb.min.ToString("F1") + " max " + rb.max.ToString("F1") + " probes " + r.lightProbeUsage + " block " + r.HasPropertyBlock() + " rl " + r.renderingLayerMask);
                if (r.HasPropertyBlock())
                {
                    var blk = new MaterialPropertyBlock();
                    r.GetPropertyBlock(blk);
                    sb.Append(" SHAr " + blk.GetVector(shIds[0]) + " SHC " + blk.GetVector(shIds[6]));
                }
                foreach (var v in vols)
                {
                    var t = v.transform;
                    var b = new Bounds(t.position, Vector3.Scale(v.size, t.lossyScale));
                    if (v.mode == ProbeVolume.Mode.Global || b.Intersects(rb)) sb.Append(" IN " + v.name);
                }
                foreach (var m in r.sharedMaterials)
                    if (m != null) sb.Append(" mat " + m.name + " base " + (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "-") + " metal " + (m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString("0.00") : "-"));
            }
            return sb.Length == 0 ? "nothing" : sb.ToString();
        }

        /// <summary>Bridge / debug: where the sky reflection boxes are.</summary>
        public static string DebugReflection()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in all)
            {
                if (c == null) continue;
                c.nextProbeCheck = 0;
                c.SkyReflectionTick();
                foreach (var p in new[] { c.probeCanal, c.probeCity })
                {
                    if (p == null) { sb.Append(" | none"); continue; }
                    var b = p.bounds;
                    sb.Append(" | " + p.name + " on " + p.enabled + " mode " + p.mode + " tex " + (p.texture != null ? p.texture.name : "null")
                              + " min " + b.min.ToString("F1") + " max " + b.max.ToString("F1") + " int " + p.intensity);
                }
                sb.Append(" | canal pos " + c.transform.position.ToString("F1") + " rot " + c.transform.eulerAngles.ToString("F1") + " areaCx " + c.areaCx);
            }
            return sb.Length == 0 ? "no canal" : sb.ToString();
        }

        public static string ApplyOutdoorNow()
        {
            int n = 0, movers = 0;
            foreach (var c in all)
            {
                if (c == null || !c.isActiveAndEnabled) continue;
                c.Rebuild();
                c.ResortOutdoor();
                c.nextMovers = 0;
                c.OutdoorTick();
                n++;
                movers += c.knownMovers.Count;
            }
            int outside = 0, sky = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null) continue;
                if ((r.renderingLayerMask & (1u << 3)) != 0) outside++;
                if (r.lightProbeUsage == LightProbeUsage.CustomProvided) sky++;
            }
            return "canals " + n + ", movers " + movers + ", outdoor renderers " + outside + ", with sky light " + sky;
        }

        /// <summary>Bridge / debug: tunnel renderers that reach above the canal ground (they make the probe box tall).</summary>
        public static string DebugHighTunnels()
        {
            var sb = new System.Text.StringBuilder();
            CanalEntrance c = all.Count > 0 ? all[0] : null;
            if (c == null) return "no canal";
            float g = c.transform.TransformPoint(new Vector3(0f, c.groundHeight, 0f)).y;
            int n = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || r.bounds.max.y < g + 0.3f) continue;
                if (r.GetComponentInParent<TunnelPath>() == null && r.GetComponentInParent<TunnelJunction>() == null && r.GetComponentInParent<TunnelRoom>() == null && r.GetComponentInParent<TunnelColumn>() == null) continue;
                if (r.bounds.max.y > g + 2.5f && n++ < 25) sb.Append(" | " + r.transform.parent.name + "/" + r.name + " maxY " + r.bounds.max.y.ToString("F1") + " c " + r.bounds.center.ToString("F0") + " static " + r.gameObject.isStatic);
            }
            return "ground " + g + ", high tunnel renderers " + n + sb;
        }

        /// <summary>Bridge / debug: shadow casters hanging above a spot of the canal floor (x, z in world space).</summary>
        public static string DebugAbove(float x, float z, float radius)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || r.shadowCastingMode == ShadowCastingMode.Off) continue;
                var b = r.bounds;
                if (b.min.y < 1.5f || b.size.magnitude > 200f) continue;
                var c = b.center;
                if (Mathf.Abs(c.x - x) > radius || Mathf.Abs(c.z - z) > radius) continue;
                var t = r.transform;
                string path = t.name;
                for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
                sb.Append(" | " + path + " c " + c.ToString("F1") + " size " + b.size.ToString("F1"));
            }
            return sb.Length == 0 ? "nothing" : sb.ToString();
        }

        /// <summary>Bridge / debug: every renderer and decal whose footprint covers the point (any height, any shadow mode).</summary>
        public static string DebugAt(float x, float z, float half)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || !r.enabled) continue;
                var b = r.bounds;
                if (b.size.magnitude > 200f) continue;
                if (b.max.x < x - half || b.min.x > x + half || b.max.z < z - half || b.min.z > z + half) continue;
                var t = r.transform;
                string path = t.name;
                for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
                var m = r.sharedMaterial;
                sb.Append(" | " + path + " [" + r.GetType().Name + " " + r.shadowCastingMode + " y " + b.min.y.ToString("F1") + ".." + b.max.y.ToString("F1")
                          + " probes " + r.lightProbeUsage + " rl " + r.renderingLayerMask + " mat " + (m != null ? m.name + "/" + (m.shader != null ? m.shader.name : "-") : "null") + "]");
            }
            foreach (var d in Object.FindObjectsByType<UnityEngine.Rendering.Universal.DecalProjector>(FindObjectsInactive.Exclude))
            {
                if (d == null || !d.enabled) continue;
                var c = d.transform.position;
                if (Mathf.Abs(c.x - x) > half + 3f || Mathf.Abs(c.z - z) > half + 3f) continue;
                sb.Append(" | DECAL " + d.name + " at " + c.ToString("F1") + " size " + d.size.ToString("F1") + " mat " + (d.material != null ? d.material.name : "null"));
            }
            return sb.Length == 0 ? "nothing" : sb.ToString();
        }

        /// <summary>Bridge / debug: which shadow casters a ray straight up from (x, from, z) really hits (triangle test, any size).</summary>
        public static string DebugRayUp(float x, float z, float from)
        {
            var sb = new System.Text.StringBuilder();
            var o = new Vector3(x, from, z);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || !r.enabled || r.shadowCastingMode == ShadowCastingMode.Off) continue;
                var b = r.bounds;
                if (b.max.y < from || b.max.x < x || b.min.x > x || b.max.z < z || b.min.z > z) continue;
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) { mesh = new Mesh(); smr.BakeMesh(mesh, true); }
                string path = r.transform.name;
                for (var p = r.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                if (mesh == null || !mesh.isReadable) { sb.Append(" | ? " + path + " (" + r.GetType().Name + ", mesh not readable) y " + b.min.y.ToString("F1") + ".." + b.max.y.ToString("F1")); continue; }
                var m = r.transform.localToWorldMatrix;
                var v = mesh.vertices;
                var tri = mesh.triangles;
                float best = float.MaxValue;
                for (int i = 0; i < tri.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), c1 = m.MultiplyPoint3x4(v[tri[i + 1]]), c2 = m.MultiplyPoint3x4(v[tri[i + 2]]);
                    // 2D point-in-triangle on XZ, then height of the plane there
                    float d = (c1.z - c2.z) * (a.x - c2.x) + (c2.x - c1.x) * (a.z - c2.z);
                    if (Mathf.Abs(d) < 1e-9f) continue;
                    float w0 = ((c1.z - c2.z) * (x - c2.x) + (c2.x - c1.x) * (z - c2.z)) / d;
                    float w1 = ((c2.z - a.z) * (x - c2.x) + (a.x - c2.x) * (z - c2.z)) / d;
                    float w2 = 1f - w0 - w1;
                    if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                    float y = w0 * a.y + w1 * c1.y + w2 * c2.y;
                    if (y > from && y < best) best = y;
                }
                if (smr != null) Object.DestroyImmediate(mesh);
                if (best < float.MaxValue)
                {
                    var mat = r.sharedMaterial;
                    sb.Append(" | HIT " + path + " at y " + best.ToString("F2") + " [" + r.shadowCastingMode + " mat " + (mat != null ? mat.name : "null") + "]");
                }
            }
            return sb.Length == 0 ? "nothing" : sb.ToString();
        }

        /// <summary>Bridge / debug: which shadow casters the ray from (x, y, z) toward the sun hits (triangle test).</summary>
        public static string DebugRayToSun(float x, float y, float z)
        {
            var sun = RenderSettings.sun;
            if (sun == null) return "no sun";
            var dir = -sun.transform.forward;
            var sb = new System.Text.StringBuilder("sun " + sun.name + " fwd " + sun.transform.forward.ToString("F4") + " euler " + sun.transform.eulerAngles.ToString("F1"));
            var o = new Vector3(x, y, z);
            var ray = new Ray(o, dir);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || !r.enabled || r.shadowCastingMode == ShadowCastingMode.Off) continue;
                if (!r.bounds.IntersectRay(ray)) continue;
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) { mesh = new Mesh(); smr.BakeMesh(mesh, true); }
                string path = r.transform.name;
                for (var p = r.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                if (mesh == null) { sb.Append(" | ? " + path + " (" + r.GetType().Name + ")"); continue; }
                var m = r.transform.localToWorldMatrix;
                var v = mesh.vertices;
                var tri = mesh.triangles;
                float best = float.MaxValue;
                for (int i = 0; i < tri.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), b = m.MultiplyPoint3x4(v[tri[i + 1]]), c = m.MultiplyPoint3x4(v[tri[i + 2]]);
                    Vector3 e1 = b - a, e2 = c - a, pv = Vector3.Cross(dir, e2);
                    float det = Vector3.Dot(e1, pv);
                    if (Mathf.Abs(det) < 1e-9f) continue;
                    float inv = 1f / det;
                    Vector3 tv = o - a;
                    float u = Vector3.Dot(tv, pv) * inv;
                    if (u < 0f || u > 1f) continue;
                    Vector3 q = Vector3.Cross(tv, e1);
                    float w = Vector3.Dot(dir, q) * inv;
                    if (w < 0f || u + w > 1f) continue;
                    float t = Vector3.Dot(e2, q) * inv;
                    if (t > 0.05f && t < best) best = t;
                }
                if (smr != null) Object.DestroyImmediate(mesh);
                if (best < float.MaxValue)
                {
                    var mat = r.sharedMaterial;
                    sb.Append(" | HIT " + path + " at " + (o + dir * best).ToString("F1") + " [" + r.shadowCastingMode + " mat " + (mat != null ? mat.name : "null") + "]");
                }
                else sb.Append(" | box-only " + path);
            }
            return sb.ToString();
        }

        /// <summary>Bridge / debug: hidden (not saved, not in the hierarchy) renderers that cast shadows.</summary>
        public static string DebugHidden()
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (r == null || !r.gameObject.scene.IsValid() || !r.gameObject.activeInHierarchy || !r.enabled) continue;
                var go = r.gameObject;
                bool hidden = go.hideFlags != HideFlags.None || r.hideFlags != HideFlags.None;
                for (var p = go.transform.parent; p != null && !hidden; p = p.parent) hidden = p.gameObject.hideFlags != HideFlags.None;
                if (!hidden) continue;
                n++;
                string path = r.transform.name;
                for (var p = r.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                var b = r.bounds;
                sb.Append(" | " + path + " [" + go.hideFlags + " " + r.GetType().Name + " " + r.shadowCastingMode + " c " + b.center.ToString("F1") + " size " + b.size.ToString("F1") + " scene " + go.scene.name + "]");
                if (n >= 40) break;
            }
            return n + " hidden" + sb;
        }

        /// <summary>Bridge / debug: what the sky probe holds and what a renderer got.</summary>
        public static string DebugSky(string rendererName)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in all)
            {
                if (c == null) continue;
                c.RefreshSkyProbe();
                var res = new Color[3];
                c.skySH.Evaluate(shDirs, res);
                sb.Append("probe up " + res[0] + " side " + res[1] + " down " + res[2]);
                for (int i = 0; i < 7; i++) sb.Append(" | " + shNames[i] + " " + c.shVectors[i]);
            }
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || r.name != rendererName) continue;
                var b = new MaterialPropertyBlock();
                r.GetPropertyBlock(b);
                sb.Append(" || " + r.name + " probes " + r.lightProbeUsage + " block " + r.HasPropertyBlock() + " layers " + r.renderingLayerMask
                          + " SHAr " + b.GetVector(shIds[0]) + " SHAg " + b.GetVector(shIds[1]) + " SHC " + b.GetVector(shIds[6]));
                break;
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the sun dazzles

        static float glare, glareTime;
        static int glareFrame = -1;

        /// <summary>0..1: how much the camera looks into the sun (smoothed over time for the game camera, like an eye).</summary>
        float Dazzle(Camera cam, float outside)
        {
            if (sunGlare <= 0f && sunFlare <= 0f) return 0f;
            float look = Vector3.Dot(cam.transform.forward, -SunForward);
            float want = Smooth(0.6f, 0.985f, look) * outside;
            if (cam.cameraType != CameraType.Game) return want;
            if (glareFrame != Time.frameCount)
            {
                glareFrame = Time.frameCount;
                float now = Time.realtimeSinceStartup, dt = Mathf.Clamp(now - glareTime, 0f, 0.25f);
                glareTime = now;
                glare = Mathf.MoveTowards(glare, want, dt / Mathf.Max(0.05f, sunGlareTime));
            }
            return glare;
        }

        // ------------------------------------------------------------------ daylight in the mouths

        /// <summary>Daylight inside the tunnel mouths: full when the camera is in the tunnel (t = 0), "outside" share when it is out (t = 1).</summary>
        static void ScaleMouthLights(float t, float outside)
        {
            float k = Mathf.Lerp(1f, Mathf.Clamp01(outside), Mathf.Clamp01(t));
            foreach (var e in TunnelExit.Active)
            {
                if (e == null || !e.tunnelShadowsOnly) continue;
                var lt = e.transform.Find(TunnelExit.LightName);
                if (lt == null) continue;
                var l = lt.GetComponent<Light>();
                if (l == null) continue;
                float want = e.lightIntensity * Mathf.Max(0f, e.brightness) * k;
                if (Mathf.Abs(l.intensity - want) > 1e-3f) l.intensity = want;
            }
        }
    }
}
