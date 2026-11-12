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

    /// <summary>Keeps per-shaft material instances in sync with the transform.</summary>
    [ExecuteAlways]
    public class ShaftRuntime : MonoBehaviour
    {
        [NonSerialized] public Material beamMaterial;
        [NonSerialized] public Material dustMaterial;

        public void Sync()
        {
            if (dustMaterial != null) dustMaterial.SetMatrix("_WorldToShaft", transform.worldToLocalMatrix);
            transform.hasChanged = false;
        }

        void LateUpdate()
        {
            if (transform.hasChanged) Sync();
        }

        void OnDestroy()
        {
            if (beamMaterial != null) DestroyImmediate(beamMaterial);
            if (dustMaterial != null) DestroyImmediate(dustMaterial);
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
            var rt = root.AddComponent<ShaftRuntime>();

            float top = ceilY + Mathf.Max(0.3f, spec.height);
            float floorY = floorAtSlabX(cx);
            var poly = op.Polygon(Vector2.zero);

            // ---------- concrete walls, iron, lid ----------
            var kit = new MeshKit(3);
            kit.Prism(0, poly, ceilY - 0.002f, top + 0.03f, true, op.round, 0.5f);

            float grateY = top - BarHeight * 0.5f;
            float barOrigin = 0f;
            if (!op.round)
            {
                int count = Mathf.Max(1, Mathf.FloorToInt((op.halfW * 2f - 0.05f) / BarPitch) + 1);
                barOrigin = -(count - 1) * BarPitch * 0.5f;
                for (int i = 0; i < count; i++)
                {
                    float x = barOrigin + i * BarPitch;
                    kit.Box(1, new Vector3(x, grateY, 0), new Vector3(BarWidth * 0.5f, 0, 0), new Vector3(0, BarHeight * 0.5f, 0), new Vector3(0, 0, op.halfL + 0.02f), 2f);
                }
                // supports across the bars
                var supportZ = new List<float>();
                if (spec.type == ShaftType.GrateSquare) { supportZ.Add(-op.halfL / 3f); supportZ.Add(op.halfL / 3f); }
                else if (op.halfL > 0.22f) supportZ.Add(0f);
                foreach (float z in supportZ)
                    kit.Box(1, new Vector3(0, grateY - BarHeight * 0.5f - 0.015f, z), new Vector3(op.halfW + 0.02f, 0, 0), new Vector3(0, 0.015f, 0), new Vector3(0, 0, 0.012f), 2f);
                // angle-iron frame
                float fw = 0.035f, fh = 0.045f;
                kit.Box(1, new Vector3(0, top - fh, op.halfL + fw), new Vector3(op.halfW + fw * 2f, 0, 0), new Vector3(0, fh, 0), new Vector3(0, 0, fw), 2f);
                kit.Box(1, new Vector3(0, top - fh, -op.halfL - fw), new Vector3(op.halfW + fw * 2f, 0, 0), new Vector3(0, fh, 0), new Vector3(0, 0, fw), 2f);
                kit.Box(1, new Vector3(op.halfW + fw, top - fh, 0), new Vector3(fw, 0, 0), new Vector3(0, fh, 0), new Vector3(0, 0, op.halfL), 2f);
                kit.Box(1, new Vector3(-op.halfW - fw, top - fh, 0), new Vector3(fw, 0, 0), new Vector3(0, fh, 0), new Vector3(0, 0, op.halfL), 2f);
            }
            float lidR = op.radius + 0.035f;
            if (op.round)
            {
                // vented cast-iron lid resting on the shaft
                kit.Disc(2, new Vector3(0, top, 0), lidR, 40, false, 1f);
                kit.Disc(2, new Vector3(0, top + 0.045f, 0), lidR, 40, true, 1f);
                kit.Tube(1, new Vector3(0, top, 0), new Vector3(0, top + 0.045f, 0), lidR, lidR, 40, true, 2f);

                // ladder against the side facing the nearer wall
                float side = cx >= 0f ? 1f : -1f;
                float xl = side * (op.radius - 0.13f);
                float zr = Mathf.Min(0.21f, Mathf.Sqrt(Mathf.Max(0.01f, op.radius * op.radius - xl * xl)) - 0.05f);
                float yB = floorAtSlabX(cx + xl), yT = top - 0.06f;
                for (int s = -1; s <= 1; s += 2)
                    kit.Box(1, new Vector3(xl, (yB + yT) * 0.5f, s * zr), new Vector3(0.022f, 0, 0), new Vector3(0, (yT - yB) * 0.5f, 0), new Vector3(0, 0, 0.012f), 2f);
                for (float y = yB + 0.3f; y < yT - 0.12f; y += 0.3f)
                    kit.Tube(1, new Vector3(xl, y, -zr), new Vector3(xl, y, zr), 0.014f, 0.014f, 8, true, 2f);
                // brackets to the tunnel wall (below the ceiling) and to the shaft wall (above)
                var bracketY = new List<float> { yB + 0.35f };
                if (ceilY - 0.35f > yB + 0.8f) bracketY.Add(ceilY - 0.35f);
                foreach (float y in bracketY)
                {
                    float wallLocal = side * wallHalfAt(y) - cx;
                    float len = (wallLocal - xl) * side;
                    if (len < 0.03f) continue;
                    for (int s = -1; s <= 1; s += 2)
                        kit.Box(1, new Vector3((xl + wallLocal) * 0.5f, y, s * zr), new Vector3(len * 0.5f, 0, 0), new Vector3(0, 0.018f, 0), new Vector3(0, 0, 0.018f), 2f);
                }
                for (float y = ceilY + 0.5f; y < top - 0.3f; y += 1.2f)
                {
                    float wallLocal = side * op.radius;
                    float len = (wallLocal - xl) * side;
                    for (int s = -1; s <= 1; s += 2)
                        kit.Box(1, new Vector3((xl + wallLocal) * 0.5f, y, s * zr), new Vector3(len * 0.5f, 0, 0), new Vector3(0, 0.018f, 0), new Vector3(0, 0, 0.018f), 2f);
                }
            }
            var body = Child(root.transform, "Body");
            AddMesh(body, kit.ToMesh(name + "_Body"), new[] { mats ? mats.concrete : null, mats ? mats.iron : null, mats ? mats.lid : null },
                    ShadowCastingMode.TwoSided, true, true);

            // ---------- sky glow above ----------
            var skyKit = new MeshKit(1);
            float sky = op.MaxHalf + 1.6f;
            skyKit.Quad(0, new Vector3(-sky, top + 0.5f, -sky), new Vector3(sky, top + 0.5f, -sky), new Vector3(sky, top + 0.5f, sky), new Vector3(-sky, top + 0.5f, sky),
                        Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.down);
            AddMesh(Child(root.transform, "Sky"), skyKit.ToMesh(name + "_Sky"), new[] { mats ? mats.sky : null }, ShadowCastingMode.Off, false, false);

            // ---------- daylight ----------
            float lightH = op.round ? 12f : 8f;
            var L = new Vector3(0, top + lightH, 0);
            float dist = Mathf.Max(1f, L.y - floorY);
            var spotGo = Child(root.transform, "Daylight");
            spotGo.transform.localPosition = L;
            spotGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var spot = spotGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            float reach = (op.round ? op.radius : Mathf.Sqrt(op.halfW * op.halfW + op.halfL * op.halfL)) + 0.25f;
            spot.spotAngle = Mathf.Clamp(2f * Mathf.Atan(reach / lightH) * Mathf.Rad2Deg, 3f, 150f);
            spot.innerSpotAngle = spot.spotAngle * 0.8f;
            spot.range = dist * 2f;
            spot.color = spec.lightColor;
            spot.intensity = spec.brightness * dist * dist;
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 1f;
            spot.shadowNearPlane = 1f;
            spot.shadowBias = 0.02f;
            spot.shadowNormalBias = 0.2f;
            var ald = spot.GetUniversalAdditionalLightData();
            ald.usePipelineSettings = false;

            // light bounced from the lit floor onto the walls (cheap, no shadows)
            var bounceGo = Child(root.transform, "Bounce");
            bounceGo.transform.localPosition = new Vector3(0, floorY + 0.7f, 0);
            var bounce = bounceGo.AddComponent<Light>();
            bounce.type = LightType.Point;
            bounce.range = op.round ? 4.5f : 6.5f;
            bounce.color = spec.lightColor * new Color(0.95f, 0.85f, 0.72f);
            bounce.intensity = spec.bounce * spec.brightness * (op.round ? 0.012f : 0.05f) * Mathf.Clamp(op.MaxHalf, 0.4f, 1.4f);
            bounce.shadows = LightShadows.None;

            // ---------- visible light ----------
            if (op.round)
            {
                var rays = new MeshKit(1);
                foreach (var h in LidPattern.Holes)
                {
                    var hp = new Vector3((h.x - 0.5f) * 2f * lidR, top, (h.y - 0.5f) * 2f * lidR);
                    var dir = (hp - L).normalized;
                    float spreadAtCeil = new Vector2(hp.x, hp.z).magnitude * (L.y - ceilY) / (L.y - top);
                    if (spreadAtCeil > op.radius - 0.015f) continue;
                    float t = (floorY - hp.y) / dir.y;
                    var bottom = hp + dir * t;
                    float r0 = h.z * 2f * lidR;
                    float r1 = r0 * (L.y - floorY) / (L.y - top);
                    float len = Vector3.Distance(bottom, hp);
                    rays.Tube(0, bottom, hp, r1, r0, 8, true, 1f / len);
                }
                if (mats != null && mats.ray != null)
                    AddMesh(Child(root.transform, "Rays"), rays.ToMesh(name + "_Rays"), new[] { mats.ray }, ShadowCastingMode.Off, false, false);
            }
            else if (mats != null && mats.shaft != null && spec.beam > 0f)
            {
                float k = (L.y - floorY) / (L.y - ceilY);
                var bMin = new Vector3(-op.halfW * k - 0.05f, floorY - 0.05f, -op.halfL * k - 0.05f);
                var bMax = new Vector3(op.halfW * k + 0.05f, top - BarHeight, op.halfL * k + 0.05f);
                var box = new MeshKit(1);
                box.Box(0, (bMin + bMax) * 0.5f, new Vector3((bMax.x - bMin.x) * 0.5f, 0, 0), new Vector3(0, (bMax.y - bMin.y) * 0.5f, 0), new Vector3(0, 0, (bMax.z - bMin.z) * 0.5f), 1f);
                var m = new Material(mats.shaft) { name = name + "_Beam", hideFlags = HideFlags.DontSave };
                SetShaftParams(m, L, op, ceilY, grateY, barOrigin);
                m.SetVector("_BoxMin", bMin);
                m.SetVector("_BoxMax", bMax);
                m.SetColor("_Color", spec.lightColor);
                m.SetFloat("_Density", 0.18f * spec.beam * Mathf.Sqrt(Mathf.Max(0.1f, spec.brightness / 30f)));
                rt.beamMaterial = m;
                AddMesh(Child(root.transform, "Beam"), box.ToMesh(name + "_Beam"), new[] { m }, ShadowCastingMode.Off, false, false);
            }

            // ---------- dust motes ----------
            if (spec.dust && mats != null && mats.dust != null)
            {
                var psGo = Child(root.transform, "Dust");
                float hy = (ceilY - floorY);
                psGo.transform.localPosition = new Vector3(0, floorY + hy * 0.5f + 0.1f, 0);
                var ps = psGo.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true;
                main.prewarm = true;
                main.duration = 10f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 14f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 1f), new Color(1f, 0.95f, 0.85f, 0.45f));
                main.gravityModifier = 0.003f;
                main.maxParticles = 400;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.playOnAwake = true;
                float ex = op.HalfX * 1.25f + 0.15f, ez = op.HalfZ * 1.25f + 0.15f;
                var em = ps.emission;
                em.rateOverTime = Mathf.Clamp(ex * ez * hy * 9f, 6f, 60f);
                var sh = ps.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = new Vector3(ex * 2f, hy, ez * 2f);
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.06f;
                noise.frequency = 0.3f;
                noise.scrollSpeed = 0.04f;
                noise.damping = true;
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);
                var pr = psGo.GetComponent<ParticleSystemRenderer>();
                var dm = new Material(mats.dust) { name = name + "_Dust", hideFlags = HideFlags.DontSave };
                SetShaftParams(dm, L, op, ceilY, grateY, barOrigin);
                dm.SetColor("_Color", spec.lightColor * 2.2f);
                pr.sharedMaterial = dm;
                pr.renderMode = ParticleSystemRenderMode.Billboard;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                pr.receiveShadows = false;
                rt.dustMaterial = dm;
                ps.Play(true);
            }
            rt.Sync();
            return root;
        }

        static void SetShaftParams(Material m, Vector3 lightLocal, ShaftOpening op, float ceilY, float grateY, float barOrigin)
        {
            m.SetVector("_ShaftLight", new Vector4(lightLocal.x, lightLocal.y, lightLocal.z, 0));
            m.SetVector("_ShaftOpening", new Vector4(-op.HalfX, -op.HalfZ, op.HalfX, op.HalfZ));
            m.SetVector("_ShaftParams", new Vector4(ceilY, grateY, op.round ? op.radius : 0f, 0.03f));
            m.SetVector("_ShaftBars", op.round ? Vector4.zero : new Vector4(BarPitch, BarWidth, 0f, barOrigin));
        }
    }
}
