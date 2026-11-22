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
        [System.Serializable]
        sealed class IntroductionLine
        {
            [SerializeField] bool _alexey;
            [SerializeField, TextArea] string _text;
            [SerializeField] AudioClip _voice;
            internal bool Alexey => _alexey;
            internal string Text => _text;
            internal AudioClip Voice => _voice;
        }

        [SerializeField, Min(0.1f)] float _interactionDistance = 2.5f;
        [SerializeField] string _displayName;
        [SerializeField] string _firstQuestion = "Вопрос один";
        [SerializeField] string _secondQuestion = "Вопрос номер два";
        [SerializeField] string _closeOption = "Закрыть";
        [SerializeField, TextArea] string[] _firstAnswer;
        [SerializeField] AudioClip _firstQuestionVoice;
        [SerializeField] AudioClip[] _firstAnswerVoices;
        [SerializeField] bool _grantsLowerFloorHint;
        [SerializeField] bool _answerOnlyOnce;
        [SerializeField, Range(0f, 25f)] float _idleArmClearance;
        [SerializeField, Min(0f)] float _automaticGreetingDistance;
        [SerializeField] IntroductionLine[] _introduction;
        [SerializeField] IntroductionLine _flashlightWarning;
        [SerializeField, TextArea] string _afterAnswerThought;
        [SerializeField] ResidentModelPose _introductionPose;
        [SerializeField] GameObject _activateAfterIntroduction;

        PlayerStatusHud _answerHud;
        NpcSpeechState _speech;
        Coroutine _answer;
        System.Action _answerCompleted;
        bool _hasFinishedAnswer;
        bool _introduced;
        bool _introducing;
        bool _warnedForFlashlight;
        PlayerMenus _nearbyPlayer;
        Flashlight _greetingFlashlight;
        AudioSource _playerVoice;
        AudioSource _npcVoice;

        internal string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        internal string FirstQuestion => _firstQuestion;
        internal string SecondQuestion => _secondQuestion;
        internal string CloseOption => _closeOption;
        internal bool CanTalk => isActiveAndEnabled && (!_answerOnlyOnce || !_hasFinishedAnswer) &&
            GetComponentInParent<HostileNpc>() == null;
        internal float InteractionDistance => _interactionDistance;
        internal bool HasAnswer => _firstAnswer != null && _firstAnswer.Length > 0;
        internal bool IsAnswering => _answer != null;
        internal bool GrantsLowerFloorHint => _grantsLowerFloorHint;
        internal bool HasAutomaticIntroduction => _introduction is { Length: > 0 };
        internal bool ShowsQuestions => !HasAutomaticIntroduction || (_introduced && HasAnswer);
        bool FlashlightOn => _greetingFlashlight != null && _greetingFlashlight.isActiveAndEnabled &&
            _greetingFlashlight.IsSwitchedOn;
        bool CanWarnAboutFlashlight => !_warnedForFlashlight && FlashlightOn;

        void Awake()
        {
            _speech = NpcSpeechState.GetOrAdd(this);
            if (GetComponentInParent<HostileNpc>() == null)
                NpcIdleMotion.Attach(this, _speech, _idleArmClearance);
        }

        void Start()
        {
            if (!HasAutomaticIntroduction)
                return;

            foreach (var player in FindObjectsByType<PlayerMenus>())
                if (player.gameObject.scene == gameObject.scene)
                {
                    _nearbyPlayer = player;
                    if (!string.IsNullOrWhiteSpace(_flashlightWarning?.Text))
                        _greetingFlashlight = Flashlight.FindForPlayer(player.transform, null);
                    break;
                }
        }

        void Update()
        {
            if (!FlashlightOn)
                _warnedForFlashlight = false;

            if (_nearbyPlayer != null && (!_introduced || CanWarnAboutFlashlight) && !IsAnswering &&
                (_nearbyPlayer.transform.position - transform.position).sqrMagnitude <=
                _automaticGreetingDistance * _automaticGreetingDistance)
                _nearbyPlayer.TryStartNpcIntroduction(this);
        }

        internal bool TryIntroduce(PlayerStatusHud hud)
        {
            if (!isActiveAndEnabled || !HasAutomaticIntroduction || IsAnswering ||
                (_introduced ? !CanWarnAboutFlashlight : !CanTalk))
                return false;

            _answerHud = hud;
            _introducing = true;
            _answer = StartCoroutine(Introduce());
            return true;
        }

        IEnumerator Introduce()
        {
            if (_introduced)
                yield return WarnAboutFlashlight();
            else
            {
                for (int i = 0; i < _introduction.Length; i++)
                {
                    if (CanWarnAboutFlashlight)
                        yield return WarnAboutFlashlight();
                    if (i == 0)
                        _introductionPose?.GlanceDown();
                    var line = _introduction[i];
                    yield return ShowLine(line.Text, line.Voice, line.Alexey);
                }
            }
            FinishAnswer(completed: true);
        }

        IEnumerator WarnAboutFlashlight()
        {
            _warnedForFlashlight = true;
            yield return ShowLine(_flashlightWarning.Text, _flashlightWarning.Voice);
        }

        internal bool TryAnswer(PlayerStatusHud hud, System.Action completed = null)
        {
            if (!CanTalk || !HasAnswer || IsAnswering || !ShowsQuestions)
                return false;

            _answerHud = hud;
            _answerCompleted = completed;
            _answer = StartCoroutine(Answer());
            return true;
        }

        IEnumerator Answer()
        {
            if (_firstQuestionVoice != null || HasAutomaticIntroduction)
                yield return ShowLine(FirstQuestion, _firstQuestionVoice, alexey: true);

            for (int i = 0; i < _firstAnswer.Length; i++)
                yield return ShowLine(_firstAnswer[i], DialogueVoice.GetClip(_firstAnswerVoices, i));
            if (!string.IsNullOrWhiteSpace(_afterAnswerThought))
                yield return ShowLine(_afterAnswerThought, null, alexey: true, thought: true);
            FinishAnswer(completed: true);
        }

        IEnumerator ShowLine(string line, AudioClip clip, bool alexey = false, bool thought = false)
        {
            string speaker = alexey ? (thought ? "Алексей, мысленно" : "Алексей") : DisplayName;
            Color color = alexey ? Color.white : new Color32(176, 218, 183, 255);
            float duration = clip != null ? clip.length : Mathf.Clamp(1f + line.Length / 17f, 2.4f, 8f);
            _answerHud.SetStorySubtitle($"{speaker}: {line}", color);
            _speech.SetTalking(!alexey);
            AudioSource source = null;
            if (clip != null)
            {
                if (alexey)
                    source = _playerVoice = DialogueVoice.GetPlayerSource(_playerVoice, _answerHud.transform);
                else
                    source = _npcVoice ??= DialogueVoice.CreateSource(transform, $"{DisplayName} Voice",
                        minDistance: 3f, maxDistance: 16f);
            }
            if (!alexey)
                _speech.BeginLine(line, duration, source, clip);
            if (source != null)
                yield return DialogueVoice.Play(source, clip);
            else
                yield return new WaitForSeconds(duration);
        }

        internal void SkipAnswer() => StopAnswer(completed: true);

        internal void CancelAnswer() => StopAnswer(completed: false);

        void StopAnswer(bool completed)
        {
            if (_answer == null)
                return;

            StopCoroutine(_answer);
            FinishAnswer(completed);
        }

        void FinishAnswer(bool completed)
        {
            var onCompleted = _answerCompleted;
            _answerCompleted = null;
            _answer = null;
            _speech.SetTalking(false);
            if (_playerVoice != null)
                _playerVoice.Stop();
            if (_npcVoice != null)
                _npcVoice.Stop();
            if (_answerHud != null)
                _answerHud.SetStorySubtitle(null, Color.white);
            if (_introducing)
            {
                _introducing = false;
                if (completed && !_introduced)
                {
                    _introduced = true;
                    if (_activateAfterIntroduction != null)
                        _activateAfterIntroduction.SetActive(true);
                }
                _answerHud = null;
                return;
            }
            _answerHud = null;
            if (completed)
            {
                _hasFinishedAnswer = true;
                onCompleted?.Invoke();
            }
        }

        void OnDisable() => CancelAnswer();

        void OnDestroy()
        {
            if (_playerVoice != null)
                Destroy(_playerVoice.gameObject);
        }
    }
}
