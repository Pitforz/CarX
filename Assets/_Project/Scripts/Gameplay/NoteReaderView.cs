using UnityEngine;
using UnityEngine.UI;

namespace CarX.Gameplay
{
    internal sealed class NoteReaderView
    {
        internal RectTransform Root { get; }
        readonly RawImage _background;
        readonly RectTransform _area;
        readonly UnityEngine.UI.Image _paper;
        readonly AspectRatioFitter _aspect;
        readonly Text _hint;
        Texture2D _capture;
        Sprite[] _documents;
        int _page;

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
            _area = new GameObject("Paper area", typeof(RectTransform)).GetComponent<RectTransform>();
            _area.SetParent(Root, false);
            _paper = new GameObject("Document", typeof(RectTransform), typeof(UnityEngine.UI.Image))
                .GetComponent<UnityEngine.UI.Image>();
            _paper.transform.SetParent(_area, false);
            _paper.raycastTarget = false;
            _aspect = _paper.gameObject.AddComponent<AspectRatioFitter>();
            _aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _hint = new GameObject("Close note", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            _hint.transform.SetParent(Root, false);
            _hint.rectTransform.anchorMin = _hint.rectTransform.anchorMax = new Vector2(0.5f, 0.085f);
            _hint.rectTransform.sizeDelta = new Vector2(550f, 40f);
            _hint.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _hint.fontSize = 19;
            _hint.alignment = TextAnchor.MiddleCenter;
            _hint.raycastTarget = false;
            Root.gameObject.SetActive(false);
        }

        internal void Open(Sprite[] documents, Material blur, Texture2D capture)
        {
            ReleaseCapture();
            _capture = capture;
            _background.texture = capture;
            _background.material = blur;
            _background.color = blur != null ? Color.white : new Color(0.35f, 0.35f, 0.35f);
            _documents = documents;
            bool single = documents.Length == 1;
            _area.anchorMin = new Vector2(0.13f, single ? 0.19f : 0.145f);
            _area.anchorMax = new Vector2(0.87f, single ? 0.85f : 0.95f);
            _area.offsetMin = _area.offsetMax = Vector2.zero;
            ShowPage(0);
            Root.gameObject.SetActive(true);
        }

        internal void TurnPage(int direction)
        {
            if (_documents == null || _documents.Length < 2 || direction == 0)
                return;
            int page = Mathf.Clamp(_page + direction, 0, _documents.Length - 1);
            if (page != _page)
                ShowPage(page);
        }

        void ShowPage(int page)
        {
            _page = page;
            Sprite image = _documents[page];
            _paper.sprite = image;
            _aspect.aspectRatio = image.rect.width / image.rect.height;
            _hint.text = _documents.Length == 1
                ? "E / Tab / Esc — закрыть записку"
                : $"{page + 1}/{_documents.Length}    ← / → — листать    E / Tab / Esc — закрыть";
        }

        internal void Close()
        {
            Root.gameObject.SetActive(false);
            _documents = null;
            _paper.sprite = null;
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
