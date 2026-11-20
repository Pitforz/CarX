using UnityEngine;

namespace CarX.Level
{
    /// <summary>One knob for the wind of all trees, bushes and grass with the CarX/WindFoliage shader.</summary>
    [ExecuteAlways]
    [AddComponentMenu("CarX/Ветер")]
    public class WindControl : MonoBehaviour
    {
        [Tooltip("Сила качания: 0 штиль, 1 сильный ветер")]
        [Range(0f, 2f)] public float strength = 0.5f;
        [Tooltip("Скорость: как часто приходят порывы и как быстро качаются деревья")]
        [Range(0.2f, 3f)] public float speed = 1f;

        void OnEnable() { Apply(); }
        void OnValidate() { Apply(); }
        void Update() { Apply(); }
        void OnDisable() { Shader.SetGlobalFloat("_CarXWindStrength", 0f); }

        void Apply()
        {
            Shader.SetGlobalFloat("_CarXWindStrength", strength);
            Shader.SetGlobalFloat("_CarXWindSpeed", speed);
        }
    }
}
