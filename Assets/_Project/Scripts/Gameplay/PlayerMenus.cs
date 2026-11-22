using System.Collections.Generic;
using CarX.NPC;
using StarterAssets;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerStatusHud))]
    [DefaultExecutionOrder(-200)]
    public sealed class PlayerMenus : MonoBehaviour
    {
        const string QuestTitle = "Задание №1";
        const string QuestDescription = "тест";
        const string DeniQuestTitle = "Вернуть Дени инструменты из мастерской";
        const string FindKeyObjective = "Найти ключ от нижнего этажа";
        const string FindKeyHint = "Расспроси местных, где можно достать ключ.";
        const string MouseSensitivityPref = "CarX.MouseSensitivityPercent";
        const string MasterVolumePref = "CarX.MasterVolumePercent";
        const int DefaultSensitivity = 100;
        const int MinSensitivity = 25;
        const int MaxSensitivity = 200;
        const int DefaultMasterVolume = 60;
        const float FullDialogueDistance = 3f;
        const float DialoguePreviewDistance = 5.5f;
        const float DeniReturnGreetingDistance = 6f;
        const float DeniKeyPickupDistance = 1.4f;
        const float SlowTimeScale = 0.02f;
        const float SlowTimePhysicsStepScale = 0.1f;

        enum Menu { None, Pause, Settings, Quests, Dying, Death, Dialogue, Clue, SlowTime }
        enum DeniQuestion { Talk, ContinueReturn, AskAboutKey, CheckFlashlight }

        [SerializeField] Material _clueBlur;

        NoteReaderView _noteReader;
        SlowTimeTutorialView _slowTimeTutorial;
        System.Action<int> _selectEncounterChoice;
        System.Action _skipEncounterDialogue;
        System.Action _resumeEncounterFight;
        bool _encounterInputLocked;
        bool _encounterCinematic;
        bool _slowTimePaused;
        bool _openingClue;
        System.Action _onClueDismissed;
        PlayerStatusHud _hud;
        PlayerHealth _health;
        PlayerInventory _inventory;
        PlayerInnerVoice _innerVoice;
        PlayerPistol _pistol;
        StarterAssetsInputs _inputs;
        FirstPersonController _controller;
        PlayerDeathFall _deathFall;
        RectTransform _overlay;
        TypewriterMenuText _typewriterText;
        PauseMenuView _pauseMenu;
        SettingsMenuView _settingsMenu;
        QuestJournalView _questJournal;
        RectTransform _deathPanel;
        NpcQuestionsView _questionsView;
        UnityEngine.UI.Text _talkHint;
        UnityEngine.UI.Text _skipDialogueHint;
        readonly List<string> _questions = new();
        readonly List<int> _caretakerQuestionIds = new();
        readonly List<DeniQuestion> _deniQuestions = new();
        NpcDialogue _nearbyNpc;
        NpcDialogue _dialogueNpc;
        NpcDialogue _dismissedDialogueNpc;
        NpcDialogue _answeringNpc;
        CaretakerTour _activeTour;
        DeniDialogue _activeDeniDialogue;
        DeniDialogue _deniQuest;
        WorkshopEncounter _workshop;
        bool _clueInView;
        int _sensitivityPercent;
        int _masterVolumePercent;
        bool _settingsChanged;
        UnityEngine.UI.Text _trackedQuest;
        bool _questTracked;
        bool _trackingDeniQuest;
        bool _hasLowerFloorHint;
        bool _restartingScene;
        Menu _menu;
        float _pauseStartedAt;
        float _totalPausedTime;
        float _previousTimeScale;
        float _previousFixedDeltaTime;
        bool _previousAudioPause;
        CursorLockMode _previousCursorLock;
        bool _previousCursorVisible;
        bool _previousInputCursorLock;
        int _closedFrame = -1;
        float _nextDialogueScrollAt;

        internal bool BlocksGameplayInput => _encounterInputLocked || _openingClue ||
            _menu != Menu.None || _closedFrame == Time.frameCount;
        internal bool AllowsEncounterWeaponInput => _encounterInputLocked && !_encounterCinematic && _menu == Menu.None;
        internal bool IsPaused => IsPauseMenu(_menu);
        internal float UnpausedTime => (IsPaused ? _pauseStartedAt : Time.unscaledTime) - _totalPausedTime;
        internal bool HasTalkTarget => _nearbyNpc != null || _clueInView;
        bool HasTrackedQuest => _questTracked && (!_trackingDeniQuest || (_deniQuest != null && !_deniQuest.QuestCompleted));
        bool HasKeyObjective => _hasLowerFloorHint && _inventory != null && !_inventory.HasLowerFloorKey;
        internal bool IsNpcSpeaking => (_activeTour != null && _activeTour.IsRunning) ||
            (_activeDeniDialogue != null && _activeDeniDialogue.IsSpeaking) ||
            (_answeringNpc != null && _answeringNpc.IsAnswering) ||
            (_workshop != null && _workshop.IsSpeaking);
        internal bool CanStartNpcConversation => _hud.HudRect != null && !_health.IsDead &&
            !BlocksGameplayInput && Time.timeScale > 0f && (_inventory == null || !_inventory.IsOpen) &&
            (_innerVoice == null || !_innerVoice.IsSpeaking) &&
            !IsNpcSpeaking;

        void Awake()
        {
            _hud = GetComponent<PlayerStatusHud>();
            _health = GetComponent<PlayerHealth>();
            _inventory = GetComponent<PlayerInventory>();
            _innerVoice = GetComponent<PlayerInnerVoice>();
            _pistol = GetComponent<PlayerPistol>();
            _inputs = GetComponent<StarterAssetsInputs>();
            _controller = GetComponent<FirstPersonController>();
            _deathFall = GetComponent<PlayerDeathFall>();
            _sensitivityPercent = Mathf.Clamp(PlayerPrefs.GetInt(MouseSensitivityPref, DefaultSensitivity),
                MinSensitivity, MaxSensitivity);
            _masterVolumePercent = Mathf.Clamp(PlayerPrefs.GetInt(MasterVolumePref, DefaultMasterVolume), 0, 100);
            ApplySensitivity();
            ApplyMasterVolume();
        }

        void OnEnable()
        {
            _health.HealthChanged += OnHealthChanged;
        }

        void OnHealthChanged(int health)
        {
            if (health == 0 && _hud.HudRect != null)
                BeginDeath();
        }

        void BeginDeath()
        {
            if (_menu == Menu.Dying || _menu == Menu.Death)
                return;

            _activeTour?.Cancel();
            _activeDeniDialogue?.Cancel();
            _answeringNpc?.CancelAnswer();
            _workshop?.CancelConversation();

            if (_deathFall == null || !_deathFall.isActiveAndEnabled)
            {
                SetMenu(Menu.Death);
                return;
            }

            Camera camera = _pistol != null ? _pistol.ViewCamera : null;
            SetMenu(Menu.Dying);
            _inventory?.StowMagazine();
            if (_pistol != null)
                _pistol.enabled = false;

            _deathFall.Play(camera != null ? camera : Camera.main, () =>
            {
                if (_menu == Menu.Dying)
                    SetMenu(Menu.Death);
            });
        }

        void Update()
        {
            if (_hud.HudRect == null || _menu == Menu.Dying || _menu == Menu.Death)
                return;

            if (_health.IsDead)
            {
                BeginDeath();
                return;
            }

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            bool escape = keyboard?.escapeKey.wasPressedThisFrame == true;
            bool quests = keyboard?.tKey.wasPressedThisFrame == true;
            bool interact = keyboard?.eKey.wasPressedThisFrame == true;
            bool skipDialogue = keyboard?.mKey.wasPressedThisFrame == true;
            bool slowTime = keyboard?.oKey.wasPressedThisFrame == true;
            bool resumeTime = keyboard?.spaceKey.wasPressedThisFrame == true;
#else
            bool escape = Input.GetKeyDown(KeyCode.Escape);
            bool quests = Input.GetKeyDown(KeyCode.T);
            bool interact = Input.GetKeyDown(KeyCode.E);
            bool skipDialogue = Input.GetKeyDown(KeyCode.M);
            bool slowTime = Input.GetKeyDown(KeyCode.O);
            bool resumeTime = Input.GetKeyDown(KeyCode.Space);
#endif
            if (escape && (IsPaused || _encounterInputLocked || _resumeEncounterFight != null))
            {
                if (_menu == Menu.Pause)
                    ContinueFromPause();
                else
                    SetMenu(Menu.Pause);
                return;
            }

            if (_menu == Menu.SlowTime)
            {
                if (_selectEncounterChoice != null)
                {
                    int choice = ReadEncounterChoice();
                    if (choice != 0)
                        SelectEncounterChoice(choice);
                }
                else if (resumeTime)
                {
                    var resumeFight = _resumeEncounterFight;
                    SetMenu(Menu.None);
                    resumeFight?.Invoke();
                }
                return;
            }

            if (_encounterInputLocked)
            {
                if (_menu == Menu.None && skipDialogue)
                    _skipEncounterDialogue?.Invoke();
                return;
            }

            if (_menu == Menu.Clue)
            {
#if ENABLE_INPUT_SYSTEM
                bool closeNote = keyboard?.tabKey.wasPressedThisFrame == true;
#else
                bool closeNote = Input.GetKeyDown(KeyCode.Tab);
#endif
                if (escape || interact || closeNote)
                {
                    var onDismissed = _onClueDismissed;
                    SetMenu(Menu.None);
                    onDismissed?.Invoke();
                }
                return;
            }

            if (_openingClue)
                return;

            if (_menu == Menu.Dialogue)
            {
                if (escape || skipDialogue || _dialogueNpc == null || !_dialogueNpc.CanTalk)
                {
                    _dismissedDialogueNpc = _dialogueNpc;
                    SetMenu(Menu.None);
                }
                else
                    UpdateDialogueSelection(interact);
                return;
            }

            if (escape)
                SetMenu(_menu == Menu.None ? Menu.Pause : Menu.None);
            else if (quests && (_menu == Menu.None || _menu == Menu.Quests))
                SetMenu(_menu == Menu.Quests ? Menu.None : Menu.Quests);
            else if (_menu == Menu.None && Time.timeScale > 0f)
            {
                if (slowTime)
                {
                    ShowSlowTimeTutorial();
                    return;
                }

                if (skipDialogue && SkipActiveDialogue())
                    return;

                if (!HandleDeniQuestInteraction(interact, skipDialogue))
                    UpdateTalkTarget(interact);
            }
        }

        bool SkipActiveDialogue()
        {
            if (!IsNpcSpeaking)
                return false;

            _activeTour?.Skip();
            _activeDeniDialogue?.SkipConversation();
            _answeringNpc?.SkipAnswer();
            _workshop?.SkipConversation();
            _closedFrame = Time.frameCount;
            HideTalkHint();
            return true;
        }

        bool HandleDeniQuestInteraction(bool interact, bool skipDialogue)
        {
            if (!CanStartNpcConversation || _deniQuest == null || _pistol == null || _pistol.ViewCamera == null)
                return false;

            bool keyReady = _deniQuest.KeyReady;
            if (!keyReady && !_deniQuest.CanReturnTools(_inventory))
                return false;

            var npc = _deniQuest.GetComponent<NpcDialogue>();
            var collider = _deniQuest.GetComponent<Collider>();
            if (npc == null || !npc.CanTalk || collider == null || !collider.enabled ||
                npc.gameObject.scene != gameObject.scene)
                return false;

            bool needsGreeting = !keyReady && !_deniQuest.HasGreetedForTools;
            float range = keyReady ? DeniKeyPickupDistance : needsGreeting
                ? Mathf.Max(DeniReturnGreetingDistance, npc.InteractionDistance)
                : npc.InteractionDistance;
            Vector3 center = transform.position + Vector3.up * 0.9f;
            if ((collider.ClosestPoint(center) - center).sqrMagnitude > range * range)
                return false;

            Vector3 origin = _pistol.ViewCamera.transform.position;
            Vector3 target = collider.bounds.center + Vector3.up * (collider.bounds.extents.y * 0.5f);
            Vector3 direction = target - origin;
            foreach (var hit in Physics.RaycastAll(origin, direction.normalized, direction.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(_deniQuest.transform))
                    return false;
            }

            if (keyReady)
            {
                if (interact && _deniQuest.TryTakeKey(_hud, _inventory))
                {
                    _activeDeniDialogue = _deniQuest;
                    _closedFrame = Time.frameCount;
                    HideTalkHint();
                    RefreshQuestDisplay();
                }
                else
                {
                    _nearbyNpc = npc;
                    _clueInView = false;
                    HideDialogueQuestions(immediate: true);
                    ShowTalkHint("E — забрать ключ");
                }
                return true;
            }

            if (needsGreeting)
            {
                _activeDeniDialogue = _deniQuest;
                _deniQuest.GreetForTools(_hud);
                HideTalkHint();
                _closedFrame = Time.frameCount;
                return true;
            }

            if (skipDialogue)
                _deniQuest.SkipRemainingReturnDialogue();
            if ((interact || (skipDialogue && _deniQuest.ToolsReturned)) && StartDeniReturn(_deniQuest))
            {
                _closedFrame = Time.frameCount;
                return true;
            }

            _nearbyNpc = npc;
            _clueInView = false;
            HideDialogueQuestions();
            string action = _deniQuest.ToolsReturned ? "E — продолжить разговор" : "E — отдать ящик";
            ShowTalkHint(_deniQuest.HasReturnDialogueRemaining ? action + "\nM — пропустить диалог" : action);
            return true;
        }

        bool StartDeniReturn(DeniDialogue deni)
        {
            if (!deni.TryReturnTools(_hud, _inventory, RefreshQuestDisplay))
                return false;

            _activeDeniDialogue = deni;
            _deniQuest = deni;
            TrackDeniQuest();
            HideTalkHint();
            return true;
        }

        internal bool TryStartNpcIntroduction(NpcDialogue npc)
        {
            Camera camera = _pistol != null ? _pistol.ViewCamera : null;
            if (!CanStartNpcConversation || camera == null)
                return false;

            Vector3 origin = camera.transform.position;
            Vector3 offset = npc.transform.position + Vector3.up * 1.45f - origin;
            int layers = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);
            if (Physics.Raycast(origin, offset.normalized, out var hit, offset.magnitude, layers,
                QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(npc.transform))
                return false;
            if (!npc.TryIntroduce(_hud))
                return false;

            _answeringNpc = npc;
            HideTalkHint();
            HideDialogueQuestions(immediate: true);
            return true;
        }

        void UpdateTalkTarget(bool interact)
        {
            bool canInteract = CanStartNpcConversation;
            float proximity = 0f;
            var npc = canInteract ? FindLookedAtNpc(out proximity) : null;
            if (canInteract && npc != _dismissedDialogueNpc)
                _dismissedDialogueNpc = null;

            var deni = npc != null ? npc.GetComponent<DeniDialogue>() : null;
            bool keyReady = deni != null && deni.KeyReady;
            bool carryingToolsForDeni = deni != null && deni.IsCarryingQuestTools(_inventory);
            _clueInView = canInteract && _workshop != null && _workshop.CanTakeClue();
            _nearbyNpc = keyReady ? null : npc;
            ShowTalkHint(_clueInView ? "E — подобрать улику" : null);

            if (_clueInView || npc == null || keyReady || carryingToolsForDeni)
            {
                HideDialogueQuestions(immediate: !canInteract || _clueInView || keyReady || carryingToolsForDeni);
                if (interact && _clueInView)
                    _workshop.TakeClue();
                return;
            }

            if (npc == _dismissedDialogueNpc && !interact)
                return;

            _dismissedDialogueNpc = null;
            bool changedTarget = _dialogueNpc != npc;
            _dialogueNpc = npc;
            ConfigureDialogue(npc, deni, proximity, changedTarget);
            if (interact)
                SetMenu(Menu.Dialogue);
            else
                UpdateDialogueSelection(confirm: false);
        }

        void ShowTalkHint(string text)
        {
            if (text != null && _talkHint == null)
            {
                Font font = _hud.QuestFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _talkHint = MakeText(_hud.HudRect, font, "Talk Hint", "", 18,
                    new Vector2(0f, -56f), new Vector2(280f, 52f));
            }

            if (_talkHint == null)
                return;
            if (text != null)
                _talkHint.text = text;
            if (_talkHint.gameObject.activeSelf != (text != null))
                _talkHint.gameObject.SetActive(text != null);
        }

        void ConfigureDialogue(NpcDialogue npc, DeniDialogue deni, float proximity, bool changedTarget)
        {
            _questions.Clear();
            _caretakerQuestionIds.Clear();
            _deniQuestions.Clear();
            if (deni != null)
            {
                if (!deni.ToolsReturned)
                {
                    string question = deni.IsWaitingInWorkshop ? "Я ещё ищу ящик" : "Где мастерская?";
                    AddDeniQuestion(DeniQuestion.Talk, deni.HasQuest ? question : npc.FirstQuestion);
                }
                else if (deni.HasReturnDialogueRemaining)
                    AddDeniQuestion(DeniQuestion.ContinueReturn, "Продолжить разговор");
                else
                {
                    if (deni.CanAskAboutKey(_hasLowerFloorHint))
                        AddDeniQuestion(DeniQuestion.AskAboutKey, "Спросить про ключ");
                    if (deni.CanCheckFlashlight(_health))
                        AddDeniQuestion(DeniQuestion.CheckFlashlight, "Проверишь свет?");
                }
                _questions.Add("Уйти");
            }
            else
            {
                var tour = npc.GetComponent<CaretakerTour>();
                if (tour != null)
                {
                    if (tour.CanStartIntroduction)
                    {
                        _questions.Add(npc.FirstQuestion);
                        _caretakerQuestionIds.Add(CaretakerTour.IntroductionQuestion);
                    }
                    for (int i = 0; i < tour.FollowUpCount; i++)
                    {
                        if (!tour.CanAskFollowUp(i))
                            continue;
                        _questions.Add(tour.GetFollowUpQuestion(i));
                        _caretakerQuestionIds.Add(i);
                    }
                    _questions.Add("Уйти");
                }
                else
                {
                    _questions.Add(npc.FirstQuestion);
                    if (!npc.HasAutomaticIntroduction)
                        _questions.Add(npc.SecondQuestion);
                    if (!string.IsNullOrEmpty(npc.CloseOption))
                        _questions.Add(npc.CloseOption);
                }
            }

            if (_overlay == null)
                BuildMenus();
            _questionsView ??= new NpcQuestionsView(_hud.HudRect, SelectDialogueOption, _hud.QuestFont);
            _questionsView.Show(npc.DisplayName, _questions, proximity, changedTarget);
        }

        void AddDeniQuestion(DeniQuestion question, string text)
        {
            _deniQuestions.Add(question);
            _questions.Add(text);
        }

        NpcDialogue FindLookedAtNpc(out float proximity)
        {
            proximity = 0f;
            Camera camera = _pistol != null ? _pistol.ViewCamera : null;
            if (camera == null)
                return null;

            var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int layers = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);
            if (!Physics.Raycast(ray, out var hit, camera.farClipPlane, layers, QueryTriggerInteraction.Ignore))
                return null;

            var npc = hit.collider.GetComponentInParent<NpcDialogue>();
            if (npc == null || npc.gameObject.scene != gameObject.scene || !npc.CanTalk || !npc.ShowsQuestions)
                return null;
            if (npc.TryGetComponent<CaretakerTour>(out var tour) && !tour.HasAvailableQuestion)
                return null;

            float distance = Vector3.Distance(transform.position, npc.transform.position);
            float previewDistance = Mathf.Max(DialoguePreviewDistance, npc.InteractionDistance);
            if (distance > previewDistance)
                return null;

            proximity = Mathf.InverseLerp(previewDistance, FullDialogueDistance, distance);
            return npc;
        }

        void UpdateDialogueSelection(bool confirm)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            bool up = keyboard?.upArrowKey.wasPressedThisFrame == true;
            bool down = keyboard?.downArrowKey.wasPressedThisFrame == true;
            confirm |= keyboard?.enterKey.wasPressedThisFrame == true ||
                keyboard?.numpadEnterKey.wasPressedThisFrame == true;
            float wheel = Mouse.current?.scroll.ReadValue().y ?? 0f;
