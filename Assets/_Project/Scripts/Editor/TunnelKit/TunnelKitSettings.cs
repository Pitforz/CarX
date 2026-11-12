using UnityEngine;

namespace CarX.EditorTools
{
    // All tunnel dimensions live here. Select the asset
    // Assets/_Project/Settings/TunnelKitSettings.asset, change numbers in the Inspector,
    // then run CarX > Tunnel Kit > Generate Modules: every module is rebuilt with the same opening.
    public class TunnelKitSettings : ScriptableObject
    {
        [Header("Opening (inner size, metres)")]
        [Tooltip("Inner width of the tunnel opening")]
        public float width = 3.0f;
        [Tooltip("Inner height of the tunnel opening")]
        public float height = 2.4f;
        [Tooltip("Size of the 45-degree chamfer in each inner corner")]
        public float chamfer = 0.3f;

        [Header("Concrete")]
        [Tooltip("Wall / floor / ceiling thickness, used for colliders")]
        public float wallThickness = 0.3f;

        [Header("Grid")]
        [Tooltip("Module length and grid step. Every module fits one cell of this size")]
        public float cellSize = 4f;

        [Header("Materials (created automatically if empty)")]
        public Material concrete;
        public Material floor;

        public void Clamp()
        {
            width = Mathf.Max(1f, width);
            height = Mathf.Max(1f, height);
            chamfer = Mathf.Clamp(chamfer, 0f, Mathf.Min(width, height) * 0.4f);
            wallThickness = Mathf.Max(0.05f, wallThickness);
            cellSize = Mathf.Max(width + 2f * wallThickness + 0.1f, cellSize);
        }
    }
}
