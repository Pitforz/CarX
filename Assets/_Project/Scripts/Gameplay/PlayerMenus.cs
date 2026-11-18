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
        const int DefaultSensitivity = 100;
        const int MinSensitivity = 25;
        const int MaxSensitivity = 200;
        const float DialogueOptionSpacing = 59f;
        static readonly Vector2 PanelSize = new(400f, 330f);

        enum Menu { None, Pause, Settings, Quests, Dying, Death, Dialogue }

        PlayerStatusHud _hud;
        PlayerHealth _health;
        PlayerInventory _inventory;
        PlayerPistol _pistol;
        StarterAssetsInputs _inputs;
        FirstPersonController _controller;
        PlayerDeathFall _deathFall;
        RectTransform _overlay;
        TypewriterMenuText _typewriterText;
        PauseMenuView _pauseMenu;
        RectTransform _settingsPanel;
        QuestJournalView _questJournal;
        RectTransform _deathPanel;
        RectTransform _dialoguePanel;
        UnityEngine.UI.Text _talkHint;
        UnityEngine.UI.Text _dialogueTitle;
        readonly List<UnityEngine.UI.Text> _dialogueOptionLabels = new();
        int _dialogueOptionCount;
        NpcDialogue _nearbyNpc;
        NpcDialogue _dialogueNpc;
        CaretakerTour _activeTour;
        DeniDialogue _activeDeniDialogue;
        DeniDialogue _deniQuest;
        WorkshopEncounter _workshop;
        bool _clueInView;
        UnityEngine.UI.Text _sensitivityLabel;
        int _sensitivityPercent;
        bool _sensitivityChanged;
        UnityEngine.UI.Text _trackedQuest;
        bool _questTracked;
        bool _trackingDeniQuest;
        bool _restartingScene;
        Menu _menu;
        float _previousTimeScale;
        bool _previousAudioPause;
        CursorLockMode _previousCursorLock;
        bool _previousCursorVisible;
        bool _previousInputCursorLock;
        int _closedFrame = -1;

        internal bool BlocksGameplayInput => _menu != Menu.None || _closedFrame == Time.frameCount;
        internal bool HasTalkTarget => _nearbyNpc != null || _clueInView;
        internal bool CanStartNpcConversation => _hud.HudRect != null && !_health.IsDead &&
            !BlocksGameplayInput && Time.timeScale > 0f && (_inventory == null || !_inventory.BlocksWeaponInput) &&
            (_activeTour == null || !_activeTour.IsRunning) &&
            (_activeDeniDialogue == null || !_activeDeniDialogue.IsSpeaking) &&
            (_workshop == null || !_workshop.IsSpeaking);

        void Awake()
        {
            _hud = GetComponent<PlayerStatusHud>();
            _health = GetComponent<PlayerHealth>();
            _inventory = GetComponent<PlayerInventory>();
            _pistol = GetComponent<PlayerPistol>();
            _inputs = GetComponent<StarterAssetsInputs>();
            _controller = GetComponent<FirstPersonController>();
            _deathFall = GetComponent<PlayerDeathFall>();
            _sensitivityPercent = Mathf.Clamp(PlayerPrefs.GetInt(MouseSensitivityPref, DefaultSensitivity),
                MinSensitivity, MaxSensitivity);
            ApplySensitivity();
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
#else
            bool escape = Input.GetKeyDown(KeyCode.Escape);
            bool quests = Input.GetKeyDown(KeyCode.T);
            bool interact = Input.GetKeyDown(KeyCode.E);
#endif
            if (_menu == Menu.Dialogue)
            {
                if (escape || _dialogueNpc == null || !_dialogueNpc.CanTalk)
                    SetMenu(Menu.None);
                else
                    UpdateDialogueSelection();
                return;
            }

            if (escape)
            {
                if (_menu == Menu.Settings)
                    SetMenu(Menu.Pause);
                else
                    SetMenu(_menu == Menu.None ? Menu.Pause : Menu.None);
            }
            else if (quests && (_menu == Menu.None || _menu == Menu.Quests))
                SetMenu(_menu == Menu.Quests ? Menu.None : Menu.Quests);
            else if (_menu == Menu.None && Time.timeScale > 0f)
                UpdateTalkTarget(interact);
        }

        void UpdateTalkTarget(bool interact)
        {
            bool canInteract = CanStartNpcConversation;
            var npc = canInteract ? FindLookedAtNpc() : null;
            _clueInView = canInteract && _workshop != null && _workshop.CanTakeClue();
            _nearbyNpc = npc;

            if (HasTalkTarget && _talkHint == null)
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _talkHint = MakeText(_hud.HudRect, font, "Talk Hint", "", 18,
                    new Vector2(0f, -56f), new Vector2(280f, 52f));
            }

            if (_talkHint != null)
            {
                _talkHint.text = _clueInView ? "E — подобрать улику" : "E — поговорить";
                if (_talkHint.gameObject.activeSelf != HasTalkTarget)
                    _talkHint.gameObject.SetActive(HasTalkTarget);
            }

            if (interact && _clueInView)
            {
                _workshop.TakeClue();
                _talkHint.gameObject.SetActive(false);
                // Keep the input claimed until the inventory has processed this frame.
                return;
            }

            if (!interact || npc == null)
                return;

            _dialogueNpc = npc;
            SetMenu(Menu.Dialogue);
            ConfigureDialogue(npc);
            UpdateLayout();
        }

        void ConfigureDialogue(NpcDialogue npc)
        {
            _dialogueTitle.text = npc.DisplayName;
            var tour = npc.GetComponent<CaretakerTour>();
            var deni = npc.GetComponent<DeniDialogue>();
            int followUpCount = tour != null ? tour.FollowUpCount : 0;
            int lastQuestion = 2 + followUpCount;
            bool toolsReturned = deni != null && deni.QuestCompleted;
            bool offerRepair = toolsReturned && _health.CanRepairFlashlight;
            if (toolsReturned)
                _dialogueOptionCount = offerRepair ? 2 : 1;
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
                if (toolsReturned)
                    text = option == 1 && offerRepair ? "Починить фонарик" : npc.SecondQuestion;
                else if (option == 1 && deni != null && deni.HasQuest)
                    text = _inventory.IsCarryingTools ? "Вернуть инструменты" : "Где мастерская?";
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
                if (option != 1)
                    return;
                if (deni.QuestCompleted)
                {
                    if (_dialogueOptionCount == 2 && _health.TryRepairFlashlight())
                    {
                        _hud.RefreshFlashlight();
                        _hud.ShowSubtitle("Дени починил фонарик");
                    }
                }
                else if (deni.HasQuest && _inventory.DeliverTools())
                {
                    deni.CompleteQuest();
                    TrackDeniQuest();
                    _hud.ShowSubtitle("Инструменты возвращены Дени");
                }
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
            else if (option <= 2)
                _hud.ShowSubtitle(option == 1 ? npc.FirstQuestion : npc.SecondQuestion);
        }

        void LateUpdate()
        {
            if (HasTalkTarget && _inventory != null && _inventory.IsOpen)
                HideTalkHint();

            if (_overlay == null)
                return;

            _trackedQuest.gameObject.SetActive(_menu != Menu.Dying && _menu != Menu.Death && _menu != Menu.Quests &&
                _questTracked && (!_trackingDeniQuest || (_deniQuest != null && !_deniQuest.QuestCompleted)) &&
                (_inventory == null || !_inventory.IsOpen));
            UpdateLayout();
        }

        void SetMenu(Menu menu)
        {
            if (_menu == menu)
                return;

            HideTalkHint();
            if (menu != Menu.Dialogue)
                _dialogueNpc = null;

            if (_menu == Menu.Settings && _sensitivityChanged)
            {
                PlayerPrefs.SetInt(MouseSensitivityPref, _sensitivityPercent);
                PlayerPrefs.Save();
                _sensitivityChanged = false;
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
                    () => SetMenu(Menu.None), () => SetMenu(Menu.Settings), QuitGame);
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

                Time.timeScale = 0f;
            }

            // Keep audio running during the fall so breathing and heartbeat can fade out.
            AudioListener.pause = menu == Menu.Dying ? _previousAudioPause : true;
            if (menu == Menu.Dying)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
                ShowCursor();

            _menu = menu;
            if (_pauseMenu != null)
                _pauseMenu.Root.gameObject.SetActive(menu == Menu.Pause);
            _settingsPanel.gameObject.SetActive(menu == Menu.Settings);
            if (_questJournal != null)
                _questJournal.Root.gameObject.SetActive(menu == Menu.Quests);
            _deathPanel.gameObject.SetActive(menu == Menu.Death);
            _dialoguePanel.gameObject.SetActive(menu == Menu.Dialogue);
            _overlay.SetAsLastSibling();
            _overlay.gameObject.SetActive(menu != Menu.Dying);
            UpdateLayout();
            if (menu == Menu.Pause)
                _pauseMenu.FocusContinue();
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
                Vector2.zero, new Vector2(270f, 56f));
            _trackedQuest.alignment = TextAnchor.UpperRight;
            _trackedQuest.rectTransform.pivot = Vector2.one;
            _trackedQuest.gameObject.SetActive(_questTracked);

            _overlay = MakePanel(hud, "Menu Overlay", new Color32(0, 0, 0, 160));
            _overlay.anchorMin = Vector2.zero;
            _overlay.anchorMax = Vector2.one;
            _overlay.offsetMin = _overlay.offsetMax = Vector2.zero;

            _settingsPanel = MakePanel(_overlay, "Settings Menu", new Color32(30, 33, 35, 255));
            _settingsPanel.sizeDelta = PanelSize;
            MakeText(_settingsPanel, font, "Settings Title", "Настройки", 29,
                new Vector2(0f, 117f), new Vector2(320f, 42f));
            _sensitivityLabel = MakeText(_settingsPanel, font, "Mouse Sensitivity Value", "", 19,
                new Vector2(0f, 49f), new Vector2(320f, 34f));
            MakeSensitivitySlider();
            ApplySensitivity();
            MakeButton(_settingsPanel, font, "Назад", -99f, () => SetMenu(Menu.Pause));

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

        void MakeSensitivitySlider()
        {
            var sliderRect = MakePanel(_settingsPanel, "Mouse Sensitivity", new Color32(24, 27, 29, 255));
            sliderRect.sizeDelta = new Vector2(280f, 42f);
            var slider = sliderRect.gameObject.AddComponent<UnityEngine.UI.Slider>();

            var track = MakePanel(sliderRect, "Track", new Color32(61, 65, 68, 255));
            track.sizeDelta = new Vector2(260f, 6f);
            track.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;

            var handleArea = new GameObject("Handle Area", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(sliderRect, false);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);

            var handle = MakePanel(handleArea, "Handle", new Color32(241, 188, 118, 255));
            handle.sizeDelta = new Vector2(20f, 24f);
            slider.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();
            slider.handleRect = handle;
            slider.maxValue = MaxSensitivity;
            slider.minValue = MinSensitivity;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(_sensitivityPercent);
            slider.onValueChanged.AddListener(value =>
            {
                _sensitivityPercent = Mathf.RoundToInt(value);
                _sensitivityChanged = true;
                ApplySensitivity();
            });
        }

        void ApplySensitivity()
        {
            if (_controller != null)
                _controller.MouseSensitivityScale = _sensitivityPercent / 100f;
            if (_sensitivityLabel != null)
                _sensitivityLabel.text = $"Чувствительность мыши: {_sensitivityPercent}%";
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
            string description = completed ? "Инструменты возвращены Дени."
                : carrying ? "Отнеси ящик Дени."
                : "Найди ящик с красной ручкой. Ориентир — дверь с белой полосой.";
            string objective = carrying ? "Вернуть ящик" : "Найти ящик";
            if (inWorkshopQuest && !completed && !carrying)
            {
                if (_workshop.NeedsIntimidation)
                {
                    objective = "Напугать вымогателя";
                    description = "Достань пистолет и напугай вымогателя.\nНаведи на него пистолет, удерживая ПКМ.";
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
            if (_trackedQuest != null)
                _trackedQuest.text = _trackingDeniQuest
                    ? "Инструменты Дени\n" + objective
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
            float scale = Mathf.Clamp01(Mathf.Min(size.x / (PanelSize.x + 32f), size.y / (PanelSize.y + 32f)));
            Vector2 center = (min + max) * 0.5f;
            _settingsPanel.anchorMin = _settingsPanel.anchorMax = center;
            _settingsPanel.localScale = new Vector3(scale, scale, 1f);
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
            float trackerScale = Mathf.Clamp01(size.x / 318f);
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
            if (focused && _menu != Menu.None && _menu != Menu.Dying)
                ShowCursor();
        }

        void OnDisable()
        {
            _activeTour?.Cancel();
            _activeDeniDialogue?.Cancel();
            _workshop?.CancelConversation();
            if (_health != null)
                _health.HealthChanged -= OnHealthChanged;
            SetMenu(Menu.None);
            HideTalkHint();
            if (_trackedQuest != null)
                _trackedQuest.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            _typewriterText?.ReleaseFont();
            if (_talkHint != null)
                Destroy(_talkHint.gameObject);
            if (_overlay != null)
                Destroy(_overlay.gameObject);
            if (_trackedQuest != null)
                Destroy(_trackedQuest.gameObject);
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
