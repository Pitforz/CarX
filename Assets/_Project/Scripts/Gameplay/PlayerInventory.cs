using StarterAssets;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerPistol))]
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerInventory : MonoBehaviour
    {
        internal const int SlotCount = 7;
        internal const int PistolSlot = 0;
        internal const int MagazineSlot = 1;
        const float PointerResumeDistance = 3f;
        const float PickupDistance = 3f;
        const float DropDistance = 1f;
        const float DropRadius = 0.15f;
        const int WorldLayers = Physics.DefaultRaycastLayers;
        const string PistolDropSubtitle = "Эта вещь мне ещё понадобится";

        [SerializeField] Sprite _wheelBase;
        [SerializeField] Sprite _occupiedSector;
        [SerializeField] Sprite _selectedSector;
        [SerializeField] Sprite _pistolIcon;
        [SerializeField] Sprite _magazineIcon;
        [SerializeField] Shader _pickupShader;

        readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly Collider[] _overlaps = new Collider[16];
        int? _spareRounds = PlayerPistol.MagazineSize;
        FirstPersonController _controller;
        StarterAssetsInputs _inputs;
        PlayerPistol _pistol;
        PlayerStatusHud _hud;
        PlayerMenus _menus;
        InventoryWheelView _wheel;
        DroppedMagazine _focusedMagazine;
        GameObject _heldMagazine;
        Material _heldMagazineMaterial;
        bool _open;
        int _selectedSlot;
        int _closedFrame = -1;
        Vector2? _pointerAtKeyboardSelection;
        CursorLockMode _previousCursorLock;
        bool _previousCursorVisible;
        bool _previousLookCursorLock;

        internal int ReserveRounds => _spareRounds ?? 0;
        internal bool IsOpen => _open;
        internal bool BlocksWeaponInput =>
            _open || _closedFrame == Time.frameCount || (_menus != null && _menus.BlocksGameplayInput);
        bool IsHoldingMagazine => _heldMagazine != null && _heldMagazine.activeSelf;

        void Awake()
        {
            _controller = GetComponent<FirstPersonController>();
            _inputs = GetComponent<StarterAssetsInputs>();
            _pistol = GetComponent<PlayerPistol>();
            _hud = GetComponent<PlayerStatusHud>();
            _menus = GetComponent<PlayerMenus>();
        }

        void Update()
        {
            if (_hud.HudRect == null || _pistol.ViewCamera == null ||
                (_menus != null && _menus.BlocksGameplayInput))
                return;

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
                    if (_selectedSlot == PistolSlot)
                        _hud.ShowSubtitle(PistolDropSubtitle);
                    else if (_selectedSlot == MagazineSlot)
                        DropMagazine();
                }
                else if (confirm && numberSlot < 0)
                    ConfirmSelectedItem();

                return;
            }

            if (drop)
            {
                if (_pistol.IsEquipped)
                    _hud.ShowSubtitle(PistolDropSubtitle);
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
                SetFocusedMagazine(null);
                RefreshWheel();
            }
        }

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
                _wheel.Refresh(_selectedSlot, _spareRounds, _pistol.IsEquipped, IsHoldingMagazine);
        }

        void ConfirmSelectedItem()
        {
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
            if (!_spareRounds.HasValue || _pickupShader == null || !TryGetDropPosition(out var position))
                return;

            DroppedMagazine.Spawn(position, _spareRounds.Value, _pickupShader, gameObject.scene,
                GetComponentsInChildren<Collider>());
            _spareRounds = null;
            StowMagazine();
            RefreshWheel();
        }

        bool TryGetDropPosition(out Vector3 position)
        {
            var camera = _pistol.ViewCamera.transform;
            var origin = camera.position;
            var direction = camera.forward;
            position = origin;
            // A sphere cast does not detect geometry that already overlaps its starting sphere.
            if (!HasDropClearance(origin))
                return false;

            int count = Physics.SphereCastNonAlloc(origin, DropRadius, direction, _hits,
                DropDistance, WorldLayers, QueryTriggerInteraction.Ignore);
            var nearest = count < _hits.Length
                ? FindNearestWorldHit(_hits, count)
                : FindNearestWorldHit(Physics.SphereCastAll(origin, DropRadius, direction, DropDistance,
                    WorldLayers, QueryTriggerInteraction.Ignore));
            float distance = nearest.collider != null ? Mathf.Max(0f, nearest.distance - 0.03f) : DropDistance;
            position = origin + direction * distance;
            return HasDropClearance(position);
        }

        bool HasDropClearance(Vector3 position)
        {
            int overlaps = Physics.OverlapSphereNonAlloc(position, DropRadius, _overlaps,
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
            var ray = _pistol.ViewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int count = Physics.RaycastNonAlloc(ray, _hits, PickupDistance, WorldLayers,
                QueryTriggerInteraction.Ignore);
            var nearest = count < _hits.Length
                ? FindNearestWorldHit(_hits, count)
                : FindNearestWorldHit(Physics.RaycastAll(ray, PickupDistance, WorldLayers,
                    QueryTriggerInteraction.Ignore));
            var magazine = nearest.collider != null
                ? nearest.collider.GetComponentInParent<DroppedMagazine>()
                : null;
            SetFocusedMagazine(magazine);

            if (pickupPressed && magazine != null && !_spareRounds.HasValue)
            {
                _spareRounds = magazine.Rounds;
                SetFocusedMagazine(null);
                magazine.gameObject.SetActive(false);
                Destroy(magazine.gameObject);
            }
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

        void SetFocusedMagazine(DroppedMagazine magazine)
        {
            if (ReferenceEquals(_focusedMagazine, magazine))
                return;

            if (_focusedMagazine != null)
                _focusedMagazine.SetHighlighted(false);
            _focusedMagazine = magazine;
            if (magazine == null)
            {
                if (_hud != null)
                    _hud.ShowPickupHint(null);
                return;
            }

            magazine.SetHighlighted(true);
            string action = _spareRounds.HasValue ? "Слот магазина занят" : "E — подобрать";
            _hud.ShowPickupHint($"Магазин · {magazine.Rounds}/{PlayerPistol.MagazineSize}\n{action}");
        }

        void OnDisable()
        {
            CloseForMenu();
            StowMagazine();
        }

        internal void CloseForMenu()
        {
            SetOpen(false);
            SetFocusedMagazine(null);
        }

        void OnDestroy()
        {
            _wheel?.Destroy();
            if (_heldMagazine != null)
                Destroy(_heldMagazine);
            if (_heldMagazineMaterial != null)
                Destroy(_heldMagazineMaterial);
        }
    }
}
