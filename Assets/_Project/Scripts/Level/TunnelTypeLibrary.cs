using System.Collections.Generic;
using UnityEngine;

namespace CarX.Level
{
    /// <summary>Palette of tunnel types for the graph editor (order = order of buttons).</summary>
    [CreateAssetMenu(menuName = "CarX/Tunnel Type Library", fileName = "TunnelTypes")]
    public class TunnelTypeLibrary : ScriptableObject
    {
        public List<TunnelType> types = new List<TunnelType>();
        [Tooltip("Материалы по умолчанию для всех типов и камер")]
        public TunnelMaterials materials;

        public TunnelType Default { get { return types.Count > 0 ? types[0] : null; } }
    }
}
