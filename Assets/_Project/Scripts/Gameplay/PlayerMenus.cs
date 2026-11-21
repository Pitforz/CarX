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
        const string MouseSensitivityPref = "CarX.MouseSensitivityPercent";
        const string MasterVolumePref = "CarX.MasterVolumePercent";
        const int DefaultSensitivity = 100;
        const int MinSensitivity = 25;
        const int MaxSensitivity = 200;
        const int DefaultMasterVolume = 60;
        const float DialogueOptionSpacing = 59f;
        const float SlowTimeScale = 0.02f;
        const float SlowTimePhysicsStepScale = 0.1f;
        static readonly Vector2 PanelSize = new(400f, 330f);

        enum Menu { None, Pause, Settings, Quests, Dying, Death, Dialogue, Clue, SlowTime }

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
        RectTransform _dialoguePanel;
        UnityEngine.UI.Text _talkHint;
        UnityEngine.UI.Text _skipDialogueHint;
        UnityEngine.UI.Text _dialogueTitle;
        readonly List<UnityEngine.UI.Text> _dialogueOptionLabels = new();
        int _dialogueOptionCount;
        NpcDialogue _nearbyNpc;
        NpcDialogue _dialogueNpc;
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

        internal bool BlocksGameplayInput => _encounterInputLocked || _openingClue ||
            _menu != Menu.None || _closedFrame == Time.frameCount;
        internal bool AllowsEncounterWeaponInput => _encounterInputLocked && !_encounterCinematic && _menu == Menu.None;
        internal bool IsPaused => IsPauseMenu(_menu);
        internal float UnpausedTime => (IsPaused ? _pauseStartedAt : Time.unscaledTime) - _totalPausedTime;
        internal bool HasTalkTarget => _nearbyNpc != null || _clueInView;
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
            _answeringNpc?.SkipAnswer();
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
                    SetMenu(Menu.None);
                else
                    UpdateDialogueSelection();
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

                if (!HandleDeniReturn(interact, skipDialogue))
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

        bool HandleDeniReturn(bool interact, bool skipDialogue)
        {
            if (!CanStartNpcConversation || _deniQuest == null || !_deniQuest.CanReturnTools(_inventory) ||
                _pistol == null || _pistol.ViewCamera == null)
                return false;

            var npc = _deniQuest.GetComponent<NpcDialogue>();
            var collider = _deniQuest.GetComponent<Collider>();
            if (npc == null || !npc.CanTalk || collider == null || !collider.enabled ||
                npc.gameObject.scene != gameObject.scene)
                return false;

            Vector3 center = transform.position + Vector3.up * 0.9f;
            if ((collider.ClosestPoint(center) - center).sqrMagnitude > npc.InteractionDistance * npc.InteractionDistance)
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

            if (!_deniQuest.HasGreetedForTools)
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

        void UpdateTalkTarget(bool interact)
        {
            bool canInteract = CanStartNpcConversation;
            var npc = canInteract ? FindLookedAtNpc() : null;
            _clueInView = canInteract && _workshop != null && _workshop.CanTakeClue();
            _nearbyNpc = npc;

            ShowTalkHint(!HasTalkTarget ? null : _clueInView ? "E — подобрать улику" : "E — поговорить");

            if (interact && _clueInView)
            {
                _workshop.TakeClue();
                return;
            }

            if (!interact || npc == null)
                return;

            _dialogueNpc = npc;
            SetMenu(Menu.Dialogue);
            ConfigureDialogue(npc);
            UpdateLayout();
        }

        void ShowTalkHint(string text)
        {
            if (text != null && _talkHint == null)
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

        void ConfigureDialogue(NpcDialogue npc)
        {
            _dialogueTitle.text = npc.DisplayName;
            var tour = npc.GetComponent<CaretakerTour>();
            var deni = npc.GetComponent<DeniDialogue>();
            int followUpCount = tour != null ? tour.FollowUpCount : 0;
            int lastQuestion = 2 + followUpCount;
            bool offerRepair = deni != null && deni.QuestCompleted && _health.CanRepairFlashlight;
            if (deni != null)
            {
                bool hasQuestion = !deni.ToolsReturned || deni.HasReturnDialogueRemaining ||
                    deni.CanAskAboutKey || offerRepair;
                _dialogueOptionCount = hasQuestion ? 2 : 1;
            }
            else if (tour != null)
                _dialogueOptionCount = lastQuestion;
            else
                _dialogueOptionCount = lastQuestion + (string.IsNullOrEmpty(npc.CloseOption) ? 0 : 1);
            float extraHeight = Mathf.Max(0, _dialogueOptionCount - 3) * DialogueOptionSpacing;
            float lift = extraHeight * 0.5f;
            float buttonWidth = followUpCount > 0 ? 390f : 280f;
            _dialoguePanel.sizeDelta = new Vector2(followUpCount > 0 ? 450f : PanelSize.x, PanelSize.y + extraHeight);
            _dialogueTitle.rectTransform.anchoredPosition = new Vector2(0f, 117f + lift);

            while (_dialogueOptionLabels.Count < _dialogueOptionCount)
            {
                int option = _dialogueOptionLabels.Count + 1;
                _dialogueOptionLabels.Add(MakeButton(_dialoguePanel, _dialogueTitle.font, $"Option {option}", 0f,
                    () => SelectDialogueOption(option)));
            }

            for (int i = 0; i < _dialogueOptionLabels.Count; i++)
            {
                var label = _dialogueOptionLabels[i];
                var button = (RectTransform)label.transform.parent;
                int option = i + 1;
                bool visible = option <= _dialogueOptionCount;
                button.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                string text;
                if (deni != null)
                {
                    if (option == _dialogueOptionCount)
                        text = "Уйти";
                    else if (deni.CanAskAboutKey)
                        text = "Спросить про ключ";
                    else if (offerRepair)
                        text = "Проверишь свет?";
                    else if (deni.ToolsReturned)
                        text = "Продолжить разговор";
                    else if (deni.HasQuest)
                        text = deni.CanReturnTools(_inventory) ? "Отдать ящик" : "Где мастерская?";
                    else
                        text = npc.FirstQuestion;
                }
                else if (option == 1)
                    text = npc.FirstQuestion;
                else if (tour != null)
                    text = option < lastQuestion ? tour.GetFollowUpQuestion(option - 2) : "Уйти";
                else
                    text = option == lastQuestion ? npc.SecondQuestion : npc.CloseOption;

                button.anchoredPosition = new Vector2(0f, 54f + lift - i * DialogueOptionSpacing);
                button.sizeDelta = new Vector2(buttonWidth, 46f);
                label.rectTransform.sizeDelta = new Vector2(buttonWidth - 12f, 46f);
                label.text = $"{option}. {text}";
            }
        }

        NpcDialogue FindLookedAtNpc()
        {
            Camera camera = _pistol != null ? _pistol.ViewCamera : null;
            if (camera == null)
                return null;

            var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int layers = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);
            if (!Physics.Raycast(ray, out var hit, camera.farClipPlane, layers, QueryTriggerInteraction.Ignore))
                return null;

            var npc = hit.collider.GetComponentInParent<NpcDialogue>();
            if (npc == null || npc.gameObject.scene != gameObject.scene || !npc.CanTalk)
                return null;

            Vector3 playerCenter = transform.position + Vector3.up * 0.9f;
            return (hit.point - playerCenter).sqrMagnitude <= npc.InteractionDistance * npc.InteractionDistance
                ? npc : null;
        }

        void UpdateDialogueSelection()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
#endif
            int shortcutCount = Mathf.Min(_dialogueOptionCount, 9);
            for (int i = 0; i < shortcutCount; i++)
            {
#if ENABLE_INPUT_SYSTEM
                bool pressed = keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame ||
                    keyboard[(Key)((int)Key.Numpad1 + i)].wasPressedThisFrame;
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
        }

        void SelectDialogueOption(int option)
        {
            if (_menu != Menu.Dialogue || _dialogueNpc == null || !_dialogueNpc.CanTalk)
                return;

            if (option < 1 || option > _dialogueOptionCount)
                return;

            var npc = _dialogueNpc;
            var tour = npc.GetComponent<CaretakerTour>();
            var deni = npc.GetComponent<DeniDialogue>();
            SetMenu(Menu.None);
            if (deni != null)
            {
                if (option == _dialogueOptionCount)
                    return;
                if (deni.CanAskAboutKey)
                {
                    if (deni.TryAskAboutKey(_hud, RefreshQuestDisplay))
                        _activeDeniDialogue = deni;
                }
                else if (deni.QuestCompleted)
                {
                    if (deni.TryRepairFlashlight(_hud, _health))
                        _activeDeniDialogue = deni;
                }
                else if (StartDeniReturn(deni))
                    return;
                else if (deni.TryTalk(_hud, () => AcceptDeniQuest(deni)))
                    _activeDeniDialogue = deni;
            }
            else if (tour != null)
            {
                if (option == _dialogueOptionCount)
                    return;

                bool started = option == 1
                    ? tour.TryStart(transform, _hud)
                    : tour.TryStartFollowUp(option - 2, transform, _hud);
                if (started)
                    _activeTour = tour;
            }
            else if (npc.HasAnswer)
            {
                if (option == 1 && npc.TryAnswer(_hud))
                    _answeringNpc = npc;
            }
            else if (option <= 2)
                _hud.ShowSubtitle(option == 1 ? npc.FirstQuestion : npc.SecondQuestion);
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

            if (_skipDialogueHint != null)
                _skipDialogueHint.gameObject.SetActive(_menu == Menu.None &&
                    (_skipEncounterDialogue != null || IsNpcSpeaking) &&
                    (_inventory == null || !_inventory.IsOpen));

            if (_overlay == null)
                return;

            _trackedQuest.gameObject.SetActive(_menu != Menu.Dying && _menu != Menu.Death && _menu != Menu.Quests &&
                _questTracked && (!_trackingDeniQuest || (_deniQuest != null && !_deniQuest.QuestCompleted)) &&
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
            if (menu != Menu.Dialogue)
                _dialogueNpc = null;

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
                _questJournal = new QuestJournalView(_overlay, _typewriterText ??= new TypewriterMenuText(),
                    QuestTitle, QuestDescription, TrackQuest, TrackDeniQuest);
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
            _dialoguePanel.gameObject.SetActive(menu == Menu.Dialogue);
            _overlay.SetAsLastSibling();
            _overlay.gameObject.SetActive(menu != Menu.Dying && menu != Menu.SlowTime);
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
            _trackedQuest = MakeText(hud, font, "Tracked Quest", $"{QuestTitle}\n{QuestDescription}", 18,
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
            _deathPanel.sizeDelta = new Vector2(PanelSize.x, 230f);
            var deathTitle = MakeText(_deathPanel, font, "Death Title", "Вы погибли", 32,
                new Vector2(0f, 70f), new Vector2(320f, 42f));
            deathTitle.color = new Color32(244, 120, 110, 255);
            MakeButton(_deathPanel, font, "Начать заново", 6f, RestartScene);
            MakeButton(_deathPanel, font, "Выйти из игры", -53f, QuitGame);

            _dialoguePanel = MakePanel(_overlay, "NPC Dialogue", new Color32(30, 33, 35, 255));
            _dialoguePanel.sizeDelta = PanelSize;
            _dialogueTitle = MakeText(_dialoguePanel, font, "NPC Name", "", 25,
                new Vector2(0f, 117f), new Vector2(320f, 42f));
        }

        void EnsureSkipDialogueHint()
        {
            if (_skipDialogueHint != null)
                return;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            string description = completed ? "Инструменты возвращены Дени. Получен ключ от второго этажа."
                : keyReady ? "Ключ от второго этажа лежит на полу рядом с Дени. Посмотри на ключ и нажми E."
                : returning ? "Спроси Дени про ключ от второго этажа."
                : carrying ? "Отнеси ящик Дени."
                : "Найди красный ящик с инструментами. Ориентир — дверь с белой полосой.";
            string objective = keyReady ? "Подобрать ключ рядом с Дени" : returning ? "Спросить Дени про ключ" :
                carrying ? "Вернуть ящик" : "Найти ящик";
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
                    description = "Найди ящик в первом тоннеле от жилого зала.";
                }
            }
            _questJournal?.SetQuests(_questTracked && !_trackingDeniQuest, _deniQuest != null,
                _trackingDeniQuest, completed, DeniQuestTitle, description);
            bool aimingAtExtortionist = inWorkshopQuest && !completed && _workshop.NeedsIntimidation;
            if (_trackedQuest != null)
                _trackedQuest.text = _trackingDeniQuest
                    ? "Инструменты Дени\n" + objective + (aimingAtExtortionist ? "\nУдерживай ПКМ" : "")
                    : $"{QuestTitle}\n{QuestDescription}";
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
            _dialoguePanel.anchorMin = _dialoguePanel.anchorMax = center;
            float dialogueScale = Mathf.Clamp01(Mathf.Min(size.x / (_dialoguePanel.sizeDelta.x + 32f),
                size.y / (_dialoguePanel.sizeDelta.y + 32f)));
            _dialoguePanel.localScale = new Vector3(dialogueScale, dialogueScale, 1f);
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
            _answeringNpc?.SkipAnswer();
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
            _nearbyNpc = null;
            _clueInView = false;
            if (_talkHint != null)
                _talkHint.gameObject.SetActive(false);
        }
    }
}
