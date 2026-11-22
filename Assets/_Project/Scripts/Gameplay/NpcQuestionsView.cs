using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CarX.Gameplay
{
    sealed class NpcQuestionsView
    {
        const float Width = 426f;
        const float CompactScale = 0.72f;
        const float TitleHeight = 34f;
        const float MinRowHeight = 34f;
        const float RevealDuration = 0.32f;
        const float FadeOutDuration = 0.21f;
        const float RevealSlide = 14f;
        static readonly Color32 Accent = new(225, 168, 108, 255);
        static readonly Color32 Muted = new(213, 207, 195, 255);
        static readonly Color32 Selected = new(255, 244, 225, 255);

        sealed class Row
        {
            internal RectTransform Rect;
            internal UnityEngine.UI.RawImage Fill;
            internal UnityEngine.UI.Image Marker;
            internal UnityEngine.UI.Text Key;
            internal UnityEngine.UI.Text Question;
        }

        readonly RectTransform _root;
        readonly CanvasGroup _visibility;
        readonly Font _font;
        readonly Texture2D _fade;
        readonly UnityEngine.UI.Text _name;
        readonly UnityEngine.UI.Text _controls;
        readonly List<Row> _rows = new();
        readonly System.Action<int> _choose;
        int _count;
        int _selected;
        float _targetScale = CompactScale;
        float _scale = CompactScale;
        float _revealProgress;
        bool _shown;
        bool _focused;

        internal int SelectedOption => _selected + 1;

        internal NpcQuestionsView(RectTransform hud, System.Action<int> choose, Font font)
        {
            _choose = choose;
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _font = font != null ? font : fallback;
            _fade = BuildFade();
            _root = PlayerStatusHud.CreateRect(hud, "NPC Questions", typeof(CanvasGroup));
            _root.pivot = Vector2.zero;
            _visibility = _root.GetComponent<CanvasGroup>();
            _visibility.alpha = 0f;
            _visibility.blocksRaycasts = false;
            _visibility.interactable = false;

            var shade = PlayerStatusHud.CreateRect(_root, "Question Backdrop", typeof(UnityEngine.UI.RawImage))
                .GetComponent<UnityEngine.UI.RawImage>();
            Stretch(shade.rectTransform, Vector2.zero, Vector2.zero);
            shade.texture = _fade;
            shade.color = new Color32(12, 13, 15, 222);
            shade.raycastTarget = false;

            var rule = PlayerStatusHud.CreateRect(_root, "Name Rule", typeof(UnityEngine.UI.Image))
                .GetComponent<UnityEngine.UI.Image>();
            rule.color = Accent;
            rule.raycastTarget = false;
            PlaceFromTop(rule.rectTransform, 16f, 13f, 14f, 1f);

            _name = CreateLabel(_root, "NPC Name", _font, 18, Accent);
            PlaceFromTop(_name.rectTransform, 41f, 0f, Width - 90f, 28f);

            _controls = CreateLabel(_root, "Dialogue Controls", _font, 14, Muted);
            _controls.rectTransform.anchorMin = _controls.rectTransform.anchorMax =
                _controls.rectTransform.pivot = Vector2.zero;
            _controls.rectTransform.anchoredPosition = new Vector2(17f, 3f);
            _controls.rectTransform.sizeDelta = new Vector2(Width - 40f, 24f);
            _root.gameObject.SetActive(false);
        }

        internal void Show(string name, List<string> questions, float proximity, bool resetSelection)
        {
            _targetScale = Mathf.Lerp(CompactScale, 1f, Mathf.SmoothStep(0f, 1f, proximity));
            if (resetSelection)
            {
                _selected = 0;
                if (_revealProgress <= 0f)
                    _scale = _targetScale;
            }
            _shown = true;
            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);
            _name.text = name;
            bool layoutChanged = _count != questions.Count;
            _count = questions.Count;

            while (_rows.Count < _count)
                AddRow(_rows.Count);

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                bool visible = i < _count;
                if (row.Rect.gameObject.activeSelf != visible)
                    row.Rect.gameObject.SetActive(visible);
                if (!visible || row.Question.text == questions[i])
                    continue;

                row.Question.text = questions[i];
                layoutChanged = true;
            }

            if (layoutChanged)
            {
                float top = TitleHeight;
                for (int i = 0; i < _count; i++)
                {
                    var row = _rows[i];
                    float height = Mathf.Max(MinRowHeight, Mathf.Ceil(row.Question.preferredHeight) + 10f);
                    PlaceFromTop(row.Rect, 9f, top, Width - 9f, height);
                    top += height + 2f;
                }
                _root.sizeDelta = new Vector2(Width, top + 31f);
                UpdateControls();
            }
            if (layoutChanged || resetSelection)
                Select(Mathf.Clamp(_selected, 0, Mathf.Max(0, _count - 1)));
        }

        internal void SetFocused(bool focused)
        {
            _focused = focused;
            _visibility.blocksRaycasts = focused;
            _visibility.interactable = focused;
            foreach (var row in _rows)
                row.Key.enabled = focused;
            UpdateControls();
            if (focused)
                _root.SetAsLastSibling();
        }

        void UpdateControls()
        {
            _controls.text = _focused
                ? "ЛКМ или Enter — выбрать   ·   Esc — закрыть"
                : "Стрелки + Enter — выбрать   ·   E — курсор";
        }

        internal void Move(int offset)
        {
            if (_count > 0)
                Select((_selected + offset + _count) % _count);
        }

        void Select(int index)
        {
            _selected = index;
            for (int i = 0; i < _count; i++)
            {
                bool selected = i == index;
                var row = _rows[i];
                row.Fill.enabled = selected;
                row.Marker.enabled = selected;
                row.Question.color = selected ? Selected : Muted;
                row.Key.color = selected ? Accent : Muted;
            }
        }

        internal void Hide(bool immediate = false)
        {
            _shown = false;
            if (_focused)
                SetFocused(false);
            if (!immediate)
                return;

            _revealProgress = 0f;
            _visibility.alpha = 0f;
            _root.gameObject.SetActive(false);
        }

        internal void UpdateLayout(RectTransform hud)
        {
            if (!_root.gameObject.activeSelf || Screen.width <= 0 || Screen.height <= 0)
                return;

            float dt = Time.unscaledDeltaTime;
            float duration = _shown ? RevealDuration : FadeOutDuration;
            _revealProgress = Mathf.MoveTowards(_revealProgress, _shown ? 1f : 0f, dt / duration);
            float reveal = Mathf.SmoothStep(0f, 1f, _revealProgress);
            _visibility.alpha = reveal;
            if (!_shown && _revealProgress <= 0f)
            {
                _root.gameObject.SetActive(false);
                return;
            }

            _scale = Mathf.Lerp(_scale, _targetScale, 1f - Mathf.Exp(-12f * dt));
            Rect safe = Screen.safeArea;
            var screenSize = new Vector2(Screen.width, Screen.height);
            Vector2 safeSize = Vector2.Scale(safe.size / screenSize, hud.rect.size);
            float bottom = Mathf.Min(152f, safeSize.y * 0.21f);
            float fit = Mathf.Clamp01(Mathf.Min((safeSize.x - 28f) / Width,
                (safeSize.y - bottom - 18f) / _root.sizeDelta.y));
            _root.anchorMin = _root.anchorMax = safe.min / screenSize;
            _root.anchoredPosition = new Vector2(26f - RevealSlide * (1f - reveal), bottom);
            _root.localScale = new Vector3(_scale * fit, _scale * fit, 1f);
        }

        void AddRow(int index)
        {
            var row = new Row
            {
                Rect = PlayerStatusHud.CreateRect(_root, $"Question {index + 1}", typeof(UnityEngine.UI.Image))
            };
            PlaceFromTop(row.Rect, 9f, 0f, Width - 9f, MinRowHeight);
            var hit = row.Rect.GetComponent<UnityEngine.UI.Image>();
            hit.color = Color.clear;
            var button = row.Rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = hit;
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            button.onClick.AddListener(() => _choose(index + 1));

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => Select(index));
            row.Rect.gameObject.AddComponent<EventTrigger>().triggers.Add(enter);

            row.Fill = PlayerStatusHud.CreateRect(row.Rect, "Selection Fill", typeof(UnityEngine.UI.RawImage))
                .GetComponent<UnityEngine.UI.RawImage>();
            Stretch(row.Fill.rectTransform, Vector2.zero, Vector2.zero);
            row.Fill.texture = _fade;
            row.Fill.color = new Color32(111, 70, 35, 171);
            row.Fill.raycastTarget = false;
            row.Marker = PlayerStatusHud.CreateRect(row.Rect, "Selection Marker", typeof(UnityEngine.UI.Image))
                .GetComponent<UnityEngine.UI.Image>();
            row.Marker.rectTransform.anchorMin = Vector2.zero;
            row.Marker.rectTransform.anchorMax = Vector2.up;
            row.Marker.rectTransform.offsetMin = new Vector2(1f, 4f);
            row.Marker.rectTransform.offsetMax = new Vector2(3f, -4f);
            row.Marker.color = Accent;
            row.Marker.raycastTarget = false;

            row.Key = CreateLabel(row.Rect, "Shortcut", _font, 15, Muted);
            row.Key.text = index < 9 ? (index + 1).ToString() : "";
            row.Key.enabled = _focused;
            row.Key.rectTransform.anchorMin = Vector2.zero;
            row.Key.rectTransform.anchorMax = Vector2.up;
            row.Key.rectTransform.offsetMin = new Vector2(9f, 0f);
            row.Key.rectTransform.offsetMax = new Vector2(27f, 0f);
            row.Question = CreateLabel(row.Rect, "Question Text", _font, 20, Muted);
            Stretch(row.Question.rectTransform, new Vector2(33f, 0f), new Vector2(-64f, 0f));
            _rows.Add(row);
        }

        static UnityEngine.UI.Text CreateLabel(RectTransform parent, string name, Font font, int size, Color color)
        {
            var label = PlayerStatusHud.CreateLabel(parent, name, font);
            label.fontSize = size;
            label.alignment = TextAnchor.MiddleLeft;
            label.supportRichText = false;
            label.color = color;
            return label;
        }

        static void PlaceFromTop(RectTransform rect, float x, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }

        static Texture2D BuildFade()
        {
            const int width = 128;
            var pixels = new Color32[width];
            for (int i = 0; i < width; i++)
            {
                float fade = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.64f, 1f, i / (width - 1f)));
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(fade * 255f));
            }

            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "Dialogue horizontal fade",
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        internal void Destroy()
        {
            UnityEngine.Object.Destroy(_fade);
            if (_root != null)
                UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}
