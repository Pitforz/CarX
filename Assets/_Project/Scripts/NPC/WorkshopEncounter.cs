using System.Collections;
using CarX.Gameplay;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(NpcSpeechState))]
    public sealed class WorkshopEncounter : MonoBehaviour
    {
        enum Stage { WaitingForPlayer, Introduction, WaitingForThreat, Surrender, Finished }

        const float TalkDistance = 4f;
        const float ClueDistance = 2.5f;
        static readonly Color ExtortionistColor = new Color32(226, 174, 131, 255);

        [SerializeField] DeniDialogue _questGiver;
        [SerializeField] ToolboxPickup _toolbox;
        [SerializeField] AudioClip _demandToolsVoice;
        [SerializeField] AudioClip _askAboutFatherVoice;
        [SerializeField] AudioClip _askWhenVoice;
        [SerializeField] AudioClip _readClueVoice;
        [SerializeField] AudioClip _paidPassageVoice;
        [SerializeField] AudioClip _deniSentYouVoice;
        [SerializeField] AudioClip _threatVoice;
        [SerializeField] AudioClip _surrenderVoice;
        [SerializeField] AudioClip _sawFatherVoice;
        [SerializeField] AudioClip _atLowerDoorVoice;
        [SerializeField] AudioClip _droppedNoteVoice;
        [SerializeField] AudioClip _takeNoteVoice;
        [SerializeField] AudioClip _noteReminderVoice;
        [SerializeField] ClueNote _clue;

        PlayerMenus _menus;
        PlayerPistol _pistol;
        PlayerInventory _inventory;
        PlayerStatusHud _hud;
        PlayerInnerVoice _innerVoice;
        AudioSource _playerVoice, _voice;
        NpcSpeechState _speech;
        DeniConversationAnimation _animation;
        WorkshopActorAnimation _actor;
        Coroutine _conversation;
        Stage _stage;
        bool _clueTaken, _clueReminderShown;

        internal DeniDialogue QuestGiver => _questGiver;
        internal bool IsSpeaking => _conversation != null;
        internal bool NeedsIntimidation => _stage == Stage.WaitingForThreat;
        internal bool HasIntimidated => _stage == Stage.Surrender || _stage == Stage.Finished;

        void Awake()
        {
            _speech = NpcSpeechState.GetOrAdd(this);
            _animation = GetComponent<DeniConversationAnimation>();
            _actor = GetComponent<WorkshopActorAnimation>();
            if (_toolbox != null)
                _toolbox.PickupAllowed = false;
            if (_questGiver != null)
                _questGiver.HasWorkshopEncounter = true;
        }

        void Start()
        {
            foreach (var menus in FindObjectsByType<PlayerMenus>())
            {
                if (menus.gameObject.scene != gameObject.scene)
                    continue;
                _menus = menus;
                _pistol = menus.GetComponent<PlayerPistol>();
                _inventory = menus.GetComponent<PlayerInventory>();
                _hud = menus.GetComponent<PlayerStatusHud>();
                _innerVoice = menus.GetComponent<PlayerInnerVoice>();
                _menus.RegisterWorkshop(this);
                break;
            }
        }

        void LateUpdate()
        {
            if (_menus == null || !_menus.CanStartNpcConversation || _pistol == null)
                return;

            if (_stage == Stage.WaitingForPlayer)
            {
                if (_questGiver == null || !_questGiver.HasQuest || _questGiver.QuestCompleted ||
                    !CanSeeNpcWithin(TalkDistance))
                    return;
                _stage = Stage.Introduction;
                _conversation = StartCoroutine(Introduce());
            }
            else if (_stage == Stage.WaitingForThreat && _pistol.IsAimingAt(transform, TalkDistance))
            {
                _stage = Stage.Surrender;
                _actor?.Surrender();
                _menus.RefreshQuestDisplay();
                _conversation = StartCoroutine(Surrender());
            }
            else if (_stage == Stage.Finished && !_clueTaken && !_clueReminderShown &&
                (_menus.transform.position - transform.position).sqrMagnitude > TalkDistance * TalkDistance)
            {
                _conversation = StartCoroutine(RemindAboutClue());
            }
        }

        IEnumerator RemindAboutClue()
        {
            yield return Line("Эй, погоди! Бумажку тоже забери.", _noteReminderVoice);
            _clueReminderShown = true;
            FinishConversation();
        }

        bool CanSeeNpcWithin(float range) => _pistol.IsLookingAt(transform, range);

        IEnumerator Introduce()
        {
            yield return Line("Стоять! Дальше платный проход.", _paidPassageVoice);
            yield return Line("Я за инструментами Дени. Отдай ящик!", _demandToolsVoice, alexey: true);
            yield return Line("Дени за ящиком прислал? Пусть сам приходит.", _deniSentYouVoice);
            yield return Line("А ты ещё шаг сделаешь — пожалеешь.", _threatVoice);
            CompleteIntroduction();
            _innerVoice?.ConsiderIntimidation();
        }

        void CompleteIntroduction()
        {
            _actor?.Threaten();
            FinishConversation();
            _stage = Stage.WaitingForThreat;
            _menus.TrackWorkshopObjective(_questGiver);
        }

        IEnumerator Surrender()
        {
            yield return Line("Понял, понял. Забирай. Из-за железок я тут подыхать не собираюсь.", _surrenderVoice);
            if (_toolbox != null)
                _toolbox.PickupAllowed = true;
            yield return Line("Не видел тут пожилого мужчину? Высокий, полный, Александр зовут.",
                _askAboutFatherVoice, alexey: true);
            yield return Line("С конвертом, да? Видел его пару дней назад.", _sawFatherVoice);
            if (_sawFatherVoice == null)
                yield return Line("Давно ты его видел?", _askWhenVoice, alexey: true);
            yield return Line("У двери на нижний этаж его двое ждали. Сказали, что внизу с ним рассчитаются.", _atLowerDoorVoice);
            yield return Line("Он за ними пошёл. У двери бумажку выронил.", _droppedNoteVoice);
            yield return Line("На, возьми. Может, поймёшь, зачем он туда полез.", _takeNoteVoice);
            CompleteSurrender();
        }

        void CompleteSurrender()
        {
            if (_toolbox != null)
                _toolbox.PickupAllowed = true;
            FinishConversation();
            _stage = Stage.Finished;
            _menus.RefreshQuestDisplay();
        }

        IEnumerator Line(string text, AudioClip clip = null, bool alexey = false)
        {
            _animation?.BeginConversation(_pistol.ViewCamera);
            AudioSource source = null;
            if (clip != null)
            {
                if (alexey)
                    source = _playerVoice = DialogueVoice.GetPlayerSource(_playerVoice, _hud.transform);
                else
                {
                    if (_voice == null)
                    {
                        _voice = DialogueVoice.CreateSource(transform, "Extortionist Voice", 3f, 16f);
                        _animation?.AttachVoice(_voice.transform);
                    }
                    source = _voice;
                }
            }
            float duration = clip != null ? clip.length : Mathf.Clamp(1f + text.Length / 17f, 2.4f, 8f);
            _speech.SetTalking(!alexey);
            _hud.SetStorySubtitle($"{(alexey ? "Алексей" : "Вымогатель")}: {text}", alexey ? Color.white : ExtortionistColor);
            _animation?.BeginLine(text, !alexey, duration, source, clip);
            if (source != null)
                yield return DialogueVoice.Play(source, clip);
            else
                yield return new WaitForSeconds(duration);
            _animation?.EndLine();
            _speech.SetTalking(false);
        }

        internal bool CanTakeClue() => isActiveAndEnabled && _stage == Stage.Finished && !_clueTaken &&
            _inventory != null && _clue != null && _clue.Paper != null &&
            _pistol != null && _pistol.IsLookingAt(transform, ClueDistance);

        internal void TakeClue()
        {
            if (!CanTakeClue())
                return;
            _clueTaken = _inventory.TryCollectClue(_clue, OnClueDismissed);
        }

        void OnClueDismissed()
        {
            if (isActiveAndEnabled)
                _innerVoice?.ReadClue(_readClueVoice, "Домой… И что бы ты нам сказал?");
        }

        internal void SkipConversation()
        {
            if (!IsSpeaking)
                return;

            StopCoroutine(_conversation);
            switch (_stage)
            {
                case Stage.Introduction:
                    CompleteIntroduction();
                    break;
                case Stage.Surrender:
                    CompleteSurrender();
                    break;
                default:
                    _clueReminderShown = true;
                    FinishConversation();
                    break;
            }
        }

        internal void CancelConversation()
        {
            if (!IsSpeaking)
                return;
            StopCoroutine(_conversation);
            if (_stage == Stage.Introduction)
            {
                _stage = Stage.WaitingForPlayer;
                _actor?.CancelThreat();
            }
            else if (_stage == Stage.Surrender)
            {
                _stage = Stage.Finished;
                if (_toolbox != null)
                    _toolbox.PickupAllowed = true;
            }
            FinishConversation();
        }

        void FinishConversation()
        {
            _conversation = null;
            if (_playerVoice != null)
                _playerVoice.Stop();
            if (_voice != null)
                _voice.Stop();
            _animation?.EndConversation();
            if (_speech != null)
                _speech.SetTalking(false);
            if (_hud != null)
                _hud.SetStorySubtitle(null, Color.white);
        }

        void OnDisable()
        {
            CancelConversation();
            _actor?.CancelThreat();
            _animation?.EndConversation();
            if (_speech != null)
                _speech.SetTalking(false);
        }

        void OnDestroy()
        {
            if (_playerVoice != null)
                Destroy(_playerVoice.gameObject);
        }
    }
}
