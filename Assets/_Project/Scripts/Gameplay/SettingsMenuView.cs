using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;
using Image = UnityEngine.UI.Image;

namespace CarX.Gameplay
{
    internal sealed class SettingsMenuView
    {
        const string Artwork = "CarX/SettingsMenu/ConsoleFace";
        static readonly Vector2 PanelSize = new(924f, 488f);
        static readonly Vector2 CardSize = new(398f, 196f);
        static readonly Color Ink = new Color32(246, 229, 203, 255);
        static readonly Color ValueInk = new Color32(255, 188, 133, 255);
        static readonly Color SecondaryInk = new Color32(220, 188, 160, 255);
        static readonly Color Divider = new Color32(75, 52, 39, 255);
        static readonly Color Recess = new Color32(28, 22, 17, 255);
        static readonly Color Orange = new Color32(247, 151, 81, 255);
        const float TrackWidth = 352f;
        const float SegmentGap = 3f;
        const int SegmentCount = 20;

        readonly Font _font;
        readonly FontStyle _fontStyle;
        readonly bool _hasArtwork;
        readonly Slider _sensitivity;

        internal RectTransform Root { get; }

        internal SettingsMenuView(RectTransform parent,
            int minSensitivity, int maxSensitivity, int sensitivity, int volume,
            UnityAction<int> changeSensitivity, UnityAction<int> changeVolume, UnityAction back)
        {
            var bundledFont = Resources.Load<Font>("CarX/Fonts/LiberationMono-Bold");
            _font = bundledFont != null ? bundledFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _fontStyle = bundledFont != null ? FontStyle.Normal : FontStyle.Bold;

            Root = PlayerStatusHud.CreateRect(parent, "Settings Menu");
            Root.anchorMin = Root.anchorMax = Root.pivot = new Vector2(0.5f, 0.5f);
            Root.anchoredPosition = Vector2.zero;
            Root.sizeDelta = PanelSize;
            Box(Root, "Panel Shadow", new Rect(0f, 12f, PanelSize.x, PanelSize.y), new Color32(0, 0, 0, 118));
            var artwork = Resources.Load<Texture2D>(Artwork);
            _hasArtwork = artwork != null;
            if (_hasArtwork)
            {
                var face = TypewriterMenuText.Place(Root, "Console Face", typeof(RawImage),
                    new Rect(Vector2.zero, PanelSize)).GetComponent<RawImage>();
                face.texture = artwork;
                face.raycastTarget = false;
            }
            else
                Box(Root, "Console Face", new Rect(Vector2.zero, PanelSize), Recess);

            var heading = Label(Root, "Title", "Настройки", new Rect(50f, 27f, 520f, 62f), 44, Ink);
            heading.rectTransform.localScale = new Vector3(0.92f, 1f, 1f);

            _sensitivity = AddSlider(new Vector2(53f, 122f), "Чувствительность мыши",
                minSensitivity, maxSensitivity, sensitivity, changeSensitivity);
            var masterVolume = AddSlider(new Vector2(473f, 122f), "Общая громкость",
                0, 100, volume, changeVolume);

            var backButton = AddBackButton(back);
            SetNavigation(_sensitivity, backButton, masterVolume);
            SetNavigation(masterVolume, _sensitivity, backButton);
            SetNavigation(backButton, masterVolume, _sensitivity);
        }

        internal void FocusFirst() => EventSystem.current?.SetSelectedGameObject(_sensitivity.gameObject);

        internal void Fit(Vector2 safeCenter, Vector2 safeSize)
        {
            float scale = Mathf.Min(safeSize.x * 0.722f / PanelSize.x, safeSize.y * 0.678f / PanelSize.y);
            Root.anchorMin = Root.anchorMax = safeCenter;
            Root.localScale = new Vector3(scale, scale, 1f);
        }

