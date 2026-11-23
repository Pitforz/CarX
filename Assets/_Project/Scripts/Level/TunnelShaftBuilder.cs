using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarX.Level
{
    public struct ShaftOpening
    {
        public bool round;
        public float halfW, halfL, radius;

        public float MaxHalf { get { return round ? radius : Mathf.Max(halfW, halfL); } }
        public float HalfX { get { return round ? radius : halfW; } }
        public float HalfZ { get { return round ? radius : halfL; } }

        public List<Vector2> Polygon(Vector2 centre)
        {
            return round ? MeshKit.Circle(centre, radius, TunnelShaftBuilder.RoundSides) : MeshKit.Rect(centre, halfW, halfL);
        }

        public int Hash()
        {
            unchecked { return ((round ? 1 : 0) * 397) ^ (halfW.GetHashCode() * 31) ^ (halfL.GetHashCode() * 17) ^ radius.GetHashCode(); }
        }
    }

    /// <summary>Shape of a built shaft in its local space (x across the tunnel, y up, z along; origin on the floor under the opening).</summary>
    [Serializable]
    public struct ShaftGeom
    {
        public ShaftType type;
        public bool round;
        public float halfW, halfL, radius, lidR;
        /// <summary>Ceiling of the tunnel (the opening), top of the shaft, plane of the grate bars, floor under the opening.</summary>
        public float ceilY, top, grateY, floorY;
        public int barCount;
        public float barOrigin, barPitch, barWidth;
        public int supportCount;
        public float support0, support1, supportHalf;

        public float HalfX { get { return round ? radius : halfW; } }
        public float HalfZ { get { return round ? radius : halfL; } }
        public bool Valid { get { return top > ceilY && (round ? radius > 0f : halfW > 0f && halfL > 0f); } }
        public float Area { get { return round ? Mathf.PI * radius * radius : 4f * halfW * halfL; } }

        public List<Vector2> Outline()
        {
            var list = new List<Vector2>();
            if (round)
            {
                for (int i = 0; i < 24; i++)
                {
                    float a = i * Mathf.PI * 2f / 24f;
                    list.Add(new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius));
                }
            }
            else
            {
                list.Add(new Vector2(-halfW, -halfL));
                list.Add(new Vector2(halfW, -halfL));
                list.Add(new Vector2(halfW, halfL));
                list.Add(new Vector2(-halfW, halfL));
            }
            return list;
        }

        public int Hash()
        {
            unchecked
            {
                int h = (int)type * 7 + (round ? 1 : 0);
                foreach (var f in new[] { halfW, halfL, radius, lidR, ceilY, top, grateY, floorY, barOrigin, barPitch, barWidth, support0, support1, supportHalf })
                    h = h * 31 + Mathf.RoundToInt(f * 10000f);
                return (h * 31 + barCount) * 31 + supportCount;
            }
        }
    }

    /// <summary>
    /// Daylight through a grate or a manhole. Owns the sunlight (a realtime spot far above the grate with a cookie that draws
    /// the bars and the opening), the beam in the air, the dust and, for openings that can go dark, a live bounce light.
    /// Everything follows the shared settings in TunnelTypes live.
    /// </summary>
    [ExecuteAlways]
    public class ShaftRuntime : MonoBehaviour
    {
        [Tooltip("Насколько этот проём слепит глаз (для адаптации глаза)")]
        public float glare;
        [HideInInspector] public Vector3 openingLocal;
        [HideInInspector] public ShaftGeom geom;
        [HideInInspector] public TunnelMaterials materials;
        [HideInInspector] public Color tint = Color.white;
        [HideInInspector] public float brightness = 1f, beam = 1f, bounce = 1f;
        [HideInInspector] public bool dust = true;
        [Tooltip("Может гаснуть: свет этого проёма не запекается и целиком живой (задаётся в графе у решётки)")]
        public bool canFlicker;
        [Tooltip("Сколько света сейчас проходит, 0..1. Для мигания и проезжающих сверху машин: меняйте из скрипта")]
        [Range(0f, 1f)] public float lit = 1f;

        public const float LidOpenFraction = 0.022f;   // holes of the vented lid / lid area (LidPattern)

        [NonSerialized] public Light sun;
        [NonSerialized] public Light bounceLight;
        [NonSerialized] public Material beamMaterial;
        [NonSerialized] public Material dustMaterial;
        /// <summary>Local box where the light falls, from the grate down to the floor.</summary>
        [NonSerialized] public Bounds lightBounds;
        /// <summary>Centre of the lit patch on the floor (local) and its lit area, m².</summary>
        [NonSerialized] public Vector3 patchCentre;
        [NonSerialized] public float patchArea;
        /// <summary>Light on the floor right under the opening, and its colour.</summary>
        [NonSerialized] public float floorLight;
        [NonSerialized] public Color lightColor;

        [NonSerialized] Texture2D cookie;
        [NonSerialized] int cookieKey;
        [NonSerialized] Mesh beamMesh;
        [NonSerialized] float appliedLit = -1f;
        [NonSerialized] float baseSun, baseBounce, baseBeam;
        [NonSerialized] Color baseDust, baseSky;
        [NonSerialized] MaterialPropertyBlock skyBlock;

        static readonly List<ShaftRuntime> all = new List<ShaftRuntime>();
        public static List<ShaftRuntime> All { get { return all; } }

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
            TunnelEyeAdaptation.Hook();
            GrateLightManager.Hook();
            Apply();
        }

        void OnDisable() { all.Remove(this); }

        void OnDestroy()
        {
            Kill(beamMaterial);
            Kill(dustMaterial);
            Kill(cookie);
            Kill(beamMesh);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        /// <summary>Re-applies the shared settings to every opening (called when TunnelTypes changes).</summary>
        public static void RefreshAll()
        {
            foreach (var s in all.ToArray())
                if (s != null) s.Apply();
        }

        void LateUpdate()
        {
            if (transform.hasChanged)
            {
                transform.hasChanged = false;
                Apply();
                return;
            }
            if (!Mathf.Approximately(appliedLit, lit)) ApplyLit();
        }

        /// <summary>Kept for old callers: the dust needs the world-to-shaft matrix.</summary>
        public void Sync()
        {
            if (dustMaterial != null) dustMaterial.SetMatrix("_WorldToShaft", transform.worldToLocalMatrix);
            transform.hasChanged = false;
        }

        /// <summary>Strength of the soft halo: light scattered by the dusty beam and the bright floor (a point in the beam).</summary>
        public float HaloIntensity(TunnelTypeLibrary lib)
        {
            return lib.haloStrength * floorLight * patchArea * 0.2f;
        }

        /// <summary>Strength of the faint light that goes far down the dark tunnel (a point in the beam, long range).</summary>
        public float FarGlowIntensity(TunnelTypeLibrary lib)
        {
            return lib.farGlowStrength * floorLight * patchArea * 0.3f;
        }

        /// <summary>Strength of the light thrown up by the lit floor onto the walls and the ceiling (a spot facing up from the patch).</summary>
        public float WallBounceIntensity(TunnelTypeLibrary lib)
        {
            return lib.bounceStrength * bounce * floorLight * patchArea * 0.35f;
        }

        /// <summary>Height of the soft lights above the floor: in the middle of the beam.</summary>
        public float GlowHeight()
        {
            return Mathf.Clamp((geom.ceilY - geom.floorY) * 0.45f, 0.4f, 1.3f);
        }

        /// <summary>Bounce of the grate sunlight in the bake (1 = physical; the walls need more to read in the dark).</summary>
        public const float BounceBoost = 2.5f;

        public void Apply()
        {
            if (!geom.Valid) return;
            var lib = TunnelTypeLibrary.Current;
            var mats = TunnelMaterials.Resolve(materials);
            var g = geom;

            // ---------- where the sun is, seen from this opening ----------
            var s = transform.InverseTransformDirection(lib.SunDirection);
            if (s.sqrMagnitude < 1e-6f) s = Vector3.up;
            s.Normalize();
            if (s.y < 0.3f) { s.y = 0.3f; s.Normalize(); }   // a tilted network never gets light from below the horizon
            float D = Mathf.Max(2f, lib.sunDistance);
            var L = new Vector3(0f, g.grateY, 0f) + s * D;   // the spot light stands this far above the grate
            var f = -s;
            var rot = Quaternion.LookRotation(f, Vector3.forward);   // light x = across the tunnel (bars), light y = along
            var xa = rot * Vector3.right;
            var ya = rot * Vector3.up;

            float sharp = Mathf.Clamp01(lib.grateShadowSharpness);
            float sigBar = Mathf.Lerp(0.03f, 0.0015f, sharp);    // blur of the bar shadows at the grate, m
            float sigEdge = Mathf.Lerp(0.06f, 0.004f, sharp);    // blur of the opening edges, m
            float growth = Mathf.Lerp(0.012f, 0.002f, sharp);   // extra blur per metre below the grate (beam only)

            // ---------- the patch of light: outline through the opening down to the floor ----------
            var outline = g.Outline();
            float R = 0f, dMax = 0f;
            float kFloor = (L.y - g.floorY) / Mathf.Max(0.01f, L.y - g.ceilY);
            var bMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var bMax = -bMin;
            foreach (var p in outline)
            {
                foreach (float y in new[] { g.ceilY, g.grateY })
                {
                    var q = new Vector3(p.x, y, p.y) - L;
                    float z = Vector3.Dot(q, f);
                    if (z < 0.01f) continue;
                    float a = Vector3.Dot(q, xa) / z, b = Vector3.Dot(q, ya) / z;
                    R = Mathf.Max(R, Mathf.Sqrt(a * a + b * b));
                }
                var pc = new Vector3(p.x, g.ceilY, p.y);
                var pf = L + (pc - L) * kFloor;
                dMax = Mathf.Max(dMax, (pf - L).magnitude);
                bMin = Vector3.Min(bMin, Vector3.Min(pc, pf));
                bMax = Vector3.Max(bMax, Vector3.Max(pc, pf));
                var pg = new Vector3(p.x, g.grateY, p.y);
                bMin = Vector3.Min(bMin, pg);
                bMax = Vector3.Max(bMax, pg);
            }
            bMin -= new Vector3(0.05f, 0.05f, 0.05f);
            bMax += new Vector3(0.05f, 0f, 0.05f);
            lightBounds = new Bounds((bMin + bMax) * 0.5f, bMax - bMin);
            patchCentre = L + (new Vector3(0f, g.ceilY, 0f) - L) * kFloor;
            float open = g.round ? LidOpenFraction : Mathf.Clamp01(1f - g.barWidth / Mathf.Max(0.01f, g.barPitch));
            patchArea = g.Area * kFloor * kFloor * open;

            float E = Mathf.Max(0f, lib.shaftFloorLight) * brightness;
            var col = lib.grateLightColor * tint;
            col.a = 1f;
            floorLight = E;
            lightColor = col;
            glare = Mathf.Clamp01(E * (g.round ? lib.manholeOpenness : lib.grateOpenness) * Mathf.Sqrt(Mathf.Max(0.05f, g.Area)) / 4f);
            openingLocal = new Vector3(0f, g.ceilY, 0f);

            // ---------- the sunlight ----------
            float t = R * 1.08f + 3.5f * sigEdge / D + 0.002f;
            float dC = (patchCentre - L).magnitude;
            float range = Mathf.Max(dMax, dC) * 1.6f;
            float fr = dC / range;
            fr *= fr;
            float att = 1f - fr * fr;
            att *= att;
            sun = EnsureLight(sun, "Daylight", LightShadows.Soft);
            sun.transform.localPosition = L;
            sun.transform.localRotation = rot;
            sun.type = LightType.Spot;
            sun.spotAngle = 2f * Mathf.Atan(t) * Mathf.Rad2Deg;
            sun.innerSpotAngle = Mathf.Min(2f * Mathf.Atan(R * 1.02f) * Mathf.Rad2Deg, sun.spotAngle - 0.2f);
            sun.range = range;
            sun.color = col;
            baseSun = E * dC * dC / Mathf.Max(0.05f, att);
            sun.shadowStrength = 1f;
            sun.shadowNearPlane = Mathf.Clamp(D - 2f, 0.1f, 10f);
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            var ald = sun.GetUniversalAdditionalLightData();
            ald.usePipelineSettings = false;
            ald.softShadowQuality = SoftShadowQuality.Medium;
#if UNITY_EDITOR
            // Mixed: its bounce goes into the baked probes, the direct light stays live (NPC shadows). A flickering one is live only.
            sun.lightmapBakeType = canFlicker ? LightmapBakeType.Realtime : LightmapBakeType.Mixed;
            sun.bounceIntensity = BounceBoost * lib.bounceStrength * bounce;
            sun.shapeRadius = 0.05f;
#endif
            // a spot light's cookie must be square (Unity drops any other one: no bars, a round patch)
            int W = g.round ? 256 : 512, H = W;
            int key;
            unchecked
            {
                key = g.Hash();
                key = key * 31 + Mathf.RoundToInt(s.x * 10000f);
                key = key * 31 + Mathf.RoundToInt(s.y * 10000f);
                key = key * 31 + Mathf.RoundToInt(s.z * 10000f);
                key = key * 31 + Mathf.RoundToInt(D * 100f);
                key = key * 31 + Mathf.RoundToInt(sharp * 1000f);
                key = key * 31 + W * 7 + H;
            }
            if (cookie == null || key != cookieKey)
            {
                Kill(cookie);
                cookie = GrateCookie.Build(g, L, f, xa, ya, t, W, H, sigBar, sigEdge);
                cookie.name = name + "_Cookie";
                cookieKey = key;
            }
            sun.cookie = cookie;

            // ---------- live bounce: only for openings that can go dark (the others are baked) ----------
            if (canFlicker)
            {
                bounceLight = EnsureLight(bounceLight, "Bounce", LightShadows.Hard);
                bounceLight.transform.localPosition = patchCentre + Vector3.up * 0.05f;
                bounceLight.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                bounceLight.type = LightType.Spot;
                bounceLight.spotAngle = 160f;
                bounceLight.innerSpotAngle = 70f;
                bounceLight.range = Mathf.Max(1f, lib.haloRadius);
                bounceLight.color = col;
                bounceLight.shadowNearPlane = 0.1f;
                bounceLight.shadowBias = 0.05f;
                bounceLight.shadowNormalBias = 0.4f;
                baseBounce = HaloIntensity(lib) + WallBounceIntensity(lib) + lib.bounceStrength * BounceBoost * floorLight * patchArea * 0.11f;   // what the bake gives the others
#if UNITY_EDITOR
                bounceLight.lightmapBakeType = LightmapBakeType.Realtime;
#endif
            }
            else
            {
                var old = transform.Find("Bounce");
                if (old != null) KillGo(old.gameObject);
                bounceLight = null;
                baseBounce = 0f;
            }

            // ---------- the visible column of light in the air ----------
            float beamK = lib.shaftBeam * beam;
            var beamTr = transform.Find("Beam");
            if (mats != null && mats.shaft != null && beamK > 0f && E > 0.0001f)
            {
                var bm = bMin;
                var bx = bMax;
                bx.y = g.round ? g.ceilY : g.grateY - 0.035f;
                var go = beamTr != null ? beamTr.gameObject : NewChild("Beam");
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null) mf = go.AddComponent<MeshFilter>();
                var mr = go.GetComponent<MeshRenderer>();
                if (mr == null) mr = go.AddComponent<MeshRenderer>();
                var box = new MeshKit(1);
                box.Box(0, (bm + bx) * 0.5f, new Vector3((bx.x - bm.x) * 0.5f, 0, 0), new Vector3(0, (bx.y - bm.y) * 0.5f, 0), new Vector3(0, 0, (bx.z - bm.z) * 0.5f), 1f);
                Kill(beamMesh);
                beamMesh = box.ToMesh(name + "_Beam");
                mf.sharedMesh = beamMesh;
                if (beamMaterial == null || beamMaterial.shader != mats.shaft.shader)
                {
                    Kill(beamMaterial);
                    beamMaterial = new Material(mats.shaft) { name = name + "_Beam", hideFlags = HideFlags.DontSave };
                }
                SetShaftParams(beamMaterial, L, g, sigBar, sigEdge, growth);
                beamMaterial.SetVector("_BoxMin", bm);
                beamMaterial.SetVector("_BoxMax", bx);
                beamMaterial.SetColor("_Color", col);
                baseBeam = 0.18f * beamK * Mathf.Sqrt(E / 4f) * (g.round ? Mathf.Sqrt(lib.manholeOpenness) : 1f);
                mr.sharedMaterial = beamMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                TunnelGI.Exclude(mr);
            }
            else if (beamTr != null) KillGo(beamTr.gameObject);

            // ---------- dust that shows only inside the light ----------
            float amount = dust ? lib.dustAmount : 0f;
            var dustTr = transform.Find("Dust");
            if (amount > 0.001f && mats != null && mats.dust != null && E > 0.0001f)
            {
                bool created = dustTr == null;
                var go = created ? NewChild("Dust") : dustTr.gameObject;
                var ps = go.GetComponent<ParticleSystem>();
                if (ps == null) { ps = go.AddComponent<ParticleSystem>(); created = true; }
                float hy = Mathf.Max(0.3f, g.ceilY - g.floorY);
                var size = new Vector3(Mathf.Max(0.3f, lightBounds.size.x), hy, Mathf.Max(0.3f, lightBounds.size.z));
                go.transform.localPosition = new Vector3(lightBounds.center.x, g.floorY + hy * 0.5f + 0.05f, lightBounds.center.z);
                go.transform.localRotation = Quaternion.identity;
                var main = ps.main;
                if (created)
                {
                    // duration and prewarm can only be set on a stopped system
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    main.loop = true;
                    main.prewarm = true;
                    main.duration = 10f;
                }
                main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 14f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 1f), new Color(1f, 0.95f, 0.85f, 0.45f));
                main.gravityModifier = 0.003f;
                main.maxParticles = Mathf.Max(10, Mathf.RoundToInt(400f * amount));
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.playOnAwake = true;
                var em = ps.emission;
                em.rateOverTime = Mathf.Clamp(size.x * size.z * hy * 2.2f, 6f, 60f) * amount;
                var sh = ps.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = size;
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.06f;
                noise.frequency = 0.3f;
                noise.scrollSpeed = 0.04f;
                noise.damping = true;
                var colm = ps.colorOverLifetime;
                colm.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
                colm.color = new ParticleSystem.MinMaxGradient(grad);
                var pr = go.GetComponent<ParticleSystemRenderer>();
                if (dustMaterial == null || dustMaterial.shader != mats.dust.shader)
                {
                    Kill(dustMaterial);
                    dustMaterial = new Material(mats.dust) { name = name + "_Dust", hideFlags = HideFlags.DontSave };
                }
                SetShaftParams(dustMaterial, L, g, sigBar, sigEdge, growth);
                baseDust = col * (2.2f * Mathf.Sqrt(E / 4f) * (g.round ? Mathf.Sqrt(lib.manholeOpenness) : 1f));
                pr.sharedMaterial = dustMaterial;
                pr.renderMode = ParticleSystemRenderMode.Billboard;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                pr.receiveShadows = false;
                pr.lightProbeUsage = LightProbeUsage.Off;
                if (created || !ps.isPlaying) ps.Play(true);
            }
            else
            {
                if (dustTr != null) KillGo(dustTr.gameObject);
                Kill(dustMaterial);
                dustMaterial = null;
            }

            // ---------- the sky seen through the opening ----------
            var skyTr = transform.Find("Sky");
            baseSky = lib.GrateSkyColor * tint * brightness;
            if (skyTr != null)
            {
                var skyR = skyTr.GetComponent<MeshRenderer>();
                if (skyR != null) TunnelGI.Exclude(skyR);
            }

            Sync();
            ApplyLit();
        }

        /// <summary>Applies «lit» without rebuilding anything (cheap: call every frame while it flickers).</summary>
        public void ApplyLit()
        {
            appliedLit = lit;
            float k = Mathf.Clamp01(lit);
            if (sun != null)
            {
                sun.intensity = baseSun * k;
                sun.enabled = baseSun * k > 0.0001f;
            }
            if (bounceLight != null) bounceLight.intensity = baseBounce * k;
            if (beamMaterial != null) beamMaterial.SetFloat("_Density", baseBeam * k);
            if (dustMaterial != null) dustMaterial.SetColor("_Color", baseDust * k);
            var skyTr = transform.Find("Sky");
            if (skyTr != null)
            {
                var skyR = skyTr.GetComponent<MeshRenderer>();
                if (skyR != null)
                {
                    if (skyBlock == null) skyBlock = new MaterialPropertyBlock();
                    skyBlock.SetColor("_Color", baseSky * k);
                    skyR.SetPropertyBlock(skyBlock);
                }
            }
        }

        Light EnsureLight(Light l, string childName, LightShadows shadows)
        {
            if (l != null) return l;
            var tr = transform.Find(childName);
            if (tr != null) l = tr.GetComponent<Light>();
            if (l != null) return l;
            var go = tr != null ? tr.gameObject : NewChild(childName);
            l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.shadows = shadows;
            return l;
        }

        GameObject NewChild(string childName)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            return go;
        }

        static void KillGo(GameObject go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        static void SetShaftParams(Material m, Vector3 L, ShaftGeom g, float sigBar, float sigEdge, float growth)
        {
            m.SetVector("_ShaftLight", new Vector4(L.x, L.y, L.z, 0));
            m.SetVector("_ShaftOpening", new Vector4(-g.HalfX, -g.HalfZ, g.HalfX, g.HalfZ));
            m.SetVector("_ShaftParams", new Vector4(g.ceilY, g.grateY, g.round ? g.radius : 0f, sigEdge));
            m.SetVector("_ShaftBars", g.round ? Vector4.zero : new Vector4(g.barPitch, g.barWidth, 0f, g.barOrigin));
            m.SetVector("_ShaftSharp", new Vector4(sigBar, growth, sigEdge, growth * 1.5f));
        }
    }

    /// <summary>The picture the sunlight projects: the grate bars and the opening (or the lid holes), seen from the light.</summary>
    static class GrateCookie
    {
        const float TableStep = 0.0025f;

        public static Texture2D Build(ShaftGeom g, Vector3 L, Vector3 f, Vector3 xa, Vector3 ya, float t, int W, int H, float sigBar, float sigEdge)
        {
            var px = new byte[W * H * 4];
            float[] topX = null, topZ = null, ceilX = null, ceilZ = null;
            float x0 = 0f, z0 = 0f;
            Vector3[] holes = null;
            if (!g.round)
            {
                float ex = g.halfW + 0.6f, ez = g.halfL + 0.6f;
                x0 = -ex;
                z0 = -ez;
                int nx = Mathf.CeilToInt(2f * ex / TableStep) + 1, nz = Mathf.CeilToInt(2f * ez / TableStep) + 1;
                topX = new float[nx];
                ceilX = new float[nx];
                for (int i = 0; i < nx; i++)
                {
                    float x = x0 + i * TableStep;
                    float edge = Step(g.halfW - Mathf.Abs(x), sigEdge);
                    ceilX[i] = edge;
                    float bars = 1f;
                    if (g.barCount > 0 && g.barPitch > 0f)
                    {
                        float k = Mathf.Round((x - g.barOrigin) / g.barPitch);
                        for (int n = -2; n <= 2; n++)
                        {
                            float bi = k + n;
                            if (bi < 0 || bi > g.barCount - 1) continue;
                            bars -= Cover(x - (g.barOrigin + bi * g.barPitch), g.barWidth * 0.5f, sigBar);
                        }
                    }
                    topX[i] = edge * Mathf.Clamp01(bars);
                }
                topZ = new float[nz];
                ceilZ = new float[nz];
                for (int i = 0; i < nz; i++)
                {
                    float z = z0 + i * TableStep;
                    float edge = Step(g.halfL - Mathf.Abs(z), sigEdge);
                    ceilZ[i] = edge;
                    float sup = 1f;
                    if (g.supportCount > 0) sup -= Cover(z - g.support0, g.supportHalf, sigBar);
                    if (g.supportCount > 1) sup -= Cover(z - g.support1, g.supportHalf, sigBar);
                    topZ[i] = edge * Mathf.Clamp01(sup);
                }
            }
            else
            {
                holes = new Vector3[LidPattern.Holes.Length];
                for (int i = 0; i < holes.Length; i++)
                {
                    var hp = LidPattern.Holes[i];
                    holes[i] = new Vector3((hp.x - 0.5f) * 2f * g.lidR, (hp.y - 0.5f) * 2f * g.lidR, hp.z * 2f * g.lidR);
                }
            }
            float sigHole = Mathf.Max(0.001f, sigBar);

            for (int j = 0; j < H; j++)
            {
                float b = ((j + 0.5f) / H * 2f - 1f) * t;
                for (int i = 0; i < W; i++)
                {
                    float v = 0f;
                    if (i > 0 && j > 0 && i < W - 1 && j < H - 1)
                    {
                        float a = ((i + 0.5f) / W * 2f - 1f) * t;
                        var d = f + xa * a + ya * b;
                        if (d.y < -1e-4f)
                        {
                            float lc = (g.ceilY - L.y) / d.y;
                            float cx = L.x + d.x * lc, cz = L.z + d.z * lc;
                            if (!g.round)
                            {
                                float lt = (g.grateY - L.y) / d.y;
                                float gx = L.x + d.x * lt, gz = L.z + d.z * lt;
                                v = Lookup(ceilX, x0, cx) * Lookup(ceilZ, z0, cz);
                                if (v > 0f) v *= Lookup(topX, x0, gx) * Lookup(topZ, z0, gz);
                            }
                            else
                            {
                                v = Step(g.radius - Mathf.Sqrt(cx * cx + cz * cz), sigEdge);
                                if (v > 0f)
                                {
                                    float lt = (g.top - L.y) / d.y;
                                    float gx = L.x + d.x * lt, gz = L.z + d.z * lt;
                                    float h = 0f;
                                    foreach (var hole in holes)
                                    {
                                        float dx = gx - hole.x, dz = gz - hole.y;
                                        float dist = Mathf.Sqrt(dx * dx + dz * dz);
                                        float edge = hole.z - dist;
                                        if (edge < -4f * sigHole) continue;
                                        h = Mathf.Max(h, Step(edge, Mathf.Min(sigHole, hole.z * 0.6f)));
                                    }
                                    v *= h;
                                }
                            }
                        }
                    }
                    byte c = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
                    int k = (j * W + i) * 4;
                    px[k] = c;
                    px[k + 1] = c;
                    px[k + 2] = c;
                    px[k + 3] = c;
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixelData(px, 0);
            tex.Apply(false, true);
            return tex;
        }

        static float Lookup(float[] table, float origin, float x)
        {
            float u = (x - origin) / TableStep;
            if (u <= 0f) return table[0];
            int n = table.Length - 1;
            if (u >= n) return table[n];
            int i = (int)u;
            float fr = u - i;
            return table[i] + (table[i + 1] - table[i]) * fr;
        }

        /// <summary>Soft step: 0 outside, 1 inside, blurred by a Gaussian of width sigma (d = signed distance inside).</summary>
        static float Step(float d, float sigma)
        {
            if (sigma < 1e-5f) return d >= 0f ? 1f : 0f;
            return 0.5f * (1f + Erf(d / (sigma * 1.4142136f)));
        }

        /// <summary>How much of a blurred band [-half, half] covers the point at distance d from its centre.</summary>
        static float Cover(float d, float half, float sigma)
        {
            if (sigma < 1e-5f) return Mathf.Abs(d) <= half ? 1f : 0f;
            float k = 1f / (sigma * 1.4142136f);
            return 0.5f * (Erf((d + half) * k) - Erf((d - half) * k));
        }

        static float Erf(float x)
        {
            float sgn = x < 0f ? -1f : 1f;
            x = Mathf.Abs(x);
            if (x > 4f) return sgn;
            float t = 1f / (1f + 0.3275911f * x);
            float y = 1f - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t * Mathf.Exp(-x * x);
            return sgn * y;
        }
    }

    public static class TunnelShaftBuilder
    {
        public const int RoundSides = 24;
        const float BarPitch = 0.1f, BarWidth = 0.028f, BarHeight = 0.07f;

        /// <summary>Fits the opening into the flat ceiling [xMin, xMax]; returns the lateral centre cx.</summary>
        public static ShaftOpening MakeOpening(ShaftSpec spec, float xMin, float xMax, float maxHalfL, out float cx)
        {
            const float margin = 0.1f;
            float availHalf = Mathf.Max(0.15f, (xMax - xMin) * 0.5f - margin);
            maxHalfL = Mathf.Max(0.1f, maxHalfL);
            float mid = (xMin + xMax) * 0.5f;
            var op = new ShaftOpening();
            switch (spec.type)
            {
                case ShaftType.GrateSquare:
                    op.halfW = op.halfL = Mathf.Min(spec.width * 0.5f, availHalf, maxHalfL);
                    break;
                case ShaftType.Manhole:
                    op.round = true;
                    op.radius = Mathf.Min(spec.diameter * 0.5f, availHalf, maxHalfL);
                    break;
                default:
                    op.halfW = Mathf.Min(spec.width * 0.5f, availHalf);
                    op.halfL = Mathf.Clamp(spec.length * 0.5f, 0.1f, maxHalfL);
                    break;
            }
            float lo = xMin + margin + op.HalfX, hi = xMax - margin - op.HalfX;
            cx = lo <= hi ? Mathf.Clamp(mid + spec.lateral, lo, hi) : mid;
            return op;
        }

        static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static MeshRenderer AddMesh(GameObject go, Mesh mesh, Material[] mats, ShadowCastingMode shadows, bool receive, bool collider)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = shadows;
            r.receiveShadows = receive;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return r;
        }

        /// <summary>
        /// Builds a shaft rising from the ceiling. Local space of the result: origin = centre of the opening at floor level,
        /// x = across the tunnel, y = up, z = along the tunnel.
        /// wallHalfAt(y): half width of the host room at height y (for ladder brackets).
        /// floorAtSlabX(x): floor height of the host at lateral position x (in host coordinates, x = cx + local x).
        /// </summary>
        public static GameObject Build(Transform parent, string name, Vector3 localPos, Quaternion localRot, float ceilY, float cx,
                                       ShaftOpening op, ShaftSpec spec, Func<float, float> wallHalfAt, Func<float, float> floorAtSlabX,
                                       TunnelMaterials mats)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;

            float top = ceilY + Mathf.Max(0.3f, spec.height);
            float floorY = floorAtSlabX(cx);
            var poly = op.Polygon(Vector2.zero);

            // ---------- concrete walls (cast shadows), iron and lid (no shadows: the light's cookie draws the bars) ----------
            var concrete = new MeshKit(1);
            concrete.Prism(0, poly, ceilY - 0.002f, top + 0.03f, true, op.round, 0.5f);
            var kit = new MeshKit(2);   // 0 iron, 1 lid

            float grateY = top - BarHeight * 0.5f;
            float barOrigin = 0f;
            int barCount = 0;
            var supportZ = new List<float>();
            if (!op.round)
            {
                barCount = Mathf.Max(1, Mathf.FloorToInt((op.halfW * 2f - 0.05f) / BarPitch) + 1);
                barOrigin = -(barCount - 1) * BarPitch * 0.5f;
                for (int i = 0; i < barCount; i++)
                {
                    float x = barOrigin + i * BarPitch;
                    kit.Box(0, new Vector3(x, grateY, 0), new Vector3(BarWidth * 0.5f, 0, 0), new Vector3(0, BarHeight * 0.5f, 0), new Vector3(0, 0, op.halfL + 0.02f), 2f);
                }
                // supports across the bars
                if (spec.type == ShaftType.GrateSquare) { supportZ.Add(-op.halfL / 3f); supportZ.Add(op.halfL / 3f); }
                else if (op.halfL > 0.22f) supportZ.Add(0f);
                foreach (float z in supportZ)
                    kit.Box(0, new Vector3(0, grateY - BarHeight * 0.5f - 0.015f, z), new Vector3(op.halfW + 0.02f, 0, 0), new Vector3(0, 0.015f, 0), new Vector3(0, 0, 0.012f), 2f);
                // angle-iron frame
                float fw = 0.035f, fh = 0.045f;
                kit.Box(0, new Vector3(0, top - fh, op.halfL + fw), new Vector3(op.halfW + fw * 2f, 0, 0), new Vector3(0, fh, 0), new Vector3(0, 0, fw), 2f);
                kit.Box(0, new Vector3(0, top - fh, -op.halfL - fw), new Vector3(op.halfW + fw * 2f, 0, 0), new Vector3(0, fh, 0), new Vector3(0, 0, fw), 2f);
                kit.Box(0, new Vector3(op.halfW + fw, top - fh, 0), new Vector3(fw, 0, 0), new Vector3(0, fh, 0), new Vector3(0, 0, op.halfL), 2f);
                kit.Box(0, new Vector3(-op.halfW - fw, top - fh, 0), new Vector3(fw, 0, 0), new Vector3(0, fh, 0), new Vector3(0, 0, op.halfL), 2f);
            }
            float lidR = op.radius + 0.035f;
            if (op.round)
            {
                // vented cast-iron lid resting on the shaft
                kit.Disc(1, new Vector3(0, top, 0), lidR, 40, false, 1f);
                kit.Disc(1, new Vector3(0, top + 0.045f, 0), lidR, 40, true, 1f);
                kit.Tube(0, new Vector3(0, top, 0), new Vector3(0, top + 0.045f, 0), lidR, lidR, 40, true, 2f);

                // ladder against the side facing the nearer wall
                float side = cx >= 0f ? 1f : -1f;
                float xl = side * (op.radius - 0.13f);
                float zr = Mathf.Min(0.21f, Mathf.Sqrt(Mathf.Max(0.01f, op.radius * op.radius - xl * xl)) - 0.05f);
                float yB = floorAtSlabX(cx + xl), yT = top - 0.06f;
                for (int s = -1; s <= 1; s += 2)
                    kit.Box(0, new Vector3(xl, (yB + yT) * 0.5f, s * zr), new Vector3(0.022f, 0, 0), new Vector3(0, (yT - yB) * 0.5f, 0), new Vector3(0, 0, 0.012f), 2f);
                for (float y = yB + 0.3f; y < yT - 0.12f; y += 0.3f)
                    kit.Tube(0, new Vector3(xl, y, -zr), new Vector3(xl, y, zr), 0.014f, 0.014f, 8, true, 2f);
                // brackets to the tunnel wall (below the ceiling) and to the shaft wall (above)
                var bracketY = new List<float> { yB + 0.35f };
                if (ceilY - 0.35f > yB + 0.8f) bracketY.Add(ceilY - 0.35f);
                foreach (float y in bracketY)
                {
                    float wallLocal = side * wallHalfAt(y) - cx;
                    float len = (wallLocal - xl) * side;
                    if (len < 0.03f) continue;
                    for (int s = -1; s <= 1; s += 2)
                        kit.Box(0, new Vector3((xl + wallLocal) * 0.5f, y, s * zr), new Vector3(len * 0.5f, 0, 0), new Vector3(0, 0.018f, 0), new Vector3(0, 0, 0.018f), 2f);
                }
                for (float y = ceilY + 0.5f; y < top - 0.3f; y += 1.2f)
                {
                    float wallLocal = side * op.radius;
                    float len = (wallLocal - xl) * side;
                    for (int s = -1; s <= 1; s += 2)
                        kit.Box(0, new Vector3((xl + wallLocal) * 0.5f, y, s * zr), new Vector3(len * 0.5f, 0, 0), new Vector3(0, 0.018f, 0), new Vector3(0, 0, 0.018f), 2f);
                }
            }
            // storm-drain grates (ливнёвки) take no part in collision: only the manhole keeps its colliders
            bool collide = spec.type == ShaftType.Manhole;
            var body = AddMesh(Child(root.transform, "Body"), concrete.ToMesh(name + "_Body"), new[] { mats ? mats.concrete : null },
                               ShadowCastingMode.TwoSided, true, collide);
            TunnelGI.Setup(body);
            var iron = AddMesh(Child(root.transform, "Iron"), kit.ToMesh(name + "_Iron"), new[] { mats ? mats.iron : null, mats ? mats.lid : null },
                               ShadowCastingMode.Off, true, collide);
            TunnelGI.Setup(iron, false);

            // ---------- sky glow above ----------
            var skyKit = new MeshKit(1);
            float sky = op.MaxHalf + 1.6f;
            skyKit.Quad(0, new Vector3(-sky, top + 0.5f, -sky), new Vector3(sky, top + 0.5f, -sky), new Vector3(sky, top + 0.5f, sky), new Vector3(-sky, top + 0.5f, sky),
                        Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.down);
            var skyR = AddMesh(Child(root.transform, "Sky"), skyKit.ToMesh(name + "_Sky"), new[] { mats ? mats.sky : null }, ShadowCastingMode.Off, false, false);
            TunnelGI.Exclude(skyR);

            // ---------- daylight: the runtime owns the light, the beam and the dust ----------
            // old scenes stored the former warm default per shaft: treat it as «no tint»
            var tint = spec.lightColor;
            if (Mathf.Abs(tint.r - 1f) < 0.01f && Mathf.Abs(tint.g - 0.93f) < 0.01f && Mathf.Abs(tint.b - 0.8f) < 0.01f) tint = Color.white;
            var g = new ShaftGeom
            {
                type = spec.type,
                round = op.round,
                halfW = op.round ? 0f : op.halfW,
                halfL = op.round ? 0f : op.halfL,
                radius = op.round ? op.radius : 0f,
                lidR = lidR,
                ceilY = ceilY,
                top = top,
                grateY = grateY,
                floorY = floorY,
                barCount = barCount,
                barOrigin = barOrigin,
                barPitch = BarPitch,
                barWidth = BarWidth,
                supportCount = supportZ.Count,
                support0 = supportZ.Count > 0 ? supportZ[0] : 0f,
                support1 = supportZ.Count > 1 ? supportZ[1] : 0f,
                supportHalf = 0.012f
            };
            var rt = root.AddComponent<ShaftRuntime>();
            rt.materials = mats;
            rt.tint = tint;
            float def = ShaftSpec.DefaultBrightness(spec.type);
            rt.brightness = def > 0f ? spec.brightness / def : 0f;
            rt.beam = spec.beam;
            rt.bounce = spec.bounce;
            rt.dust = spec.dust;
            rt.canFlicker = spec.canFlicker;
            rt.geom = g;
            rt.Apply();
            return root;
        }

        /// <summary>
        /// Vertical iron grate across the tunnel section. Same local space as Build:
        /// origin on the axis at floor level, x across, y up, z along. A solid box blocks the pipe.
        /// </summary>
        public static GameObject BuildPipeGrate(Transform parent, string name, Vector3 localPos, Quaternion localRot,
                                               TunnelSection section, TunnelMaterials mats)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;

            var kit = new MeshKit(1);
            float hz = BarHeight * 0.5f;
            const float inset = 0.03f;
            float span = Mathf.Max(0.2f, section.HalfSpan - inset);
            int count = Mathf.Max(1, Mathf.FloorToInt((span * 2f - 0.05f) / BarPitch) + 1);
            float origin = -(count - 1) * BarPitch * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float x = origin + i * BarPitch;
                float y0 = section.FloorHeightAt(x) + inset;
                float y1 = section.CeilingHeightAt(x) - inset;
                if (y1 - y0 < 0.04f) continue;
                float mid = (y0 + y1) * 0.5f;
                kit.Box(0, new Vector3(x, mid, 0f), new Vector3(BarWidth * 0.5f, 0f, 0f), new Vector3(0f, (y1 - y0) * 0.5f, 0f), new Vector3(0f, 0f, hz), 2f);
            }
            float midY = section.Height * 0.5f;
            float halfW = Mathf.Max(0.05f, section.WallXAt(midY) - inset);
            kit.Box(0, new Vector3(0f, midY, 0f), new Vector3(halfW, 0f, 0f), new Vector3(0f, BarWidth * 0.5f, 0f), new Vector3(0f, 0f, hz), 2f);

            float fw = 0.035f;
            if (section.profile == TunnelProfile.Round)
            {
                float r = Mathf.Max(0.2f, section.HalfSpan - inset);
                float cy = section.Height - section.HalfSpan;
                int sides = Mathf.Max(12, RoundSides);
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    var p0 = new Vector3(Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r, 0f);
                    var p1 = new Vector3(Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r, 0f);
                    var mid = (p0 + p1) * 0.5f;
                    if (mid.y < inset) continue;
                    var alongBar = p1 - p0;
                    float len = alongBar.magnitude;
                    if (len < 1e-4f) continue;
                    alongBar /= len;
                    var radial = new Vector3(mid.x, mid.y - cy, 0f);
                    if (radial.sqrMagnitude < 1e-6f) continue;
                    radial.Normalize();
                    kit.Box(0, mid, alongBar * (len * 0.5f), radial * fw, Vector3.forward * (hz + 0.01f), 2f);
                }
            }
            else
            {
                float y0 = inset, y1 = section.Height - inset;
                float hy = (y1 - y0) * 0.5f;
                kit.Box(0, new Vector3(0f, y1, 0f), new Vector3(span + fw, 0f, 0f), new Vector3(0f, fw, 0f), new Vector3(0f, 0f, hz + 0.01f), 2f);
                kit.Box(0, new Vector3(0f, y0, 0f), new Vector3(span + fw, 0f, 0f), new Vector3(0f, fw, 0f), new Vector3(0f, 0f, hz + 0.01f), 2f);
                kit.Box(0, new Vector3(span, midY, 0f), new Vector3(fw, 0f, 0f), new Vector3(0f, hy, 0f), new Vector3(0f, 0f, hz + 0.01f), 2f);
                kit.Box(0, new Vector3(-span, midY, 0f), new Vector3(fw, 0f, 0f), new Vector3(0f, hy, 0f), new Vector3(0f, 0f, hz + 0.01f), 2f);
            }

            var body = Child(root.transform, "Body");
            var bodyR = AddMesh(body, kit.ToMesh(name + "_Body"), new[] { mats != null ? mats.iron : null },
                                ShadowCastingMode.TwoSided, true, true);
            TunnelGI.Setup(bodyR, false);

            float blockH = Mathf.Max(0.4f, section.Height - 0.04f);
            var block = Child(root.transform, "Block");
            var box = block.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, blockH * 0.5f + 0.02f, 0f);
            box.size = new Vector3(Mathf.Max(0.4f, section.HalfSpan * 2f - 0.06f), blockH, BarHeight);
            return root;
        }
    }
}
