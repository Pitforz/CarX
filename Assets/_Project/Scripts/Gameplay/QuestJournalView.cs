using UnityEngine;
using UnityEngine.Events;

namespace CarX.Gameplay
{
    internal sealed class QuestJournalView
    {
        const string ArtPath = "CarX/QuestJournal/";
        static readonly Vector2 PageSize = new(1280f, 720f);
        static readonly Color Ink = new Color32(42, 33, 24, 255);
        static readonly Color SoftInk = new Color32(74, 59, 43, 255);
        static readonly Color RustInk = new Color32(118, 62, 31, 255);

        readonly TypewriterMenuText _text;
        string _testTitle;
        string _testDescription;
        readonly UnityEngine.UI.Button _questButton;
        readonly UnityEngine.UI.Text _trackingStatus;
        readonly UnityEngine.UI.Text _questRowTitle;
        readonly UnityEngine.UI.Button _deniQuestButton;
        readonly UnityEngine.UI.Text _deniTrackingStatus;
        readonly UnityEngine.UI.Text _objectiveTitle;
        readonly UnityEngine.UI.Text _objectiveDescription;

        internal RectTransform Root { get; }

        internal QuestJournalView(RectTransform parent, TypewriterMenuText text, string title, string description,
            UnityAction trackQuest, UnityAction trackDeniQuest)
        {
            _text = text;
            _testTitle = title;
            _testDescription = description;
            Root = PlayerStatusHud.CreateRect(parent, "Quest Journal", typeof(UnityEngine.UI.RawImage));
            Root.anchorMin = Root.anchorMax = Root.pivot = new Vector2(0.5f, 0.5f);
            Root.anchoredPosition = Vector2.zero;
            Root.sizeDelta = PageSize;
            var paper = Root.GetComponent<UnityEngine.UI.RawImage>();
            paper.texture = Resources.Load<Texture2D>(ArtPath + "Paper");
            paper.color = paper.texture != null ? Color.white : new Color32(221, 193, 148, 255);
            paper.raycastTarget = false;

            _text.MakeLabel(Root, "Notebook Label", "ПОЛЕВОЙ ДНЕВНИК", new Rect(206, 93, 296, 28), 14, SoftInk, bold: true);
            _text.MakeLabel(Root, "Journal Title", "Задания", new Rect(205, 136, 305, 48), 34, Ink, bold: true);

            _questButton = AddQuestRow(214f, title, trackQuest, out var trackingStatus, out _questRowTitle);
            _trackingStatus = trackingStatus;
            _deniQuestButton = AddQuestRow(290f, "Инструменты Дени", trackDeniQuest, out var deniTrackingStatus, out _);
            _deniTrackingStatus = deniTrackingStatus;

            _text.MakeLabel(Root, "Objective Label", "ТЕКУЩАЯ ЦЕЛЬ", new Rect(546, 138, 526, 28), 14, RustInk, bold: true);
            _objectiveTitle = _text.MakeLabel(Root, "Objective Title", title, new Rect(546, 167, 524, 38), 28, Ink,
                bold: true, alignment: TextAnchor.UpperLeft);
            _objectiveDescription = _text.MakeLabel(Root, "Quest Description", description,
                new Rect(546, 214, 524, 330), 18, SoftInk,
                alignment: TextAnchor.UpperLeft);
            SetQuests(false, false, false, false, "", "");

            MakeKey("ЛКМ", 206, 41);
            _text.MakeLabel(Root, "Choose Quest Hint", "выбрать задание", new Rect(255, 610, 285, 27), 16, SoftInk);
            MakeKey("T", 885, 27);
            _text.MakeLabel(Root, "Key Hint Separator", "или", new Rect(919, 610, 30, 27), 15, SoftInk);
            MakeKey("Esc", 947, 37);
            _text.MakeLabel(Root, "Close Journal Hint", "закрыть", new Rect(994, 610, 90, 27), 16, SoftInk);
        }

