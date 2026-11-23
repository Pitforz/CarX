using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Level
{
    /// <summary>Material set used by the tunnel generators. Created by CarX > Tunnels > Regenerate Textures and Materials.</summary>
    [CreateAssetMenu(menuName = "CarX/Tunnel Materials", fileName = "TunnelMaterials")]
    public class TunnelMaterials : ScriptableObject
    {
        [Header("Бетон и пол")]
        public Material concrete;
        [Tooltip("Нижняя полоса стен со следом воды и грязи")]
        public Material concreteDirty;
        [Tooltip("Варианты бетона стен (чуть светлее/темнее): каждая плита тоннеля случайно берёт один из них, так стыки читаются кольцами. Пусто = все плиты одинаковые")]
        public Material[] concreteVariants = new Material[0];
        public Material floor;
        public Material soil;
        public Material sand;
        public Material water;
        [Header("Металл")]
        public Material iron;
        public Material lid;
        [Header("Свет сверху")]
        public Material shaft;
        public Material ray;
        public Material dust;
        public Material sky;

        /// <summary>Set by the editor so new objects get the project materials automatically.</summary>
        public static Func<TunnelMaterials> DefaultProvider;

        public static TunnelMaterials Resolve(TunnelMaterials current)
        {
            if (current != null) return current;
            return DefaultProvider != null ? DefaultProvider() : null;
        }

        /// <summary>Sub-mesh order in tunnel meshes: 0 concrete, 1 floor, 2 soil, 3 sand, 4 dirty concrete.</summary>
        public Material[] TunnelSet()
        {
            return new[] { concrete, floor, soil, sand, concreteDirty != null ? concreteDirty : concrete };
        }

        public int VariantCount
        {
            get
            {
                if (concreteVariants == null) return 0;
                int n = 0;
                foreach (var m in concreteVariants) if (m != null) n++;
                return n;
            }
        }

        /// <summary>TunnelSet plus the wall variants as sub-meshes 5, 6, …</summary>
        public Material[] TunnelSetWithVariants()
        {
            var list = new System.Collections.Generic.List<Material>(TunnelSet());
            if (concreteVariants != null) foreach (var m in concreteVariants) if (m != null) list.Add(m);
            return list.ToArray();
        }

        static Material skin;

        /// <summary>Matt black, unlit: the light-tight skin behind tunnel walls. No light ever brightens it.</summary>
        public static Material Skin
        {
            get
            {
                if (skin != null) return skin;
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) return null;
                skin = new Material(sh) { name = "M_TunnelSkin (generated)", hideFlags = HideFlags.DontSave };
                skin.SetColor("_BaseColor", Color.black);
                return skin;
            }
        }

        /// <summary>Renderer setup of a light-tight skin: drawn black, casts shadows (both sides), receives nothing.</summary>
        public static void ApplySkin(MeshRenderer mr, TunnelMaterials mats)
        {
            var m = Skin;
            mr.sharedMaterial = m != null ? m : (mats != null ? mats.concrete : null);
            mr.shadowCastingMode = m != null ? ShadowCastingMode.TwoSided : ShadowCastingMode.ShadowsOnly;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.staticShadowCaster = false;
            TunnelGI.Setup(mr);   // an occluder for the probe bake (it stays black itself)
        }
    }
}
