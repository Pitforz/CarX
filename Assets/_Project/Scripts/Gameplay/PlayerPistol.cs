using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(PlayerStatusHud))]
    [RequireComponent(typeof(PlayerMuzzleFlash))]
    public class PlayerPistol : MonoBehaviour
    {
        // Matches the First Person Weapon rendering layer in Project Settings.
        internal const uint WeaponRenderingLayer = 1u << 1;

        internal const int MagazineSize = 15;
        const int ShotDamage = 10;
        const float SilentReloadDuration = 0.55f;
        const float AimFovReduction = 0.2808f;
        const float AimLookSensitivity = 0.7f;
        const float EncounterAimSmoothTime = 0.03f;
        const float ShotInterval = 0.25f;
        const float LeanFovBonus = 1f;
        const float ShotRange = 100f;
        const int MaxImpactMarks = 80;
        const float StressRiseTime = 0.35f;
        const float StressSettleTime = 0.55f;

        [SerializeField] Shader _placeholderShader;
        [SerializeField] Shader _weaponShader;
        [SerializeField] BerettaView _berettaPrefab;
        [SerializeField, Min(0.01f)] float _aimTransitionTime = 0.24f;
        [Header("Camera recoil")]
        [Tooltip("Minimum (X) and maximum (Y) upward kick per shot in degrees.")]
        [SerializeField] Vector2 _upwardRecoilDegrees = new(0.9f, 1.4f);
        [Tooltip("Maximum sideways kick in either direction per shot in degrees.")]
        [SerializeField, Min(0f)] float _sidewaysRecoilDegrees = 0.6f;
        [Tooltip("Fraction of the hip-fire camera kick when fully aimed.")]
        [SerializeField, Range(0f, 1f)] float _aimedRecoilMultiplier = 0.6f;
        [Header("Combat stress")]
        [Tooltip("Maximum horizontal (X) and vertical (Y) aim sway in degrees.")]
        [SerializeField] Vector2 _stressAimSwayDegrees = new(1.05f, 0.75f);
        [Tooltip("Maximum hand roll in degrees when holding the pistol at the hip.")]
        [SerializeField, Min(0f)] float _stressHandRollDegrees = 2f;
        [Tooltip("Multiplier for camera recoil at full combat stress.")]
        [SerializeField, Min(1f)] float _stressRecoilMultiplier = 1.45f;
        [Header("Pistol audio")]
        [SerializeField] AudioClip _shotClip;
        [SerializeField] AudioClip _reloadClip;
        [SerializeField, Range(0f, 1f)] float _shotVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] float _reloadVolume = 0.8f;

        static readonly Vector3 HipPosition = new(0.16f, -0.13f, 0.43f);
        static readonly Vector3 AimPosition = new(0f, -0.09f, 0.38f);
        static readonly Vector3 BerettaHipPosition = new(0.152f, -0.218f, 0.25f);
        static readonly Vector3 BerettaAimPosition = new(0f, -0.133f, 0.19f);
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        Camera _camera;
        StarterAssets.FirstPersonController _player;
        CinemachineCamera _followCamera;
        PlayerStatusHud _hud;
        PlayerInventory _inventory;
        PlayerMenus _menus;
        PlayerInnerVoice _innerVoice;
        Transform _view;
        BerettaView _beretta;
        PistolWallClearance _wallClearance;
        PlayerMuzzleFlash _muzzleFlash;
        AudioSource _shotAudio;
        AudioSource _reloadAudio;
        Material _placeholderMaterial;
        Material _weaponMaterial;
        readonly Queue<GameObject> _impactMarks = new();
        int _loaded = MagazineSize;
        int _reserve = MagazineSize;
        bool _equipped;
        bool _aiming;
        bool _scriptedEncounterAim;
        bool _shotPending;
        bool _aimedTriggerPending;
        float _reloadRemaining;
        float _reloadDuration;
        float _reloadStartedAt;
        float _flashlightLoweredAt;
        float _flashlightRaiseAt;
        float _flashlightRestoredAt;
        float _nextShotTime;
        float _aimBlend;
        float _aimVelocity;
        float _placeholderRecoil;
        float _placeholderRecoilVelocity;
        float _normalFov;
        float _stressBlend;
        Vector2 _stressTremor;

        internal Camera ViewCamera => _camera;
        internal bool IsEquipped => _equipped;
        internal bool IsEncounterAiming => _scriptedEncounterAim && _aiming;
        public bool CombatStressActive { get; private set; }
        /// <summary>Set by another system (dark orientation): true puts the pistol and its arms away, it stays equipped.</summary>
        internal System.Func<bool> HideQuery;
        /// <summary>Set by another system: true means no aiming, firing or reloading.</summary>
        internal System.Func<bool> BlockQuery;
        internal bool IsHiddenExternally { get; private set; }
        internal Vector3 GripLightPosition => _beretta != null ? _beretta.GripLightPosition : _view.position;
        internal event System.Action<Vector3> ShotFired;
        int ReserveRounds => _inventory != null ? _inventory.ReserveRounds : _reserve;
        float StressAmount => Mathf.SmoothStep(0f, 1f, _stressBlend);

        // The final encounter can use the same control as the prototype key.
        public void SetCombatStress(bool active) => CombatStressActive = active;

        void Start()
        {
            _player = GetComponent<StarterAssets.FirstPersonController>();
            var followTarget = _player != null ? _player.CinemachineCameraTarget : null;
            var cameras = transform.root.GetComponentsInChildren<CinemachineCamera>();
            foreach (var camera in cameras)
            {
                if (followTarget == null || camera.Follow != followTarget.transform)
                    continue;
                _followCamera = camera;
                break;
            }

            _camera = transform.root.GetComponentInChildren<Camera>();
            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null || _placeholderShader == null)
            {
                enabled = false;
                return;
            }

            _hud = GetComponent<PlayerStatusHud>();
            _inventory = GetComponent<PlayerInventory>();
            _menus = GetComponent<PlayerMenus>();
            _innerVoice = GetComponent<PlayerInnerVoice>();
            _muzzleFlash = GetComponent<PlayerMuzzleFlash>();
            _shotAudio = CreateAudioSource("Pistol Shot Audio", _shotVolume);
            _reloadAudio = CreateAudioSource("Pistol Reload Audio", _reloadVolume);
            _reloadAudio.clip = _reloadClip;
            _normalFov = _followCamera != null ? _followCamera.Lens.FieldOfView : _camera.fieldOfView;
            CreateWeaponView();
            _muzzleFlash.Initialize(_view, _camera, _beretta != null ? _beretta.Muzzle : null);
            _wallClearance = new PistolWallClearance(transform, _view);
            _view.gameObject.SetActive(false);
        }

        void Update()
        {
            if (_view == null || Time.timeScale == 0f || Time.deltaTime <= 0f)
                return;

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool equipPressed = keyboard?.digit1Key.wasPressedThisFrame == true;
            bool reloadPressed = keyboard?.rKey.wasPressedThisFrame == true;
            bool firePressed = mouse?.leftButton.wasPressedThisFrame == true ||
                keyboard?.pKey.wasPressedThisFrame == true;
            bool aimHeld = mouse?.rightButton.isPressed == true;
            bool stressPressed = keyboard?.bKey.wasPressedThisFrame == true;
#else
            bool equipPressed = Input.GetKeyDown(KeyCode.Alpha1);
            bool reloadPressed = Input.GetKeyDown(KeyCode.R);
            bool firePressed = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.P);
            bool aimHeld = Input.GetMouseButton(1);
            bool stressPressed = Input.GetKeyDown(KeyCode.B);
