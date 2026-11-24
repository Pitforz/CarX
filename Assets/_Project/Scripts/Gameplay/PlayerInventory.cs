using StarterAssets;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerPistol))]
    [RequireComponent(typeof(PlayerInnerVoice))]
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerInventory : MonoBehaviour
    {
        internal const int SlotCount = 7;
        internal const int PistolSlot = 0;
        internal const int MagazineSlot = 1;
        internal const int FirstClueSlot = 2;
        internal const int LowerFloorKeySlot = SlotCount - 1;
        const float PointerResumeDistance = 3f;
        const float PickupDistance = 3f;
        const float PickupAimRadius = 0.085f;
        const float DropDistance = 1f;
        const float DropRadius = 0.15f;
        const int WorldLayers = Physics.DefaultRaycastLayers;
        internal const string HandsFullSubtitle = "Сначала поставь ящик на Q";
        internal const string LowerFloorKeyName = "Ключ от второго этажа";

        [SerializeField] Sprite _wheelBase;
        [SerializeField] Sprite _occupiedSector;
        [SerializeField] Sprite _selectedSector;
        [SerializeField] Sprite _pistolIcon;
        [SerializeField] Sprite _magazineIcon;
        [SerializeField] DroppedMagazine _droppedMagazinePrefab;
        [SerializeField] Shader _pickupShader;
        [SerializeField] GameObject _keyInspectionHand;

        readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly RaycastHit[] _pickupSightHits = new RaycastHit[8];
        readonly Collider[] _overlaps = new Collider[16];
        readonly ClueNote[] _clues = new ClueNote[SlotCount];
        int? _spareRounds = PlayerPistol.MagazineSize;
        FirstPersonController _controller;
        StarterAssetsInputs _inputs;
        PlayerPistol _pistol;
        PlayerStatusHud _hud;
        PlayerHealth _health;
        PlayerMenus _menus;
        PlayerInnerVoice _innerVoice;
        InventoryWheelView _wheel;
        MonoBehaviour _focusedPickup;
        ToolboxPickup _carriedToolbox;
        GameObject _heldMagazine;
        Material _heldMagazineMaterial;
        Sprite _lowerFloorKeyIcon;
        PlayerKeyView _keyView;
        bool _keyViewPending;
        bool _open;
        int _selectedSlot;
        int _closedFrame = -1;
        Vector2? _pointerAtKeyboardSelection;
        CursorLockMode _previousCursorLock;
        bool _previousCursorVisible;
        bool _previousLookCursorLock;

        internal int ReserveRounds => _spareRounds ?? 0;
        internal bool IsOpen => _open;
        internal bool IsCarryingTools => _carriedToolbox != null;
        internal bool IsCarryingToolbox(ToolboxPickup toolbox) => toolbox != null && _carriedToolbox == toolbox;
        public bool HasLowerFloorKey { get; private set; }

        internal bool BlocksGameplayInput => _open || _closedFrame == Time.frameCount ||
            (_menus != null && _menus.BlocksGameplayInput);
        internal bool BlocksWeaponInput => IsCarryingTools || BlocksGameplayInput || IsInspectingKey;
        bool IsInspectingKey => _keyViewPending || _keyView?.IsPlaying == true;
        bool IsHoldingMagazine => _heldMagazine != null && _heldMagazine.activeSelf;

        void Awake()
        {
            _controller = GetComponent<FirstPersonController>();
            _inputs = GetComponent<StarterAssetsInputs>();
            _pistol = GetComponent<PlayerPistol>();
            _hud = GetComponent<PlayerStatusHud>();
            _health = GetComponent<PlayerHealth>();
            _menus = GetComponent<PlayerMenus>();
            _innerVoice = GetComponent<PlayerInnerVoice>();
        }

        void Start()
        {
            FloorDoorCheckpoint.Apply(this);
        }

        void Update()
        {
            if (_hud.HudRect == null || _pistol.ViewCamera == null || (_health != null && _health.IsDead) ||
                (_menus != null && _menus.BlocksGameplayInput))
                return;

            _keyView?.Advance(Time.deltaTime);
            if (_keyViewPending)
                ShowLowerFloorKey();

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool toggle = keyboard?.tabKey.wasPressedThisFrame == true;
            bool drop = keyboard?.qKey.wasPressedThisFrame == true;
            bool pickup = keyboard?.eKey.wasPressedThisFrame == true;
            bool confirm = mouse?.leftButton.wasPressedThisFrame == true;
#else
            bool toggle = Input.GetKeyDown(KeyCode.Tab);
            bool drop = Input.GetKeyDown(KeyCode.Q);
            bool pickup = Input.GetKeyDown(KeyCode.E);
            bool confirm = Input.GetMouseButtonDown(0);
#endif
            if (toggle)
            {
                SetOpen(!_open);
                return;
            }

            if (_open)
            {
                _wheel.FitToScreen();
#if ENABLE_INPUT_SYSTEM
                Vector2? pointer = mouse != null ? mouse.position.ReadValue() : null;
                int numberSlot = ReadNumberSlot(keyboard);
#else
                Vector2? pointer = (Vector2)Input.mousePosition;
                int numberSlot = ReadNumberSlot();
#endif
                UpdateSelection(numberSlot, pointer);
                _wheel.UpdateVisuals(Time.unscaledDeltaTime);

                if (drop)
                {
                    if (IsCarryingTools)
                        TryPutDownToolbox();
                    else if (_selectedSlot == PistolSlot)
                        _innerVoice.KeepPistol();
                    else if (_selectedSlot == MagazineSlot)
                        DropMagazine();
                }
                else if (confirm && numberSlot < 0)
                    ConfirmSelectedItem();

                return;
            }

            if (IsInspectingKey)
                return;

            if (drop)
            {
                if (IsCarryingTools)
                    TryPutDownToolbox();
                else if (_pistol.IsEquipped)
                    _innerVoice.KeepPistol();
                else if (IsHoldingMagazine)
                    DropMagazine();
            }

            UpdatePickup(pickup);
        }

#if ENABLE_INPUT_SYSTEM
        static int ReadNumberSlot(Keyboard keyboard)
        {
            if (keyboard == null)
                return -1;

            for (int i = 0; i < SlotCount; i++)
            {
                if (keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame ||
                    keyboard[(Key)((int)Key.Numpad1 + i)].wasPressedThisFrame)
                    return i;
            }
            return -1;
        }
#else
        static int ReadNumberSlot()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)) ||
                    Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + i)))
                    return i;
            }
            return -1;
        }
