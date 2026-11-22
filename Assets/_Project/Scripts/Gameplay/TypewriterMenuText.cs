using UnityEngine;

namespace CarX.Gameplay
{
    internal sealed class TypewriterMenuText
    {
        const string FontPath = "CarX/Fonts/";

        readonly Font _systemFont;
        readonly Font _regularFont;
        readonly Font _boldFont;

        internal TypewriterMenuText(Font font) => _regularFont = _boldFont = font;

        internal TypewriterMenuText()
        {
            if (System.Array.IndexOf(Font.GetOSInstalledFontNames(), "Courier New") >= 0)
                _systemFont = Font.CreateDynamicFontFromOSFont("Courier New", 24);

            _regularFont = _systemFont != null ? _systemFont : Resources.Load<Font>(FontPath + "LiberationMono-Regular");
            if (_regularFont == null)
                _regularFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _boldFont = _systemFont != null ? _systemFont : Resources.Load<Font>(FontPath + "LiberationMono-Bold");
            if (_boldFont == null)
                _boldFont = _regularFont;
        }

        internal UnityEngine.UI.Text MakeLabel(RectTransform parent, string name, string text, Rect box, int size,
            Color color, bool bold = false, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var rect = Place(parent, name, typeof(UnityEngine.UI.Text), box);
            var label = rect.GetComponent<UnityEngine.UI.Text>();
            label.font = bold ? _boldFont : _regularFont;
            label.fontStyle = bold && _boldFont == _regularFont ? FontStyle.Bold : FontStyle.Normal;
            label.fontSize = size;
            label.color = color;
            label.text = text;
            label.alignment = alignment;
            label.supportRichText = false;
            label.raycastTarget = false;
            return label;
        }

        internal static RectTransform Place(RectTransform parent, string name, System.Type component, Rect box)
        {
            var rect = PlayerStatusHud.CreateRect(parent, name, component);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(box.x, -box.y);
            rect.sizeDelta = box.size;
            return rect;
        }

        internal void ReleaseFont()
        {
            if (_systemFont != null)
                Object.Destroy(_systemFont);
        }
    }
}