#endif
            UpdateReload();

            bool gameplayBlocked = _inventory != null ? _inventory.BlocksGameplayInput :
                _menus != null && _menus.BlocksGameplayInput;
            if (stressPressed && !gameplayBlocked)
            {
                SetCombatStress(!CombatStressActive);
                _hud.ShowSubtitle(CombatStressActive ? "Мандраж включён" : "Мандраж выключен", duration: 1.5f);
            }

            bool weaponInputBlocked = _inventory?.BlocksWeaponInput ?? gameplayBlocked;
            bool encounterAllowsWeapon = _menus?.AllowsEncounterWeaponInput == true &&
                _inventory?.IsCarryingTools != true;
            if (weaponInputBlocked && !encounterAllowsWeapon)
                equipPressed = reloadPressed = firePressed = aimHeld = false;
            IsHiddenExternally = HideQuery != null && HideQuery();
            bool weaponBlockedExternally = BlockQuery != null && BlockQuery();
            if (weaponBlockedExternally)
                equipPressed = reloadPressed = firePressed = aimHeld = false;

            if (equipPressed)
                SetEquipped(!_equipped);

            bool loweredForFlashlight = Time.time < _flashlightRaiseAt;
            if (!loweredForFlashlight && _flashlightRaiseAt > 0f)
            {
                _flashlightRaiseAt = 0f;
                if (_equipped)
                {
                    _flashlightRestoredAt = Time.time + BerettaView.FlashlightRaiseDuration;
                    if (_beretta != null)
                        _beretta.RaiseAfterFlashlight();
                }
            }
            bool canUsePistol = _equipped && !loweredForFlashlight && Time.time >= _flashlightRestoredAt &&
                !(_beretta != null && _beretta.IsDrawing);
            bool reloadForEncounter = _scriptedEncounterAim && _loaded == 0;
            if (canUsePistol && (reloadPressed || reloadForEncounter) && ReserveRounds > 0 && _reloadRemaining <= 0f)
                StartReload();

            _aiming = canUsePistol && (aimHeld || _scriptedEncounterAim) && _reloadRemaining <= 0f;
            _aimedTriggerPending = _aiming && firePressed;
            _shotPending = canUsePistol && firePressed && _loaded > 0 && _reloadRemaining <= 0f &&
                Time.time >= _nextShotTime;

            // One continuous blend drives both the hands and the lens, even when RMB is tapped rapidly.
            if (Time.deltaTime > 0f)
            {
                float aimTarget = _aiming ? 1f : 0f;
                float aimSmoothTime = _scriptedEncounterAim ? EncounterAimSmoothTime : _aimTransitionTime * 0.5f;
                _aimBlend = Mathf.SmoothDamp(_aimBlend, aimTarget, ref _aimVelocity,
                    aimSmoothTime);
                if (Mathf.Abs(_aimBlend - aimTarget) < 0.0001f && Mathf.Abs(_aimVelocity) < 0.001f)
                {
                    _aimBlend = aimTarget;
                    _aimVelocity = 0f;
                }
            }
            float zoom = _aimBlend;
            UpdateCombatStress(canUsePistol && (!gameplayBlocked || encounterAllowsWeapon || _scriptedEncounterAim) &&
                !IsHiddenExternally && !weaponBlockedExternally);
            if (_player != null)
            {
                _player.LookSensitivityMultiplier = Mathf.Lerp(1f, AimLookSensitivity, zoom);
                _player.IsAiming = _aiming;
            }
            float leanFov = LeanFovBonus * (_player != null ? _player.LeanAmount : 0f);
            SetFieldOfView(_normalFov * (1f - AimFovReduction * zoom) + leanFov);
            UpdatePose(zoom, loweredForFlashlight);
            _hud.ShowPistol(_equipped, _aiming, _loaded, ReserveRounds, _reloadRemaining > 0f);
        }

        internal bool TryPrepareEncounterShot()
        {
            if (_view == null || !isActiveAndEnabled || (_inventory != null && !_inventory.TryPutDownToolbox()))
                return false;

            if (_beretta != null && _beretta.IsHolstering)
            {
                _beretta.ResetPose();
                _view.gameObject.SetActive(false);
            }
            return true;
        }

        internal void BeginEncounterShot()
        {
            _scriptedEncounterAim = true;
            SetEquipped(true);
        }

        internal void EndEncounterShot() => _scriptedEncounterAim = false;

        internal void FireEncounterShot()
        {
            Fire(applyCameraRecoil: false);
            EndEncounterShot();
            _hud.ShowPistol(_equipped, _aiming, _loaded, ReserveRounds, false);
        }

        internal void SetEquipped(bool equipped)
        {
            if (_view == null || !isActiveAndEnabled || _equipped == equipped)
                return;
            if (equipped && _inventory != null && _inventory.IsCarryingTools)
            {
                _hud.ShowSubtitle(PlayerInventory.HandsFullSubtitle);
                return;
            }

            if (equipped && _inventory != null)
                _inventory.StowMagazine();

            _equipped = equipped;
            if (equipped)
            {
                if (Time.time < _flashlightRaiseAt)
                    return;

                _flashlightRaiseAt = 0f;
                if (_beretta != null)
                    _beretta.Draw(_view.gameObject.activeSelf);
                _view.gameObject.SetActive(true);
            }
            else
            {
                CancelReload(resetPose: false);
                _muzzleFlash.Stop();
                if (_beretta != null && _view.gameObject.activeSelf)
                {
                    if (!_beretta.IsHolstering)
                        _beretta.Holster();
                }
                else
                    _view.gameObject.SetActive(false);
            }
        }

        internal bool TryLowerForFlashlight(float duration, out float switchDelay)
        {
            switchDelay = 0f;
            if (!_equipped || !isActiveAndEnabled)
                return true;

            float now = Time.time;
            if (_flashlightRaiseAt > 0f && _flashlightLoweredAt == now)
            {
                // Lights driven by the same player can share the current gesture.
                switchDelay = BerettaView.HolsterDuration;
                return true;
            }
            if (_flashlightRaiseAt > 0f || now < _flashlightRestoredAt)
                return false;

            _flashlightLoweredAt = now;
            _flashlightRaiseAt = now + duration;
            switchDelay = BerettaView.HolsterDuration;
            CancelReload(resetPose: false);
            if (_beretta != null && !_beretta.IsHolstering)
                _beretta.Holster();
            _muzzleFlash.Stop();
            return true;
        }

        void LateUpdate()
        {
            if (_view != null && _view.gameObject.activeInHierarchy && Time.deltaTime > 0f)
            {
                Vector3 muzzle = _beretta != null
                    ? _beretta.Muzzle.position : _view.TransformPoint(new Vector3(0f, 0.016f, 0.16f));
                _wallClearance.Apply(muzzle, GripLightPosition, Time.deltaTime);
            }

            if (!_shotPending && !_aimedTriggerPending)
                return;

            bool canFire = _shotPending;
            _shotPending = false;
            _aimedTriggerPending = false;
            // The target can lean between physics steps. Use the same pose that is rendered this frame.
            if (canFire)
                Physics.SyncTransforms();
            Ray shotRay = CenterRay();
            bool hasHit = FindShotHit(shotRay, out var hit);
            var enemy = hasHit ? hit.collider.GetComponentInParent<NPC.HostileNpc>() : null;
            if (hasHit && IsProtectedNpc(hit.collider))
            {
                _innerVoice?.WarnAboutCivilian();
                return;
            }

            if (!canFire)
                return;

            Fire();
            if (enemy != null)
                enemy.TakeShot(hit, ShotDamage, shotRay.origin);
            else if (hasHit)
                PlaceImpactMark(hit);
            _hud.ShowPistol(_equipped, _aiming, _loaded, ReserveRounds, _reloadRemaining > 0f);
        }

        internal bool IsLookingAt(Transform target, float range) => _camera != null &&
            FindShotHit(CenterRay(), out var hit, range) && hit.collider.transform.IsChildOf(target);

        internal bool IsAimingAt(Transform target, float range) => _aiming && IsLookingAt(target, range);

        Ray CenterRay() => _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        bool FindShotHit(Ray ray, out RaycastHit closestHit, float range = ShotRange)
        {
            closestHit = default;
            float closestDistance = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(ray, range, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= closestDistance)
                    continue;

                closestDistance = hit.distance;
                closestHit = hit;
            }

            return closestHit.collider != null;
        }

        static bool IsProtectedNpc(Collider collider)
        {
            var npc = collider.GetComponentInParent<NPC.NpcDialogue>();
            return (npc != null && npc.CanTalk) || collider.GetComponentInParent<NPC.WorkshopEncounter>() != null;
        }

        void PlaceImpactMark(RaycastHit hit)
        {
            if (_impactMarks.Count >= MaxImpactMarks)
                Destroy(_impactMarks.Dequeue());

            var mark = CreatePart(null, "Bullet Mark", PrimitiveType.Sphere,
                hit.point + hit.normal * 0.003f, new(0.025f, 0.025f, 0.002f),
                Quaternion.LookRotation(hit.normal).eulerAngles, Color.black);
            mark.transform.SetParent(hit.collider.transform, true);
            _impactMarks.Enqueue(mark);
        }

        AudioSource CreateAudioSource(string name, float volume)
        {
            var audioObject = new GameObject(name);
            audioObject.transform.SetParent(transform, false);
            var source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.pitch = 1f;
            source.volume = volume;
            source.priority = 64;
            source.ignoreListenerPause = false;
            return source;
        }

        void StartReload()
        {
            if (_beretta != null)
            {
                _reloadStartedAt = Time.time;
                _reloadDuration = _beretta.Reload(_loaded == 0);
                _reloadRemaining = _reloadDuration;
                return;
            }

            _reloadDuration = _reloadClip != null ? _reloadClip.length : SilentReloadDuration;
            _reloadRemaining = _reloadDuration;
            if (_reloadClip != null)
                _reloadAudio.Play();
        }

        void CancelReload(bool resetPose = true)
        {
            if (resetPose && _beretta != null && _reloadRemaining > 0f)
                _beretta.ResetPose();
            _reloadRemaining = 0f;
            if (_reloadAudio != null)
                _reloadAudio.Stop();
        }

        void UpdateReload()
        {
            if (_reloadRemaining <= 0f || AudioListener.pause)
                return;

            if (ReserveRounds <= 0)
            {
                CancelReload();
                return;
            }

            if (_beretta != null)
            {
                _reloadRemaining = _reloadDuration - (Time.time - _reloadStartedAt);
                if (_reloadRemaining > 0f)
                    return;
            }
            else if (_reloadClip != null && _reloadAudio.isPlaying)
            {
                // The PCM playback position drives the placeholder animation and HUD timer.
                // Keep the reload active until the complete clip, including pocket handling, ends.
                float elapsed = _reloadAudio.timeSamples / (float)_reloadClip.frequency;
                _reloadRemaining = Mathf.Max(0.001f, _reloadDuration - elapsed);
                return;
            }
            else if (_reloadClip == null)
            {
                _reloadRemaining -= Time.deltaTime;
                if (_reloadRemaining > 0f)
                    return;
            }

            _reloadRemaining = 0f;
            if (_inventory != null)
                _inventory.SwapMagazine(ref _loaded);
            else
                (_loaded, _reserve) = (_reserve, _loaded);
        }

        void Fire(bool applyCameraRecoil = true)
        {
            _nextShotTime = Time.time + ShotInterval;
            _loaded = Mathf.Max(0, _loaded - 1);
            if (_beretta != null)
                _beretta.Fire(_loaded == 0, ShotInterval);
            else
            {
                _placeholderRecoil = Mathf.Min(_placeholderRecoil + 0.65f, 1f);
                _placeholderRecoilVelocity = 0f;
            }
            if (applyCameraRecoil)
                ApplyCameraRecoil();
            _muzzleFlash.Play();
            ShotFired?.Invoke(transform.position);
            if (_shotClip != null)
                _shotAudio.PlayOneShot(_shotClip);
        }

        void ApplyCameraRecoil()
        {
            if (_player == null)
                return;

            float minPitch = Mathf.Max(0f, Mathf.Min(_upwardRecoilDegrees.x, _upwardRecoilDegrees.y));
            float maxPitch = Mathf.Max(minPitch, Mathf.Max(_upwardRecoilDegrees.x, _upwardRecoilDegrees.y));
            float scale = Mathf.Lerp(1f, _aimedRecoilMultiplier, _aimBlend);
            scale *= Mathf.Lerp(1f, _stressRecoilMultiplier, StressAmount);
            float upward = Random.Range(minPitch, maxPitch);
            float sideways = Random.Range(-_sidewaysRecoilDegrees, _sidewaysRecoilDegrees);
            _player.AddLookRecoil(upward * scale, sideways * scale);
        }

        void UpdateCombatStress(bool canTremble)
        {
            bool buildStress = CombatStressActive && canTremble;
            float duration = buildStress ? StressRiseTime : StressSettleTime;
            _stressBlend = Mathf.MoveTowards(_stressBlend, buildStress ? 1f : 0f, Time.deltaTime / duration);
            if (_stressBlend > 0f)
            {
                float time = Time.time;
                _stressTremor = new Vector2(SampleTremor(time, 3.17f), SampleTremor(time, 8.63f)) * StressAmount;
            }
            else
                _stressTremor = Vector2.zero;

            if (_player != null)
            {
                // The shot ray follows the resulting camera orientation, including this offset.
                Vector2 lookOffset = Vector2.Scale(_stressTremor, _stressAimSwayDegrees) *
                    Mathf.Lerp(0.35f, 1f, _aimBlend);
                _player.SetAimSway(lookOffset);
            }
        }

        static float SampleTremor(float time, float channel)
        {
            float drift = Mathf.Clamp01(Mathf.PerlinNoise(time * 2.1f, channel));
            float tremor = Mathf.Clamp01(Mathf.PerlinNoise(time * 7.3f, channel + 20f));
            return 2f * (drift * 0.58f + tremor * 0.42f) - 1f;
        }

        void UpdatePose(float aimBlend, bool loweredForFlashlight)
        {
            float reloadDip = _reloadRemaining > 0f
                ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(_reloadRemaining / _reloadDuration))
                : 0f;
            float flashlightDip = loweredForFlashlight
                ? Mathf.SmoothStep(0f, 1f, (Time.time - _flashlightLoweredAt) / BerettaView.HolsterDuration)
                : Mathf.SmoothStep(0f, 1f, (_flashlightRestoredAt - Time.time) / BerettaView.FlashlightRaiseDuration);

            if (_beretta != null)
            {
                _view.localPosition = Vector3.Lerp(BerettaHipPosition, BerettaAimPosition, aimBlend)
                    + new Vector3(-0.035f, -0.075f, -0.035f) * flashlightDip;
                _view.localRotation = Quaternion.Euler(
                    Mathf.Lerp(4f, 0.65f, aimBlend) + 8f * flashlightDip, 0f, Mathf.Lerp(-3f, 0f, aimBlend));
            }
            else
            {
                _placeholderRecoil = Mathf.SmoothDamp(_placeholderRecoil, 0f, ref _placeholderRecoilVelocity, 0.07f);
                _view.localPosition = Vector3.Lerp(HipPosition, AimPosition, aimBlend)
                    - Vector3.forward * (_placeholderRecoil * 0.04f)
                    - Vector3.up * (reloadDip * 0.18f + flashlightDip * 0.65f);
                _view.localRotation =
                    Quaternion.Euler(reloadDip * 24f + flashlightDip * 35f - _placeholderRecoil * 6f, 0f, 0f);
            }

            // Keep the sights near the shot ray while retaining some visible hand movement.
            float handTremor = Mathf.Lerp(1f, 0.25f, aimBlend) * (1f - flashlightDip);
            _view.localPosition += new Vector3(_stressTremor.x * 0.006f, _stressTremor.y * 0.004f, 0f) * handTremor;
            _view.localRotation *= Quaternion.AngleAxis(_stressTremor.x * _stressHandRollDegrees * handTremor,
                Vector3.forward);

            bool showView = _beretta != null
                ? _beretta.IsHolstering || (_equipped && !loweredForFlashlight)
                : _equipped && flashlightDip < 1f;
            if (IsHiddenExternally)
                showView = false;
            if (_view.gameObject.activeSelf != showView)
                _view.gameObject.SetActive(showView);
            if (_beretta != null && showView)
                _beretta.UpdatePose(aimBlend, _loaded);
        }

        void SetFieldOfView(float fieldOfView)
        {
            if (_followCamera != null)
                _followCamera.Lens.FieldOfView = fieldOfView;
            else if (_camera != null)
                _camera.fieldOfView = fieldOfView;
        }

        void OnDisable()
        {
            CancelReload();
            SetCombatStress(false);
            _stressBlend = 0f;
            _stressTremor = Vector2.zero;
            if (_player != null)
                _player.SetAimSway(Vector2.zero);
            if (_shotAudio != null)
                _shotAudio.Stop();
            if (_view == null)
                return;

            _equipped = false;
            _aiming = false;
            _scriptedEncounterAim = false;
            _flashlightRaiseAt = 0f;
            _flashlightRestoredAt = 0f;
            _shotPending = false;
            _aimedTriggerPending = false;
            _aimBlend = 0f;
            _aimVelocity = 0f;
            if (_beretta != null)
                _beretta.ResetPose();
            if (_player != null)
            {
                _player.LookSensitivityMultiplier = 1f;
                _player.IsAiming = false;
            }
            if (_muzzleFlash != null)
                _muzzleFlash.Stop();
            _view.gameObject.SetActive(false);
            SetFieldOfView(_normalFov);
            if (_hud != null)
                _hud.ShowPistol(false, false, _loaded, ReserveRounds, false);
        }

        void OnDestroy()
        {
            if (_shotAudio != null)
                Destroy(_shotAudio.gameObject);
            if (_reloadAudio != null)
                Destroy(_reloadAudio.gameObject);
            foreach (var mark in _impactMarks)
                Destroy(mark);

            if (_view != null)
                Destroy(_view.gameObject);
            if (_placeholderMaterial != null)
                Destroy(_placeholderMaterial);
            if (_weaponMaterial != null)
                Destroy(_weaponMaterial);
        }

        void CreateWeaponView()
        {
            _view = new GameObject("Pistol View").transform;
            _view.SetParent(_camera.transform, false);
            _placeholderMaterial = new Material(_placeholderShader);
            if (_berettaPrefab != null)
            {
                var beretta = Instantiate(_berettaPrefab, _view);
                if (beretta.Initialize(_reloadAudio))
                {
                    _beretta = beretta;
                    return;
                }

                beretta.gameObject.SetActive(false);
                Destroy(beretta.gameObject);
                Debug.LogWarning("The Beretta view is missing an animation or transform reference.", this);
            }

            CreatePlaceholder();
        }

        void CreatePlaceholder()
        {
            _weaponMaterial = new Material(_weaponShader != null ? _weaponShader : _placeholderShader);
            var steel = new Color32(58, 63, 69, 255);
            var grip = new Color32(73, 44, 34, 255);
            var sleeve = new Color32(49, 55, 48, 255);
            var hand = new Color32(161, 112, 84, 255);

            CreatePart(_view, "Slide", PrimitiveType.Cube, new(0f, 0.015f, 0.045f),
                new(0.050f, 0.045f, 0.185f), Vector3.zero, steel, true, 0.65f, 0.38f,
                receivesFlashlightSpill: false);
            CreatePart(_view, "Grip", PrimitiveType.Cube, new(0f, -0.051f, -0.018f),
                new(0.047f, 0.106f, 0.054f), new(18f, 0f, 0f), grip, true);
            CreatePart(_view, "Right Hand", PrimitiveType.Cube, new(0.018f, -0.063f, -0.026f),
                new(0.064f, 0.062f, 0.068f), new(10f, 0f, 0f), hand, true);
            CreatePart(_view, "Left Hand", PrimitiveType.Cube, new(-0.036f, -0.065f, 0.014f),
                new(0.058f, 0.054f, 0.073f), new(8f, 20f, 0f), hand, true);
            CreatePart(_view, "Right Sleeve", PrimitiveType.Cube, new(0.054f, -0.112f, -0.136f),
                new(0.072f, 0.073f, 0.24f), new(-20f, -14f, 0f), sleeve, true, 0f, 0.08f);
            CreatePart(_view, "Left Sleeve", PrimitiveType.Cube, new(-0.090f, -0.117f, -0.091f),
                new(0.072f, 0.073f, 0.24f), new(-22f, 26f, 0f), sleeve, true, 0f, 0.08f);
        }

        GameObject CreatePart(Transform parent, string name, PrimitiveType shape, Vector3 position,
            Vector3 size, Vector3 angles, Color color, bool lit = false, float metallic = 0f, float smoothness = 0.18f,
            bool receivesFlashlightSpill = true)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(angles);
            part.transform.localScale = size;

            var collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);

            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = lit ? _weaponMaterial : _placeholderMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColor, color);
            if (lit)
            {
                renderer.renderingLayerMask = WeaponRenderingLayer |
                    (receivesFlashlightSpill ? Flashlight.WeaponSpillRenderingLayer : 0u);
                properties.SetFloat(Metallic, metallic);
                properties.SetFloat(Smoothness, smoothness);
            }
            renderer.SetPropertyBlock(properties);
            return part;
        }
    }
}
