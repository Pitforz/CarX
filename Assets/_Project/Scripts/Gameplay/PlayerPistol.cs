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
        const float SilentReloadDuration = 0.55f;
        const float AimFovReduction = 0.2808f;
        const float AimLookSensitivity = 0.7f;
        const float ShotInterval = 0.25f;
        const float FlashlightPoseTransition = 0.15f;
        const float LeanFovBonus = 2f;
        const float ShotRange = 100f;
        const float HipSpread = 0.045f;
        const int MaxImpactMarks = 80;

        [SerializeField] Shader _placeholderShader;
        [SerializeField] Shader _weaponShader;
        [SerializeField, Min(0.01f)] float _aimTransitionTime = 0.24f;
        [Header("Pistol audio")]
        [SerializeField] AudioClip _shotClip;
        [SerializeField] AudioClip _reloadClip;
        [SerializeField, Range(0f, 1f)] float _shotVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] float _reloadVolume = 0.8f;

        static readonly Vector3 HipPosition = new(0.16f, -0.13f, 0.43f);
        static readonly Vector3 AimPosition = new(0f, -0.09f, 0.38f);
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        Camera _camera;
        StarterAssets.FirstPersonController _player;
        CinemachineCamera _followCamera;
        PlayerStatusHud _hud;
        PlayerInventory _inventory;
        Transform _view;
        PlayerMuzzleFlash _muzzleFlash;
        AudioSource _shotAudio;
        AudioSource _reloadAudio;
        Material _placeholderMaterial;
        Material _weaponMaterial;
        readonly Queue<GameObject> _impactMarks = new();
        int _loaded = MagazineSize;
        int _reserve = MagazineSize;
        bool _equipped;
        bool _shotPending;
        bool _shotAimed;
        float _reloadRemaining;
        float _reloadDuration;
        float _flashlightLoweredAt;
        float _flashlightRaiseAt;
        float _nextShotTime;
        float _aimBlend;
        float _aimVelocity;
        float _recoil;
        float _recoilVelocity;
        float _normalFov;

        internal Camera ViewCamera => _camera;
        internal bool IsEquipped => _equipped;
        int ReserveRounds => _inventory != null ? _inventory.ReserveRounds : _reserve;

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
            _muzzleFlash = GetComponent<PlayerMuzzleFlash>();
            _shotAudio = CreateAudioSource("Pistol Shot Audio", _shotVolume);
            _reloadAudio = CreateAudioSource("Pistol Reload Audio", _reloadVolume);
            _reloadAudio.clip = _reloadClip;
            _normalFov = _followCamera != null ? _followCamera.Lens.FieldOfView : _camera.fieldOfView;
            CreatePlaceholder();
            _muzzleFlash.Initialize(_view, _camera);
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
            bool firePressed = mouse?.leftButton.wasPressedThisFrame == true;
            bool aimHeld = mouse?.rightButton.isPressed == true;
#else
            bool equipPressed = Input.GetKeyDown(KeyCode.Alpha1);
            bool reloadPressed = Input.GetKeyDown(KeyCode.R);
            bool firePressed = Input.GetMouseButtonDown(0);
            bool aimHeld = Input.GetMouseButton(1);
