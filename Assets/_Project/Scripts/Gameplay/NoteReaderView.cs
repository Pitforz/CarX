using UnityEngine;
using UnityEngine.UI;

namespace CarX.Gameplay
{
    internal sealed class NoteReaderView
    {
        internal RectTransform Root { get; }
        readonly RawImage _background;
        readonly UnityEngine.UI.Image _paper;
        Texture2D _capture;

        internal NoteReaderView(RectTransform parent)
        {
            Root = new GameObject("Note Reader", typeof(RectTransform)).GetComponent<RectTransform>();
            Root.SetParent(parent, false);
            Root.anchorMin = Vector2.zero;
            Root.anchorMax = Vector2.one;
            Root.offsetMin = Root.offsetMax = Vector2.zero;
            _background = Image(Root, "Blurred surroundings");
            _background.rectTransform.anchorMin = Vector2.zero;
            _background.rectTransform.anchorMax = Vector2.one;
            _background.rectTransform.offsetMin = _background.rectTransform.offsetMax = Vector2.zero;
            _background.raycastTarget = true;
            var area = new GameObject("Paper area", typeof(RectTransform)).GetComponent<RectTransform>();
            area.SetParent(Root, false);
            area.anchorMin = new Vector2(0.13f, 0.19f);
            area.anchorMax = new Vector2(0.87f, 0.85f);
            area.offsetMin = area.offsetMax = Vector2.zero;
            _paper = new GameObject("Note", typeof(RectTransform), typeof(UnityEngine.UI.Image))
                .GetComponent<UnityEngine.UI.Image>();
            _paper.transform.SetParent(area, false);
            _paper.raycastTarget = false;
            _paper.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            var hint = new GameObject("Close note", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            hint.transform.SetParent(Root, false);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0.085f);
            hint.rectTransform.sizeDelta = new Vector2(420f, 40f);
            hint.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hint.fontSize = 19;
            hint.alignment = TextAnchor.MiddleCenter;
            hint.text = "E / Tab / Esc — закрыть записку";
            hint.raycastTarget = false;
            Root.gameObject.SetActive(false);
        }

        internal void Open(Sprite image, Material blur, Texture2D capture)
        {
            ReleaseCapture();
            _capture = capture;
            _background.texture = capture;
            _background.material = blur;
            _background.color = blur != null ? Color.white : new Color(0.35f, 0.35f, 0.35f);
            _paper.sprite = image;
            _paper.GetComponent<AspectRatioFitter>().aspectRatio = image.rect.width / image.rect.height;
            Root.gameObject.SetActive(true);
        }

        internal void Close()
        {
            Root.gameObject.SetActive(false);
            ReleaseCapture();
        }

        void ReleaseCapture()
        {
            _background.texture = null;
            if (_capture != null)
                Object.Destroy(_capture);
            _capture = null;
        }

        static RawImage Image(Transform parent, string name)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            image.transform.SetParent(parent, false);
            image.raycastTarget = false;
            return image;
        }
    }
}
