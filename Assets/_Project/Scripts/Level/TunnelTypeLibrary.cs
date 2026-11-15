using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>Palette of tunnel types for the graph editor (order = order of buttons) and the shared look of all graphs.</summary>
    [CreateAssetMenu(menuName = "CarX/Tunnel Type Library", fileName = "TunnelTypes")]
    public class TunnelTypeLibrary : ScriptableObject
    {
        public List<TunnelType> types = new List<TunnelType>();
        [Tooltip("Материалы по умолчанию для всех типов и камер")]
        public TunnelMaterials materials;

        [Header("Дневной свет: общий для выходов, решёток и люков")]
        [Tooltip("Цвет дневного света. Один на все выходы, решётки и люки")]
        public Color daylightColor = new Color(1f, 0.94f, 0.85f);
        [Tooltip("Яркость неба, видного в проёмах (выход, решётка, дырочки люка)")]
        [Min(0f)] public float skyBrightness = 19f;
        [Tooltip("Яркость пятна света на полу под полностью открытым проёмом. Решётка и люк дают долю от неё (поля ниже)")]
        [Min(0f)] public float shaftFloorLight = 8f;
        [Tooltip("Какую долю света пропускает решётка (0..1)")]
        [Range(0f, 1f)] public float grateOpenness = 0.45f;
        [Tooltip("Какую долю света пропускает люк с дырочками (0..1)")]
        [Range(0f, 1f)] public float manholeOpenness = 0.06f;
        [Tooltip("На сколько метров в каждую сторону решётка или люк освещает тоннель")]
        [Range(1f, 40f)] public float shaftReach = 10f;
        [Tooltip("Видимый столб света в воздухе под решёткой и люком (0 = нет)")]
        [Range(0f, 2f)] public float shaftBeam = 0.25f;
        [Tooltip("Летающие пылинки в луче. Выключено = нет ни у одного водостока")]
        public bool shaftDust = false;

        [Header("Адаптация глаза (камера темнеет у яркого света)")]
        [Tooltip("Включено: у выхода и под решёткой картинка темнеет, особенно если смотришь на свет. Тоннель за светом становится чёрным")]
        public bool eyeAdaptation = true;
        [Tooltip("За сколько секунд глаз привыкает")]
        [Range(0.05f, 5f)] public float eyeAdaptTime = 1f;
        [Tooltip("Сила адаптации (1 = как в профиле TunnelExitFX)")]
        [Range(0f, 2f)] public float eyeAdaptStrength = 1f;

        [Header("Выходы наружу (узлы «Выход наружу»)")]
        [Tooltip("Сила прожектора, который светит из выхода вдоль тоннеля")]
        [Min(0f)] public float exitLightIntensity = 40f;
        [Tooltip("Дальность света из выхода, м: дальше без фонарика темно")]
        [Min(5f)] public float exitLightRange = 55f;
        [Tooltip("На сколько метров снаружи от проёма стоит этот свет (дальше = мягче у самого выхода)")]
        [Min(0.6f)] public float exitLightBack = 8f;
        [HideInInspector] public Color exitLightColor = new Color(0.9f, 0.95f, 1f);
        [HideInInspector, ColorUsage(false, true)] public Color exitSkyColor = new Color(6f, 6.3f, 7f);
        [Tooltip("Постобработка у выхода (пониженная экспозиция, как будто глаз привыкает к свету)")]
        public VolumeProfile exitExposure;
        [Tooltip("До какой глубины от выхода глаз ещё «ослеплён» (картинка темнее), м")]
        [Min(2f)] public float exitExposureDepth = 14f;
        [Tooltip("Тени от дневного света")]
        public bool exitShadows = true;

        [Header("Атмосфера: общий свет и туман во всех сценах с графом")]
        [Tooltip("Включено: граф сам ставит сцене общий свет и туман из полей ниже")]
        public bool applyAtmosphere;
        public Color ambient = new Color(0.010f, 0.011f, 0.013f);
        public Color fogColor = new Color(0.005f, 0.0055f, 0.006f);
        [Range(0f, 0.1f)] public float fogDensity = 0.045f;

        [Header("Мусор на полу")]
        [Tooltip("Разбрасывать лужи по полу новых тоннелей (если у графа включены «Лужи»)")]
        public bool puddles = true;

        /// <summary>Library of the graph built last (shafts and eye adaptation read the shared daylight from it).</summary>
        public static TunnelTypeLibrary Active;
        static TunnelTypeLibrary fallback;
        public static TunnelTypeLibrary Current
        {
            get
            {
                if (Active != null) return Active;
#if UNITY_EDITOR
                foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:TunnelTypeLibrary"))
                {
                    Active = UnityEditor.AssetDatabase.LoadAssetAtPath<TunnelTypeLibrary>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
                    if (Active != null) return Active;
                }
#endif
                if (fallback == null) { fallback = CreateInstance<TunnelTypeLibrary>(); fallback.hideFlags = HideFlags.DontSave; }
                return fallback;
            }
        }
        public Color SkyColor { get { return daylightColor * skyBrightness; } }
        /// <summary>Changes when anything the shafts are built from changes.</summary>
        public int DaylightHash()
        {
            unchecked
            {
                int h = daylightColor.GetHashCode();
                foreach (var f in new[] { skyBrightness, shaftFloorLight, grateOpenness, manholeOpenness, shaftReach, shaftBeam })
                    h = h * 31 + f.GetHashCode();
                return h * 31 + (shaftDust ? 1 : 0);
            }
        }

        public TunnelType Default { get { return types.Count > 0 ? types[0] : null; } }

        /// <summary>Near-black ambient and exponential fog for the active scene (only what differs is touched).</summary>
        public void ApplyAtmosphere()
        {
            if (!applyAtmosphere) return;
            if (RenderSettings.ambientMode != AmbientMode.Flat) RenderSettings.ambientMode = AmbientMode.Flat;
            if (RenderSettings.ambientLight != ambient)
            {
                RenderSettings.ambientLight = ambient;
                var sh = new SphericalHarmonicsL2();
                sh.AddAmbientLight(ambient);
                RenderSettings.ambientProbe = sh;
            }
            if (!RenderSettings.fog) RenderSettings.fog = true;
            if (RenderSettings.fogMode != FogMode.ExponentialSquared) RenderSettings.fogMode = FogMode.ExponentialSquared;
            if (!Mathf.Approximately(RenderSettings.fogDensity, fogDensity)) RenderSettings.fogDensity = fogDensity;
            if (RenderSettings.fogColor != fogColor) RenderSettings.fogColor = fogColor;
            if (RenderSettings.reflectionIntensity != 0f) RenderSettings.reflectionIntensity = 0f;
        }

#if UNITY_EDITOR
        void OnValidate() { UnityEditor.EditorApplication.delayCall += TunnelNetwork.MarkAllDirty; }
#endif
    }
}