#endif
            UpdateReload();

            if (_inventory != null && _inventory.BlocksWeaponInput)
                equipPressed = reloadPressed = firePressed = aimHeld = false;

            if (equipPressed)
                SetEquipped(!_equipped);

            bool loweredForFlashlight = Time.time < _flashlightRaiseAt;
            bool canUsePistol = _equipped && !loweredForFlashlight;
            if (canUsePistol && reloadPressed && ReserveRounds > 0 && _reloadRemaining <= 0f)
                StartReload();

            bool aiming = canUsePistol && aimHeld && _reloadRemaining <= 0f;
            if (canUsePistol && firePressed && _loaded > 0 && _reloadRemaining <= 0f &&
                Time.time >= _nextShotTime)
            {
                _shotPending = true;
                _shotAimed = aiming;
                Fire();
            }

            // One continuous blend drives both the hands and the lens, even when RMB is tapped rapidly.
            if (Time.deltaTime > 0f)
            {
                float aimTarget = aiming ? 1f : 0f;
                _aimBlend = Mathf.SmoothDamp(_aimBlend, aimTarget, ref _aimVelocity,
                    _aimTransitionTime * 0.5f);
                if (Mathf.Abs(_aimBlend - aimTarget) < 0.0001f && Mathf.Abs(_aimVelocity) < 0.001f)
                {
                    _aimBlend = aimTarget;
                    _aimVelocity = 0f;
                }
            }
            float zoom = _aimBlend;
            if (_player != null)
                _player.LookSensitivityMultiplier = Mathf.Lerp(1f, AimLookSensitivity, zoom);
            float leanFov = LeanFovBonus * (_player != null ? _player.LeanAmount : 0f);
            SetFieldOfView(_normalFov * (1f - AimFovReduction * zoom) + leanFov);
            UpdatePose(zoom, loweredForFlashlight);
            _hud.ShowPistol(_equipped, aiming, _loaded, ReserveRounds, _reloadRemaining > 0f);
        }

        internal void SetEquipped(bool equipped)
        {
            if (_view == null || !isActiveAndEnabled)
                return;

            if (equipped && _inventory != null)
                _inventory.StowMagazine();

            _equipped = equipped;
            _view.gameObject.SetActive(equipped);
            if (!equipped)
            {
                CancelReload();
                _muzzleFlash.Stop();
            }
        }

        internal void LowerForFlashlight(float duration)
        {
            if (!_equipped || !isActiveAndEnabled)
                return;

            _flashlightLoweredAt = Time.time;
            _flashlightRaiseAt = Time.time + duration;
            CancelReload();
            _muzzleFlash.Stop();
        }

        void LateUpdate()
        {
            if (!_shotPending)
                return;

            _shotPending = false;
            var centerRay = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            bool centerHasHit = FindShotHit(centerRay, out var centerHit);
            if (!_shotAimed)
            {
                Vector2 spread = Random.insideUnitCircle * HipSpread;
                var spreadRay = _camera.ViewportPointToRay(
                    new Vector3(0.5f + spread.x / _camera.aspect, 0.5f + spread.y, 0f));
                if (FindShotHit(spreadRay, out var spreadHit))
                {
                    // A small hip-fire offset should not turn a shot at a wall into one at the floor or ceiling.
                    if (Mathf.Abs(spreadHit.normal.y) < 0.7f ||
                        (centerHasHit && spreadHit.collider == centerHit.collider &&
                         Vector3.Dot(spreadHit.normal, centerHit.normal) > 0.8f))
                    {
                        PlaceImpactMark(spreadHit);
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            if (centerHasHit)
                PlaceImpactMark(centerHit);
        }

        bool FindShotHit(Ray ray, out RaycastHit closestHit)
        {
            closestHit = default;
            float closestDistance = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(ray, ShotRange, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= closestDistance)
                    continue;

                closestDistance = hit.distance;
                closestHit = hit;
            }

            return closestHit.collider != null;
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
            _reloadDuration = _reloadClip != null ? _reloadClip.length : SilentReloadDuration;
            _reloadRemaining = _reloadDuration;
            if (_reloadClip != null)
                _reloadAudio.Play();
        }

        void CancelReload()
        {
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

            if (_reloadClip != null && _reloadAudio.isPlaying)
            {
                // The PCM playback position drives the placeholder animation and HUD timer.
                // Keep the reload active until the complete clip, including pocket handling, ends.
                float elapsed = _reloadAudio.timeSamples / (float)_reloadClip.frequency;
                _reloadRemaining = Mathf.Max(0.001f, _reloadDuration - elapsed);
                return;
            }
            if (_reloadClip == null)
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

        void Fire()
        {
            _nextShotTime = Time.time + ShotInterval;
            _loaded--;
            _recoil = Mathf.Min(_recoil + 0.65f, 1f);
            _recoilVelocity = 0f;
            _muzzleFlash.Play();
            if (_shotClip != null)
                _shotAudio.PlayOneShot(_shotClip);
        }

        void UpdatePose(float aimBlend, bool loweredForFlashlight)
        {
            _recoil = Mathf.SmoothDamp(_recoil, 0f, ref _recoilVelocity, 0.07f);
            float reloadDip = _reloadRemaining > 0f
                ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(_reloadRemaining / _reloadDuration))
                : 0f;
            float flashlightDip = 0f;
            if (loweredForFlashlight)
            {
                float transition = Mathf.Min(Time.time - _flashlightLoweredAt, _flashlightRaiseAt - Time.time);
                flashlightDip = Mathf.SmoothStep(0f, 1f, transition / FlashlightPoseTransition);
            }

            _view.localPosition = Vector3.Lerp(HipPosition, AimPosition, aimBlend)
                - Vector3.forward * (_recoil * 0.04f)
                - Vector3.up * (reloadDip * 0.18f + flashlightDip * 0.65f);
            _view.localRotation = Quaternion.Euler(reloadDip * 24f + flashlightDip * 35f - _recoil * 6f, 0f, 0f);
            bool showView = _equipped && flashlightDip < 1f;
            if (_view.gameObject.activeSelf != showView)
                _view.gameObject.SetActive(showView);
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
            if (_shotAudio != null)
                _shotAudio.Stop();
            if (_view == null)
                return;

            _equipped = false;
            _flashlightRaiseAt = 0f;
            _shotPending = false;
            _aimBlend = 0f;
            _aimVelocity = 0f;
            if (_player != null)
                _player.LookSensitivityMultiplier = 1f;
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

        void CreatePlaceholder()
        {
            _view = new GameObject("PM View Model").transform;
            _view.SetParent(_camera.transform, false);
            _placeholderMaterial = new Material(_placeholderShader);
            _weaponMaterial = new Material(_weaponShader != null ? _weaponShader : _placeholderShader);
            var steel = new Color32(58, 63, 69, 255);
            var grip = new Color32(73, 44, 34, 255);
            var sleeve = new Color32(49, 55, 48, 255);
            var hand = new Color32(161, 112, 84, 255);

            CreatePart(_view, "Slide", PrimitiveType.Cube, new(0f, 0.015f, 0.045f),
                new(0.050f, 0.045f, 0.185f), Vector3.zero, steel, true, 0.65f, 0.38f);
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
            Vector3 size, Vector3 angles, Color color, bool lit = false, float metallic = 0f, float smoothness = 0.18f)
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
                renderer.renderingLayerMask = WeaponRenderingLayer;
                properties.SetFloat(Metallic, metallic);
                properties.SetFloat(Smoothness, smoothness);
            }
            renderer.SetPropertyBlock(properties);
            return part;
        }
    }
}
