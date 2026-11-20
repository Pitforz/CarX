using UnityEngine;

namespace CarX.Gameplay
{
    internal sealed class InventoryWheelView
    {
        const float WheelSize = 512f;
        const float SectorDegrees = 360f / PlayerInventory.SlotCount;
        const float PointerDeadZone = 36f;
        const float SectorBoundaryMargin = 3f;
        const float HighlightFadeTime = 0.04f;
        static readonly Color Ivory = new Color32(241, 230, 211, 255);
        static readonly Color Amber = new Color32(241, 188, 118, 255);
        static readonly Color Muted = new Color32(172, 163, 150, 255);

        readonly RectTransform _canvas;
        readonly RectTransform _root;
        readonly RectTransform _wheel;
        readonly UnityEngine.UI.Text _controlHints;
        readonly UnityEngine.UI.Image[] _selectionOverlays = new UnityEngine.UI.Image[PlayerInventory.SlotCount];
        readonly UnityEngine.UI.Image[] _occupiedSectors = new UnityEngine.UI.Image[PlayerInventory.SlotCount];
        readonly UnityEngine.UI.Image[] _clueIcons = new UnityEngine.UI.Image[PlayerInventory.SlotCount];
        readonly GameObject _magazineIcon;
        readonly UnityEngine.UI.Text[] _numbers = new UnityEngine.UI.Text[PlayerInventory.SlotCount];
        readonly UnityEngine.UI.Text[] _captions = new UnityEngine.UI.Text[PlayerInventory.SlotCount];
        readonly UnityEngine.UI.Image[] _emptyMarks = new UnityEngine.UI.Image[PlayerInventory.SlotCount];
        readonly UnityEngine.UI.Text _centerSlot;
        readonly UnityEngine.UI.Text _centerItem;
        readonly UnityEngine.UI.Text _centerHint;
        readonly Font _font;
        int _selectedSlot;

        internal InventoryWheelView(RectTransform canvas, Sprite wheelBase, Sprite occupiedSector,
            Sprite selectedSector, Sprite pistolIcon, Sprite magazineIcon)
        {
            _canvas = canvas;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _root = PlayerStatusHud.CreateRect(canvas, "Inventory", typeof(UnityEngine.UI.Image));
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;
            var backdrop = _root.GetComponent<UnityEngine.UI.Image>();
            backdrop.color = new Color32(10, 10, 9, 125);
            backdrop.raycastTarget = false;

            var dimensions = new Vector2(WheelSize, WheelSize);
            var baseImage = CreateImage(_root, "Wheel Base", wheelBase, dimensions, Vector2.zero);
            baseImage.color = new Color32(255, 255, 255, 226);
            _wheel = baseImage.rectTransform;
            for (int i = PlayerInventory.MagazineSlot; i < _occupiedSectors.Length; i++)
            {
                var occupied = CreateImage(_wheel, $"Occupied Slot {i + 1}", occupiedSector, dimensions, Vector2.zero);
                occupied.color = new Color32(255, 255, 255, 208);
                occupied.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -i * SectorDegrees);
                _occupiedSectors[i] = occupied;
            }
            // Fixed overlays keep the outlines aligned with their sectors during a crossfade.
            for (int i = 0; i < _selectionOverlays.Length; i++)
            {
                var overlay = CreateImage(_wheel, $"Slot {i + 1} Highlight", selectedSector, dimensions, Vector2.zero);
                overlay.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -i * SectorDegrees);
                _selectionOverlays[i] = overlay;
            }

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                float angle = i * SectorDegrees * Mathf.Deg2Rad;
                var center = 185f * new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                var number = CreateText(_wheel, "Slot Number", (i + 1).ToString(), 12,
                    center + Vector2.up * 29f, new Vector2(30f, 20f));
                number.fontStyle = FontStyle.Bold;
                _numbers[i] = number;
                _captions[i] = CreateText(_wheel, "Slot Item", "", 12, center - Vector2.up * 30f,
                    new Vector2(104f, 20f));
                _captions[i].fontStyle = FontStyle.Bold;
                _emptyMarks[i] = CreateImage(_wheel, "Empty Slot", null, new Vector2(16f, 2f),
                    center - Vector2.up * 5f);
                _emptyMarks[i].color = new Color32(118, 110, 99, 255);

