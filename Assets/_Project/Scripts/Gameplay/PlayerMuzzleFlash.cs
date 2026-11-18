using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(220)]
    [AddComponentMenu("CarX/Player Muzzle Flash")]
    public sealed class PlayerMuzzleFlash : MonoBehaviour
    {
        [SerializeField] Shader _glowShader;
        [Header("Environment lighting")]
        [SerializeField, Min(0f)] float _peakIntensity = 12f;
        [SerializeField, Min(0.1f)] float _lightRange = 3.2f;
        [SerializeField, Range(0.03f, 0.15f)] float _lightDuration = 0.085f;
        [SerializeField, Range(0f, 0.25f)] float _shotVariation = 0.1f;
        [Header("Small muzzle glow")]
        [SerializeField, Range(0.005f, 0.04f)] float _glowSize = 0.014f;
        [SerializeField, Range(0f, 2f)] float _glowIntensity = 0.6f;
        [SerializeField, Range(0.01f, 0.06f)] float _glowDuration = 0.028f;

        const float ProbeRadius = 0.035f;
        const float WallPadding = 0.015f;
        static readonly Color HotColor = new(1f, 0.86f, 0.64f);
        static readonly Color CoolingColor = new(1f, 0.38f, 0.12f);
        static readonly int GlowColor = Shader.PropertyToID("_Color");
        static readonly int GlowPhase = Shader.PropertyToID("_Phase");

        readonly RaycastHit[] _wallHits = new RaycastHit[16];
        readonly Collider[] _overlaps = new Collider[16];
        Camera _camera;
        Transform _weaponView;
        Transform _muzzle;
        Light _light;
        MeshRenderer _glow;
        Material _glowMaterial;
        MaterialPropertyBlock _glowProperties;
        bool _playing;
        float _startedAt;
        float _duration;
        float _shotStrength;
        float _shotGlowSize;
        float _shotRoll;

        public void Initialize(Transform weaponView, Camera viewCamera, Transform muzzleAnchor = null)
        {
            if (_muzzle != null)
                return;

            _camera = viewCamera;
            _weaponView = weaponView;
            _muzzle = new GameObject("Muzzle Flash").transform;
            _muzzle.SetParent(muzzleAnchor != null ? muzzleAnchor : weaponView, false);
            if (muzzleAnchor == null)
                _muzzle.localPosition = new Vector3(0f, 0.016f, 0.16f);

            var lightObject = new GameObject("Shot Environment Light");
            lightObject.transform.SetParent(_muzzle, false);
            _light = lightObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = _lightRange;
            _light.intensity = 0f;
            _light.bounceIntensity = 0f;
            _light.renderMode = LightRenderMode.ForcePixel;
            _light.shadows = LightShadows.Soft;
            _light.shadowStrength = 1f;
            _light.shadowNearPlane = 0.02f;
            _light.shadowBias = 0.03f;
            _light.shadowNormalBias = 0.12f;

            var lightData = _light.GetUniversalAdditionalLightData();
            lightData.renderingLayers = (uint)lightData.renderingLayers | PlayerPistol.WeaponRenderingLayer;
            lightData.usePipelineSettings = false;
            lightData.additionalLightsShadowResolutionTier =
                UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh;
            lightData.softShadowQuality = SoftShadowQuality.High;

            if (_glowShader != null)
            {
                var glowObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                glowObject.name = "Muzzle Glow";
                glowObject.transform.SetParent(_muzzle, false);
                var collider = glowObject.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                _glow = glowObject.GetComponent<MeshRenderer>();
                _glow.shadowCastingMode = ShadowCastingMode.Off;
                _glow.receiveShadows = false;
                _glow.lightProbeUsage = LightProbeUsage.Off;
                _glow.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _glowMaterial = new Material(_glowShader) { name = "Muzzle Glow (Runtime)" };
                _glow.sharedMaterial = _glowMaterial;
                _glowProperties = new MaterialPropertyBlock();
            }

            Stop();
        }

        public void Play()
        {
            if (_muzzle == null || !isActiveAndEnabled)
                return;

            // Objects and materials are reused; variation is sampled once, never flickered every frame.
            _shotStrength = Random.Range(1f - _shotVariation, 1f + _shotVariation);
            _shotGlowSize = _glowSize * Random.Range(0.9f, 1.1f);
            _shotRoll = Random.Range(0f, 360f);
            _duration = Mathf.Max(0.01f, _lightDuration) * Random.Range(0.95f, 1.05f);
            _startedAt = Time.time;
            _playing = true;
            _muzzle.gameObject.SetActive(true);
        }

        // Run after the camera and weapon pose, including crouch, lean, aim and recoil.
        void LateUpdate()
        {
            if (!_playing || _muzzle == null || _camera == null)
                return;
            if (!_muzzle.gameObject.activeInHierarchy)
            {
                Stop();
                return;
            }

            float elapsed = Time.time - _startedAt;
            if (elapsed >= Mathf.Max(_duration, _glowDuration))
            {
                Stop();
                return;
            }

            bool safeOrigin = TryGetLightPosition(out var position, out bool muzzleClear);
            _light.enabled = safeOrigin && elapsed < _duration;
            if (safeOrigin)
                _light.transform.position = position;

            float frameDuration = Mathf.Max(Time.deltaTime, 0.001f);
            float phase = Mathf.Clamp01(elapsed / _duration);
            Color color = Color.Lerp(HotColor, CoolingColor, Mathf.SmoothStep(0f, 1f, phase));
            // Average the short impulse over a frame so low FPS does not stretch a full-strength flash.
            float energy = 0.82f * AverageDecay(elapsed, frameDuration, _duration, 6)
                + 0.18f * AverageDecay(elapsed, frameDuration, _duration, 2);
            _light.intensity = _peakIntensity * _shotStrength * energy;
            _light.color = color;

            if (_glow == null)
                return;

            _glow.enabled = safeOrigin && muzzleClear && elapsed < _glowDuration;
            if (!_glow.enabled)
                return;

            float glowPhase = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, _glowDuration));
            _glow.transform.rotation = _camera.transform.rotation * Quaternion.AngleAxis(_shotRoll, Vector3.forward);
            _glow.transform.localScale = Vector3.one * (_shotGlowSize * Mathf.Lerp(0.8f, 1.15f, glowPhase));
            float glowEnergy = AverageDecay(elapsed, frameDuration, _glowDuration, 3);
            _glowProperties.SetColor(GlowColor,
                Color.Lerp(HotColor, CoolingColor, glowPhase) * (_glowIntensity * _shotStrength * glowEnergy));
            _glowProperties.SetFloat(GlowPhase, glowPhase);
            _glow.SetPropertyBlock(_glowProperties);
        }

        static float AverageDecay(float elapsed, float frameDuration, float duration, int power)
        {
            duration = Mathf.Max(0.001f, duration);
            float start = 1f - Mathf.Clamp01(elapsed / duration);
            float end = 1f - Mathf.Clamp01((elapsed + frameDuration) / duration);
            return (Mathf.Pow(start, power + 1) - Mathf.Pow(end, power + 1))
                * duration / ((power + 1) * frameDuration);
        }

        bool TryGetLightPosition(out Vector3 position, out bool muzzleClear)
        {
            Vector3 origin = _camera.transform.position;
            position = origin;
            muzzleClear = false;

            int overlaps = Physics.OverlapSphereNonAlloc(origin, ProbeRadius, _overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // A full buffer can hide an obstacle behind player colliders; fail closed in that case.
            if (overlaps == _overlaps.Length)
                return false;
            for (int i = 0; i < overlaps; i++)
            {
                if (!IsPlayerCollider(_overlaps[i]))
                    return false;
            }

            Vector3 offset = _muzzle.position - origin;
            float distance = offset.magnitude;
            if (distance <= 0.001f)
                return true;

            Vector3 direction = offset / distance;
            int count = Physics.SphereCastNonAlloc(origin, ProbeRadius, direction, _wallHits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == _wallHits.Length)
                return false;

            float safeDistance = distance;
            for (int i = 0; i < count; i++)
            {
                if (IsPlayerCollider(_wallHits[i].collider))
                    continue;
                safeDistance = Mathf.Min(safeDistance, Mathf.Max(0f, _wallHits[i].distance - WallPadding));
            }

            position = origin + direction * safeDistance;
            muzzleClear = safeDistance >= distance - 0.001f;
            return true;
        }

        bool IsPlayerCollider(Collider collider)
        {
            return collider.transform.IsChildOf(transform) || collider.transform.IsChildOf(_weaponView);
        }

        public void Stop()
        {
            _playing = false;
            if (_light != null)
            {
                _light.intensity = 0f;
                _light.enabled = false;
            }
            if (_muzzle != null)
                _muzzle.gameObject.SetActive(false);
        }

        void OnDisable() => Stop();

        void OnDestroy()
        {
            if (_muzzle != null)
                Destroy(_muzzle.gameObject);
            if (_glowMaterial != null)
                Destroy(_glowMaterial);
        }
    }
}