        Slider AddSlider(Vector2 position, string title, int min, int max, int initial, UnityAction<int> onChange)
        {
            RectTransform card = TypewriterMenuText.Place(Root, title, null, new Rect(position, CardSize));
            if (!_hasArtwork)
                Box(card, "Card", new Rect(Vector2.zero, CardSize), new Color32(56, 35, 23, 255));
            var accent = Box(card, "Focus Accent", new Rect(0.5f, 0f, CardSize.x - 12.5f, 2.4f), Orange);

            var display = Label(card, "Value", "", new Rect(158f, 16f, 219f, 54f), 46, ValueInk,
                TextAnchor.MiddleRight);
            display.rectTransform.pivot = Vector2.one;
            display.rectTransform.anchoredPosition += Vector2.right * display.rectTransform.sizeDelta.x;
            display.rectTransform.localScale = new Vector3(0.90f, 1f, 1f);
            Label(card, "Label", title, new Rect(22f, 84f, 352f, 31f), 18, Ink);
            Label(card, "Minimum", FormatPercent(min), new Rect(22f, 158f, 130f, 24f), 12, SecondaryInk);
            Label(card, "Maximum", FormatPercent(max), new Rect(244f, 158f, 130f, 24f), 12, SecondaryInk,
                TextAnchor.MiddleRight);

            var hit = Box(card, "Slider", new Rect(10f, 124f, TrackWidth + 24f, 34f), Color.clear, hitTarget: true);
            var slider = hit.gameObject.AddComponent<Slider>();
            var travel = PlayerStatusHud.CreateRect(hit.rectTransform, "Handle Travel");
            travel.anchorMin = travel.anchorMax = travel.pivot = new Vector2(0f, 1f);
            travel.anchoredPosition = new Vector2(12f, 0f);
            travel.sizeDelta = new Vector2(TrackWidth, 34f);

            var segments = new Image[SegmentCount];
            const float step = (TrackWidth + SegmentGap) / SegmentCount;
            for (int i = 0; i < segments.Length; i++)
            {
                var box = new Rect(step * i, 12f, step - SegmentGap, 11f);
                if (!_hasArtwork)
                    Box(travel, "Empty Segment", box, new Color32(103, 74, 49, 255));
                segments[i] = Box(travel, "Filled Segment", box, Orange);
            }

            var handle = Box(travel, "Thumb", new Rect(0f, 0f, 4f, 22f), new Color32(255, 227, 190, 255));
            var thumb = handle.rectTransform;
            thumb.anchorMin = new Vector2(0f, 0f);
            thumb.anchorMax = new Vector2(0f, 1f);
            thumb.pivot = new Vector2(0.5f, 0.5f);
            thumb.sizeDelta = new Vector2(4f, 22f - travel.sizeDelta.y);
            thumb.anchoredPosition = new Vector2(0f, -0.5f);
            Box(thumb, "Thumb Edge", new Rect(-2f, 0f, 2f, 22f), new Color32(251, 153, 76, 189));

            slider.handleRect = thumb;
            slider.targetGraphic = accent;
            slider.colors = FocusColors();
            slider.maxValue = max;
            slider.minValue = min;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(initial);
            Refresh();
            slider.onValueChanged.AddListener(value =>
            {
                Refresh();
                onChange.Invoke(Mathf.RoundToInt(value));
            });
            return slider;

            void Refresh()
            {
                float filled = slider.normalizedValue * TrackWidth;
                for (int i = 0; i < segments.Length; i++)
                {
                    float width = Mathf.Clamp(filled - i * step, 0f, step - SegmentGap);
                    segments[i].enabled = width > 0f;
                    segments[i].rectTransform.sizeDelta = new Vector2(width, 11f);
                }
                display.text = FormatPercent(Mathf.RoundToInt(slider.value));
            }
        }

        Button AddBackButton(UnityAction back)
        {
            var hit = Box(Root, "Back", new Rect(765f, 398f, 106f, 41f), Orange, hitTarget: true);
            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            var colors = FocusColors();
            colors.highlightedColor = new Color32(255, 255, 255, 31);
            colors.pressedColor = new Color32(255, 255, 255, 61);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.onClick.AddListener(back);

            Box(hit.rectTransform, "Esc Frame", new Rect(10f, 10f, 34f, 21f), Divider);
            Box(hit.rectTransform, "Esc Keycap", new Rect(11f, 11f, 32f, 19f), Recess);
            Label(hit.rectTransform, "Esc", "Esc", new Rect(10f, 10f, 34f, 21f), 12, ValueInk,
                TextAnchor.MiddleCenter);
            Label(hit.rectTransform, "Back Label", "Назад", new Rect(52f, 5f, 54f, 31f), 15, Ink);
            return button;
        }

        static ColorBlock FocusColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.65f);
            colors.selectedColor = colors.pressedColor = Color.white;
            colors.fadeDuration = 0.12f;
            return colors;
        }

        static string FormatPercent(int value) => value + "%";

        static void SetNavigation(Selectable control, Selectable up, Selectable down)
        {
            control.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down
            };
        }

        Text Label(RectTransform parent, string name, string text, Rect box, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var rect = TypewriterMenuText.Place(parent, name, typeof(Text), box);
            var label = rect.GetComponent<Text>();
            label.font = _font;
            label.fontStyle = _fontStyle;
            label.fontSize = size;
            label.color = color;
            label.text = text;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        static Image Box(RectTransform parent, string name, Rect box, Color color, bool hitTarget = false)
        {
            var rect = TypewriterMenuText.Place(parent, name, typeof(Image), box);
            var image = rect.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = hitTarget;
            if (hitTarget)
                image.canvasRenderer.cullTransparentMesh = false;
            return image;
        }
    }
}
