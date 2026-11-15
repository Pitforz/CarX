using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarX.Level
{
    /// <summary>
    /// Open end of a tunnel to the outside (graph node of kind «Выход наружу»). Built by TunnelNetwork.
    /// Local frame: origin = floor centre of the mouth, +Z = out of the tunnel.
    /// Children: a glowing «daylight» box seen through the mouth (blown out by bloom), a wide spot light shining
    /// along the tunnel (grazing light on the walls) and a local volume that lowers the exposure near the light.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("CarX/Tunnel Exit")]
    public class TunnelExit : MonoBehaviour
    {
        [Tooltip("Ширина и высота проёма тоннеля, м")]
        public float width = 3f, height = 2.4f;
        [Tooltip("Множитель яркости этого выхода")]
        public float brightness = 1f;
        public float lightIntensity = 40f, lightRange = 55f;
        [Tooltip("На сколько метров снаружи от проёма стоит свет. Дальше = ровнее свет у выхода (как от большого окна), а не слепящее пятно")]
        public float lightBack = 8f;
        public Color lightColor = new Color(0.9f, 0.95f, 1f);
        public Color skyColor = new Color(6f, 6.3f, 7f);
        [Tooltip("Тени от дневного света (люди и предметы у выхода дают тени на пол)")]
        public bool shadows = true;
        public Material skyMaterial;
        public VolumeProfile exposureProfile;
        [Tooltip("На какую глубину внутрь тоннеля действует пониженная экспозиция, м")]
        public float exposureDepth = 14f;
        [HideInInspector] public int drivenSignature;
        [SerializeField, HideInInspector] Volume volume;

        static readonly System.Collections.Generic.List<TunnelExit> active = new System.Collections.Generic.List<TunnelExit>();

        public const string SkyName = "_Daylight", LightName = "_Light", VolumeName = "_Exposure";
        [System.NonSerialized] Mesh mesh;

        void OnEnable()
        {
            // the light box mesh is not saved with the scene: make it again
            drivenSignature = 0;
            Rebuild();
            if (!active.Contains(this)) active.Add(this);
            TunnelEyeAdaptation.Hook();
        }

        void OnDisable()
        {
            active.Remove(this);
        }

        void OnDestroy() { if (mesh != null) DestroyImmediate(mesh); }

        public static System.Collections.Generic.List<TunnelExit> Active { get { return active; } }

        int Signature()
        {
            unchecked
            {
                int h = 17;
                foreach (var f in new[] { width, height, brightness, lightIntensity, lightRange, lightBack, exposureDepth, lightColor.r, lightColor.g, lightColor.b, skyColor.r, skyColor.g, skyColor.b })
                    h = h * 31 + Mathf.RoundToInt(f * 1000f);
                h = h * 31 + (skyMaterial != null ? skyMaterial.name.GetHashCode() : 1);
                h = h * 31 + (exposureProfile != null ? exposureProfile.name.GetHashCode() : 1);
                h = h * 31 + (mesh != null ? 1 : 0) + (shadows ? 2 : 0);
                return h;
            }
        }

        public void Rebuild()
        {
            int sig = Signature();
            if (sig == drivenSignature) return;
            float w = Mathf.Max(0.8f, width), h = Mathf.Max(0.8f, height), k = Mathf.Max(0f, brightness);

            // daylight: an open box of glowing walls just past the mouth (everything seen through the opening is bright)
            var sky = Child(SkyName);
            var mf = Get<MeshFilter>(sky);
            var mr = Get<MeshRenderer>(sky);
            if (mesh == null) mesh = new Mesh { name = name + "_Daylight", hideFlags = HideFlags.DontSave };
            BuildBox(mesh, w * 0.5f + 4f, -2f, h + 4f, 0.05f, 7f);
            mf.sharedMesh = mesh;
            mr.sharedMaterial = skyMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", skyColor * k);
            mpb.SetFloat("_Horizon", 0.15f);
            mr.SetPropertyBlock(mpb);

            // grazing light: a spot out in the daylight, on the tunnel axis, shining in through the mouth like a window.
            // Set back from the mouth so the walls right at the exit are not burnt out (a point light at the mouth would be)
            float back = Mathf.Max(0.6f, lightBack);
            var lt = Child(LightName);
            lt.localPosition = new Vector3(0f, h * 0.6f, back);
            lt.localRotation = Quaternion.Euler(2f, 180f, 0f);
            var light = Get<Light>(lt);
            light.type = LightType.Spot;
            float half = Mathf.Atan(Mathf.Max(w * 0.5f, h) / back) * Mathf.Rad2Deg;
            // the cone only just covers the mouth: light does not spill into the ground around the tunnel
            light.spotAngle = Mathf.Clamp(half * 2f + 4f, 20f, 150f);
            light.innerSpotAngle = Mathf.Clamp(half * 2f * 0.8f, 10f, 140f);
            light.range = Mathf.Max(5f, lightRange) + back;
            light.intensity = lightIntensity * k;
            light.color = lightColor;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowNearPlane = 0.3f;
            light.shadowStrength = 1f;
            // the walls run almost parallel to this light: without a strong normal bias they shadow themselves (acne)
            light.shadowBias = 1f;
            light.shadowNormalBias = 3f;
            var data = light.GetUniversalAdditionalLightData();
            if (data != null) data.usePipelineSettings = false;
            light.enabled = k > 0.001f;

            // eye adaptation stand-in: a volume with lower exposure; its weight follows the camera (1 at the mouth, 0 deep inside)
            var vt = Child(VolumeName);
            var oldBox = vt.GetComponent<BoxCollider>();
            if (oldBox != null) DestroyImmediate(oldBox);
            volume = Get<Volume>(vt);
            volume.isGlobal = true;
            volume.priority = 20f;
            volume.weight = 0f;
            volume.sharedProfile = exposureProfile;
            volume.enabled = false;   // eye adaptation is one shared volume now (TunnelEyeAdaptation)

            drivenSignature = Signature();
        }

        static T Get<T>(Transform t) where T : Component
        {
            var c = t.GetComponent<T>();
            if (c == null) c = t.gameObject.AddComponent<T>();
            return c;
        }

        Transform Child(string n)
        {
            var t = transform.Find(n);
            if (t != null) return t;
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        /// <summary>Open box (no face towards the tunnel): x in [-hx, hx], y in [y0, y1], z in [z0, z1].</summary>
        static void BuildBox(Mesh m, float hx, float y0, float y1, float z0, float z1)
        {
            var kit = new MeshKit(1);
            Vector3 a = new Vector3(-hx, y0, z1), b = new Vector3(hx, y0, z1), c = new Vector3(hx, y1, z1), d = new Vector3(-hx, y1, z1);
            Vector3 a0 = new Vector3(-hx, y0, z0), b0 = new Vector3(hx, y0, z0), c0 = new Vector3(hx, y1, z0), d0 = new Vector3(-hx, y1, z0);
            Vector2 u00 = new Vector2(0, 0), u10 = new Vector2(1, 0), u11 = new Vector2(1, 1), u01 = new Vector2(0, 1);
            Vector2 m0 = new Vector2(0.5f, 0.5f);
            kit.Quad(0, a, b, c, d, u00, u10, u11, u01, Vector3.back);           // far wall
            kit.Quad(0, a0, a, d, d0, m0, m0, m0, m0, Vector3.right);             // left
            kit.Quad(0, b, b0, c0, c, m0, m0, m0, m0, Vector3.left);              // right
            kit.Quad(0, d, c, c0, d0, m0, m0, m0, m0, Vector3.down);              // top
            kit.Quad(0, a0, b0, b, a, m0, m0, m0, m0, Vector3.up);                // bottom
            kit.Fill(m);
        }
    }
}
