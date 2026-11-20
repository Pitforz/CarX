using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CarX.Gameplay
{
    internal sealed class SlowTimeTutorialView
    {
        static readonly Vector2 CardSize = new(408f, 148f);
        static readonly Vector2 CombatCardSize = new(408f, 196f);
        static readonly Vector2 ChoiceSize = new(472f, 245f);
        const float BottomMargin = 36f;

        readonly RectTransform _root;
        readonly RectTransform _card;
        readonly RectTransform _sprintHint;
        readonly UnityEngine.UI.Text _tutorialExit;
        readonly Canvas _gameplayCanvas;
        readonly VolumeProfile _profile;
        readonly ColorAdjustments _colorAdjustments;
        RectTransform _choiceCard;
        UnityEngine.UI.Text _timer;
        UnityEngine.UI.Text _choiceHint;
        System.Action<int> _onChoice;
        UniversalAdditionalCameraData _cameraData;
        LayerMask _previousVolumeMask;
        bool _previousPostProcessing;
        bool _previousHudVisibility;

        internal SlowTimeTutorialView(RectTransform gameplayHud)
        {
            var overlay = new GameObject("Slow Time Tutorial", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler));
            overlay.SetActive(false);
            if (overlay.scene != gameplayHud.gameObject.scene)
                SceneManager.MoveGameObjectToScene(overlay, gameplayHud.gameObject.scene);
            _root = (RectTransform)overlay.transform;

            _gameplayCanvas = gameplayHud.GetComponent<Canvas>();
            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = _gameplayCanvas.targetDisplay;

            var hudScaler = gameplayHud.GetComponent<CanvasScaler>();
            var scaler = overlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = hudScaler.uiScaleMode;
            scaler.referenceResolution = hudScaler.referenceResolution;
            scaler.screenMatchMode = hudScaler.screenMatchMode;
            scaler.matchWidthOrHeight = hudScaler.matchWidthOrHeight;

            var volume = overlay.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = float.MaxValue;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _colorAdjustments = _profile.Add<ColorAdjustments>();
            _colorAdjustments.saturation.Override(-100f);
            volume.sharedProfile = _profile;

            _card = MakeBox(_root, "Tutorial Card", Vector2.zero, CardSize, new Color32(20, 20, 20, 238));
            _card.pivot = new Vector2(0.5f, 0f);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            MakeText(_card, font, "Title", "Наклон корпуса", 25, new Vector2(0f, 45f),
                new Vector2(360f, 32f)).fontStyle = FontStyle.Bold;
            MakeLeanKey(font, "Z", "Влево", -144f);
            MakeLeanKey(font, "C", "Вправо", 24f);
            _tutorialExit = MakeText(_card, font, "Exit Hint", "Нажмите <b>Пробел</b>, чтобы выйти", 17,
                new Vector2(0f, -49f), new Vector2(360f, 26f));
            _tutorialExit.color = new Color32(216, 216, 216, 255);

            _sprintHint = PlayerStatusHud.CreateRect(_card, "Sprint Hint");
            Place(_sprintHint, new Vector2(0f, -43f), new Vector2(160f, 30f));
            var ctrlSize = new Vector2(54f, 30f);
            var ctrl = MakeBox(_sprintHint, "Ctrl Key", new Vector2(-36f, 0f), ctrlSize,
                new Color32(66, 66, 66, 255));
            MakeText(ctrl, font, "Key Label", "Ctrl", 17, Vector2.zero, ctrlSize).fontStyle = FontStyle.Bold;
            MakeText(_sprintHint, font, "Sprint Label", "Бежать", 19, new Vector2(28f, 0f),
                new Vector2(64f, 30f));
            _sprintHint.gameObject.SetActive(false);
        }

        internal void Open(Camera camera)
        {
            _card.sizeDelta = CardSize;
            _sprintHint.gameObject.SetActive(false);
            _tutorialExit.rectTransform.anchoredPosition = new Vector2(0f, -49f);
            _card.gameObject.SetActive(true);
            if (_choiceCard != null)
                _choiceCard.gameObject.SetActive(false);
            _cameraData = camera.GetUniversalAdditionalCameraData();
            _previousVolumeMask = _cameraData.volumeLayerMask;
            _previousPostProcessing = _cameraData.renderPostProcessing;
            _previousHudVisibility = _gameplayCanvas.enabled;
            _cameraData.volumeLayerMask = _previousVolumeMask.value | (1 << _root.gameObject.layer);
            _cameraData.renderPostProcessing = true;
            _gameplayCanvas.enabled = false;
            _root.gameObject.SetActive(true);
            FitToScreen();
        }

        internal void ShowEncounterChoice(System.Action<int> onChoice)
        {
            _onChoice = onChoice;
            if (_choiceCard == null)
                BuildEncounterChoice();

            _card.gameObject.SetActive(false);
            _choiceCard.gameObject.SetActive(true);
            _choiceHint.text = "";
            FitToScreen();
        }

        internal void ShowCombatTutorial()
        {
            _onChoice = null;
            _choiceCard.gameObject.SetActive(false);
            _card.sizeDelta = CombatCardSize;
            _sprintHint.gameObject.SetActive(true);
            _tutorialExit.rectTransform.anchoredPosition = new Vector2(0f, -78f);
            _card.gameObject.SetActive(true);
            FitToScreen();
        }

        internal void SetEncounterTimer(int seconds)
        {
            string value = $"{seconds / 60:00}:{seconds % 60:00}";
            if (_timer.text != value)
                _timer.text = value;
        }

        internal void SetChoiceHint(string text) => _choiceHint.text = text;

        void BuildEncounterChoice()
        {
            _root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            _choiceCard = MakeBox(_root, "Final Encounter Choice", Vector2.zero, ChoiceSize,
                new Color32(20, 20, 20, 244));
            _choiceCard.pivot = new Vector2(0.5f, 0f);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            MakeText(_choiceCard, font, "Title", "Что сделает Алексей?", 24, new Vector2(-75f, 90f),
                new Vector2(280f, 30f)).fontStyle = FontStyle.Bold;
            _timer = MakeText(_choiceCard, font, "Timer", "", 24, new Vector2(175f, 90f),
                new Vector2(70f, 30f));
            MakeChoiceButton(font, 1, "Выстрелить в Александра", 45f);
            MakeChoiceButton(font, 2, "Напомнить, чему он меня учил", 2f);
            MakeChoiceButton(font, 3, "Отойти в сторону", -41f);
            _choiceHint = MakeText(_choiceCard, font, "Choice hint", "", 15, new Vector2(0f, -91f),
                new Vector2(420f, 34f));
        }

        void MakeChoiceButton(Font font, int choice, string text, float y)
        {
            var row = MakeBox(_choiceCard, "Option " + choice, new Vector2(0f, y),
                new Vector2(424f, 36f), new Color32(220, 220, 220, 255));
            var background = row.GetComponent<UnityEngine.UI.Image>();
            background.raycastTarget = true;
            var button = row.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = background;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = choice == 3 ? new Color32(105, 105, 105, 255) : new Color32(64, 64, 64, 255);
            colors.highlightedColor = new Color32(135, 135, 135, 255);
            colors.pressedColor = new Color32(95, 95, 95, 255);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
            button.onClick.AddListener(() => _onChoice?.Invoke(choice));
            var label = MakeText(row, font, "Label", $"{choice}    {text}", 18, Vector2.zero,
                new Vector2(394f, 36f));
            label.alignment = TextAnchor.MiddleLeft;
        }

        internal void Close()
        {
            _onChoice = null;
            if (_root != null)
                _root.gameObject.SetActive(false);
            if (_cameraData != null)
            {
                _cameraData.volumeLayerMask = _previousVolumeMask;
                _cameraData.renderPostProcessing = _previousPostProcessing;
            }
            if (_gameplayCanvas != null)
                _gameplayCanvas.enabled = _previousHudVisibility;
            _cameraData = null;
        }

        internal void FitToScreen()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return;

            Rect safe = Screen.safeArea;
            var display = new Vector2(Screen.width, Screen.height);
            Vector2 size = Vector2.Scale(safe.size / display, _root.rect.size);
            var card = _choiceCard != null && _choiceCard.gameObject.activeSelf ? _choiceCard : _card;
            float scale = Mathf.Clamp01(Mathf.Min(size.x / (card.sizeDelta.x + 32f),
                size.y / (card.sizeDelta.y + BottomMargin + 16f)));
            card.anchorMin = card.anchorMax = new Vector2(safe.center.x / display.x, safe.yMin / display.y);
            card.anchoredPosition = new Vector2(0f, BottomMargin * scale);
            card.localScale = new Vector3(scale, scale, 1f);
        }

        internal void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root.gameObject);
            Object.Destroy(_profile);
            Object.Destroy(_colorAdjustments);
        }

        void MakeLeanKey(Font font, string key, string direction, float x)
        {
            var size = new Vector2(38f, 38f);
            var keycap = MakeBox(_card, key + " Key", new Vector2(x, 0f), size, new Color32(66, 66, 66, 255));
            MakeText(keycap, font, "Letter", key, 22, Vector2.zero, size).fontStyle = FontStyle.Bold;
            var label = MakeText(_card, font, direction, direction, 20, new Vector2(x + 25f, 0f),
                new Vector2(98f, 38f));
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
        }

        static RectTransform MakeBox(RectTransform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var rect = PlayerStatusHud.CreateRect(parent, name, typeof(Image));
            Place(rect, position, size);
            var image = rect.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        static Text MakeText(RectTransform parent, Font font, string name, string text, int fontSize,
            Vector2 position, Vector2 size)
        {
            var label = PlayerStatusHud.CreateLabel(parent, name, font);
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            Place(label.rectTransform, position, size);
            return label;
        }

        static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
