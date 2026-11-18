using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [DefaultExecutionOrder(190)]
    [AddComponentMenu("CarX/Flashlight")]
    public sealed class Flashlight : MonoBehaviour
    {
        const float ToggleInterval = 1f;
        const float DimmedCharge = 0.1f;
        const float MinChargeBrightness = 0.5f;
        const float WeaponSpillMinDistance = 0.24f;

        internal const uint WeaponSpillRenderingLayer = 1u << 2;

        [Header("Light")]
        [SerializeField, Min(1f)] float _beamRange = 60f;
        [SerializeField, Min(0f)] float _beamIntensity = 1134f;
        [SerializeField, Range(5f, 90f)] float _beamAngle = 32f;
        [SerializeField] Color _beamColor = new(1f, 0.95f, 0.86f);
        [SerializeField, Min(1f)] float _spillRange = 16f;
        [SerializeField, Min(0f)] float _spillIntensity = 11.34f;
        [SerializeField, Range(30f, 120f)] float _spillAngle = 76f;
        [SerializeField, Min(0f)] float _weaponSpillIntensity = 1.1f;

        [Header("Motion")]
        [SerializeField, Min(0.01f)] float _followTime = 0.075f;
        [SerializeField, Range(0f, 15f)] float _maxLagAngle = 5f;
        [SerializeField, Min(0.01f)] float _switchTime = 0.07f;
        [SerializeField, Min(0.1f)] float _surfaceBrightness = 3.15f;
        [SerializeField, Min(0.01f)] float _brightnessRecoveryTime = 0.22f;
        [SerializeField] LayerMask _obstacleLayers = Physics.DefaultRaycastLayers;

        [Header("Switch audio")]
        [SerializeField, FormerlySerializedAs("_switchOnClip")] AudioClip _switchClip;
        [SerializeField, Range(0f, 1f)] float _switchVolume = 0.325f;

        [Header("Player")]
        [SerializeField] PlayerHealth _health;
        [SerializeField] bool _requiresPlayerHealth;
        [SerializeField] Transform _chestAnchor;
        [SerializeField] Vector3 _chestOffset = new(0.12f, 0.18f, 0.18f);

        Light _beam;
        Light _spill;
        Light _weaponSpill;
        AudioSource _switchAudio;
        PlayerPistol _pistol;
        PlayerInventory _inventory;
        Transform _view;
        Vector3 _viewOffset;
        Quaternion _viewRotation;
        Quaternion _smoothedRotation;
        int _obstacleMask;
        bool _isOn;
        bool _toggleQueued;
        float _nextToggleAt;
        float _switchAt;
        float _switchBlend;
        float _mountFraction = 1f;
        float _beamPower;
        float _spillPower;

        float Charge => _health != null ? _health.FlashlightCharge : 1f;
        internal bool IsOn => isActiveAndEnabled && _isOn && Charge > 0f;

        internal bool IsPointInMainBeam(Vector3 point)
        {
            if (!IsOn || _beam == null || !_beam.enabled || _beam.intensity <= 0f)
                return false;

            Vector3 offset = point - _beam.transform.position;
            float distance = offset.magnitude;
            float coneCosine = Mathf.Cos(_beam.spotAngle * 0.5f * Mathf.Deg2Rad);
            return distance <= _beam.range &&
                Vector3.Dot(_beam.transform.forward, offset) >= distance * coneCosine;
        }

        void Awake()
        {
            _beam = GetComponent<Light>();
            _beam.enabled = false;
            if (_health == null)
                _health = GetComponentInParent<PlayerHealth>();
            if (_health == null)
                _health = transform.root.GetComponentInChildren<PlayerHealth>(true);

            var controller = GetComponentInParent<CharacterController>();
            _obstacleMask = _obstacleLayers.value;
            if (_health != null || controller != null)
            {
                var player = _health != null ? _health.transform : controller.transform;
                _obstacleMask &= ~(1 << player.gameObject.layer);
                _pistol = player.GetComponent<PlayerPistol>();
                _inventory = player.GetComponent<PlayerInventory>();
            }

            if (_switchClip != null)
            {
                _switchAudio = gameObject.AddComponent<AudioSource>();
                _switchAudio.playOnAwake = false;
                _switchAudio.spatialBlend = 0f;
            }

            _view = transform.parent;
            var camera = transform.root.GetComponentInChildren<Camera>();
            if (camera == null && _health != null)
                camera = Camera.main;
            if (camera != null)
                _view = camera.transform;
            _viewOffset = transform.localPosition;
            _viewRotation = transform.localRotation;
            _smoothedRotation = transform.rotation;

            // Use the imported cookie already assigned to the prefab: it works in URP builds.
            _beam.type = LightType.Spot;
            _beam.range = _beamRange;
            _beam.spotAngle = _beamAngle;
            _beam.innerSpotAngle = _beamAngle * 0.65f;
            _beam.color = _beamColor;
            _beam.intensity = 0f;
            _beam.bounceIntensity = 0f;
            _beam.shadows = LightShadows.Soft;
            _beam.renderingLayerMask = 1;

            var spillObject = new GameObject("Flashlight Peripheral Light");
            spillObject.transform.SetParent(transform, false);
            _spill = spillObject.AddComponent<Light>();
            _spill.enabled = false;
            _spill.type = LightType.Spot;
            _spill.range = _spillRange;
            _spill.spotAngle = _spillAngle;
            _spill.innerSpotAngle = _spillAngle * 0.65f;
            _spill.color = _beamColor;
            _spill.intensity = 0f;
            _spill.bounceIntensity = 0f;
            _spill.shadows = LightShadows.None;
            _spill.renderingLayerMask = 1;

            if (_pistol != null && _weaponSpillIntensity > 0f &&
                UniversalRenderPipeline.asset != null && UniversalRenderPipeline.asset.useRenderingLayers)
            {
                // The wide, soft edge of the chest light reaches the forearms and lower frame.
                var weaponSpill = new GameObject("Flashlight Weapon Spill");
                weaponSpill.transform.SetParent(transform, false);
                _weaponSpill = weaponSpill.AddComponent<Light>();
                _weaponSpill.enabled = false;
                _weaponSpill.type = LightType.Spot;
                _weaponSpill.spotAngle = 112f;
                _weaponSpill.innerSpotAngle = 48f;
                _weaponSpill.color = _beamColor;
                _weaponSpill.intensity = 0f;
                _weaponSpill.range = 0.8f;
                _weaponSpill.bounceIntensity = 0f;
                _weaponSpill.shadows = LightShadows.None;
                _weaponSpill.GetUniversalAdditionalLightData().renderingLayers = WeaponSpillRenderingLayer;
            }
        }

        void OnEnable()
        {
            if (_health != null)
                _health.HealthChanged += OnHealthChanged;
        }

        void OnDisable()
        {
            if (_health != null)
                _health.HealthChanged -= OnHealthChanged;
            if (_switchAudio != null)
                _switchAudio.Stop();
            TurnOff();
        }

        void OnHealthChanged(int _)
        {
            if (Charge <= 0f)
                TurnOff();
        }

        void Update()
        {
            if (Charge <= 0f)
            {
                TurnOff();
                return;
            }
            if (Time.timeScale == 0f)
                return;

            float now = Time.time;
            if (_switchAt > 0f && now >= _switchAt)
            {
                _switchAt = 0f;
                SwitchLight();
            }

            if (_inventory != null && _inventory.BlocksWeaponInput)
                return;

#if ENABLE_INPUT_SYSTEM
            bool toggle = Keyboard.current?.fKey.wasPressedThisFrame == true;
#else
            bool toggle = Input.GetKeyDown(KeyCode.F);
#endif
            if (toggle)
                _toggleQueued = true;
            if (!_toggleQueued || _switchAt > 0f || now < _nextToggleAt)
                return;

            float switchDelay = 0f;
            if (_pistol != null && !_pistol.TryLowerForFlashlight(ToggleInterval, out switchDelay))
                return;
            _toggleQueued = false;
            _nextToggleAt = now + ToggleInterval;
            if (switchDelay > 0f)
                _switchAt = now + switchDelay;
            else
                SwitchLight();
        }

        void SwitchLight()
        {
            if (_switchAudio != null)
                _switchAudio.PlayOneShot(_switchClip, _switchVolume);
            SetLightEnabled(!_isOn);
        }

        void SetLightEnabled(bool enabled)
        {
            _isOn = enabled;
            _switchBlend = 0f;
            if (_beam != null) _beam.enabled = enabled;
            if (_spill != null) _spill.enabled = enabled;
            if (_weaponSpill != null && !enabled) _weaponSpill.enabled = false;
        }

        void LateUpdate()
        {
            float charge = Charge;
            if (!_isOn || charge <= 0f || Time.deltaTime <= 0f)
                return;

            float dt = Time.deltaTime;
            if (_view != null)
            {
                Quaternion target = _view.rotation * _viewRotation;
                _smoothedRotation = Quaternion.RotateTowards(target, _smoothedRotation, _maxLagAngle);
                _smoothedRotation = Quaternion.Slerp(_smoothedRotation, target,
                    1f - Mathf.Exp(-dt / Mathf.Max(0.01f, _followTime)));

                Vector3 origin = _view.position;
                // The source follows the capsule's chest, including crouch and torso lean.
                Vector3 chest = _chestAnchor != null
                    ? _chestAnchor.TransformPoint(_chestOffset)
                    : _view.TransformPoint(_viewOffset);
                Vector3 offset = chest - origin;
                float length = offset.magnitude;
                float safeFraction = 1f;
                if (length > 0.001f && Physics.SphereCast(origin, 0.035f, offset / length,
                    out var wall, length, _obstacleMask, QueryTriggerInteraction.Ignore))
                    safeFraction = Mathf.Max(0f, wall.distance - 0.015f) / length;

                _mountFraction = safeFraction < _mountFraction ? safeFraction
                    : Mathf.Lerp(_mountFraction, safeFraction, 1f - Mathf.Exp(-dt / 0.12f));
                transform.SetPositionAndRotation(origin + offset * _mountFraction, _smoothedRotation);
            }

            // Geometry may adjust brightness, but never switches the light off.
            float distance = Physics.Raycast(transform.position, transform.forward, out var surface,
                _beamRange, _obstacleMask, QueryTriggerInteraction.Ignore)
                ? surface.distance : _beamRange;
            float safePower = _surfaceBrightness * Mathf.Max(0.06f, distance) * Mathf.Max(0.06f, distance);
            float chargeBrightness = Mathf.Lerp(MinChargeBrightness, 1f,
                Mathf.InverseLerp(DimmedCharge, 1f, charge));
            float beamTarget = Mathf.Min(_beamIntensity, safePower) * chargeBrightness;
            float spillTarget = Mathf.Min(_spillIntensity, safePower * 0.45f) * chargeBrightness;
            float recovery = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, _brightnessRecoveryTime));
            _beamPower = Mathf.Lerp(Mathf.Min(_beamPower, beamTarget * 2f), beamTarget, recovery);
            _spillPower = Mathf.Lerp(Mathf.Min(_spillPower, spillTarget * 2f), spillTarget, recovery);
            _switchBlend = Mathf.MoveTowards(_switchBlend, 1f, dt / Mathf.Max(0.01f, _switchTime));
            float fade = Mathf.SmoothStep(0f, 1f, _switchBlend);
            _beam.intensity = _beamPower * fade;
            _spill.intensity = _spillPower * fade;
            UpdateWeaponSpill(chargeBrightness * fade);
        }

        void UpdateWeaponSpill(float power)
        {
            if (_weaponSpill == null)
                return;

            float proximity = 0f;
            if (_pistol != null && _pistol.IsEquipped)
            {
                // Limit the inverse-square hotspot when the grip moves close to the flashlight.
                float distanceSquared = (_pistol.GripLightPosition - transform.position).sqrMagnitude;
                proximity = Mathf.Clamp01(distanceSquared / (WeaponSpillMinDistance * WeaponSpillMinDistance));
            }
            _weaponSpill.intensity = _weaponSpillIntensity * power * proximity;
            _weaponSpill.enabled = _weaponSpill.intensity > 0f;
        }

        public bool IsOn => _isOn;

        /// <summary>Switches the light off with the usual click (used when the dark-orientation mode is asked for).</summary>
        public void SwitchOff()
        {
            if (!_isOn)
                return;
            _nextToggleAt = Time.time + ToggleInterval;
            if (_switchAudio != null)
                _switchAudio.PlayOneShot(_switchClip, _switchVolume);
            TurnOff();
        }

        void TurnOff()
        {
            _toggleQueued = false;
            _switchAt = 0f;
            SetLightEnabled(false);
        }

        void OnDestroy()
        {
            if (_switchAudio != null)
                Destroy(_switchAudio);
            if (_spill != null)
                Destroy(_spill.gameObject);
            if (_weaponSpill != null)
                Destroy(_weaponSpill.gameObject);
        }
    }
}
