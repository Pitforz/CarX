using System.Collections;
using CarX.Gameplay;
using StarterAssets;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcDialogue))]
    public sealed class DeniDialogue : MonoBehaviour
    {
        static readonly Color DeniColor = new Color32(255, 192, 145, 255);

        [System.Serializable]
        sealed class DialogueLine
        {
            [SerializeField] bool _alexey;
            [SerializeField, TextArea] string _text;
            [SerializeField] AudioClip _clip;
            [SerializeField, Min(0f)] float _dismissiveWaveAt;

            internal bool Alexey => _alexey;
            internal string Text => _text;
            internal AudioClip Clip => _clip;
            internal float DismissiveWaveAt => _dismissiveWaveAt;

            internal DialogueLine(bool alexey, string text)
            {
                _alexey = alexey;
                _text = text;
            }
        }

        [SerializeField] DialogueLine[] _openingLines;
        [SerializeField] AudioClip _askForDirectionsVoice;
        [SerializeField] AudioClip _directionsVoice;
        [SerializeField, TextArea] string _workshopDirections;
        [SerializeField] Transform _workshopMeetingPoint;
        [SerializeField] CaretakerTour _caretaker;
        [SerializeField] bool _requiresLowerFloorHint;
        [SerializeField] AudioClip _askToCheckLightVoice;
        [SerializeField] AudioClip _checkLightVoice;
        [SerializeField] AudioClip _lightFixedVoice;
        [SerializeField] ToolboxPickup _toolbox;
        [SerializeField] DoorKeyPickup _rewardKey;
        [SerializeField] AudioClip _keyThanksVoice;
        [SerializeField] DialogueLine[] _returnLines =
        {
            new(false, "Целый? Отлично."),
            new(true, "Отец говорил, к кому идёт?"),
            new(false, "За деньгами. Нервный был. Всё проверял, не идёт ли кто следом.")
        };
        [SerializeField] DialogueLine[] _keyLines =
        {
            new(true, "Он спустился. Мне нужен ключ."),
            new(false, "Знаешь, я тут недавно ключ нашёл. Может, он от двери на нижний этаж.")
        };

        PlayerStatusHud _hud;
        Coroutine _conversation;
        System.Action<bool> _onConversationCompleted;
        AudioSource _voice;
        AudioSource _playerVoice;
        NpcSpeechState _speech;
        DeniConversationAnimation _animation;
        FirstPersonController _repairingPlayer;
        bool _flashlightChecked;
        int _nextReturnLine;

        internal bool HasQuest { get; private set; }
        internal bool QuestCompleted { get; private set; }
        internal bool ToolsReturned { get; private set; }
        internal bool KeyReady { get; private set; }
        internal DoorKeyPickup RewardKey => _rewardKey;
        internal bool HasGreetedForTools => _nextReturnLine > 0;
        internal bool HasReturnDialogueRemaining => _returnLines != null && _nextReturnLine < _returnLines.Length;
        internal bool NeedsLowerFloorHint(bool hasHint) => _requiresLowerFloorHint && !hasHint;
        internal bool CanAskAboutKey(bool hasHint) => isActiveAndEnabled && ToolsReturned && !HasReturnDialogueRemaining &&
            !KeyReady && !QuestCompleted && !IsSpeaking && _rewardKey != null && !NeedsLowerFloorHint(hasHint);
        internal bool CanCheckFlashlight(PlayerHealth health) => isActiveAndEnabled && ToolsReturned &&
            !HasReturnDialogueRemaining && !IsSpeaking && health != null && !health.IsDead &&
            (!_flashlightChecked || health.CanRepairFlashlight);
        internal bool IsSpeaking => _conversation != null;
        internal bool HasWorkshopEncounter { get; set; }
        internal string WorkshopDirections => _workshopDirections;
        internal bool ReturnsToolsInWorkshop => _workshopMeetingPoint != null;
        internal bool IsWaitingInWorkshop { get; private set; }

        internal void MoveToWorkshop()
        {
            if (!HasQuest || !ReturnsToolsInWorkshop || IsWaitingInWorkshop)
                return;

            GetComponent<DeniIdleVariation>()?.CancelForRelocation();
            transform.SetPositionAndRotation(_workshopMeetingPoint.position, _workshopMeetingPoint.rotation);
            IsWaitingInWorkshop = true;
        }

        void Awake()
        {
            _speech = NpcSpeechState.GetOrAdd(this);
            _animation = GetComponent<DeniConversationAnimation>();
        }

        internal bool TryTalk(PlayerStatusHud hud, System.Action questAccepted)
        {
            if (!isActiveAndEnabled || QuestCompleted || ToolsReturned || IsSpeaking)
                return false;

            StartConversation(hud, Speak(), _ =>
            {
                if (HasQuest)
                    return;
                HasQuest = true;
                questAccepted?.Invoke();
            });
            return true;
        }

        internal bool CanReturnTools(PlayerInventory inventory) =>
            isActiveAndEnabled && HasQuest && !QuestCompleted && !KeyReady && !IsSpeaking && inventory != null &&
            (!ReturnsToolsInWorkshop || IsWaitingInWorkshop) &&
            _returnLines != null && _returnLines.Length > 0 &&
            (ToolsReturned ? HasReturnDialogueRemaining :
                IsCarryingQuestTools(inventory));

        internal bool IsCarryingQuestTools(PlayerInventory inventory) => inventory != null &&
            (_toolbox != null ? inventory.IsCarryingToolbox(_toolbox) : inventory.IsCarryingTools);

        internal void GreetForTools(PlayerStatusHud hud)
        {
            var greeting = _returnLines[0];
            StartConversation(hud, Line(greeting.Text, greeting.Alexey, greeting.Clip), skipped =>
            {
                _nextReturnLine = skipped ? _returnLines.Length : 1;
            });
        }

        internal void SkipRemainingReturnDialogue() => _nextReturnLine = _returnLines.Length;

        internal bool TryReturnTools(PlayerStatusHud hud, PlayerInventory inventory, System.Action completed)
        {
            if (!CanReturnTools(inventory) || !HasGreetedForTools)
                return false;
            if (!ToolsReturned)
            {
                if (!inventory.DeliverTools())
                    return false;

                ToolsReturned = true;
                _caretaker?.MoveToAfterToolsPoint();
            }
            if (HasReturnDialogueRemaining)
                StartConversation(hud, ReturnTools(), _ =>
                {
                    SkipRemainingReturnDialogue();
                    completed?.Invoke();
                });
            else
                completed?.Invoke();
            return true;
        }

        internal bool TryAskAboutKey(PlayerStatusHud hud, bool hasHint, System.Action updated)
        {
            if (!CanAskAboutKey(hasHint))
                return false;

            StartConversation(hud, DiscussKey(), _ =>
            {
                KeyReady = true;
                updated?.Invoke();
            });
            return true;
        }

        IEnumerator DiscussKey()
        {
            foreach (var line in _keyLines)
                yield return Line(line.Text, line.Alexey, line.Clip);
        }

        internal bool TryTakeKey(PlayerStatusHud hud, PlayerInventory inventory)
        {
            if (!isActiveAndEnabled || !KeyReady || IsSpeaking || _rewardKey == null || inventory == null ||
                (!inventory.HasLowerFloorKey && !inventory.ReceiveLowerFloorKey(_rewardKey)))
                return false;

            KeyReady = false;
            QuestCompleted = true;
            StartConversation(hud, Line("Спасибо.", alexey: true, clip: _keyThanksVoice), null);
            return true;
        }

        IEnumerator ReturnTools()
        {
            while (_nextReturnLine < _returnLines.Length)
            {
                var line = _returnLines[_nextReturnLine];
                yield return Line(line.Text, line.Alexey, line.Clip);
                _nextReturnLine++;
            }
        }

        internal bool TryRepairFlashlight(PlayerStatusHud hud, PlayerHealth health)
        {
            if (!CanCheckFlashlight(health))
                return false;

            _repairingPlayer = hud.GetComponent<FirstPersonController>();
            _repairingPlayer?.SetMovementLocked(true);
            StartConversation(hud, RepairFlashlight(health), skipped =>
            {
                if (skipped && health.TryRepairFlashlight())
                    hud.RefreshFlashlight();
                _flashlightChecked = !health.CanRepairFlashlight;
            });
            return true;
        }

        IEnumerator RepairFlashlight(PlayerHealth health)
        {
            yield return Line("Проверишь свет?", alexey: true,
                clip: _askToCheckLightVoice ?? DialogueVoice.GetStoryClip("Alexey/Deni_CheckLight"));
            if (!health.CanRepairFlashlight)
            {
                yield return Line("С фонариком всё в порядке.", clip: DialogueVoice.GetStoryClip("Deni/FlashlightOkay"));
                yield break;
            }
            yield return Line("Давай. Контакт тут капризный.",
                clip: _checkLightVoice ?? DialogueVoice.GetStoryClip("Deni/Repair_Contact"));
            if (health.TryRepairFlashlight())
                _hud.RefreshFlashlight();
            yield return Line("Всё, держится.", clip: _lightFixedVoice ?? DialogueVoice.GetStoryClip("Deni/Repair_Done"));
        }

        IEnumerator Speak()
        {
            if (IsWaitingInWorkshop)
                yield break;

            if (!HasQuest)
            {
                if (_openingLines != null && _openingLines.Length > 0)
                {
                    foreach (var line in _openingLines)
                        yield return Line(line.Text, line.Alexey, line.Clip, line.DismissiveWaveAt);
                }
                else
                {
                    yield return Line("Всё как в тот день… Мужик, туда лучше не идти, с нижнего этажа не возвращаются.", dismissiveWaveAt: 1.55f);
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
                string directions = _workshopDirections;
                if (string.IsNullOrWhiteSpace(directions))
                    directions = HasWorkshopEncounter
                        ? "Иди прямо в первый тоннель от жилого зала. Там ищи вымогателя. Ящик красный."
                        : "Прямо и сразу направо. Потом дверь с белой полосой. Ящик красный.";
                yield return Line(directions);
                yield return Line("Хотя сейчас весь в грязи. Но ручка красная.");
            }

            if (ReturnsToolsInWorkshop)
            {
                yield return Line("Я буду ждать у себя в мастерской. Принеси ящик туда.",
                    clip: DialogueVoice.GetStoryClip("Deni/WaitInWorkshop"));
                yield return Line("От зала с койками сверни в правый тоннель. Мастерская в боковой комнате.",
                    clip: DialogueVoice.GetStoryClip("Deni/WorkshopRoute"));
            }
        }

        void StartConversation(PlayerStatusHud hud, IEnumerator lines, System.Action<bool> completed)
        {
            _hud = hud;
            _onConversationCompleted = completed;
            _animation?.BeginConversation(hud.GetComponent<PlayerPistol>()?.ViewCamera);
            _conversation = StartCoroutine(PlayConversation(lines));
        }

        IEnumerator PlayConversation(IEnumerator lines)
        {
            yield return lines;
            CompleteConversation(skipped: false);
        }

        internal void SkipConversation()
        {
            if (!IsSpeaking)
                return;
            StopCoroutine(_conversation);
            CompleteConversation(skipped: true);
        }

        void CompleteConversation(bool skipped)
        {
            var completed = _onConversationCompleted;
            FinishConversation();
            completed?.Invoke(skipped);
        }

        IEnumerator Line(string text, bool alexey = false, AudioClip clip = null, float dismissiveWaveAt = 0f)
        {
            string speaker = alexey ? "Алексей" : "Дени";
            float duration = clip != null
                ? Mathf.Max(0.01f, clip.length)
                : Mathf.Clamp(1f + text.Length / 17f, 2.4f, 8f);
            AudioSource voice = null;
            if (clip != null)
            {
                if (alexey)
                    voice = _playerVoice = DialogueVoice.GetPlayerSource(_playerVoice, _hud.transform);
                else
                {
                    if (_voice == null)
                    {
                        _voice = DialogueVoice.CreateSource(transform, "Deni Voice", minDistance: 3f, maxDistance: 16f);
                        _animation?.AttachVoice(_voice.transform);
                    }
                    voice = _voice;
                }
            }

            _speech.SetTalking(!alexey);
            _hud.SetStorySubtitle($"{speaker}: {text}", alexey ? Color.white : DeniColor);
            _animation?.BeginLine(text, !alexey, duration, voice, clip, dismissiveWaveAt);
            if (voice == null)
                yield return new WaitForSeconds(duration);
            else
                yield return DialogueVoice.Play(voice, clip);

            _animation?.EndLine();
            _speech.SetTalking(false);
        }

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
            _onConversationCompleted = null;
            if (_repairingPlayer != null)
                _repairingPlayer.SetMovementLocked(false);
            _repairingPlayer = null;
            if (_voice != null)
                _voice.Stop();
            if (_playerVoice != null)
                _playerVoice.Stop();
            if (_speech != null)
                _speech.SetTalking(false);
            _animation?.EndConversation();
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
