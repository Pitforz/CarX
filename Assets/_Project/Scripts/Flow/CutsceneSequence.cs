using System;
using UnityEngine;

namespace CarX.Flow
{
    /// <summary>
    /// Описание одного перехода между сценами: затемнение, чёрный экран с карточками текста, загрузка сцены, проявление.
    /// Создаётся как файл (Create → CarX → Катсцена перехода), настраивается в Inspector, запускается через
    /// <see cref="SceneFlow.Play"/> или компонент <see cref="PlayCutsceneButton"/> на любой кнопке.
    /// </summary>
    [CreateAssetMenu(menuName = "CarX/Катсцена перехода", fileName = "Cutscene_New")]
    public class CutsceneSequence : ScriptableObject
    {
        [Serializable]
        public class Card
        {
            [TextArea(2, 6)] [Tooltip("Текст на чёрном экране")]
            public string text = "Текст карточки";
            [Min(0f)] [Tooltip("Сколько секунд текст проявляется")]
            public float fadeIn = 0.8f;
            [Min(0f)] [Tooltip("Сколько секунд текст держится на экране (после того как проявился или допечатался)")]
            public float hold = 3f;
            [Min(0f)] [Tooltip("Сколько секунд текст гаснет")]
            public float fadeOut = 0.8f;
            [Min(0f)] [Tooltip("Пауза в темноте после карточки")]
            public float pauseAfter = 0.4f;
            [Tooltip("Печатать буквы по одной вместо плавного проявления")]
            public bool typewriter = false;
            [Min(1f)] [Tooltip("Скорость печати, символов в секунду (только если включена печать)")]
            public float charsPerSecond = 28f;
            [Tooltip("Звук в момент начала карточки (необязательно)")]
            public AudioClip sound;
            [Range(0f, 1f)] public float soundVolume = 1f;
        }

        [Header("Куда ведёт")]
        [Tooltip("Имя сцены. Она должна быть в File → Build Profiles → Scene List")]
        public string targetScene = "CanalEntrance_Test";

        [Header("Затемнение")]
        [Min(0f)] [Tooltip("Секунд на затемнение экрана")]
        public float fadeOut = 1.2f;
        [Min(0f)] [Tooltip("Секунд полной темноты до первой карточки")]
        public float blackHold = 0.6f;
        public Color fadeColor = Color.black;

        [Header("Чёрный экран")]
        public Card[] cards = new Card[0];
        [Min(0f)] [Tooltip("Пауза в темноте после последней карточки, перед показом новой сцены")]
        public float holdAfterCards = 0.8f;
        [Tooltip("Можно пропустить карточку пробелом, Enter или кликом")]
        public bool skippable = true;

        [Header("Появление в новой сцене")]
        [Min(0f)] [Tooltip("Секунд на проявление новой сцены из темноты")]
        public float fadeIn = 2.5f;
        [Tooltip("Скрыть и закрепить курсор в новой сцене (для игровых сцен). Для сцены меню выключите")]
        public bool lockCursorInTarget = true;

        [Header("Звук (все слоты необязательны)")]
        [Tooltip("Фон на чёрном экране (машина, дорога). Играет по кругу и затихает при проявлении")]
        public AudioClip ambientLoop;
        [Range(0f, 1f)] public float ambientVolume = 0.7f;
        [Tooltip("Звук в момент, когда новая сцена начинает проявляться (хлопок двери, падение, ветер)")]
        public AudioClip arriveSound;
        [Range(0f, 1f)] public float arriveVolume = 1f;
    }
}
