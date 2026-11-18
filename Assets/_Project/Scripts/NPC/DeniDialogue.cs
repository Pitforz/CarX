using System.Collections;
using CarX.Gameplay;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcDialogue))]
    public sealed class DeniDialogue : MonoBehaviour
    {
        static readonly Color DeniColor = new Color32(255, 192, 145, 255);

        [System.Serializable]
        sealed class OpeningLine
        {
            [SerializeField] bool _alexey;
            [SerializeField, TextArea] string _text;
            [SerializeField] AudioClip _clip;

            internal bool Alexey => _alexey;
            internal string Text => _text;
            internal AudioClip Clip => _clip;
        }

        [SerializeField] OpeningLine[] _openingLines;
        [SerializeField] AudioClip _askForDirectionsVoice;
        [SerializeField] AudioClip _directionsVoice;

        PlayerStatusHud _hud;
        Coroutine _conversation;
        AudioSource _voice;
        AudioSource _playerVoice;
        NpcSpeechState _speech;

        internal bool HasQuest { get; private set; }
        internal bool QuestCompleted { get; private set; }
        internal bool IsSpeaking => _conversation != null;
        internal bool HasWorkshopEncounter { get; set; }

        void Awake() => _speech = NpcSpeechState.GetOrAdd(this);

        internal bool TryTalk(PlayerStatusHud hud, System.Action questAccepted)
        {
            if (!isActiveAndEnabled || QuestCompleted || IsSpeaking)
                return false;

            _hud = hud;
            _conversation = StartCoroutine(Speak(questAccepted));
            return true;
        }

        IEnumerator Speak(System.Action questAccepted)
        {
            if (!HasQuest)
            {
                if (_openingLines != null && _openingLines.Length > 0)
                {
                    foreach (var line in _openingLines)
                        yield return Line(line.Text, line.Alexey, line.Clip);
                }
                else
                {
                    yield return Line("Всё как в тот день… Мужик, туда лучше не идти, с нижнего этажа не возвращаются.");
                    yield return Line("Хотя, вижу, тебя не переубедить, даже фонарь взял. Ладно, тогда можно тебя попрошу.");
                    yield return Line("У меня инструменты украли. Ночью. Вот мрази. Я им и то, и это, а они… Ээх.");
                    yield return Line("Говорят, видели, как убегали в старую мастерскую, но идти туда опасно.");
                    yield return Line("Раз ты решил вниз идти, принеси сначала мои инструменты, а я смогу чинить тебе фонарь, если сломаешь.");
                }
            }

            yield return Line("Где мастерская?", alexey: true, clip: _askForDirectionsVoice);
            if (_directionsVoice != null)
                yield return Line("Тебе прямо и сразу направо.", clip: _directionsVoice);
            else
            {
                string directions = HasWorkshopEncounter
                    ? "Иди прямо в первый тоннель от жилого зала. Там ищи вымогателя. Ящик красный."
                    : "Прямо и сразу направо. Потом дверь с белой полосой. Ящик красный.";
                yield return Line(directions);
                yield return Line("Хотя сейчас весь в грязи. Но ручка красная.");
            }

            FinishConversation();
            if (!HasQuest)
            {
                HasQuest = true;
                questAccepted?.Invoke();
            }
        }

        IEnumerator Line(string text, bool alexey = false, AudioClip clip = null)
        {
            string speaker = alexey ? "Алексей" : "Дени";
            _speech.SetTalking(!alexey);
            _hud.SetStorySubtitle($"{speaker}: {text}", alexey ? Color.white : DeniColor);
            if (clip == null)
                yield return new WaitForSeconds(Mathf.Clamp(1f + text.Length / 17f, 2.4f, 8f));
            else if (alexey)
            {
                _playerVoice = DialogueVoice.GetPlayerSource(_playerVoice, _hud.transform);
                yield return DialogueVoice.Play(_playerVoice, clip);
            }
            else
            {
                if (_voice == null)
                    _voice = DialogueVoice.CreateSource(transform, "Deni Voice", minDistance: 3f, maxDistance: 16f);
                yield return DialogueVoice.Play(_voice, clip);
            }

            _speech.SetTalking(false);
        }

        internal void CompleteQuest() => QuestCompleted = true;

        internal void Cancel()
        {
            if (!IsSpeaking)
                return;

            StopCoroutine(_conversation);
            FinishConversation();
        }

        void FinishConversation()
        {
            _conversation = null;
            if (_voice != null)
                _voice.Stop();
            if (_playerVoice != null)
                _playerVoice.Stop();
            if (_speech != null)
                _speech.SetTalking(false);
            if (_hud != null)
                _hud.SetStorySubtitle(null, Color.white);
            _hud = null;
        }

        void OnDisable() => Cancel();

        void OnDestroy()
        {
            if (_playerVoice != null)
                Destroy(_playerVoice.gameObject);
        }
    }
}
