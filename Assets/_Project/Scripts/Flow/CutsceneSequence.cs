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
        public string targetScene = "MAIN_LEVEL_GRAPHICS_lab";

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

        [Serializable]
        public class PlaceCard
        {
            [TextArea(1, 3)] public string text;
            [Min(0f)] [Tooltip("Секунда клипа, с которой надпись появляется")]
            public float time;
            [Min(0.2f)] [Tooltip("Сколько секунд надпись держится, вместе с проявлением и затуханием")]
            public float hold = 6.5f;
        }

        [Serializable]
        public class Line
        {
            [Tooltip("Кто говорит. Пусто — без подписи.")]
            public string speaker;
            [TextArea(1, 4)] [Tooltip("Текст субтитра")]
            public string text;
            [Min(0f)] [Tooltip("Секунда записи диалога, с которой открывается эта реплика")]
            public float time;
        }

        [Serializable]
        public class Moment
        {
            [Tooltip("Основной звук куска. Пусто — только текст или тишина.")]
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 1f;
            [Tooltip("Держать звук фоном всё время куска (шаги, езда). Длинный файл при этом не зацикливается, если кусок короче.")]
            public bool loop;
            [Tooltip("Второй фон под основным: ветер, проезжающие машины.")]
            public AudioClip layer;
            [Range(0f, 1f)] public float layerVolume = 0.4f;
            [Tooltip("Короткий звук поверх, один раз в начале (проезжающая машина).")]
            public AudioClip sting;
            [Range(0f, 1f)] public float stingVolume = 1f;
            [Tooltip("Сколько секунд длится кусок. 0 = вся длина основного клипа. Для диалога это нижняя граница: запись не обрывается раньше, чем кончится файл.")]
            public float seconds;
            [Tooltip("За сколько секунд до конца куска основной звук и слой плавно затихают. 0 = обрыв.")]
            public float fadeOut;
            [Tooltip("Убрать предыдущие фоны перед этим куском.")]
            public bool stopBeds = true;
            [TextArea(1, 3)] [Tooltip("Крупная надпись места на всё время куска. Пусто — не показывать. Если заданы карточки ниже, они важнее.")]
            public string title;
            [Tooltip("Надписи места по секундам этого клипа. Звук при этом не режется.")]
            public PlaceCard[] places;
            [Tooltip("Реплики. Если список не пуст, клип — запись разговора, строки открываются по своим секундам.")]
            public Line[] lines;
        }

        [Header("Ход катсцены")]
        [Tooltip("Если заполнено, идёт вместо карточек: телефон, диалог с субтитрами, дорога.")]
        public Moment[] moments = new Moment[0];

        [Header("Звук (все слоты необязательны)")]
        [Tooltip("Фон на чёрном экране (машина, дорога). Играет по кругу и затихает при проявлении")]
        public AudioClip ambientLoop;
        [Range(0f, 1f)] public float ambientVolume = 0.7f;
        [Tooltip("Звук в момент, когда новая сцена начинает проявляться (хлопок двери, падение, ветер)")]
        public AudioClip arriveSound;
        [Range(0f, 1f)] public float arriveVolume = 1f;
    }
}
