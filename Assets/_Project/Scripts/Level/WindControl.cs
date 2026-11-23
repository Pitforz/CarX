using UnityEngine;

namespace CarX.Level
{
    /// <summary>One knob for the wind of all trees, bushes and grass with the CarX/WindFoliage shader.</summary>
    [ExecuteAlways]
    [AddComponentMenu("CarX/Ветер")]
    public class WindControl : MonoBehaviour
    {
        [Tooltip("Сила ветра: 0 штиль, 1 сильный ветер")]
        [Range(0f, 2f)] public float strength = 0.5f;
        [Tooltip("Скорость: как часто приходят порывы и как быстро всё качается")]
        [Range(0.2f, 3f)] public float speed = 1f;
        [Tooltip("Насколько качаются стволы и всё дерево целиком: 0 стоят, 1 как раньше")]
        [Range(0f, 1f)] public float trunkSway = 0.35f;
        [Tooltip("Насколько дрожат листья и покачиваются ветки (у деревьев из пака)")]
        [Range(0f, 3f)] public float leafFlutter = 1f;

        void OnEnable() { Apply(); }
        void OnValidate() { Apply(); }
        void Update() { Apply(); }
        void OnDisable() { Shader.SetGlobalFloat("_CarXWindStrength", 0f); }

        void Apply()
        {
            Shader.SetGlobalFloat("_CarXWindStrength", strength);
            Shader.SetGlobalFloat("_CarXWindSpeed", speed);
            Shader.SetGlobalFloat("_CarXTrunkSway", trunkSway);
            Shader.SetGlobalFloat("_CarXLeafFlutter", leafFlutter);
        }
    }
}
