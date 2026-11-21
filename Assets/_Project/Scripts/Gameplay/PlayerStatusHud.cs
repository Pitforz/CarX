using System.Collections.Generic;
using UnityEngine;

namespace CarX.Gameplay
{
    [RequireComponent(typeof(PlayerHealth))]
    [DisallowMultipleComponent]
    public class PlayerStatusHud : MonoBehaviour
    {
        const float StatusPadding = 24f;
        const float HealthOnlyHeight = 40f;
        const float BleedingHeight = 26f;
        const float BleedingGap = 5f;
        const float SubtitleDuration = 3f;
        const float DamageFlashAttack = 0.04f;
        const float DamageFlashFade = 0.55f;
        const float DamageBarsDuration = 7f;
        static readonly Vector2 StatusSize = new(200f, 82f);

        PlayerHealth _health;
        readonly List<NPC.HostileNpc> _hostiles = new();
        PlayerStatusZone[] _statusZones;
        RectTransform _hudRect;
        RectTransform _statusPanel;
        RectTransform _barsPanel;
        RectTransform _bleedingPanel;
        Rect _lastSafeArea;
        Vector2 _lastCanvasSize;
        UnityEngine.UI.Slider _healthBar;
        UnityEngine.UI.Slider _flashlightBar;
        UnityEngine.UI.Text _healthLabel;
        UnityEngine.UI.Text _bleedingCount;
        UnityEngine.UI.Text _flashlightLabel;
        UnityEngine.UI.Text _ammoLabel;
        UnityEngine.UI.Text _reloadLabel;
        UnityEngine.UI.Text _aimDot;
        UnityEngine.UI.Text _pickupLabel;
        UnityEngine.UI.Text _subtitleLabel;
        UnityEngine.UI.Text _storySubtitle;
        NPC.NpcSpeechState _subtitleSpeaker;
        UnityEngine.UI.RawImage _damageFlash;
        UnityEngine.UI.Image _criticalDarkness;
        Texture2D _damageTexture;
        int _lastHealth;
        float _damageFlashElapsed;
        float _damageFlashStartAlpha;
        float _damageFlashPeak;
        double _damageBarsVisibleUntil;
        float _subtitleUntil;
        PlayerMenus _menus;
        bool _aiming;
        bool _inventoryOpen;
        int _displayedLoaded = -1;
        int _displayedReserve = -1;

        internal RectTransform HudRect => _hudRect;
        float SubtitleTime => _menus != null ? _menus.UnpausedTime : Time.unscaledTime;

        internal void RegisterHostile(NPC.HostileNpc hostile) => _hostiles.Add(hostile);

        internal void UnregisterHostile(NPC.HostileNpc hostile) => _hostiles.Remove(hostile);

