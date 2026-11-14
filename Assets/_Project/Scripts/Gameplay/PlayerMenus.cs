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
        const string MouseSensitivityPref = "CarX.MouseSensitivityPercent";
        const int DefaultSensitivity = 100;
        const int MinSensitivity = 25;
        const int MaxSensitivity = 200;
        static readonly Vector2 PanelSize = new(400f, 330f);

        enum Menu { None, Pause, Settings, Quests, Death }

        PlayerStatusHud _hud;
        PlayerHealth _health;
        PlayerInventory _inventory;
        StarterAssetsInputs _inputs;
        FirstPersonController _controller;
        RectTransform _overlay;
        RectTransform _pausePanel;
        RectTransform _settingsPanel;
        RectTransform _questPanel;
        RectTransform _deathPanel;
        UnityEngine.UI.Text _sensitivityLabel;
        int _sensitivityPercent;
        bool _sensitivityChanged;
        UnityEngine.UI.Text _questStatus;
        UnityEngine.UI.Text _trackedQuest;
        bool _questTracked;
        bool _restartingScene;
        Menu _menu;
        float _previousTimeScale;
        bool _previousAudioPause;
        CursorLockMode _previousCursorLock;
        bool _previousCursorVisible;
        bool _previousInputCursorLock;
        int _closedFrame = -1;

        internal bool BlocksGameplayInput => _menu != Menu.None || _closedFrame == Time.frameCount;

        void Awake()
        {
            _hud = GetComponent<PlayerStatusHud>();
            _health = GetComponent<PlayerHealth>();
            _inventory = GetComponent<PlayerInventory>();
            _inputs = GetComponent<StarterAssetsInputs>();
            _controller = GetComponent<FirstPersonController>();
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
                SetMenu(Menu.Death);
        }

        void Update()
        {
            if (_hud.HudRect == null || _menu == Menu.Death)
                return;

            if (_health.IsDead)
            {
                SetMenu(Menu.Death);
                return;
            }

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            bool escape = keyboard?.escapeKey.wasPressedThisFrame == true;
            bool quests = keyboard?.tKey.wasPressedThisFrame == true;
#else
            bool escape = Input.GetKeyDown(KeyCode.Escape);
            bool quests = Input.GetKeyDown(KeyCode.T);
#endif
            if (escape)
            {
                if (_menu == Menu.Settings)
                    SetMenu(Menu.Pause);
                else
                    SetMenu(_menu == Menu.None ? Menu.Pause : Menu.None);
            }
            else if (quests && (_menu == Menu.None || _menu == Menu.Quests))
                SetMenu(_menu == Menu.Quests ? Menu.None : Menu.Quests);
        }

        void LateUpdate()
        {
            if (_overlay == null)
                return;

            _trackedQuest.gameObject.SetActive(_menu != Menu.Death && _questTracked &&
                (_inventory == null || !_inventory.IsOpen));
            UpdateLayout();
        }

        void SetMenu(Menu menu)
        {
            if (_menu == menu)
                return;

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
                AudioListener.pause = true;
                ShowCursor();
            }

            _menu = menu;
            _pausePanel.gameObject.SetActive(menu == Menu.Pause);
            _settingsPanel.gameObject.SetActive(menu == Menu.Settings);
            _questPanel.gameObject.SetActive(menu == Menu.Quests);
            _deathPanel.gameObject.SetActive(menu == Menu.Death);
            _overlay.SetAsLastSibling();
            _overlay.gameObject.SetActive(true);
            UpdateLayout();
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

            _pausePanel = MakePanel(_overlay, "Pause Menu", new Color32(30, 33, 35, 255));
            _pausePanel.sizeDelta = PanelSize;
            MakeText(_pausePanel, font, "Pause Title", "Пауза", 29, new Vector2(0f, 117f), new Vector2(320f, 42f));
            MakeButton(_pausePanel, font, "Продолжить игру", 54f, () => SetMenu(Menu.None));
            MakeButton(_pausePanel, font, "Настройки", -5f, () => SetMenu(Menu.Settings));
            MakeButton(_pausePanel, font, "Выйти из игры", -64f, QuitGame);

            _settingsPanel = MakePanel(_overlay, "Settings Menu", new Color32(30, 33, 35, 255));
            _settingsPanel.sizeDelta = PanelSize;
            MakeText(_settingsPanel, font, "Settings Title", "Настройки", 29,
                new Vector2(0f, 117f), new Vector2(320f, 42f));
            _sensitivityLabel = MakeText(_settingsPanel, font, "Mouse Sensitivity Value", "", 19,
                new Vector2(0f, 49f), new Vector2(320f, 34f));
            MakeSensitivitySlider();
            ApplySensitivity();
            MakeButton(_settingsPanel, font, "Назад", -99f, () => SetMenu(Menu.Pause));

            _questPanel = MakePanel(_overlay, "Quest Journal", new Color32(30, 33, 35, 255));
            _questPanel.sizeDelta = PanelSize;
            MakeText(_questPanel, font, "Journal Title", "Задания", 29,
                new Vector2(0f, 117f), new Vector2(320f, 42f));
            MakeText(_questPanel, font, "Quest Selection Hint", "ЛКМ — выбрать задание", 16,
                new Vector2(0f, 75f), new Vector2(320f, 30f));
            MakeButton(_questPanel, font, QuestTitle, 20f, TrackQuest);
            MakeText(_questPanel, font, "Quest Description", QuestDescription, 18,
                new Vector2(0f, -33f), new Vector2(320f, 34f));
            _questStatus = MakeText(_questPanel, font, "Quest Tracking Status", _questTracked ? "Отслеживается" : "", 15,
                new Vector2(0f, -70f), new Vector2(320f, 24f));
            MakeText(_questPanel, font, "Close Journal Hint", "T или Esc — закрыть", 15,
                new Vector2(0f, -118f), new Vector2(320f, 30f));

            _deathPanel = MakePanel(_overlay, "Death Menu", new Color32(30, 33, 35, 255));
            _deathPanel.sizeDelta = new Vector2(PanelSize.x, 230f);
            var deathTitle = MakeText(_deathPanel, font, "Death Title", "Вы погибли", 32,
                new Vector2(0f, 70f), new Vector2(320f, 42f));
            deathTitle.color = new Color32(244, 120, 110, 255);
            MakeButton(_deathPanel, font, "Начать заново", 6f, RestartScene);
            MakeButton(_deathPanel, font, "Выйти из игры", -53f, QuitGame);
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
            _questStatus.text = "Отслеживается";
            _trackedQuest.gameObject.SetActive(true);
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
            _pausePanel.anchorMin = _pausePanel.anchorMax = center;
            _settingsPanel.anchorMin = _settingsPanel.anchorMax = center;
            _questPanel.anchorMin = _questPanel.anchorMax = center;
            _pausePanel.localScale = _settingsPanel.localScale = _questPanel.localScale = new Vector3(scale, scale, 1f);
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

        static void MakeButton(RectTransform parent, Font font, string text, float y, UnityAction action)
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
            MakeText(rect, font, "Label", text, 18, Vector2.zero, rect.sizeDelta);
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
            if (focused && _menu != Menu.None)
                ShowCursor();
        }

        void OnDisable()
        {
            if (_health != null)
                _health.HealthChanged -= OnHealthChanged;
            SetMenu(Menu.None);
            if (_trackedQuest != null)
                _trackedQuest.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_overlay != null)
                Destroy(_overlay.gameObject);
            if (_trackedQuest != null)
                Destroy(_trackedQuest.gameObject);
        }
    }
}