#else
            bool up = Input.GetKeyDown(KeyCode.UpArrow);
            bool down = Input.GetKeyDown(KeyCode.DownArrow);
            confirm |= Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            float wheel = Input.mouseScrollDelta.y;
#endif
            int shortcutCount = _menu == Menu.Dialogue ? Mathf.Min(_questions.Count, 9) : 0;
            for (int i = 0; i < shortcutCount; i++)
            {
#if ENABLE_INPUT_SYSTEM
                bool pressed = keyboard != null && (keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame ||
                    keyboard[(Key)((int)Key.Numpad1 + i)].wasPressedThisFrame);
#else
                bool pressed = Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)) ||
                    Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + i));
#endif
                if (pressed)
                {
                    SelectDialogueOption(i + 1);
                    return;
                }
            }

            int direction = up ? -1 : down ? 1 : 0;
            if (direction == 0 && wheel != 0f && Time.unscaledTime >= _nextDialogueScrollAt)
            {
                direction = wheel > 0f ? -1 : 1;
                _nextDialogueScrollAt = Time.unscaledTime + 0.12f;
            }
            if (direction != 0)
                _questionsView.Move(direction);

            if (confirm)
                SelectDialogueOption(_questionsView.SelectedOption);
        }

        void SelectDialogueOption(int option)
        {
            bool focused = _menu == Menu.Dialogue;
            if (_dialogueNpc == null || !_dialogueNpc.CanTalk ||
                (!focused && (!CanStartNpcConversation || _dialogueNpc != _nearbyNpc)))
                return;

            if (option < 1 || option > _questions.Count)
                return;

            var npc = _dialogueNpc;
            var tour = npc.GetComponent<CaretakerTour>();
            var deni = npc.GetComponent<DeniDialogue>();
            bool lastOption = option == _questions.Count;
            if (focused)
                SetMenu(Menu.None);
            else
            {
                _closedFrame = Time.frameCount;
                HideTalkHint();
            }

            if (deni != null)
            {
                if (lastOption)
                {
                    _dismissedDialogueNpc = npc;
                    return;
                }
                switch (_deniQuestions[option - 1])
                {
                    case DeniQuestion.AskAboutKey:
                        if (deni.TryAskAboutKey(_hud, _hasLowerFloorHint, RefreshQuestDisplay))
                            _activeDeniDialogue = deni;
                        break;
                    case DeniQuestion.CheckFlashlight:
                        if (deni.TryRepairFlashlight(_hud, _health))
                            _activeDeniDialogue = deni;
                        break;
                    case DeniQuestion.ContinueReturn:
                        if ((npc.transform.position - transform.position).sqrMagnitude <=
                            npc.InteractionDistance * npc.InteractionDistance)
                            StartDeniReturn(deni);
                        break;
                    case DeniQuestion.Talk:
                        if (!deni.IsCarryingQuestTools(_inventory) && deni.TryTalk(_hud, () => AcceptDeniQuest(deni)))
                            _activeDeniDialogue = deni;
                        break;
                }
            }
            else if (tour != null)
            {
                if (lastOption)
                {
                    _dismissedDialogueNpc = npc;
                    return;
                }

                int questionId = _caretakerQuestionIds[option - 1];
                bool started = questionId == CaretakerTour.IntroductionQuestion
                    ? tour.TryStart(transform, _hud)
                    : tour.TryStartFollowUp(questionId, transform, _hud);
                if (started)
                    _activeTour = tour;
            }
            else if (npc.HasAnswer)
            {
                if (option == 1 && npc.TryAnswer(_hud, npc.GrantsLowerFloorHint ? LearnLowerFloorHint : null))
                    _answeringNpc = npc;
                else
                    _dismissedDialogueNpc = npc;
            }
            else if (option <= 2)
                _hud.ShowSubtitle(option == 1 ? npc.FirstQuestion : npc.SecondQuestion);
            else
                _dismissedDialogueNpc = npc;
        }

        void LateUpdate()
        {
            if (_menu == Menu.SlowTime)
            {
                _slowTimeTutorial.FitToScreen();
                return;
            }

            if (HasTalkTarget && _inventory != null && _inventory.IsOpen)
                HideTalkHint();

            _questionsView?.UpdateLayout(_hud.HudRect);

            if (_skipDialogueHint != null)
                _skipDialogueHint.gameObject.SetActive(_menu == Menu.None &&
                    (_skipEncounterDialogue != null || IsNpcSpeaking) &&
                    (_inventory == null || !_inventory.IsOpen));

            if (_overlay == null)
                return;

            _trackedQuest.gameObject.SetActive(_menu != Menu.Dying && _menu != Menu.Death && _menu != Menu.Quests &&
                (HasTrackedQuest || HasKeyObjective) &&
                (_inventory == null || !_inventory.IsOpen));
            UpdateLayout();
        }

        public void ShowSlowTimeTutorial()
        {
            if (!isActiveAndEnabled || BlocksGameplayInput || _hud.HudRect == null || _health.IsDead ||
                Time.timeScale <= 0f || (_inventory != null && _inventory.IsOpen))
                return;

            OpenSlowTime();
        }

        internal void LockEncounterInput(Vector3 facingPoint, System.Action skipDialogue)
        {
            _encounterInputLocked = true;
            _skipEncounterDialogue = skipDialogue;
            _inventory?.CloseForMenu();
            _controller?.BeginEncounterLook(facingPoint);
            EnsureSkipDialogueHint();
            _skipDialogueHint.text = "M — перейти к выбору";
            HideTalkHint();
        }

        internal void UnlockEncounterInput()
        {
            if (!_encounterInputLocked)
                return;

            _encounterInputLocked = false;
            _encounterCinematic = false;
            _skipEncounterDialogue = null;
            _controller?.EndEncounterLook();
            if (_skipDialogueHint != null)
                _skipDialogueHint.text = "M — пропустить диалог";
            if (_menu != Menu.SlowTime)
                _controller?.SetControlsFrozen(false);
        }

        internal bool ShowEncounterChoice(System.Action<int> selectChoice)
        {
            if (_menu != Menu.None || _health.IsDead || !OpenSlowTime())
                return false;

            _selectEncounterChoice = selectChoice;
            _slowTimeTutorial.ShowEncounterChoice(SelectEncounterChoice);
            if (_inputs != null)
                _inputs.cursorLocked = false;
            ShowCursor();
            return true;
        }

        internal void UpdateEncounterTimer(int seconds) => _slowTimeTutorial.SetEncounterTimer(seconds);

        internal void ShowEncounterChoiceHint(string text) => _slowTimeTutorial.SetChoiceHint(text);

        internal void BeginEncounterCinematic(System.Action skipDialogue = null)
        {
            _encounterCinematic = true;
            _skipEncounterDialogue = skipDialogue;
            if (_skipDialogueHint != null && skipDialogue != null)
                _skipDialogueHint.text = "M — пропустить диалог";
            SetMenu(Menu.None);
        }

        internal bool ShowEncounterCombatTutorial(System.Action resumeFight)
        {
            if (_menu != Menu.SlowTime && (_menu != Menu.None || !OpenSlowTime()))
                return false;

            _selectEncounterChoice = null;
            _resumeEncounterFight = resumeFight;
            _slowTimeTutorial.ShowCombatTutorial();
            if (_inputs != null)
                _inputs.cursorLocked = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            return true;
        }

        internal void CloseEncounterOverlay()
        {
            if ((_menu == Menu.SlowTime || _slowTimePaused) && _encounterInputLocked)
                SetMenu(Menu.None);
        }

        void SelectEncounterChoice(int choice)
        {
            if (_menu != Menu.SlowTime || _selectEncounterChoice == null)
                return;
            _selectEncounterChoice.Invoke(choice);
        }

        static int ReadEncounterChoice()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return 0;
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) return 1;
            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) return 2;
            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) return 3;
