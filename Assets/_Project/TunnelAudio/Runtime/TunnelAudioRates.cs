using UnityEngine;

namespace CarX.TunnelAudio
{
    /// <summary>
    /// How often each event fires. Higher rate means a shorter wait.
    /// A zone carries its own copy and, while the ear is inside, that copy replaces the director's.
    /// </summary>
    [System.Serializable]
    public sealed class TunnelAudioRates
    {
        [Tooltip("Общая частота. 1 — как интервалы ниже, 2 — все события вдвое чаще")]
        [Min(0.05f)] public float master = 1f;

        [Header("Капли")]
        [Tooltip("Секунды между каплями")]
        public Vector2 dripGap = new Vector2(0.35f, 2.4f);
        [Tooltip("Частота капель. 1 — как интервал выше")]
        [Min(0.05f)] public float dripRate = 1f;

        [Header("Камень и металл")]
        public Vector2 fallGap = new Vector2(45f, 110f);
        [Min(0.05f)] public float fallRate = 1f;

        [Header("Крысы")]
        public Vector2 ratGap = new Vector2(7f, 20f);
        [Min(0.05f)] public float ratRate = 1f;

        [Header("Мусор")]
        public Vector2 trashGap = new Vector2(9f, 26f);
        [Min(0.05f)] public float trashRate = 1f;

        [Header("Голоса")]
        public Vector2 voiceGap = new Vector2(6f, 14.4f);
        [Min(0.05f)] public float voiceRate = 1f;

        [Header("Легковые")]
        public Vector2 carGap = new Vector2(8f, 20f);
        [Min(0.05f)] public float carRate = 1f;
        [Range(0f, 1f)] public float carHornChance = 0.07f;

        [Header("Тяжёлые")]
        public Vector2 heavyGap = new Vector2(16f, 38f);
        [Min(0.05f)] public float heavyRate = 1f;
        [Range(0f, 1f)] public float heavyHornChance = 0.2f;

        [Header("Далёкий город у глухой решётки")]
        public Vector2 cityGap = new Vector2(14f, 36f);
        [Min(0.05f)] public float cityRate = 1f;

        /// <summary>Fills zeroes left by an older scene object. A value the user set stays.</summary>
        public void Repair()
        {
            if (master <= 0f) master = 1f;
            if (dripRate <= 0f) dripRate = 1f;
            if (fallRate <= 0f) fallRate = 1f;
            if (ratRate <= 0f) ratRate = 1f;
            if (trashRate <= 0f) trashRate = 1f;
            if (voiceRate <= 0f) voiceRate = 1f;
            if (carRate <= 0f) carRate = 1f;
            if (heavyRate <= 0f) heavyRate = 1f;
            if (cityRate <= 0f) cityRate = 1f;
            if (dripGap.y <= 0f) dripGap = new Vector2(0.35f, 2.4f);
            if (fallGap.y <= 0f) fallGap = new Vector2(45f, 110f);
            if (ratGap.y <= 0f) ratGap = new Vector2(7f, 20f);
            if (trashGap.y <= 0f) trashGap = new Vector2(9f, 26f);
            if (voiceGap.y <= 0f) voiceGap = new Vector2(6f, 14.4f);
            if (carGap.y <= 0f) carGap = new Vector2(8f, 20f);
            if (heavyGap.y <= 0f) heavyGap = new Vector2(16f, 38f);
            if (cityGap.y <= 0f) cityGap = new Vector2(14f, 36f);
        }
    }
}
