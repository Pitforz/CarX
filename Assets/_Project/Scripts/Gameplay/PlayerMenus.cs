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
        const string FindKeyObjective = "Найти ключ от второго этажа";
        const string FindKeyHint = "Расспроси местных, где можно достать ключ.";
        const string InvestigateFatherObjective = "Разузнать, что случилось с отцом";
        const string InvestigateFatherHint = "Расспроси местных об отце и о том, как спуститься на второй этаж.";
        const string FollowFatherTitle = "По следам отца";
        const string FollowFatherHint = "Пройди дальше по тоннелям и выясни, куда увели отца.";
        const string MouseSensitivityPref = "CarX.MouseSensitivityPercent";
        const string MasterVolumePref = "CarX.MasterVolumePercent";
        const string DifficultyPref = "CarX.Difficulty";
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

        enum Menu { None, Pause, Settings, Quests, Dying, Death, Dialogue, Clue, SlowTime, EndingFade, Ending }
        enum DeniQuestion { Talk, ContinueReturn, AskAboutKey, CheckFlashlight }

        [SerializeField] Material _clueBlur;

        NoteReaderView _noteReader;
        SlowTimeTutorialView _slowTimeTutorial;
        System.Action<int> _selectEncounterChoice;
        System.Action _resumeEncounterFight;
        bool _encounterInputLocked;
        bool _encounterCinematic;
        bool _slowTimePaused;
        bool _openingClue;
        System.Action _onClueDismissed;
        AudioSource[] _clueAmbience;
        bool[] _previousClueAmbiencePauseOverrides;
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
        RectTransform _endingPanel;
        CanvasGroup _endingBlack;
        UnityEngine.UI.Image _movingEndingFade;
        UnityEngine.UI.Text _endingTitle;
        UnityEngine.UI.Text _endingCaption;
        UnityEngine.UI.Text _endingSecondaryLabel;
        UnityEngine.UI.Button _endingRestartButton;
        UnityEngine.UI.Button _endingSecondaryButton;
        bool _endingReturnsToMenu;
        string _deathEndingTitle;
        bool _deathEndingReturnsToMenu;
        const float EndingWait = 5f;
        NpcQuestionsView _questionsView;
        UnityEngine.UI.Text _talkHint;
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
        bool _followingFather;
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
            _health.DifficultyLevel = (PlayerHealth.Difficulty)Mathf.Clamp(
                PlayerPrefs.GetInt(DifficultyPref, (int)PlayerHealth.Difficulty.Normal),
                (int)PlayerHealth.Difficulty.Easy, (int)PlayerHealth.Difficulty.Hard);
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
            if (_menu is Menu.Dying or Menu.Death or Menu.EndingFade or Menu.Ending)
                return;

            _activeTour?.Cancel();
            _activeDeniDialogue?.Cancel();
            _answeringNpc?.CancelAnswer();
            _workshop?.CancelConversation();

            if (_deathFall == null || !_deathFall.isActiveAndEnabled)
            {
                FinishDeath();
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
                    FinishDeath();
            });
        }

        void FinishDeath()
        {
            ConfigureResult("Вы погибли", _deathEndingTitle ?? "Плохая концовка", success: false,
                returnToMainMenu: _deathEndingReturnsToMenu);
            _endingBlack.alpha = 1f;
            SetMenu(Menu.Death);
        }

        internal void SetDeathEnding(string title, bool returnToMainMenu)
        {
            _deathEndingTitle = title;
            _deathEndingReturnsToMenu = returnToMainMenu;
        }

        internal void ShowStoryEnding(string title, string subtitle, bool success, bool returnToMainMenu)
        {
            _hud.ShowSubtitle(subtitle, duration: EndingWait);
            if (!success)
            {
                StartCoroutine(FadeWhilePlaying(title, returnToMainMenu));
                return;
            }

            ConfigureResult("Вы прошли игру", title, success: true, returnToMainMenu: returnToMainMenu);
            StartEndingFade(Time.unscaledTime + EndingWait);
        }

        System.Collections.IEnumerator FadeWhilePlaying(string title, bool returnToMainMenu)
        {
            const float fadeDuration = 1.65f;
            var rect = MakePanel(_hud.HudRect, "Final Scene Fade", Color.clear);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _movingEndingFade = rect.GetComponent<UnityEngine.UI.Image>();
            _movingEndingFade.raycastTarget = false;

            float elapsed = 0f;
            while (isActiveAndEnabled && !_health.IsDead && elapsed < EndingWait + fadeDuration)
            {
                yield return null;
                if (_movingEndingFade == null || _health.IsDead || !isActiveAndEnabled)
                    break;

                bool playing = _menu == Menu.None;
                _movingEndingFade.enabled = playing;
                if (!playing)
                    continue;

                elapsed += Time.unscaledDeltaTime;
                rect.SetAsLastSibling();
                float alpha = Mathf.SmoothStep(0f, 1f, (elapsed - EndingWait) / fadeDuration);
                _movingEndingFade.color = new Color(0f, 0f, 0f, alpha);
            }

            ClearMovingEndingFade();
            if (!isActiveAndEnabled || _health.IsDead)
                yield break;
            ConfigureResult(title, null, success: false, returnToMainMenu: returnToMainMenu);
            _endingBlack.GetComponent<UnityEngine.UI.Image>().color = Color.black;
            _endingBlack.alpha = 1f;
            SetMenu(Menu.Ending);
        }

        void ClearMovingEndingFade()
        {
            if (_movingEndingFade != null)
            {
                _movingEndingFade.enabled = false;
                Destroy(_movingEndingFade.gameObject);
            }
            _movingEndingFade = null;
        }

        void ConfigureResult(string title, string caption, bool success, bool returnToMainMenu)
        {
            if (_overlay == null)
                BuildMenus();
            if (_endingPanel == null)
                BuildEndingMenu();

            _endingBlack.GetComponent<UnityEngine.UI.Image>().color = new Color32(30, 17, 11, 255);
            _endingTitle.text = title;
            _endingTitle.color = success ? new Color32(154, 222, 128, 255) : new Color32(255, 151, 51, 255);
            _endingCaption.text = caption;
            _endingCaption.gameObject.SetActive(!string.IsNullOrEmpty(caption));
            _endingReturnsToMenu = returnToMainMenu;
            _endingSecondaryLabel.text = returnToMainMenu ? "Главное меню" : "Выйти из игры";
            _endingRestartButton.interactable = _endingSecondaryButton.interactable = true;
        }

        void StartEndingFade(float fadeAt)
        {
            _endingBlack.alpha = 0f;
            SetMenu(Menu.EndingFade);
            StartCoroutine(FadeToEnding(fadeAt));
        }

        System.Collections.IEnumerator FadeToEnding(float fadeAt)
        {
            const float fadeDuration = 1.65f;
            while (_menu == Menu.EndingFade && Time.unscaledTime < fadeAt)
                yield return null;
            float start = Time.unscaledTime;
            while (_menu == Menu.EndingFade)
            {
                float progress = (Time.unscaledTime - start) / fadeDuration;
                _endingBlack.alpha = Mathf.SmoothStep(0f, 1f, progress);
                if (progress >= 1f)
                {
                    SetMenu(Menu.Ending);
                    yield break;
                }
                yield return null;
            }
        }

        void Update()
        {
            if (_hud.HudRect == null || _menu is Menu.Dying or Menu.Death or Menu.EndingFade or Menu.Ending)
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
            bool inventory = keyboard?.tabKey.wasPressedThisFrame == true;
            bool interact = keyboard?.eKey.wasPressedThisFrame == true;
            bool resumeTime = keyboard?.spaceKey.wasPressedThisFrame == true;
#else
            bool escape = Input.GetKeyDown(KeyCode.Escape);
            bool quests = Input.GetKeyDown(KeyCode.T);
            bool inventory = Input.GetKeyDown(KeyCode.Tab);
            bool interact = Input.GetKeyDown(KeyCode.E);
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
                return;

            if (_menu == Menu.Clue)
            {
                if (escape || interact || inventory)
                {
                    var onDismissed = _onClueDismissed;
                    SetMenu(Menu.None);
                    onDismissed?.Invoke();
                }
                else
                {
#if ENABLE_INPUT_SYSTEM
                    bool previous = keyboard?.leftArrowKey.wasPressedThisFrame == true;
                    bool next = keyboard?.rightArrowKey.wasPressedThisFrame == true;
#else
                    bool previous = Input.GetKeyDown(KeyCode.LeftArrow);
                    bool next = Input.GetKeyDown(KeyCode.RightArrow);
#endif
                    _noteReader.TurnPage((next ? 1 : 0) - (previous ? 1 : 0));
                }
                return;
            }

            if (_openingClue)
                return;

            if (_menu == Menu.Dialogue)
            {
                if (escape || inventory || _dialogueNpc == null || !_dialogueNpc.CanTalk)
                {
                    _dismissedDialogueNpc = _dialogueNpc;
                    SetMenu(Menu.None);
                    if (inventory)
                        _inventory?.OpenFromDialogue();
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
                if (!HandleDeniQuestInteraction(interact))
                    UpdateTalkTarget(interact);
            }
        }

        bool HandleDeniQuestInteraction(bool interact)
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

            if (interact && StartDeniReturn(_deniQuest))
            {
                _closedFrame = Time.frameCount;
                return true;
            }

            _nearbyNpc = npc;
            _clueInView = false;
            HideDialogueQuestions();
            string action = _deniQuest.ToolsReturned ? "E — продолжить разговор" : "E — отдать ящик";
            ShowTalkHint(action);
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
                    if (!deni.IsWaitingInWorkshop)
                        AddDeniQuestion(DeniQuestion.Talk, deni.HasQuest ? "Где мастерская?" : npc.FirstQuestion);
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
                    if (!npc.HasAutomaticIntroduction && !string.IsNullOrWhiteSpace(npc.SecondQuestion))
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
            if (npc == null || npc.gameObject.scene != gameObject.scene || !npc.CanTalk || !npc.ShowsQuestions ||
                (npc.RequiresKeyObjective && !HasKeyObjective))
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
            int shortcutCount = _menu == Menu.Dialogue ? Mathf.Min(_questions.Count, 8) : 0;
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
                (_dialogueNpc.RequiresKeyObjective && !HasKeyObjective) ||
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

            if (_overlay == null)
                return;

            _trackedQuest.gameObject.SetActive(_menu != Menu.Dying && _menu != Menu.Death && _menu != Menu.Quests &&
                (HasTrackedQuest || HasKeyObjective || _followingFather) &&
                (_inventory == null || !_inventory.IsOpen));
            UpdateLayout();
        }

        internal void LockEncounterInput(Vector3 facingPoint)
        {
            _encounterInputLocked = true;
            _inventory?.CloseForMenu();
            _controller?.BeginEncounterLook(facingPoint);
            HideTalkHint();
        }

        internal void UnlockEncounterInput()
        {
            if (!_encounterInputLocked)
                return;

            _encounterInputLocked = false;
            _encounterCinematic = false;
            _controller?.EndEncounterLook();
            if (_menu != Menu.SlowTime)
                _controller?.SetControlsFrozen(false);
        }

        internal bool ShowEncounterChoice(System.Action<int> selectChoice)
        {
            if (_menu != Menu.None || _health.IsDead || !OpenSlowTime())
                return false;

            _selectEncounterChoice = selectChoice;
            _slowTimeTutorial.ShowEncounterChoice(SelectEncounterChoice, _hud.SubtitleFont);
            if (_inputs != null)
                _inputs.cursorLocked = false;
            ShowCursor();
            return true;
        }

        internal void UpdateEncounterTimer(int seconds) => _slowTimeTutorial.SetEncounterTimer(seconds);

        internal void ShowEncounterChoiceHint(string text) => _slowTimeTutorial.SetChoiceHint(text);

        internal void BeginEncounterCinematic()
        {
            _encounterCinematic = true;
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
            return note != null && note.Paper != null && ShowClues(new[] { note.Paper }, onDismissed);
        }

        internal bool ShowClues(Sprite[] documents, System.Action onDismissed = null, AudioSource[] ambience = null)
        {
            if (!isActiveAndEnabled || BlocksGameplayInput || _health.IsDead)
                return false;

            StartCoroutine(OpenClue(documents, onDismissed, ambience));
            return true;
        }

        System.Collections.IEnumerator OpenClue(Sprite[] documents, System.Action onDismissed, AudioSource[] ambience)
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
            _noteReader.Open(documents, _clueBlur, capture);
            _onClueDismissed = onDismissed;
            if (ambience != null)
            {
                _clueAmbience = ambience;
                _previousClueAmbiencePauseOverrides = new bool[ambience.Length];
                for (int i = 0; i < ambience.Length; i++)
                {
                    if (ambience[i] == null)
                        continue;
                    _previousClueAmbiencePauseOverrides[i] = ambience[i].ignoreListenerPause;
                    ambience[i].ignoreListenerPause = true;
                }
            }
            SetMenu(Menu.Clue);
        }

        void RestoreClueAmbience()
        {
            if (_clueAmbience == null)
                return;
            for (int i = 0; i < _clueAmbience.Length; i++)
                if (_clueAmbience[i] != null)
                    _clueAmbience[i].ignoreListenerPause = _previousClueAmbiencePauseOverrides[i];
            _clueAmbience = null;
            _previousClueAmbiencePauseOverrides = null;
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
                RestoreClueAmbience();
            }
            if (_menu == Menu.Settings && _settingsChanged)
            {
                PlayerPrefs.SetInt(MouseSensitivityPref, _sensitivityPercent);
                PlayerPrefs.SetInt(MasterVolumePref, _masterVolumePercent);
                PlayerPrefs.SetInt(DifficultyPref, (int)_health.DifficultyLevel);
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
                    MinSensitivity, MaxSensitivity, _sensitivityPercent, _masterVolumePercent, _health.DifficultyLevel,
                    ChangeSensitivity, ChangeMasterVolume, ChangeDifficulty, () => SetMenu(Menu.Pause));
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
            AudioListener.pause = menu is Menu.Dying or Menu.EndingFade ? _previousAudioPause : true;
            if ((menu is Menu.Dying or Menu.EndingFade) || (menu == Menu.SlowTime && _selectEncounterChoice == null))
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
            bool result = menu is Menu.Death or Menu.Ending;
            bool ending = result || menu == Menu.EndingFade;
            _overlay.GetComponent<UnityEngine.UI.Image>().color = ending ? Color.clear : new Color32(0, 0, 0, 160);
            if (_endingPanel != null)
            {
                _endingBlack.gameObject.SetActive(ending);
                _endingPanel.gameObject.SetActive(result);
            }
            _overlay.SetAsLastSibling();
            _overlay.gameObject.SetActive(menu != Menu.Dying && menu != Menu.SlowTime && menu != Menu.Dialogue);
            if (menu == Menu.Dialogue)
                _questionsView.SetFocused(true);
            UpdateLayout();
            if (menu == Menu.Pause)
                _pauseMenu.FocusContinue();
            else if (menu == Menu.Settings)
                _settingsMenu.FocusFirst();
            else if (result && _endingRestartButton.interactable)
                EventSystem.current?.SetSelectedGameObject(_endingRestartButton.gameObject);
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

            _overlay = MakePanel(hud, "Menu Overlay", new Color32(0, 0, 0, 160));
            _overlay.anchorMin = Vector2.zero;
            _overlay.anchorMax = Vector2.one;
            _overlay.offsetMin = _overlay.offsetMax = Vector2.zero;

            _overlay.gameObject.SetActive(false);
        }

        void BuildEndingMenu()
        {
            Font font = _hud.SubtitleFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform black = MakePanel(_overlay, "Result Background", new Color32(30, 17, 11, 255));
            black.anchorMin = Vector2.zero;
            black.anchorMax = Vector2.one;
            black.offsetMin = black.offsetMax = Vector2.zero;
            _endingBlack = black.gameObject.AddComponent<CanvasGroup>();
            _endingPanel = MakePanel(black, "Result Menu", Color.clear);
            _endingPanel.sizeDelta = new Vector2(1000f, 550f);
            _endingCaption = MakeText(_endingPanel, font, "Ending Caption", string.Empty, 31,
                new Vector2(0f, 215f), new Vector2(910f, 48f));
            _endingCaption.color = new Color32(236, 188, 150, 255);
            _endingTitle = MakeText(_endingPanel, font, "Result Title", string.Empty, 78,
                new Vector2(0f, 151f), new Vector2(930f, 118f));
            _endingTitle.resizeTextForBestFit = true;
            _endingTitle.resizeTextMinSize = 40;
            _endingTitle.resizeTextMaxSize = 78;

            MakeResultButton(_endingPanel, font, "Начать заново", 8f, primary: true, RestartScene,
                out _endingRestartButton);
            _endingSecondaryLabel = MakeResultButton(_endingPanel, font, "Выйти из игры", -140f,
                primary: false, LeaveResult, out _endingSecondaryButton);
            _endingRestartButton.navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnDown = _endingSecondaryButton
            };
            _endingSecondaryButton.navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnUp = _endingRestartButton
            };
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

        void ChangeDifficulty(PlayerHealth.Difficulty value)
        {
            _health.DifficultyLevel = value;
            _settingsChanged = true;
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

        internal void StartFatherSearch()
        {
            _followingFather = true;
            _questTracked = true;
            _trackingDeniQuest = false;
            if (_overlay == null)
                BuildMenus();
            RefreshQuestDisplay();
        }

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
            string keyObjective = needsHint ? InvestigateFatherObjective : FindKeyObjective;
            string keyDirections = needsHint ? InvestigateFatherHint : FindKeyHint;
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
            string journalTitle = returning ? objective : DeniQuestTitle;
            if (_followingFather)
                _questJournal?.SetMainQuest(FollowFatherTitle, FollowFatherHint);
            _questJournal?.SetQuests(_questTracked && !_trackingDeniQuest, _deniQuest != null,
                _trackingDeniQuest, completed, journalTitle, description);

            if (_trackedQuest == null)
                return;

            bool aimingAtExtortionist = inWorkshopQuest && !completed && _workshop.NeedsIntimidation;
            string trackedText = $"{QuestTitle}\n{QuestDescription}";
            if (_trackingDeniQuest)
                trackedText = returning
                    ? $"{objective}\n{description}"
                    : "Инструменты Дени\n" + objective + (aimingAtExtortionist ? "\nУдерживай ПКМ" : "");
            if (_followingFather)
                trackedText = $"{FollowFatherTitle}\n{FollowFatherHint}";
            else if (HasKeyObjective)
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
            if (_endingPanel != null)
            {
                _endingPanel.anchorMin = _endingPanel.anchorMax = center;
                float endingScale = 0.76f * Mathf.Min(size.x / _endingPanel.sizeDelta.x,
                    size.y / _endingPanel.sizeDelta.y);
                _endingPanel.localScale = new Vector3(endingScale, endingScale, 1f);
            }

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

        static UnityEngine.UI.Text MakeResultButton(RectTransform parent, Font font, string text, float y,
            bool primary, UnityAction action, out UnityEngine.UI.Button button)
        {
            var rect = MakePanel(parent, text,
                primary ? new Color32(255, 152, 55, 255) : new Color32(172, 105, 64, 255));
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(704f, 110f);
            var fill = MakePanel(rect, "Fill", Color.white);
            fill.sizeDelta = rect.sizeDelta - new Vector2(4f, 4f);
            button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = fill.GetComponent<UnityEngine.UI.Image>();
            var colors = button.colors;
            colors.normalColor = primary ? new Color32(123, 57, 20, 255) : new Color32(73, 47, 32, 255);
            colors.highlightedColor = primary ? new Color32(151, 74, 27, 255) : new Color32(105, 67, 43, 255);
            colors.pressedColor = new Color32(88, 45, 24, 255);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = colors.normalColor;
            colors.fadeDuration = 0f;
            button.colors = colors;
            button.onClick.AddListener(action);
            var label = MakeText(rect, font, "Label", text, 46, Vector2.zero, fill.sizeDelta);
            label.color = new Color32(255, 241, 216, 255);
            return label;
        }

        void RestartScene()
        {
            if (_restartingScene || _menu is not (Menu.Death or Menu.Ending) || !_endingRestartButton.interactable)
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

        void LeaveResult()
        {
            if (_restartingScene || _menu is not (Menu.Death or Menu.Ending) || !_endingSecondaryButton.interactable)
                return;
            if (_endingReturnsToMenu)
                _restartingScene = SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single) != null;
            else
                QuitGame();
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
            if (focused && _menu is not (Menu.None or Menu.Dying or Menu.EndingFade) &&
                (_menu != Menu.SlowTime || _selectEncounterChoice != null))
                ShowCursor();
        }

        void OnDisable()
        {
            ClearMovingEndingFade();
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