        void Start()
        {
            _health = GetComponent<PlayerHealth>();
            _menus = GetComponent<PlayerMenus>();
            _statusZones = FindObjectsByType<PlayerStatusZone>();

            var hud = new GameObject("Player Status HUD", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler));
            if (hud.scene != gameObject.scene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(hud, gameObject.scene);
            _hudRect = (RectTransform)hud.transform;
            hud.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = hud.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            CreateDamageFlash();
            var darkness = CreateRect(_hudRect, "Critical Darkness", typeof(UnityEngine.UI.Image));
            darkness.anchorMin = Vector2.zero;
            darkness.anchorMax = Vector2.one;
            darkness.offsetMin = darkness.offsetMax = Vector2.zero;
            _criticalDarkness = darkness.GetComponent<UnityEngine.UI.Image>();
            _criticalDarkness.raycastTarget = false;
            SetCriticalDarkness(0f);

            var panel = CreateRect(_hudRect, "Status");
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
            panel.sizeDelta = StatusSize;
            _statusPanel = panel;
            UpdateStatusLayout();

            var bars = CreateRect(panel, "Health and Flashlight", typeof(UnityEngine.UI.VerticalLayoutGroup));
            bars.anchorMin = Vector2.zero;
            bars.anchorMax = Vector2.one;
            bars.offsetMin = bars.offsetMax = Vector2.zero;
            _barsPanel = bars;
            var layout = bars.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = 3;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            CreateBleedingPanel(panel, font);
            _healthLabel = CreateLabel(bars, "Health Label", font);
            _healthBar = CreateBar(bars, "Health", new Color32(225, 83, 82, 255));
            _flashlightLabel = CreateLabel(bars, "Flashlight Label", font);
            _flashlightBar = CreateBar(bars, "Flashlight", new Color32(248, 199, 89, 255));

            var bottomRight = new Vector2(1f, 0f);
            _ammoLabel = CreateLabel(_hudRect, "Pistol Ammo", font);
            _ammoLabel.fontSize = 24;
            _ammoLabel.alignment = TextAnchor.MiddleRight;
            _ammoLabel.rectTransform.anchorMin = _ammoLabel.rectTransform.anchorMax = bottomRight;
            _ammoLabel.rectTransform.pivot = bottomRight;
            _ammoLabel.rectTransform.anchoredPosition = new Vector2(-24f, 24f);
            _ammoLabel.rectTransform.sizeDelta = new Vector2(160f, 30f);
            _ammoLabel.gameObject.SetActive(false);

            _reloadLabel = CreateLabel(_hudRect, "Pistol Reload", font);
            _reloadLabel.text = "Перезарядка";
            _reloadLabel.alignment = TextAnchor.MiddleRight;
            _reloadLabel.rectTransform.anchorMin = _reloadLabel.rectTransform.anchorMax = bottomRight;
            _reloadLabel.rectTransform.pivot = bottomRight;
            _reloadLabel.rectTransform.anchoredPosition = new Vector2(-24f, 56f);
            _reloadLabel.rectTransform.sizeDelta = new Vector2(160f, 22f);
            _reloadLabel.gameObject.SetActive(false);

            _aimDot = CreateLabel(_hudRect, "Aim Dot", font);
            _aimDot.text = "•";
            _aimDot.fontSize = 12;
            _aimDot.alignment = TextAnchor.MiddleCenter;
            _aimDot.rectTransform.anchorMin = _aimDot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _aimDot.rectTransform.anchoredPosition = Vector2.zero;
            _aimDot.rectTransform.sizeDelta = new Vector2(24f, 24f);
            _aimDot.gameObject.SetActive(false);

            _pickupLabel = CreateLabel(_hudRect, "Pickup Hint", font);
            _pickupLabel.alignment = TextAnchor.MiddleCenter;
            _pickupLabel.rectTransform.anchorMin = _pickupLabel.rectTransform.anchorMax =
                _pickupLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _pickupLabel.rectTransform.anchoredPosition = new Vector2(0f, -56f);
            _pickupLabel.rectTransform.sizeDelta = new Vector2(280f, 52f);
            _pickupLabel.gameObject.SetActive(false);

            _subtitleLabel = CreateLabel(_hudRect, "Player Subtitle", font);
            _subtitleLabel.fontSize = 20;
            _subtitleLabel.alignment = TextAnchor.MiddleCenter;
            var subtitleRect = _subtitleLabel.rectTransform;
            subtitleRect.anchorMin = new Vector2(0.15f, 0f);
            subtitleRect.anchorMax = new Vector2(0.85f, 0f);
            subtitleRect.pivot = new Vector2(0.5f, 0f);
            subtitleRect.anchoredPosition = new Vector2(0f, 28f);
            subtitleRect.sizeDelta = new Vector2(0f, 52f);
            _subtitleLabel.gameObject.SetActive(false);

            _storySubtitle = CreateLabel(_hudRect, "Story Subtitle", font);
            _storySubtitle.fontSize = 23;
            _storySubtitle.alignment = TextAnchor.LowerCenter;
            var storyRect = _storySubtitle.rectTransform;
            storyRect.anchorMin = new Vector2(0.17f, 0f);
            storyRect.anchorMax = new Vector2(0.83f, 0f);
            storyRect.pivot = new Vector2(0.5f, 0f);
            storyRect.anchoredPosition = new Vector2(0f, 72f);
            storyRect.sizeDelta = new Vector2(0f, 116f);
            _storySubtitle.gameObject.SetActive(false);

            _lastHealth = _health.CurrentHealth;
            _health.HealthChanged += Refresh;
            _health.BleedingChanged += RefreshBleeding;
            _health.DirectDamageTaken += RevealDamageBars;
            Refresh(_health.CurrentHealth);
            RefreshBleeding(_health.Bleeding);
            UpdateBarsVisibility();
        }

