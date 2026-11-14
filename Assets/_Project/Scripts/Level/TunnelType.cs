using UnityEngine;

namespace CarX.Level
{
    /// <summary>
    /// One kind of tunnel for the graph editor: colour on the schematic, cross-section, slab length
    /// and the largest bend allowed between two neighbouring slabs.
    /// </summary>
    [CreateAssetMenu(menuName = "CarX/Tunnel Type", fileName = "TunnelType")]
    public class TunnelType : ScriptableObject
    {
        [Tooltip("Название в палитре")]
        public string displayName = "Тоннель";
        [Tooltip("Цвет линий этого типа в режиме «Схема»")]
        public Color color = new Color(1f, 0.55f, 0.1f);

        [Header("Сечение")]
        public TunnelSection section = new TunnelSection();

        [Header("Плиты и ограничение изгиба")]
        [Tooltip("Длина одной плиты (префаба), м")]
        [Range(0.5f, 6f)] public float segmentLength = 2f;
        [Tooltip("Максимальный угол между соседними плитами, градусы")]
        [Range(0.2f, 45f)] public float maxJointAngle = 2f;
        [Range(0.08f, 0.6f)] public float slabThickness = 0.25f;
        [Tooltip("Ширина шва между плитами, м")]
        [Range(0f, 0.03f)] public float minJointGap = 0.006f;
        [Tooltip("На сколько шов в полу ниже пола, м (до 5 мм)")]
        [Range(0f, 0.005f)] public float jointSink = 0.001f;
        [Tooltip("Фаска на кромках плит на стенах и потолке, м")]
        [Range(0f, 0.03f)] public float edgeBevel = 0.006f;
        [Range(0f, 1.5f)] public float dirtLine = 0.9f;
        [Tooltip("Бесшовный: плиты стыкуются срезом под углом, без швов, текстура непрерывная")]
        public bool seamless;

        [Header("Вода (для короба с бордюрами)")]
        public bool channelWater = true;
        [Range(0.01f, 0.14f)] public float waterDepth = 0.05f;

        [Header("Материалы (пусто = из библиотеки)")]
        public TunnelMaterials materials;

        /// <summary>Smallest radius a smooth bend can have: R = L / (2 sin(θ/2)).</summary>
        public float MinRadius
        {
            get { return Mathf.Max(0.5f, segmentLength) / (2f * Mathf.Sin(Mathf.Max(0.2f, maxJointAngle) * 0.5f * Mathf.Deg2Rad)); }
        }

        public string Label { get { return string.IsNullOrEmpty(displayName) ? name : displayName; } }

#if UNITY_EDITOR
        // any change of the type rebuilds the tunnels that use it
        void OnValidate() { UnityEditor.EditorApplication.delayCall += TunnelNetwork.MarkAllDirty; }
#endif
    }
}
