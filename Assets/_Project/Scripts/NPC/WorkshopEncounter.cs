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

        PlayerMenus _menus;
        PlayerPistol _pistol;
        PlayerInventory _inventory;
        PlayerStatusHud _hud;
        AudioSource _playerVoice;
        NpcSpeechState _speech;
        Coroutine _conversation;
        Stage _stage;
        bool _clueTaken;
        bool _clueReminderShown;

        internal DeniDialogue QuestGiver => _questGiver;
        internal bool IsSpeaking => _conversation != null;
        internal bool NeedsIntimidation => _stage == Stage.WaitingForThreat;
        internal bool HasIntimidated => _stage == Stage.Surrender || _stage == Stage.Finished;

        void Awake()
        {
            _speech = NpcSpeechState.GetOrAdd(this);
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
                _menus.RefreshQuestDisplay();
                _conversation = StartCoroutine(Surrender());
            }
            else if (_stage == Stage.Finished)
            {
                RemindAboutClueIfLeaving();
            }
        }

        void RemindAboutClueIfLeaving()
        {
            if (_clueTaken || _clueReminderShown || _inventory == null || !_inventory.IsCarryingToolbox(_toolbox))
                return;

            if ((_inventory.transform.position - transform.position).sqrMagnitude <= TalkDistance * TalkDistance)
                return;

            _clueReminderShown = true;
            _hud.ShowSubtitle("Вымогатель: Эй, погоди! Записку-то забери.", _speech);
        }

        bool CanSeeNpcWithin(float range)
        {
            var camera = _pistol.ViewCamera;
            if (camera == null)
                return false;

            Vector3 origin = camera.transform.position;
            Vector3 offset = transform.position - origin;
            if (offset.sqrMagnitude > range * range)
                return false;

            int layers = Physics.DefaultRaycastLayers & ~(1 << _pistol.gameObject.layer);
            return Physics.Raycast(origin, offset.normalized, out var hit, offset.magnitude, layers,
                QueryTriggerInteraction.Ignore) && hit.collider.transform.IsChildOf(transform);
        }

        IEnumerator Introduce()
        {
            yield return Line("Стой там. За проход платят.");
            yield return PlayerLine(_demandToolsVoice, "Я за инструментами Дени. Отдай ящик!",
                "Я за инструментами Дени. Верни ящик.");
            yield return Line("Дени сам пусть приходит. А ты вали отсюда.");
            FinishConversation();
            _stage = Stage.WaitingForThreat;
            _menus.TrackWorkshopObjective(_questGiver);
            _hud.ShowSubtitle("А может, попробовать припугнуть его?");
        }

        IEnumerator Surrender()
        {
            yield return Line("Ладно, спокойно. Забирай ящик.");
            yield return PlayerLine(_askAboutFatherVoice, "Не видел тут пожилого мужчину? Высокий, полный, Александр зовут.",
                "Этого человека видел? Это мой отец.");
            yield return Line("Видел. Он к двери на нижний этаж шёл. Я как раз воду нёс.");
            yield return PlayerLine(_askWhenVoice, "Давно ты его видел?", "Он спустился?");
            string answer = _askWhenVoice != null
                ? "Давно. Он спустился при мне. Ему оттуда открыли. Сказал, за расчётом."
                : "При мне. Ему оттуда открыли. Сказал, за расчётом.";
            yield return Line(answer);
            yield return Line("Он у двери записку обронил. Я окликнул, он не услышал. Забирай её тоже.");
            yield return Line("Спасибо.", alexey: true);

            FinishConversation();
            _stage = Stage.Finished;
            if (_toolbox != null)
                _toolbox.PickupAllowed = true;
            _menus.RefreshQuestDisplay();
        }

        IEnumerator Line(string text, bool alexey = false)
        {
            string speaker = alexey ? "Алексей" : "Вымогатель";
            _speech.SetTalking(!alexey);
            _hud.SetStorySubtitle($"{speaker}: {text}", alexey ? Color.white : ExtortionistColor);
            yield return new WaitForSeconds(Mathf.Clamp(1f + text.Length / 17f, 2.4f, 8f));
            _speech.SetTalking(false);
        }

        IEnumerator PlayerLine(AudioClip clip, string text, string fallback)
        {
            if (clip == null)
            {
                yield return Line(fallback, alexey: true);
                yield break;
            }

            _speech.SetTalking(false);
            _hud.SetStorySubtitle($"Алексей: {text}", Color.white);
            _playerVoice = DialogueVoice.GetPlayerSource(_playerVoice, _hud.transform);
            yield return DialogueVoice.Play(_playerVoice, clip);
        }

        internal bool CanTakeClue() => isActiveAndEnabled && _stage == Stage.Finished && !_clueTaken &&
            _pistol != null && _pistol.IsLookingAt(transform, ClueDistance);

        internal void TakeClue()
        {
            _clueTaken = true;
            if (_readClueVoice != null)
                _conversation = StartCoroutine(ReactToClue());
        }

        IEnumerator ReactToClue()
        {
            yield return PlayerLine(_readClueVoice, "Домой… И что бы ты нам сказал?", null);
            FinishConversation();
        }

        internal void CancelConversation()
        {
            if (!IsSpeaking)
                return;

            StopCoroutine(_conversation);
            if (_stage == Stage.Introduction)
                _stage = Stage.WaitingForPlayer;
            else if (_stage == Stage.Surrender)
                _stage = Stage.WaitingForThreat;
            FinishConversation();
        }

        void FinishConversation()
        {
            _conversation = null;
            if (_playerVoice != null)
                _playerVoice.Stop();
            if (_speech != null)
                _speech.SetTalking(false);
            if (_hud != null)
                _hud.SetStorySubtitle(null, Color.white);
        }

        void OnDisable()
        {
            CancelConversation();
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