        void LateUpdate()
        {
            UpdateStatusLayout();
            UpdateBarsVisibility();
            UpdateDamageFlash();
            if (_subtitleLabel != null && _subtitleLabel.gameObject.activeSelf && SubtitleTime >= _subtitleUntil)
            {
                _subtitleLabel.gameObject.SetActive(false);
                ClearSubtitleSpeaker();
            }
        }

        void UpdateBarsVisibility()
        {
            bool showFlashlight = !_health.IsDead &&
                (Time.timeAsDouble < _damageBarsVisibleUntil || IsStatusRelevant());
            bool showHealth = showFlashlight || (!_health.IsDead && _health.Bleeding > 0);
            if (_barsPanel.gameObject.activeSelf == showHealth && _flashlightBar.gameObject.activeSelf == showFlashlight)
                return;

            _flashlightLabel.gameObject.SetActive(showFlashlight);
            _flashlightBar.gameObject.SetActive(showFlashlight);
            _barsPanel.gameObject.SetActive(showHealth);
            float panelHeight = showFlashlight ? StatusSize.y : HealthOnlyHeight;
            _statusPanel.sizeDelta = new Vector2(StatusSize.x, panelHeight);
            _bleedingPanel.anchoredPosition = Vector2.up * (showHealth ? panelHeight + BleedingGap : 0f);
        }

        void RevealDamageBars() => _damageBarsVisibleUntil = Time.timeAsDouble + DamageBarsDuration;

        bool IsStatusRelevant()
        {
            foreach (var hostile in _hostiles)
                if (hostile != null && hostile.IsEngaged)
                    return true;

            Vector3 position = transform.position;
            foreach (var zone in _statusZones)
                if (zone != null && zone.gameObject.scene == gameObject.scene && zone.Contains(position))
                    return true;

            return false;
        }

        void UpdateStatusLayout()
        {
            if (_statusPanel == null || _hudRect == null || Screen.width <= 0 || Screen.height <= 0)
                return;

            Rect safePixels = Screen.safeArea;
            var safeArea = new Rect(safePixels.x / Screen.width, safePixels.y / Screen.height,
                safePixels.width / Screen.width, safePixels.height / Screen.height);
            Vector2 canvasSize = _hudRect.rect.size;
            if (safeArea == _lastSafeArea && canvasSize == _lastCanvasSize)
                return;

            Vector2 safeSize = Vector2.Scale(safeArea.size, canvasSize);
            if (safeSize.x <= 0f || safeSize.y <= 0f)
                return;

            _lastSafeArea = safeArea;
            _lastCanvasSize = canvasSize;
            Vector2 paddedSize = StatusSize + Vector2.one * (2f * StatusPadding) +
                Vector2.up * (BleedingHeight + BleedingGap);
            float scale = Mathf.Min(1f, Mathf.Min(safeSize.x / paddedSize.x, safeSize.y / paddedSize.y));
            _statusPanel.anchorMin = _statusPanel.anchorMax = safeArea.min;
            _statusPanel.anchoredPosition = Vector2.one * (StatusPadding * scale);
            _statusPanel.localScale = new Vector3(scale, scale, 1f);
        }

        void OnEnable()
        {
            if (_hudRect != null)
                _hudRect.gameObject.SetActive(true);
        }

        void OnDisable()
        {
            ClearSubtitleSpeaker();
            if (_damageFlash != null)
                _damageFlash.enabled = false;
            if (_hudRect != null)
                _hudRect.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_health != null)
            {
                _health.HealthChanged -= Refresh;
                _health.BleedingChanged -= RefreshBleeding;
                _health.DirectDamageTaken -= RevealDamageBars;
            }
            if (_hudRect != null)
                Destroy(_hudRect.gameObject);
            if (_damageTexture != null)
                Destroy(_damageTexture);
        }

        public void ShowPistol(bool equipped, bool aiming, int loaded, int reserve, bool reloading)
        {
            if (_ammoLabel == null)
                return;

            _ammoLabel.gameObject.SetActive(equipped);
            _aiming = equipped && aiming;
            RefreshAimDot();
            _reloadLabel.gameObject.SetActive(equipped && reloading);
            if (_displayedLoaded == loaded && _displayedReserve == reserve)
                return;

            _displayedLoaded = loaded;
            _displayedReserve = reserve;
            _ammoLabel.text = $"{loaded} / {reserve}";
        }

