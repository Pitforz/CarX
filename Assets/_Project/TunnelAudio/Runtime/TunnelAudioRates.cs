using UnityEngine;

namespace CarX.TunnelAudio
{
    /// <summary>
    /// How often each group plays, and how loud it is.
    /// A zone carries its own copy and, while the ear is inside, replaces the director's copy.
    /// Pause = a random gap divided by the master rate. There is no second per-event rate.
    /// </summary>
    [System.Serializable]
    public sealed class TunnelAudioRates
    {
        [Tooltip("Общая частота. 2 — все паузы вдвое короче. Громкость групп не трогает")]
        [Min(0.05f)] public float master = 1f;

        [Header("Капли")]
        [Tooltip("Секунды между каплями. Делится на общую частоту")]
        public Vector2 dripGap = new Vector2(0.35f, 2.4f);
        [Tooltip("Громкость капель. 1 — как раньше. Умножается на громкость участка")]
        [Range(0f, 1f)] public float dripVolume = 1f;

        [Header("Камень и металл")]
        public Vector2 fallGap = new Vector2(45f, 110f);
        [Range(0f, 1f)] public float fallVolume = 1f;

        [Header("Крысы и мусор")]
        [Tooltip("Одна пауза на оба звука. Какие из них можно брать, решают галочки участка")]
        public Vector2 lifeGap = new Vector2(8f, 22f);
        [Range(0f, 1f)] public float lifeVolume = 1f;

        [Header("Голоса")]
        public Vector2 voiceGap = new Vector2(6f, 14.4f);
        [Tooltip("Общая громкость голосов. Личная громкость манекена умножается на неё")]
        [Range(0f, 1f)] public float voiceVolume = 1f;

        [Header("Легковые")]
        public Vector2 carGap = new Vector2(8f, 20f);
        [Range(0f, 1f)] public float carVolume = 1f;
        [Range(0f, 1f)] public float carHornChance = 0.07f;

        [Header("Тяжёлые")]
        public Vector2 heavyGap = new Vector2(16f, 38f);
        [Range(0f, 1f)] public float heavyVolume = 1f;
        [Range(0f, 1f)] public float heavyHornChance = 0.2f;

        [Tooltip("Громкость сигнала поверх проезда, и у легковой, и у тяжёлой. 0,2 — тихо и далеко")]
        [Range(0f, 1f)] public float hornVolume = 0.2f;

        [Header("Далёкий город у глухой решётки")]
        public Vector2 cityGap = new Vector2(14f, 36f);
        [Range(0f, 1f)] public float cityVolume = 1f;

        [Header("Казино")]
        [Tooltip("Громкость обоих слоёв казино до приглушения улицы и среза голосов")]
        [Range(0f, 1f)] public float casinoVolume = 1f;

        [HideInInspector] public bool volumesReady;

        /// <summary>Fills zeroes left by an older scene object. A value the user set stays.</summary>
        public void Repair()
        {
            if (master <= 0f) master = 1f;
            if (dripGap.y <= 0f) dripGap = new Vector2(0.35f, 2.4f);
            if (fallGap.y <= 0f) fallGap = new Vector2(45f, 110f);
            if (lifeGap.y <= 0f) lifeGap = new Vector2(8f, 22f);
            if (voiceGap.y <= 0f) voiceGap = new Vector2(6f, 14.4f);
            if (carGap.y <= 0f) carGap = new Vector2(8f, 20f);
            if (heavyGap.y <= 0f) heavyGap = new Vector2(16f, 38f);
            if (cityGap.y <= 0f) cityGap = new Vector2(14f, 36f);
            if (volumesReady) return;
            dripVolume = 1f;
            fallVolume = 1f;
            lifeVolume = 1f;
            voiceVolume = 1f;
            carVolume = 1f;
            heavyVolume = 1f;
            hornVolume = 0.2f;
            cityVolume = 1f;
            casinoVolume = 1f;
            volumesReady = true;
        }
    }
}