#else
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) return 1;
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) return 2;
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) return 3;
#endif
            return 0;
        }

        bool OpenSlowTime()
        {
            Camera camera = _pistol != null ? _pistol.ViewCamera : null;
            if (camera == null)
                camera = Camera.main;
            if (camera == null)
                return false;

            _slowTimeTutorial ??= new SlowTimeTutorialView(_hud.HudRect);
            SetMenu(Menu.SlowTime);
            _slowTimeTutorial.Open(camera);
            return true;
        }

        internal bool ShowClue(ClueNote note, System.Action onDismissed = null)
        {
            if (!isActiveAndEnabled || note == null || note.Paper == null || BlocksGameplayInput || _health.IsDead)
                return false;

            StartCoroutine(OpenClue(note.Paper, onDismissed));
            return true;
        }

        System.Collections.IEnumerator OpenClue(Sprite image, System.Action onDismissed)
        {
            _openingClue = true;
            _inventory?.CloseForMenu();
            HideTalkHint();
            yield return new WaitForEndOfFrame();
            if (!_openingClue)
                yield break;
            _openingClue = false;
            if (!isActiveAndEnabled || _menu != Menu.None || _health.IsDead)
                yield break;
            var capture = ScreenCapture.CaptureScreenshotAsTexture();
            if (_overlay == null)
                BuildMenus();
            _noteReader ??= new NoteReaderView(_overlay);
            _noteReader.Open(image, _clueBlur, capture);
            _onClueDismissed = onDismissed;
            SetMenu(Menu.Clue);
        }

        static bool IsPauseMenu(Menu menu) => menu is Menu.Pause or Menu.Settings;

        void ContinueFromPause() => SetMenu(_slowTimePaused ? Menu.SlowTime : Menu.None);

        void SetMenu(Menu menu)
        {
            if (_menu == menu)
                return;

            bool willPause = IsPauseMenu(menu);
            if (willPause && !IsPaused)
                _pauseStartedAt = Time.unscaledTime;
            else if (!willPause && IsPaused)
                _totalPausedTime += Time.unscaledTime - _pauseStartedAt;

            if (menu == Menu.Dialogue)
                ClearTalkHint();
            else
                HideTalkHint();
            bool resumingSlowTime = _slowTimePaused && menu == Menu.SlowTime;
            if (_menu == Menu.SlowTime && menu == Menu.Pause)
            {
                _slowTimePaused = true;
                _slowTimeTutorial.SetSuspended(true);
            }
            else if (_menu == Menu.SlowTime || (_slowTimePaused && !willPause && !resumingSlowTime))
            {
                _slowTimePaused = false;
                _slowTimeTutorial.Close();
                _selectEncounterChoice = null;
                _resumeEncounterFight = null;
                Time.fixedDeltaTime = _previousFixedDeltaTime;
                _controller?.SetControlsFrozen(false);
                if (_inputs != null)
                    _inputs.cursorLocked = false;
            }
            else if (resumingSlowTime)
            {
                _slowTimePaused = false;
                _slowTimeTutorial.SetSuspended(false);
            }
            if (_menu == Menu.Clue && menu != Menu.Clue)
            {
                _noteReader?.Close();
                _onClueDismissed = null;
            }
            if (_menu == Menu.Settings && _settingsChanged)
            {
                PlayerPrefs.SetInt(MouseSensitivityPref, _sensitivityPercent);
                PlayerPrefs.SetInt(MasterVolumePref, _masterVolumePercent);
                PlayerPrefs.Save();
                _settingsChanged = false;
            }

            if (menu == Menu.None)
            {
                _menu = Menu.None;
                _closedFrame = Time.frameCount;
                if (_overlay != null)
                    _overlay.gameObject.SetActive(false);
                Time.timeScale = _previousTimeScale;
                AudioListener.pause = _previousAudioPause;
                if (_inputs != null)
                {
                    _inputs.cursorLocked = _previousInputCursorLock;
                    _inputs.LookInput(Vector2.zero);
                }
                Cursor.lockState = _previousCursorLock;
                Cursor.visible = _previousCursorVisible;
                return;
            }

            if (_overlay == null)
                BuildMenus();
            if (menu == Menu.Pause && _pauseMenu == null)
                _pauseMenu = new PauseMenuView(_overlay, _typewriterText ??= new TypewriterMenuText(),
                    ContinueFromPause, () => SetMenu(Menu.Settings), QuitGame);
            if (menu == Menu.Settings && _settingsMenu == null)
                _settingsMenu = new SettingsMenuView(_overlay,
                    MinSensitivity, MaxSensitivity, _sensitivityPercent, _masterVolumePercent,
                    ChangeSensitivity, ChangeMasterVolume, () => SetMenu(Menu.Pause));
            if (menu == Menu.Quests && _questJournal == null)
            {
                var questFont = _hud.QuestFont;
                var journalText = questFont != null ? new TypewriterMenuText(questFont) :
                    (_typewriterText ??= new TypewriterMenuText());
                _questJournal = new QuestJournalView(_overlay, journalText, QuestTitle, QuestDescription,
                    TrackQuest, TrackDeniQuest);
            }
            if (menu == Menu.Quests)
                RefreshQuestDisplay();

            if (_menu == Menu.None)
            {
                _inventory?.CloseForMenu();
                _previousTimeScale = Time.timeScale;
                _previousAudioPause = AudioListener.pause;
                _previousCursorLock = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                if (_inputs != null)
                {
                    _previousInputCursorLock = _inputs.cursorLocked;
                    _inputs.cursorLocked = false;
                    _inputs.LookInput(Vector2.zero);
                }
            }

            Time.timeScale = menu == Menu.SlowTime ? _previousTimeScale * SlowTimeScale : 0f;
            if (menu == Menu.SlowTime)
            {
                if (!resumingSlowTime)
                {
                    _previousFixedDeltaTime = Time.fixedDeltaTime;
                    Time.fixedDeltaTime *= SlowTimePhysicsStepScale;
                    _controller?.SetControlsFrozen(true);
                }
                if (_inputs != null)
                    _inputs.cursorLocked = _selectEncounterChoice == null;
            }

            // Keep audio running during the fall so breathing and heartbeat can fade out.
            AudioListener.pause = menu == Menu.Dying ? _previousAudioPause : true;
            if (menu == Menu.Dying || (menu == Menu.SlowTime && _selectEncounterChoice == null))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
                ShowCursor();

            _menu = menu;
            if (_pauseMenu != null)
                _pauseMenu.Root.gameObject.SetActive(menu == Menu.Pause);
            if (_settingsMenu != null)
                _settingsMenu.Root.gameObject.SetActive(menu == Menu.Settings);
            if (_questJournal != null)
                _questJournal.Root.gameObject.SetActive(menu == Menu.Quests);
            _deathPanel.gameObject.SetActive(menu == Menu.Death);
            _overlay.SetAsLastSibling();
            _overlay.gameObject.SetActive(menu != Menu.Dying && menu != Menu.SlowTime && menu != Menu.Dialogue);
            if (menu == Menu.Dialogue)
                _questionsView.SetFocused(true);
            UpdateLayout();
            if (menu == Menu.Pause)
                _pauseMenu.FocusContinue();
            else if (menu == Menu.Settings)
                _settingsMenu.FocusFirst();
        }

        void BuildMenus()
        {
            var hud = _hud.HudRect;
            if (!hud.TryGetComponent<UnityEngine.UI.GraphicRaycaster>(out _))
                hud.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            if (EventSystem.current == null && FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("Player Menu Event System", typeof(EventSystem));
                events.transform.SetParent(hud, false);
#if ENABLE_INPUT_SYSTEM
                events.AddComponent<InputSystemUIInputModule>();
#else
                events.AddComponent<StandaloneInputModule>();
#endif
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var questFont = _hud.QuestFont ?? font;
            _trackedQuest = MakeText(hud, questFont, "Tracked Quest", $"{QuestTitle}\n{QuestDescription}", 18,
                Vector2.zero, new Vector2(300f, 84f));
            _trackedQuest.alignment = TextAnchor.UpperRight;
            _trackedQuest.rectTransform.pivot = Vector2.one;
            _trackedQuest.gameObject.SetActive(_questTracked);
            EnsureSkipDialogueHint();

            _overlay = MakePanel(hud, "Menu Overlay", new Color32(0, 0, 0, 160));
            _overlay.anchorMin = Vector2.zero;
            _overlay.anchorMax = Vector2.one;
            _overlay.offsetMin = _overlay.offsetMax = Vector2.zero;

            _deathPanel = MakePanel(_overlay, "Death Menu", new Color32(30, 33, 35, 255));
            _deathPanel.sizeDelta = new Vector2(400f, 230f);
            var deathTitle = MakeText(_deathPanel, font, "Death Title", "Вы погибли", 32,
                new Vector2(0f, 70f), new Vector2(320f, 42f));
            deathTitle.color = new Color32(244, 120, 110, 255);
            MakeButton(_deathPanel, font, "Начать заново", 6f, RestartScene);
            MakeButton(_deathPanel, font, "Выйти из игры", -53f, QuitGame);

            _overlay.gameObject.SetActive(false);
        }

        void EnsureSkipDialogueHint()
        {
            if (_skipDialogueHint != null)
                return;

            Font font = _hud.QuestFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _skipDialogueHint = MakeText(_hud.HudRect, font, "Skip Dialogue Hint", "M — пропустить диалог", 16,
                new Vector2(0f, 18f), new Vector2(280f, 24f));
            var rect = _skipDialogueHint.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            _skipDialogueHint.gameObject.SetActive(false);
        }

        void ChangeSensitivity(int value)
        {
            _sensitivityPercent = value;
            _settingsChanged = true;
            ApplySensitivity();
        }

        void ChangeMasterVolume(int value)
        {
            _masterVolumePercent = value;
            _settingsChanged = true;
            ApplyMasterVolume();
        }

        void ApplySensitivity()
        {
            if (_controller != null)
                _controller.MouseSensitivityScale = _sensitivityPercent / 100f;
        }

        void ApplyMasterVolume()
        {
            AudioListener.volume = _masterVolumePercent / 100f;
        }

        void TrackQuest()
        {
            _questTracked = true;
            _trackingDeniQuest = false;
            RefreshQuestDisplay();
        }

        void AcceptDeniQuest(DeniDialogue deni)
        {
            _deniQuest = deni;
            TrackDeniQuest();
        }

        void LearnLowerFloorHint()
        {
            _hasLowerFloorHint = true;
            RefreshQuestDisplay();
        }

        internal void RegisterWorkshop(WorkshopEncounter workshop) => _workshop = workshop;

        internal void TrackWorkshopObjective(DeniDialogue deni)
        {
            if (_deniQuest == deni)
                TrackDeniQuest();
        }

        void TrackDeniQuest()
        {
            if (_deniQuest == null)
                return;
            _questTracked = true;
            _trackingDeniQuest = true;
            RefreshQuestDisplay();
        }

        internal void RefreshQuestDisplay()
        {
            bool completed = _deniQuest != null && _deniQuest.QuestCompleted;
            bool carrying = _inventory != null && _inventory.IsCarryingTools;
            bool inWorkshopQuest = _deniQuest != null && _workshop != null && _deniQuest == _workshop.QuestGiver;
            bool returning = _deniQuest != null && _deniQuest.ToolsReturned && !completed;
            bool keyReady = _deniQuest != null && _deniQuest.KeyReady;
            bool needsHint = _deniQuest != null && _deniQuest.NeedsLowerFloorHint(_hasLowerFloorHint);
            string keyObjective = needsHint ? "Поговорить с Виктором" : FindKeyObjective;
            string keyDirections = needsHint ? "Спроси Виктора, как попасть на второй этаж." : FindKeyHint;
            bool deliverToWorkshop = _deniQuest != null && _deniQuest.ReturnsToolsInWorkshop;
            string deliveryDirections = deliverToWorkshop
                ? "Отнеси ящик Дени в мастерскую. От зала с койками сверни в правый тоннель и найди боковую комнату."
                : "Отнеси ящик Дени.";
            string description = completed ? "Инструменты возвращены Дени. Получен ключ от второго этажа."
                : keyReady ? "Подойди к Дени и нажми E, чтобы забрать ключ."
                : returning ? keyDirections
                : carrying ? deliveryDirections
                : "Найди красный ящик с инструментами. Ориентир — дверь с белой полосой.";
            string objective = keyReady ? "Забрать ключ у Дени" : returning ? keyObjective :
                carrying ? (deliverToWorkshop ? "Отнести ящик в мастерскую Дени" : "Вернуть ящик") : "Найти ящик";
            if (inWorkshopQuest && !completed && !carrying && !returning)
            {
                if (_workshop.NeedsIntimidation)
                {
                    objective = "Наведи пистолет на вымогателя";
                    description = "Достань пистолет и наведи его на вымогателя, удерживая ПКМ.";
                }
                else if (_workshop.HasIntimidated)
                {
                    objective = "Забрать ящик";
                    description = "Забери ящик с инструментами у вымогателя.";
                }
                else
                {
                    description = string.IsNullOrWhiteSpace(_deniQuest.WorkshopDirections)
                        ? "Найди ящик в первом тоннеле от жилого зала."
                        : _deniQuest.WorkshopDirections;
                }
            }
            _questJournal?.SetQuests(_questTracked && !_trackingDeniQuest, _deniQuest != null,
                _trackingDeniQuest, completed, DeniQuestTitle, description);

            if (_trackedQuest == null)
                return;

            bool aimingAtExtortionist = inWorkshopQuest && !completed && _workshop.NeedsIntimidation;
            string trackedText = _trackingDeniQuest
                ? "Инструменты Дени\n" + objective + (aimingAtExtortionist ? "\nУдерживай ПКМ" : "")
                : $"{QuestTitle}\n{QuestDescription}";
            if (HasKeyObjective)
            {
                string keyText = keyReady ? "Забрать ключ у Дени\nПодойди к нему и нажми E." : $"{FindKeyObjective}\n{FindKeyHint}";
                bool showOtherObjective = HasTrackedQuest && (!_trackingDeniQuest || !returning);
                trackedText = showOtherObjective ? $"{keyText}\n\n{trackedText}" : keyText;
            }

            _trackedQuest.text = trackedText;
            _trackedQuest.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(84f, _trackedQuest.preferredHeight));
        }

        void UpdateLayout()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return;

            Rect safe = Screen.safeArea;
            var display = new Vector2(Screen.width, Screen.height);
            Vector2 min = safe.min / display;
            Vector2 max = safe.max / display;
            Vector2 size = Vector2.Scale(max - min, _hud.HudRect.rect.size);
            Vector2 center = (min + max) * 0.5f;
            if (_menu == Menu.Settings)
                _settingsMenu.Fit(center, size);
            if (_menu == Menu.Pause)
                _pauseMenu.Fit(min, center, size);
            if (_menu == Menu.Quests)
                _questJournal.Fit(center, size);
            _deathPanel.anchorMin = _deathPanel.anchorMax = center;
            float deathScale = Mathf.Clamp01(Mathf.Min(size.x / (_deathPanel.sizeDelta.x + 32f),
                size.y / (_deathPanel.sizeDelta.y + 32f)));
            _deathPanel.localScale = new Vector3(deathScale, deathScale, 1f);

            var tracker = _trackedQuest.rectTransform;
            tracker.anchorMin = tracker.anchorMax = max;
            float trackerScale = Mathf.Clamp01(size.x / 348f);
            tracker.localScale = new Vector3(trackerScale, trackerScale, 1f);
            tracker.anchoredPosition = new Vector2(-24f, -18f) * trackerScale;
        }

        static RectTransform MakePanel(RectTransform parent, string name, Color color)
        {
            var rect = PlayerStatusHud.CreateRect(parent, name, typeof(UnityEngine.UI.Image));
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.GetComponent<UnityEngine.UI.Image>().color = color;
            return rect;
        }

        static UnityEngine.UI.Text MakeText(RectTransform parent, Font font, string name, string text, int size,
            Vector2 position, Vector2 dimensions)
        {
            var label = PlayerStatusHud.CreateLabel(parent, name, font);
            label.text = text;
            label.fontSize = size;
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot =
                new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = dimensions;
            return label;
        }

        static UnityEngine.UI.Text MakeButton(RectTransform parent, Font font, string text, float y, UnityAction action)
        {
            var rect = MakePanel(parent, text, Color.white);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(280f, 46f);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = rect.GetComponent<UnityEngine.UI.Image>();
            var colors = button.colors;
            colors.normalColor = new Color32(57, 61, 65, 255);
            colors.highlightedColor = new Color32(79, 84, 89, 255);
            colors.pressedColor = new Color32(43, 46, 49, 255);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.onClick.AddListener(action);
            return MakeText(rect, font, "Label", text, 18, Vector2.zero, rect.sizeDelta);
        }

        void RestartScene()
        {
            if (_restartingScene || _menu != Menu.Death)
                return;

            var scene = gameObject.scene;
            AsyncOperation load;
#if UNITY_EDITOR
            if (scene.buildIndex < 0)
                load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scene.path,
                    new LoadSceneParameters(LoadSceneMode.Single));
            else