        UnityEngine.UI.Button AddQuestRow(float top, string title, UnityAction trackQuest, out UnityEngine.UI.Text status,
            out UnityEngine.UI.Text titleLabel)
        {
            var row = TypewriterMenuText.Place(Root, title, typeof(UnityEngine.UI.RawImage), new Rect(203, top, 291, 72));
            var mark = row.GetComponent<UnityEngine.UI.RawImage>();
            mark.texture = Resources.Load<Texture2D>(ArtPath + "Selection");
            mark.color = mark.texture != null ? Color.white : new Color32(174, 104, 42, 95);
            var button = row.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = mark;
            button.onClick.AddListener(trackQuest);
            titleLabel = _text.MakeLabel(row, "Quest Title", title, new Rect(19, 7, 254, 35), 22, Ink, bold: true);
            status = _text.MakeLabel(row, "Tracking Status", "", new Rect(19, 40, 254, 21), 15, RustInk, bold: true);
            return button;
        }

        internal void SetMainQuest(string title, string description)
        {
            _testTitle = title;
            _testDescription = description;
            _questRowTitle.text = title;
        }

        internal void SetQuests(bool testTracked, bool deniAvailable, bool deniSelected, bool deniCompleted,
            string deniTitle, string deniDescription)
        {
            _deniQuestButton.gameObject.SetActive(deniAvailable);
            SetRowState(_questButton, _trackingStatus, testTracked, testTracked ? "• Отслеживается" : "");
            SetRowState(_deniQuestButton, _deniTrackingStatus, deniSelected,
                deniCompleted ? "Выполнено" : deniSelected ? "• Отслеживается" : "");
            _objectiveTitle.text = deniSelected ? deniTitle : _testTitle;
            _objectiveTitle.rectTransform.sizeDelta = new Vector2(524f, deniSelected ? 72f : 38f);
            _objectiveDescription.text = deniSelected ? deniDescription : _testDescription;
            _objectiveDescription.rectTransform.anchoredPosition = new Vector2(546f, deniSelected ? -247f : -214f);
        }

        static void SetRowState(UnityEngine.UI.Button button, UnityEngine.UI.Text label, bool selected, string status)
        {
            label.text = status;
            label.gameObject.SetActive(status.Length > 0);
            var colors = UnityEngine.UI.ColorBlock.defaultColorBlock;
            colors.normalColor = new Color(1f, 1f, 1f, selected ? 1f : 0.42f);
            colors.highlightedColor = new Color(1f, 1f, 1f, selected ? 1f : 0.72f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = Color.white;
            colors.fadeDuration = 0.12f;
            button.colors = colors;
        }

        internal void Fit(Vector2 safeCenter, Vector2 safeSize)
        {
            const float margin = 24f;
            float scale = Mathf.Clamp01(Mathf.Min((safeSize.x - margin) / PageSize.x, (safeSize.y - margin) / PageSize.y));
            Root.anchorMin = Root.anchorMax = safeCenter;
            Root.localScale = new Vector3(scale, scale, 1f);
        }

        void MakeKey(string key, float x, float width)
        {
            var rect = TypewriterMenuText.Place(Root, $"{key} Key", typeof(UnityEngine.UI.Image), new Rect(x, 612, width, 23));
            var face = rect.GetComponent<UnityEngine.UI.Image>();
            face.color = new Color32(245, 220, 174, 37);
            face.raycastTarget = false;
            var edge = rect.gameObject.AddComponent<UnityEngine.UI.Outline>();
            edge.effectColor = new Color32(90, 70, 47, 63);
            edge.effectDistance = new Vector2(1f, -1f);
            edge.useGraphicAlpha = false;
            _text.MakeLabel(rect, "Key Label", key, new Rect(0, 0, width, 23), 14, Ink, bold: true,
                alignment: TextAnchor.MiddleCenter);
        }
    }
}