        internal void SetInventoryOpen(bool open)
        {
            _inventoryOpen = open;
            if (open && _subtitleLabel != null)
                _subtitleLabel.transform.SetAsLastSibling();
            RefreshAimDot();
        }

        internal void ShowSubtitle(string text, NPC.NpcSpeechState speaker = null, float duration = SubtitleDuration)
        {
            if (_subtitleLabel == null)
                return;

            ClearSubtitleSpeaker();
            _subtitleSpeaker = speaker;
            if (_subtitleSpeaker != null)
                _subtitleSpeaker.SetTalking(true);
            _subtitleUntil = SubtitleTime + duration;
            _subtitleLabel.text = text;
            _subtitleLabel.transform.SetAsLastSibling();
            _subtitleLabel.gameObject.SetActive(true);
        }

        void ClearSubtitleSpeaker()
        {
            if (_subtitleSpeaker != null)
                _subtitleSpeaker.SetTalking(false);
            _subtitleSpeaker = null;
        }

        internal void SetStorySubtitle(string text, Color color)
        {
            if (_storySubtitle == null)
                return;

            _storySubtitle.text = text;
            _storySubtitle.color = color;
            _storySubtitle.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        internal void ShowPickupHint(string text)
        {
            if (_pickupLabel == null)
                return;

            _pickupLabel.text = text;
            _pickupLabel.gameObject.SetActive(!string.IsNullOrEmpty(text));
            RefreshAimDot();
        }

        void RefreshAimDot()
        {
            if (_aimDot != null)
                _aimDot.gameObject.SetActive(!_inventoryOpen && (_aiming || _pickupLabel.gameObject.activeSelf));
        }

        void Refresh(int health)
        {
            if (health < _lastHealth && isActiveAndEnabled)
                ShowDamageFlash(_lastHealth - health);
            _lastHealth = health;
            _healthBar.SetValueWithoutNotify(health / (float)PlayerHealth.MaxHealth);
            _healthLabel.text = $"Здоровье: {health}/{PlayerHealth.MaxHealth}";
            RefreshFlashlight();
        }

        internal void RefreshFlashlight()
        {
            float charge = _health.FlashlightCharge;
            _flashlightBar.SetValueWithoutNotify(charge);
            _flashlightLabel.text = $"Фонарик: {Mathf.RoundToInt(charge * 100f)}%";
        }

        void CreateBleedingPanel(RectTransform status, Font font)
        {
            _bleedingPanel = CreateRect(status, "Bleeding", typeof(UnityEngine.UI.Image));
            _bleedingPanel.anchorMin = Vector2.zero;
            _bleedingPanel.anchorMax = Vector2.right;
            _bleedingPanel.pivot = new Vector2(0.5f, 0f);
            _bleedingPanel.sizeDelta = new Vector2(0f, BleedingHeight);
            _bleedingPanel.anchoredPosition = Vector2.up * (StatusSize.y + BleedingGap);
            var background = _bleedingPanel.GetComponent<UnityEngine.UI.Image>();
            background.color = new Color32(166, 42, 32, 245);
            background.raycastTarget = false;

            var title = CreateLabel(_bleedingPanel, "Bleeding Label", font);
            title.text = "КРОВОТЕЧЕНИЕ";
            title.fontSize = 14;
            title.fontStyle = FontStyle.Bold;
            title.alignment = TextAnchor.MiddleLeft;
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = new Vector2(0.76f, 1f);
            title.rectTransform.offsetMin = new Vector2(8f, 0f);
            title.rectTransform.offsetMax = Vector2.zero;

            _bleedingCount = CreateLabel(_bleedingPanel, "Bleeding Count", font);
            _bleedingCount.fontStyle = FontStyle.Bold;
            _bleedingCount.alignment = TextAnchor.MiddleRight;
            _bleedingCount.rectTransform.anchorMin = new Vector2(0.76f, 0f);
            _bleedingCount.rectTransform.anchorMax = Vector2.one;
            _bleedingCount.rectTransform.offsetMin = Vector2.zero;
            _bleedingCount.rectTransform.offsetMax = new Vector2(-8f, 0f);
            _bleedingPanel.gameObject.SetActive(false);
        }

        void RefreshBleeding(int amount)
        {
            _bleedingCount.text = amount.ToString();
            _bleedingPanel.gameObject.SetActive(amount > 0);
        }

        internal void SetCriticalDarkness(float amount)
        {
            if (_criticalDarkness == null)
                return;

            _criticalDarkness.color = new Color(0f, 0f, 0f, amount);
            bool visible = amount > 0f;
            if (_criticalDarkness.enabled != visible)
                _criticalDarkness.enabled = visible;
        }

        void CreateDamageFlash()
        {
            // Fade in only where the horizontal and vertical edges meet.
            const int size = 128;
            const float innerEdge = 0.8f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float edgeY = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(innerEdge, 1f, Mathf.Abs(y * 2f / (size - 1) - 1f)));
                for (int x = 0; x < size; x++)
                {
                    float edgeX = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(innerEdge, 1f, Mathf.Abs(x * 2f / (size - 1) - 1f)));
                    float alpha = edgeX * edgeY;
                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            _damageTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Damage Screen Corners",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _damageTexture.SetPixels32(pixels);
            _damageTexture.Apply(false, true);

            // First sibling keeps health, ammo, subtitles and menus readable above the flash.
            var rect = CreateRect(_hudRect, "Damage Flash", typeof(UnityEngine.UI.RawImage));
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.SetAsFirstSibling();
            _damageFlash = rect.GetComponent<UnityEngine.UI.RawImage>();
            _damageFlash.texture = _damageTexture;
            _damageFlash.color = new Color(0.8f, 0.02f, 0.01f, 0f);
            _damageFlash.raycastTarget = false;
            _damageFlash.enabled = false;
        }