                if (i == PlayerInventory.PistolSlot)
                    CreateImage(_wheel, "Pistol Icon", pistolIcon, new Vector2(72f, 48f), center - Vector2.up);
                if (i == PlayerInventory.MagazineSlot)
                    _magazineIcon = CreateImage(_wheel, "Magazine Icon", magazineIcon,
                        new Vector2(26f, 40f), center - Vector2.up).gameObject;
                if (i >= PlayerInventory.FirstClueSlot)
                {
                    var icon = CreateImage(_wheel, "Clue Icon", null, new Vector2(52f, 36f), center - Vector2.up);
                    icon.preserveAspect = true;
                    icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? 7f : -7f);
                    _clueIcons[i] = icon;
                }
            }

            _centerSlot = CreateText(_wheel, "Selected Slot Number", "", 12,
                new Vector2(0f, 28f), new Vector2(176f, 18f));
            _centerSlot.fontStyle = FontStyle.Bold;
            _centerSlot.color = Amber;
            _centerItem = CreateText(_wheel, "Selected Item Name", "", 25, Vector2.zero, new Vector2(188f, 34f));
            _centerItem.fontStyle = FontStyle.Bold;
            _centerHint = CreateText(_wheel, "Selected Item Detail", "", 13,
                new Vector2(0f, -31f), new Vector2(172f, 20f));
            _centerHint.color = Muted;

            CreateText(_wheel, "Select Item Hint", "ЛКМ — выбрать предмет", 16,
                new Vector2(0f, WheelSize * 0.5f + 18f), new Vector2(260f, 24f));

            _controlHints = CreateText(_root, "Drop Item Hint", "Q — выбросить предмет", 16,
                Vector2.zero, new Vector2(260f, 26f));
            _controlHints.alignment = TextAnchor.UpperRight;
            _controlHints.rectTransform.pivot = Vector2.one;
            _root.gameObject.SetActive(false);
        }

        internal void SetVisible(bool visible)
        {
            if (_root == null)
                return;

            _root.gameObject.SetActive(visible);
            if (visible)
            {
                FitToScreen();
                for (int i = 0; i < _selectionOverlays.Length; i++)
                    SetHighlightAlpha(i, i == _selectedSlot ? 1f : 0f);
            }
        }

        internal void Refresh(int selected, int? spareRounds, bool pistolEquipped, bool magazineHeld,
            ClueNote[] clues, bool carryingToolbox)
        {
            bool magazinePresent = spareRounds.HasValue;
            _selectedSlot = selected;
            _magazineIcon.SetActive(magazinePresent);

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                var clue = clues[i];
                string name = GetItemName(i, magazinePresent, clue);
                _captions[i].text = name;
                _emptyMarks[i].enabled = string.IsNullOrEmpty(name);
                if (_occupiedSectors[i] != null)
                    _occupiedSectors[i].enabled = !string.IsNullOrEmpty(name);
                if (_clueIcons[i] != null)
                {
                    _clueIcons[i].sprite = clue != null ? clue.Paper : null;
                    _clueIcons[i].enabled = clue != null;
                }
            }

            var selectedClue = clues[selected];
            _centerSlot.text = $"СЛОТ {selected + 1}";
            string selectedName = GetItemName(selected, magazinePresent, selectedClue);
            _centerItem.text = string.IsNullOrEmpty(selectedName) ? "Пусто" : selectedName;
            _controlHints.text = carryingToolbox ? "Q — поставить ящик" :
                selectedClue != null ? "" : "Q — выбросить предмет";
            if (selected == PlayerInventory.PistolSlot)
                _centerHint.text = pistolEquipped ? "ЛКМ — убрать" : "ЛКМ — достать";
            else if (selected == PlayerInventory.MagazineSlot && magazinePresent)
            {
                string action = magazineHeld ? "ЛКМ — убрать" : "ЛКМ — взять";
                _centerHint.text = $"{spareRounds.Value}/{PlayerPistol.MagazineSize} · {action}";
            }
            else
                _centerHint.text = selectedClue != null ? "ЛКМ — прочитать" : "";
        }

        internal void UpdateVisuals(float deltaTime)
        {
            float blend = 1f - Mathf.Exp(-deltaTime / HighlightFadeTime);
            for (int i = 0; i < _selectionOverlays.Length; i++)
            {
                float current = _selectionOverlays[i].color.a;
                float target = i == _selectedSlot ? 1f : 0f;
                if (current == target)
                    continue;

                float alpha = Mathf.Lerp(current, target, blend);
                SetHighlightAlpha(i, Mathf.Abs(alpha - target) < 0.01f ? target : alpha);
            }
        }

        void SetHighlightAlpha(int slot, float alpha)
        {
            var overlay = _selectionOverlays[slot];
            overlay.color = new Color(1f, 1f, 1f, alpha);
            overlay.enabled = alpha > 0f;
            _numbers[slot].color = Color.Lerp(Muted, Amber, alpha);
        }

        internal int GetSlotAtPointer(Vector2 screenPosition, int currentSlot)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _wheel, screenPosition, null, out Vector2 localPosition) ||
                localPosition.sqrMagnitude < PointerDeadZone * PointerDeadZone)
                return -1;

            float clockwiseAngle = Mathf.Atan2(localPosition.x, localPosition.y) * Mathf.Rad2Deg;
            float angleFromCurrent = Mathf.DeltaAngle(currentSlot * SectorDegrees, clockwiseAngle);
            if (Mathf.Abs(angleFromCurrent) < SectorDegrees * 0.5f + SectorBoundaryMargin)
                return currentSlot;

            float angle = Mathf.Repeat(clockwiseAngle + SectorDegrees * 0.5f, 360f);
            return Mathf.FloorToInt(angle / SectorDegrees) % PlayerInventory.SlotCount;
        }

        internal void FitToScreen()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return;

            Rect safe = Screen.safeArea;
            Vector2 displaySize = new(Screen.width, Screen.height);
            Vector2 safeMin = safe.min / displaySize;
            Vector2 safeMax = safe.max / displaySize;
            var safeSize = Vector2.Scale(_canvas.rect.size, safeMax - safeMin);
            float scale = Mathf.Clamp01(Mathf.Min(safeSize.x, safeSize.y) / (WheelSize + 88f));
            Vector2 center = (safeMin + safeMax) * 0.5f;
            _wheel.anchorMin = _wheel.anchorMax = center;
            _wheel.anchoredPosition = Vector2.zero;
            _wheel.localScale = new Vector3(scale, scale, 1f);
            var hintsRect = _controlHints.rectTransform;
            hintsRect.anchorMin = hintsRect.anchorMax = safeMax;
            float hintScale = Mathf.Clamp01(safeSize.x / (hintsRect.sizeDelta.x + 48f));
            hintsRect.anchoredPosition = new Vector2(-24f, -18f) * hintScale;
            hintsRect.localScale = new Vector3(hintScale, hintScale, 1f);
        }

        static string GetItemName(int slot, bool magazinePresent, ClueNote clue)
        {
            if (slot == PlayerInventory.PistolSlot)
                return "Пистолет";
            if (clue != null)
                return clue.Title;
            return slot == PlayerInventory.MagazineSlot && magazinePresent ? "Магазин" : "";
        }

        UnityEngine.UI.Text CreateText(RectTransform parent, string name, string text, int fontSize,
            Vector2 position, Vector2 size)
        {
            var label = PlayerStatusHud.CreateLabel(parent, name, _font);
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Ivory;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot =
                new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = size;
            return label;
        }

        static UnityEngine.UI.Image CreateImage(RectTransform parent, string name, Sprite sprite,
            Vector2 size, Vector2 position)
        {
            var rect = PlayerStatusHud.CreateRect(parent, name, typeof(UnityEngine.UI.Image));
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = rect.GetComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        internal void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root.gameObject);
        }
    }
}