#endif

        void UpdateSelection(int numberSlot, Vector2? pointer)
        {
            int next = numberSlot;
            if (numberSlot >= 0)
            {
                _pointerAtKeyboardSelection = pointer;
            }
            else if (pointer.HasValue)
            {
                if (_pointerAtKeyboardSelection.HasValue &&
                    (pointer.Value - _pointerAtKeyboardSelection.Value).sqrMagnitude <
                    PointerResumeDistance * PointerResumeDistance)
                    return;

                _pointerAtKeyboardSelection = null;
                next = _wheel.GetSlotAtPointer(pointer.Value, _selectedSlot);
            }

            if (next >= 0 && next != _selectedSlot)
            {
                _selectedSlot = next;
                RefreshWheel();
            }
        }

        internal void SwapMagazine(ref int loadedRounds)
        {
            if (ReserveRounds <= 0)
                return;

            int previousMagazine = loadedRounds;
            loadedRounds = ReserveRounds;
            _spareRounds = previousMagazine;
            RefreshWheel();
        }

        void SetOpen(bool open)
        {
            if (_open == open)
                return;

            if (open && _wheel == null)
            {
                if (_wheelBase == null || _occupiedSector == null || _selectedSector == null
                    || _pistolIcon == null || _magazineIcon == null)
                {
                    Debug.LogWarning("Inventory wheel sprites are missing.", this);
                    return;
                }

                _wheel = new InventoryWheelView(_hud.HudRect, _wheelBase, _occupiedSector,
                    _selectedSector, _pistolIcon, _magazineIcon);
            }

            _open = open;
            _pointerAtKeyboardSelection = null;
            if (!open)
                _closedFrame = Time.frameCount;
            SetInventoryCursor(open);
            if (_controller != null)
                _controller.LookInputBlocked = open;
            if (_inputs != null)
                _inputs.LookInput(Vector2.zero);
            if (_hud != null)
                _hud.SetInventoryOpen(open);
            _wheel?.SetVisible(open);
            if (open)
            {
                StopKeyView();
                SetFocusedPickup(null);
                RefreshWheel();
            }
        }

        internal void OpenFromDialogue() => SetOpen(true);

        void SetInventoryCursor(bool open)
        {
            if (open)
            {
                _previousCursorLock = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                if (_inputs != null)
                {
                    _previousLookCursorLock = _inputs.cursorLocked;
                    _inputs.cursorLocked = false;
                }
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                if (_inputs != null)
                    _inputs.cursorLocked = _previousLookCursorLock;
                Cursor.lockState = _previousCursorLock;
                Cursor.visible = _previousCursorVisible;
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && _open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void RefreshWheel()
        {
            if (_open)
                _wheel.Refresh(_selectedSlot, _spareRounds, _pistol.IsEquipped, IsHoldingMagazine, _clues,
                    IsCarryingTools, HasLowerFloorKey, _lowerFloorKeyIcon);
        }

        void ConfirmSelectedItem()
        {
            if (_clues[_selectedSlot] != null)
            {
                _menus?.ShowClue(_clues[_selectedSlot]);
                return;
            }

            if (_selectedSlot == LowerFloorKeySlot && HasLowerFloorKey)
            {
                if (IsCarryingTools)
                {
                    _hud.ShowSubtitle(HandsFullSubtitle);
                    return;
                }

                SetOpen(false);
                if (!ShowLowerFloorKey())
                    _hud.ShowSubtitle(LowerFloorKeyName);
                return;
            }

            if (IsCarryingTools)
            {
                _hud.ShowSubtitle(HandsFullSubtitle);
                return;
            }

            if (_selectedSlot == PistolSlot)
                _pistol.SetEquipped(!_pistol.IsEquipped);
            else if (_selectedSlot == MagazineSlot && _spareRounds.HasValue)
            {
                if (IsHoldingMagazine)
                    StowMagazine();
                else if (!HoldMagazine())
                    return;
            }
            else
                return;

            SetOpen(false);
        }

        internal bool TryCollectClue(ClueNote note)
        {
            if (note == null || _menus == null)
                return false;

            for (int slot = FirstClueSlot; slot < LowerFloorKeySlot; slot++)
            {
                if (_clues[slot] == note)
                    return false;
                if (_clues[slot] != null)
                    continue;

                if (!_menus.ShowClue(note, () => _innerVoice.ReadClue(note.ReadVoice)))
                    return false;
                _clues[slot] = note;
                return true;
            }
            return false;
        }

        bool HoldMagazine()
        {
            if (_heldMagazine == null)
            {
                if (_pickupShader == null)
                    return false;

                _heldMagazine = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _heldMagazine.name = "Held Magazine";
                var collider = _heldMagazine.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);

                var view = _heldMagazine.transform;
                view.SetParent(_pistol.ViewCamera.transform, false);
                view.localPosition = new Vector3(0.17f, -0.14f, 0.42f);
                view.localRotation = Quaternion.Euler(12f, -16f, -8f);
                view.localScale = Vector3.one * 0.085f;

                _heldMagazineMaterial = new Material(_pickupShader) { name = "Held Magazine (Runtime)" };
                _heldMagazineMaterial.SetColor("_BaseColor", DroppedMagazine.RestingColor);
                var renderer = _heldMagazine.GetComponent<Renderer>();
                renderer.sharedMaterial = _heldMagazineMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            _pistol.SetEquipped(false);
            _heldMagazine.SetActive(true);
            return true;
        }

        internal void StowMagazine()
        {
            if (_heldMagazine != null)
                _heldMagazine.SetActive(false);
        }

        void DropMagazine()
        {
            if (!_spareRounds.HasValue || _droppedMagazinePrefab == null || !TryGetDropPosition(out var position))
                return;

            DroppedMagazine.Spawn(_droppedMagazinePrefab, position, _spareRounds.Value, gameObject.scene,
                GetComponentsInChildren<Collider>());
            _spareRounds = null;
            StowMagazine();
            RefreshWheel();
        }

        bool TryGetDropPosition(out Vector3 position, float radius = DropRadius)
        {
            var camera = _pistol.ViewCamera.transform;
            var origin = camera.position;
            var direction = camera.forward;
            position = origin;
            // A sphere cast does not detect geometry that already overlaps its starting sphere.
            if (!HasDropClearance(origin, radius))
                return false;

            int count = Physics.SphereCastNonAlloc(origin, radius, direction, _hits,
                DropDistance, WorldLayers, QueryTriggerInteraction.Ignore);
            var nearest = count < _hits.Length
                ? FindNearestWorldHit(_hits, count)
                : FindNearestWorldHit(Physics.SphereCastAll(origin, radius, direction, DropDistance,
                    WorldLayers, QueryTriggerInteraction.Ignore));
            float distance = nearest.collider != null ? Mathf.Max(0f, nearest.distance - 0.03f) : DropDistance;
            position = origin + direction * distance;
            return HasDropClearance(position, radius);
        }

        bool HasDropClearance(Vector3 position, float radius)
        {
            int overlaps = Physics.OverlapSphereNonAlloc(position, radius, _overlaps,
                WorldLayers, QueryTriggerInteraction.Ignore);
            if (overlaps == _overlaps.Length)
                return false;
            for (int i = 0; i < overlaps; i++)
            {
                if (!_overlaps[i].transform.IsChildOf(transform))
                    return false;
            }
            return true;
        }

        void UpdatePickup(bool pickupPressed)
        {
            if (_menus != null && _menus.HasTalkTarget)
            {
                SetFocusedPickup(null);
                return;
            }

            var pickup = FindPickupInView();
            SetFocusedPickup(pickup);
            if (!pickupPressed)
                return;

            if (pickup is CluePickup clue && TryCollectClue(clue.Note))
            {
                clue.gameObject.SetActive(false);
                Destroy(clue.gameObject);
            }
            else if (pickup is ToolboxPickup toolbox)
                CarryToolbox(toolbox);
            else if (pickup is ClueBoard board && board.TryRead(_menus))
                SetFocusedPickup(null);
            else if (pickup is DroppedMagazine magazine && !_spareRounds.HasValue)
            {
                _spareRounds = magazine.Rounds;
                SetFocusedPickup(null);
                magazine.gameObject.SetActive(false);
                Destroy(magazine.gameObject);
            }
            else if (pickup is LowerFloorDoor door)
            {
                if (door.TryOpen(this))
                {
                    SetFocusedPickup(null);
                    _menus?.OnDoorOpened(door);
                }
                else if (!door.CanUnlock(this))
                    _hud.ShowSubtitle("Заперто. Нужен ключ.");
            }
        }

        void CarryToolbox(ToolboxPickup toolbox)
        {
            SetFocusedPickup(null);
            _pistol.SetEquipped(false);
            StowMagazine();
            _carriedToolbox = toolbox;
            toolbox.Carry(_pistol.ViewCamera.transform);
            _menus?.RefreshQuestDisplay();
            RefreshWheel();
            _hud.ShowSubtitle("Q — поставить ящик");
        }

        internal bool TryPutDownToolbox()
        {
            if (!IsCarryingTools)
                return true;

            if (!TryGetDropPosition(out var position, ToolboxPickup.DropClearanceRadius))
            {
                _hud.ShowSubtitle("Здесь нет места для ящика");
                return false;
            }

            var toolbox = _carriedToolbox;
            _carriedToolbox = null;
            toolbox.PutDown(position, transform.eulerAngles.y, GetComponentsInChildren<Collider>());
            _menus?.RefreshQuestDisplay();
            RefreshWheel();
            return true;
        }

        internal bool DeliverTools()
        {
            if (!IsCarryingTools)
                return false;

            var toolbox = _carriedToolbox;
            _carriedToolbox = null;
            toolbox.gameObject.SetActive(false);
            Destroy(toolbox.gameObject);
            RefreshWheel();
            return true;
        }

        internal void RestoreCheckpoint(bool hasKey, int reserveRounds)
        {
            _spareRounds = Mathf.Max(0, reserveRounds);
            _carriedToolbox = null;
            HasLowerFloorKey = hasKey;
            if (!hasKey)
                return;

            foreach (var key in FindObjectsByType<DoorKeyPickup>(FindObjectsInactive.Include))
            {
                _lowerFloorKeyIcon = key.Icon;
                break;
            }
            RefreshWheel();
        }

        internal bool ReceiveLowerFloorKey(DoorKeyPickup key)
        {
            if (HasLowerFloorKey || IsCarryingTools || (_menus != null && _menus.IsNpcSpeaking))
                return false;

            HasLowerFloorKey = true;
            _lowerFloorKeyIcon = key.Icon;
            if (_keyInspectionHand != null && key.Mesh != null && key.Material != null)
            {
                _keyView = new PlayerKeyView(_keyInspectionHand, _pistol.ViewCamera, transform, key.Mesh, key.Material);
                ShowLowerFloorKey();
            }
            RefreshWheel();
            _hud.ShowSubtitle($"Получен предмет: {LowerFloorKeyName}");
            return true;
        }

        bool ShowLowerFloorKey()
        {
            if (_keyView == null)
                return false;

            _keyViewPending = !_pistol.TryLowerForHandAction(
                BerettaView.HolsterDuration + PlayerKeyView.Duration, out float switchDelay);
            if (!_keyViewPending)
            {
                StowMagazine();
                _keyView.Play(switchDelay);
            }
            return true;
        }

        void StopKeyView()
        {
            _keyViewPending = false;
            _keyView?.Stop();
        }

        MonoBehaviour FindPickupInView()
        {
            var camera = _pistol.ViewCamera;
            var centerRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var ray = new Ray(camera.transform.position, centerRay.direction);
            var hits = _hits;
            int count = Physics.SphereCastNonAlloc(ray, PickupAimRadius, hits, PickupDistance, WorldLayers,
                QueryTriggerInteraction.Ignore);
            if (count == hits.Length)
            {
                hits = Physics.SphereCastAll(ray, PickupAimRadius, PickupDistance, WorldLayers,
                    QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            MonoBehaviour nearest = null;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance >= distance)
                    continue;

                var pickup = FindPickup(hit.collider);
                if (pickup == null || !pickup.isActiveAndEnabled || pickup.gameObject.scene != gameObject.scene ||
                    (IsCarryingTools && pickup is not (CluePickup or ClueBoard or LowerFloorDoor)) ||
                    !HasPickupLineOfSight(ray.origin, hit.point, pickup.transform))
                    continue;

                nearest = pickup;
                distance = hit.distance;
            }
            return nearest;
        }

        static MonoBehaviour FindPickup(Collider collider)
        {
            var clue = collider.GetComponentInParent<CluePickup>();
            if (clue != null)
                return clue.Note != null && clue.Note.Paper != null ? clue : null;
            var board = collider.GetComponentInParent<ClueBoard>();
            if (board != null)
                return board.CanRead ? board : null;

            var toolbox = collider.GetComponentInParent<ToolboxPickup>();
            if (toolbox != null)
                return toolbox.PickupAllowed ? toolbox : null;
            var door = collider.GetComponentInParent<LowerFloorDoor>();
            if (door != null)
                return door.CanInteract ? door : null;
            return collider.GetComponentInParent<DroppedMagazine>();
        }

        bool HasPickupLineOfSight(Vector3 origin, Vector3 point, Transform pickup)
        {
            // The aim tolerance may reach around a corner; confirm the actual surface is visible.
            Vector3 offset = point - origin;
            float distance = offset.magnitude;
            if (distance < 0.001f || distance > PickupDistance)
                return false;

            var ray = new Ray(origin, offset / distance);
            int count = Physics.RaycastNonAlloc(ray, _pickupSightHits, distance, WorldLayers,
                QueryTriggerInteraction.Ignore);
            var nearest = count < _pickupSightHits.Length
                ? FindNearestWorldHit(_pickupSightHits, count)
                : FindNearestWorldHit(Physics.RaycastAll(ray, distance, WorldLayers, QueryTriggerInteraction.Ignore));
            return nearest.collider == null || nearest.collider.transform.IsChildOf(pickup);
        }

        RaycastHit FindNearestWorldHit(RaycastHit[] hits, int count = -1)
        {
            if (count < 0)
                count = hits.Length;
            RaycastHit nearest = default;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance >= distance || hit.collider.transform.IsChildOf(transform))
                    continue;
                nearest = hit;
                distance = hit.distance;
            }
            return nearest;
        }

        void SetFocusedPickup(MonoBehaviour pickup)
        {
            if (ReferenceEquals(_focusedPickup, pickup))
                return;

            if (_focusedPickup != null)
            {
                if (_focusedPickup is ToolboxPickup previousToolbox)
                    previousToolbox.SetHighlighted(false);
                else if (_focusedPickup is DroppedMagazine previousMagazine)
                    previousMagazine.SetHighlighted(false);
                else if (_focusedPickup is CluePickup previousClue)
                    previousClue.SetHighlighted(false);
            }
            _focusedPickup = pickup;

            if (pickup is ToolboxPickup toolbox)
            {
                toolbox.SetHighlighted(true);
                _hud.ShowPickupHint("Ящик с инструментами\nE — подобрать", questItem: true);
            }
            else if (pickup is DroppedMagazine magazine)
            {
                magazine.SetHighlighted(true);
                string action = _spareRounds.HasValue ? "Слот обоймы занят" : "E — подобрать";
                _hud.ShowPickupHint($"Обойма · {magazine.Rounds}/{PlayerPistol.MagazineSize}\n{action}");
            }
            else if (pickup is CluePickup clue)
            {
                clue.SetHighlighted(true);
                _hud.ShowPickupHint($"{clue.Note.Title}\nE — подобрать", questItem: true);
            }
            else if (pickup is ClueBoard)
                _hud.ShowPickupHint("Доска с уликами\nE — осмотреть", questItem: true);
            else if (pickup is LowerFloorDoor door)
                _hud.ShowPickupHint(door.CanUnlock(this) ? "E — открыть дверь" : "Дверь заперта\nНужен ключ",
                    questItem: true);
            else if (_hud != null)
                _hud.ShowPickupHint(null);
        }

        void OnDisable()
        {
            CloseForMenu();
            StowMagazine();
        }

        internal void CloseForMenu()
        {
            StopKeyView();
            SetOpen(false);
            SetFocusedPickup(null);
        }

        void LateUpdate() => _keyView?.ApplyClearance();

        void OnDestroy()
        {
            _keyView?.Destroy();
            _wheel?.Destroy();
            if (_heldMagazine != null)
                Destroy(_heldMagazine);
            if (_heldMagazineMaterial != null)
                Destroy(_heldMagazineMaterial);
        }
    }
}
