using System.Collections;
using CarX.Gameplay;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [AddComponentMenu("CarX/NPC Dialogue")]
    [RequireComponent(typeof(NpcSpeechState))]
    public sealed class NpcDialogue : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float _interactionDistance = 2.5f;
        [SerializeField] string _displayName;
        [SerializeField] string _firstQuestion = "Вопрос один";
        [SerializeField] string _secondQuestion = "Вопрос номер два";
        [SerializeField] string _closeOption = "Закрыть";
        [SerializeField, TextArea] string[] _firstAnswer;
        [SerializeField] AudioClip _firstQuestionVoice;
        [SerializeField] AudioClip[] _firstAnswerVoices;
        [SerializeField, Range(0f, 25f)] float _idleArmClearance;

        PlayerStatusHud _answerHud;
        NpcSpeechState _speech;
        Coroutine _answer;
        AudioSource _playerVoice;
        AudioSource _npcVoice;

        internal string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        internal string FirstQuestion => _firstQuestion;
        internal string SecondQuestion => _secondQuestion;
        internal string CloseOption => _closeOption;
        internal bool CanTalk => isActiveAndEnabled && GetComponentInParent<HostileNpc>() == null;
        internal float InteractionDistance => _interactionDistance;
        internal bool HasAnswer => _firstAnswer != null && _firstAnswer.Length > 0;
        internal bool IsAnswering => _answer != null;

        void Awake()
        {
            _speech = NpcSpeechState.GetOrAdd(this);
            if (GetComponentInParent<HostileNpc>() == null)
                NpcIdleMotion.Attach(this, _speech, _idleArmClearance);
        }

        internal bool TryAnswer(PlayerStatusHud hud)
        {
            if (!CanTalk || !HasAnswer || IsAnswering)
                return false;

            _answerHud = hud;
            _answer = StartCoroutine(Answer());
            return true;
        }

        IEnumerator Answer()
        {
            if (_firstQuestionVoice != null)
            {
                _playerVoice = DialogueVoice.GetPlayerSource(_playerVoice, _answerHud.transform);
                _answerHud.SetStorySubtitle($"Алексей: {FirstQuestion}", Color.white);
                yield return DialogueVoice.Play(_playerVoice, _firstQuestionVoice);
            }

            var color = new Color32(176, 218, 183, 255);
            _speech.SetTalking(true);
            for (int i = 0; i < _firstAnswer.Length; i++)
            {
                string line = _firstAnswer[i];
                _answerHud.SetStorySubtitle($"{DisplayName}: {line}", color);
                var clip = DialogueVoice.GetClip(_firstAnswerVoices, i);
                float duration = Mathf.Clamp(1f + line.Length / 17f, 2.4f, 8f);
                if (clip != null)
                {
                    if (_npcVoice == null)
                        _npcVoice = DialogueVoice.CreateSource(transform, $"{DisplayName} Voice",
                            minDistance: 3f, maxDistance: 16f);
                    _speech.BeginLine(line, duration, _npcVoice, clip);
                    yield return DialogueVoice.Play(_npcVoice, clip);
                }
                else
                {
                    _speech.BeginLine(line, duration);
                    yield return new WaitForSeconds(duration);
                }
            }
            FinishAnswer();
        }

        internal void SkipAnswer()
        {
            if (_answer == null)
                return;

            StopCoroutine(_answer);
            FinishAnswer();
        }

        void FinishAnswer()
        {
            _answer = null;
            _speech.SetTalking(false);
            if (_playerVoice != null)
                _playerVoice.Stop();
            if (_npcVoice != null)
                _npcVoice.Stop();
            if (_answerHud != null)
                _answerHud.SetStorySubtitle(null, Color.white);
            _answerHud = null;
        }

        void OnDisable() => SkipAnswer();

        void OnDestroy()
        {
            if (_playerVoice != null)
                Destroy(_playerVoice.gameObject);
        }
    }
}
