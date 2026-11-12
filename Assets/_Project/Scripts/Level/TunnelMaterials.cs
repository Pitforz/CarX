using System;
using UnityEngine;

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
    }
}
