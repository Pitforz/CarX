using StarterAssets;
using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerHealth), typeof(PlayerStatusHud), typeof(FirstPersonController))]
    public sealed class PlayerCriticalCondition : MonoBehaviour
    {
        const int CriticalHealthThreshold = 5;
        const float InitialDarkness = 0.15f;
        const float CriticalDarkness = 0.32f;
        const float FatalBleedingDarkness = 0.78f;
        const float FatalBleedingSpeedMultiplier = 0.5f;
        const float HeartbeatVolume = 0.36f;
        const float BreathingVolume = 0.43f;
        const float EffectTransitionTime = 0.35f;
        const float DeathFadeTime = 0.5f;

        [SerializeField] AudioClip _heartbeatClip;
        [SerializeField] AudioClip _breathingClip;

        PlayerHealth _health;
        PlayerStatusHud _hud;
        FirstPersonController _controller;
        GameObject _audioRoot;
        AudioSource _heartbeat;
        AudioSource _breathing;
        float _audioGain;
        float _darkness;
        double _deathFadeStartedAt;
        float _deathFadeStartGain;

        void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _hud = GetComponent<PlayerStatusHud>();
            _controller = GetComponent<FirstPersonController>();

            _audioRoot = new GameObject("Critical Condition Audio");
            _audioRoot.transform.SetParent(transform, false);
            _heartbeat = CreateLoop(_heartbeatClip);
            _breathing = CreateLoop(_breathingClip);
        }

        void OnEnable()
        {
            _health.HealthChanged += OnHealthChanged;
        }

        void OnHealthChanged(int health)
        {
            if (health != 0)
                return;

            _deathFadeStartedAt = Time.unscaledTimeAsDouble;
            _deathFadeStartGain = _audioGain;
        }

        void Update()
        {
            if (_health.IsDead)
            {
                UpdateDeathFade();
                return;
            }

            bool fatalBleeding = _health.Bleeding >= _health.CurrentHealth;
            bool critical = _health.CurrentHealth <= CriticalHealthThreshold;
            bool playFatalBleedingAudio = fatalBleeding && critical;
            _controller.MovementSpeedMultiplier = fatalBleeding ? FatalBleedingSpeedMultiplier : 1f;

            float blendStep = Time.deltaTime / EffectTransitionTime;
            float darknessProgress = Mathf.InverseLerp(CriticalHealthThreshold, 1f, _health.CurrentHealth);
            float maxDarkness = fatalBleeding ? FatalBleedingDarkness : CriticalDarkness;
            float targetDarkness = critical ? Mathf.Lerp(InitialDarkness, maxDarkness, darknessProgress) : 0f;
            _darkness = Mathf.MoveTowards(_darkness, targetDarkness, blendStep);
            _hud.SetCriticalDarkness(_darkness);

            _audioGain = Mathf.MoveTowards(_audioGain, playFatalBleedingAudio ? 2f : 0f, 2f * blendStep);
            SetLoopVolume(_heartbeat, HeartbeatVolume * _audioGain);
            SetLoopVolume(_breathing, BreathingVolume * _audioGain);
        }

        void UpdateDeathFade()
        {
            if (_deathFadeStartGain <= 0f)
                return;

            float fade = Mathf.Clamp01((float)((Time.unscaledTimeAsDouble - _deathFadeStartedAt) / DeathFadeTime));
            _audioGain = _deathFadeStartGain * (1f - Mathf.SmoothStep(0f, 1f, fade));
            if (_heartbeat != null)
                _heartbeat.volume = HeartbeatVolume * _audioGain;
            if (_breathing != null)
                _breathing.volume = BreathingVolume * _audioGain;

            if (fade >= 1f)
            {
                _deathFadeStartGain = 0f;
                StopLoops();
            }
        }

        AudioSource CreateLoop(AudioClip clip)
        {
            if (clip == null)
                return null;

            var source = _audioRoot.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.priority = 96;
            source.clip = clip;
            return source;
        }

        void SetLoopVolume(AudioSource source, float volume)
        {
            if (source == null)
                return;

            source.volume = volume;
            if (volume > 0f && !source.isPlaying && !AudioListener.pause)
                source.Play();
            else if (volume <= 0f && source.isPlaying)
                source.Stop();
        }

        void ClearEffects()
        {
            _audioGain = 0f;
            _deathFadeStartGain = 0f;
            _darkness = 0f;
            StopLoops();
            if (_controller != null)
                _controller.MovementSpeedMultiplier = 1f;
            if (_hud != null)
                _hud.SetCriticalDarkness(0f);
        }

        void StopLoops()
        {
            if (_heartbeat != null)
                _heartbeat.Stop();
            if (_breathing != null)
                _breathing.Stop();
        }

        void OnDisable()
        {
            if (_health != null)
                _health.HealthChanged -= OnHealthChanged;
            ClearEffects();
        }

        void OnDestroy()
        {
            if (_audioRoot != null)
                Destroy(_audioRoot);
        }
    }
}
