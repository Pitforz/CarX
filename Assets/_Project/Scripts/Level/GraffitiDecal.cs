using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CarX.Level
{
    /// <summary>One sprayed graffiti: a URP Decal Projector driven by a picture from Assets/_Project/Graffiti.
    /// Size is the width in metres (height follows the picture), opacity and wear are per graffiti.
    /// In tunnels it also carries a TunnelAnchor (follows the tunnel graph); elsewhere it is a child of the surface it was sprayed on.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(DecalProjector)), AddComponentMenu("CarX/Graffiti")]
    public class GraffitiDecal : MonoBehaviour
    {
        [Tooltip("Картинка из папки Assets/_Project/Graffiti (PNG с прозрачным фоном).")]
        public Texture2D image;
        [Tooltip("Ширина в метрах. Высота считается по пропорциям картинки.")]
        [Min(0.05f)] public float size = 1.5f;
        [Tooltip("Прозрачность: 1 = яркая свежая краска, 0 = не видно.")]
        [Range(0f, 1f)] public float opacity = 1f;
        [Tooltip("Потёртость: 0 = свежее, 1 = почти стёрлось. Шаг 0.1.")]
        [Range(0f, 1f)] public float wear;
        [Tooltip("Отразить по горизонтали.")]
        public bool flip;
        [Tooltip("Толщина слоя, на который ложится краска, в метрах (вперёд и назад от точки нанесения). Больше = лучше ложится на изогнутые стены, но может задеть соседние поверхности.")]
        [Min(0.02f)] public float depth = 0.4f;
        [Tooltip("Дальше этого расстояния от камеры граффити не рисуется (экономия).")]
        [Min(1f)] public float drawDistance = 60f;

        /// <summary>Set by the editor library: returns the decal material for a picture and wear level (creates it if needed).</summary>
        public static System.Func<Texture2D, float, Material> MaterialFor;

        public static float WearStep(float w) { return Mathf.Clamp01(Mathf.Round(w * 10f) / 10f); }

        public float Height { get { return image != null && image.width > 0 ? size * image.height / image.width : size; } }

        /// <summary>Pushes the settings into the Decal Projector (and picks the material in the editor).</summary>
        public void Refresh()
        {
            var p = GetComponent<DecalProjector>();
            if (p == null) return;
            if (MaterialFor != null)
            {
                var m = image != null ? MaterialFor(image, WearStep(wear)) : null;
                if (p.material != m) p.material = m;
            }
            p.scaleMode = DecalScaleMode.ScaleInvariant;
            p.size = new Vector3(size, Height, depth);
            p.pivot = Vector3.zero;
            p.uvScale = new Vector2(flip ? -1f : 1f, 1f);
            p.uvBias = new Vector2(flip ? 1f : 0f, 0f);
            p.fadeFactor = opacity;
            p.drawDistance = drawDistance;
            p.fadeScale = 0.85f;
            p.startAngleFade = 60f;   // surfaces turned away from the spray (corners, floor under a wall) fade out
            p.endAngleFade = 85f;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall -= DelayedRefresh;
            UnityEditor.EditorApplication.delayCall += DelayedRefresh;
        }

        void DelayedRefresh() { if (this != null) Refresh(); }
#endif
    }
}