#endif
                load = SceneManager.LoadSceneAsync(scene.buildIndex, LoadSceneMode.Single);
            _restartingScene = load != null;
        }

        static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static void ShowCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused && _menu != Menu.None && _menu != Menu.Dying &&
                (_menu != Menu.SlowTime || _selectEncounterChoice != null))
                ShowCursor();
        }

        void OnDisable()
        {
            _openingClue = false;
            if (_encounterInputLocked)
                UnlockEncounterInput();
            _activeTour?.Cancel();
            _activeDeniDialogue?.Cancel();
            _answeringNpc?.CancelAnswer();
            _workshop?.CancelConversation();
            if (_health != null)
                _health.HealthChanged -= OnHealthChanged;
            SetMenu(Menu.None);
            HideTalkHint();
            if (_trackedQuest != null)
                _trackedQuest.gameObject.SetActive(false);
            if (_skipDialogueHint != null)
                _skipDialogueHint.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            _questionsView?.Destroy();
            _slowTimeTutorial?.Destroy();
            _typewriterText?.ReleaseFont();
            if (_talkHint != null)
                Destroy(_talkHint.gameObject);
            if (_overlay != null)
                Destroy(_overlay.gameObject);
            if (_trackedQuest != null)
                Destroy(_trackedQuest.gameObject);
            if (_skipDialogueHint != null)
                Destroy(_skipDialogueHint.gameObject);
        }

        void HideTalkHint()
        {
            ClearTalkHint();
            HideDialogueQuestions();
        }

        void HideDialogueQuestions(bool immediate = true)
        {
            _dialogueNpc = null;
            _questionsView?.Hide(immediate);
        }

        void ClearTalkHint()
        {
            _nearbyNpc = null;
            _clueInView = false;
            if (_talkHint != null)
                _talkHint.gameObject.SetActive(false);
        }
    }
}