        void ShowDamageFlash(int damage)
        {
            if (_damageFlash == null)
                return;

            _damageFlashStartAlpha = _damageFlash.enabled ? _damageFlash.color.a : 0f;
            _damageFlashPeak = Mathf.Max(_damageFlashStartAlpha,
                Mathf.Clamp(0.25f + damage * 0.015f, 0.25f, 0.65f));
            _damageFlashElapsed = 0f;
            _damageFlash.enabled = true;
        }

        void UpdateDamageFlash()
        {
            if (_damageFlash == null || !_damageFlash.enabled)
                return;

            // Keep fading when a pause menu opens; repeated hits continue from the current opacity.
            _damageFlashElapsed += Time.unscaledDeltaTime;
            float alpha = _damageFlashElapsed < DamageFlashAttack
                ? Mathf.SmoothStep(_damageFlashStartAlpha, _damageFlashPeak,
                    _damageFlashElapsed / DamageFlashAttack)
                : Mathf.SmoothStep(_damageFlashPeak, 0f,
                    (_damageFlashElapsed - DamageFlashAttack) / DamageFlashFade);
            var color = _damageFlash.color;
            color.a = alpha;
            _damageFlash.color = color;
            if (_damageFlashElapsed >= DamageFlashAttack + DamageFlashFade)
                _damageFlash.enabled = false;
        }

        internal static UnityEngine.UI.Text CreateLabel(RectTransform parent, string name, Font font)
        {
            var rect = CreateRect(parent, name, typeof(UnityEngine.UI.Text));
            var label = rect.GetComponent<UnityEngine.UI.Text>();
            label.font = font;
            label.fontSize = 16;
            label.color = Color.white;
            label.raycastTarget = false;
            var shadow = rect.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = Color.black;
            return label;
        }

        static UnityEngine.UI.Slider CreateBar(RectTransform parent, string name, Color color)
        {
            var rect = CreateRect(parent, name, typeof(UnityEngine.UI.Image));
            rect.GetComponent<UnityEngine.UI.Image>().color = new Color32(21, 23, 28, 230);
            rect.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 12;

            var fill = CreateRect(rect, "Fill", typeof(UnityEngine.UI.Image));
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            var image = fill.GetComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;

            var bar = rect.gameObject.AddComponent<UnityEngine.UI.Slider>();
            bar.fillRect = fill;
            bar.interactable = false;
            bar.transition = UnityEngine.UI.Selectable.Transition.None;
            return bar;
        }

        internal static RectTransform CreateRect(Transform parent, string name, System.Type component = null)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            if (component != null)
                rect.gameObject.AddComponent(component);
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
