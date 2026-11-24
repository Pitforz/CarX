using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace CarX.Gameplay
{
    internal sealed class PauseMenuView
    {
        const string ArtPath = "CarX/PauseMenu/";
        const float RowTop = 179f;
        const float RowStep = 67f;
        static readonly Vector2 PanelSize = new(652f, 610f);
        static readonly Color Cream = new Color32(243, 229, 204, 255);
        static readonly Color RegularInk = new Color32(210, 201, 185, 255);
        static readonly Color MutedInk = new Color32(166, 155, 139, 255);

        readonly TypewriterMenuText _text;
        readonly RectTransform _highlight;
        readonly UnityEngine.UI.Button _continueButton;
        readonly UnityEngine.UI.Button _loadButton;
        readonly UnityEngine.UI.Button _settingsButton;
        readonly UnityEngine.UI.Button _exitButton;
        readonly UnityEngine.UI.Text _loadLabel;
        readonly UnityEngine.UI.Text _loadHint;
        UnityEngine.UI.Text _highlightedLabel;

        internal RectTransform Root { get; }

        internal PauseMenuView(RectTransform parent, TypewriterMenuText text, UnityAction resume,
            UnityAction loadSave, UnityAction openSettings, UnityAction quit)
        {
            _text = text;
            Root = PlayerStatusHud.CreateRect(parent, "Pause Menu", typeof(UnityEngine.UI.RawImage));
            Root.pivot = new Vector2(0f, 0.5f);
            Root.sizeDelta = PanelSize;
            var panel = Root.GetComponent<UnityEngine.UI.RawImage>();
            panel.texture = Resources.Load<Texture2D>(ArtPath + "Panel");
            panel.color = panel.texture != null ? Color.white : new Color32(27, 28, 26, 245);
            panel.raycastTarget = false;

            _text.MakeLabel(Root, "Pause Title", "Пауза", new Rect(83, 68, 484, 74), 52, Cream, bold: true);
            _highlight = TypewriterMenuText.Place(Root, "Worn Selection", typeof(UnityEngine.UI.RawImage), RowBox(0));
            var paint = _highlight.GetComponent<UnityEngine.UI.RawImage>();
            paint.texture = Resources.Load<Texture2D>(ArtPath + "Selection");
            paint.color = paint.texture != null ? Color.white : new Color32(172, 85, 39, 255);
            paint.raycastTarget = false;

            _continueButton = MakeChoice(0, "Продолжить", resume, out _);
            _loadButton = MakeChoice(1, "Загрузить последнее сохранение", loadSave, out _loadLabel);
            _loadHint = _text.MakeLabel(_loadButton.GetComponent<RectTransform>(), "Unavailable Hint",
                "Нет сохранений", new Rect(28, 44, 480, 20), 14, MutedInk);
            _settingsButton = MakeChoice(2, "Настройки", openSettings, out _);
            _exitButton = MakeChoice(3, "Выйти", quit, out _);
            SetLoadAvailable(false);

            _text.MakeLabel(Root, "Esc Key", "Esc", new Rect(83, 505, 33, 22), 12, Cream, bold: true,
                alignment: TextAnchor.MiddleCenter);
            _text.MakeLabel(Root, "Resume Hint", "продолжить", new Rect(126, 503, 180, 26), 14, MutedInk, bold: true);
        }

        internal void SetLoadAvailable(bool available)
        {
            _loadButton.interactable = available;
            _loadLabel.color = available ? RegularInk : MutedInk;
            var labelRect = _loadLabel.rectTransform;
            labelRect.anchoredPosition = new Vector2(labelRect.anchoredPosition.x, available ? 10f : 5f);
            _loadHint.gameObject.SetActive(!available);
            if (available)
            {
                SetNavigation(_continueButton, _exitButton, _loadButton);
                SetNavigation(_loadButton, _continueButton, _settingsButton);
                SetNavigation(_settingsButton, _loadButton, _exitButton);
                SetNavigation(_exitButton, _settingsButton, _continueButton);
            }
            else
            {
                SetNavigation(_continueButton, _exitButton, _settingsButton);
                SetNavigation(_loadButton, _continueButton, _settingsButton);
                SetNavigation(_settingsButton, _continueButton, _exitButton);
                SetNavigation(_exitButton, _settingsButton, _continueButton);
            }
        }

        internal void FocusContinue()
        {
            EventSystem.current?.SetSelectedGameObject(_continueButton.gameObject);
        }

        internal void Fit(Vector2 safeMin, Vector2 safeCenter, Vector2 safeSize)
        {
            float scale = Mathf.Clamp01(Mathf.Min(safeSize.x / 728f, safeSize.y / 720f));
            Root.anchorMin = Root.anchorMax = new Vector2(safeMin.x, safeCenter.y);
            Root.anchoredPosition = new Vector2(44f, -7f) * scale;
            Root.localScale = new Vector3(scale, scale, 1f);
        }

        UnityEngine.UI.Button MakeChoice(int row, string title, UnityAction action, out UnityEngine.UI.Text label)
        {
            var rect = TypewriterMenuText.Place(Root, title, typeof(UnityEngine.UI.Image), RowBox(row));
            var hitArea = rect.GetComponent<UnityEngine.UI.Image>();
            hitArea.color = Color.clear;
            hitArea.canvasRenderer.cullTransparentMesh = false;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            button.interactable = true;
            label = _text.MakeLabel(rect, "Label", title, new Rect(28, 10f, 486, 46), 25, RegularInk, bold: true);
            button.onClick.AddListener(action);
            var trigger = rect.gameObject.AddComponent<EventTrigger>();
            var hover = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            hover.callback.AddListener(_ => button.Select());
            trigger.triggers.Add(hover);

            var select = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            select.callback.AddListener(_ =>
            {
                if (_highlightedLabel != null)
                    _highlightedLabel.color = RegularInk;
                _highlightedLabel = label;
                label.color = Cream;
                _highlight.anchoredPosition = rect.anchoredPosition;
            });
            trigger.triggers.Add(select);
            return button;
        }

        static Rect RowBox(int row) => new(56f, RowTop + row * RowStep, 539f, 66f);

        static void SetNavigation(UnityEngine.UI.Button button, UnityEngine.UI.Button up, UnityEngine.UI.Button down)
        {
            button.navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down
            };
        }
    }
}
