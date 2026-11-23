using UnityEngine;

namespace CarX.Level
{
    /// <summary>What the tunnel light was baked from (CarX → Перезапечь свет): the inspector compares it with the scene now.</summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class TunnelLightingState : MonoBehaviour
    {
        [Tooltip("Отпечаток тоннелей, решёток и солнца на момент запекания")]
        public int bakedHash;
        [Tooltip("Когда запекали")]
        public string bakedAt = "";
        [Tooltip("Сколько секунд шло запекание")]
        public float bakeSeconds;
    }
}
