using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [AddComponentMenu("CarX/Player Health")]
    public class PlayerHealth : MonoBehaviour
    {
        internal enum DamageSource { Other, Melee, Gunfire }

        public const int MaxHealth = 100;
        const int TestDamage = 5;
        const int TestBleeding = 5;
        const double BleedingInterval = 7.0;
        const double HealthRecoveryInterval = 50.0;
        const int MaxFlashlightCondition = 100;
        const int FlashlightDamagePerHealthPoint = 2;
        const float FlashlightRepairThreshold = 0.7f;

        [SerializeField] AudioClip[] _hurtClips;

        public int CurrentHealth { get; private set; } = MaxHealth;
        public int Bleeding { get; private set; }
        public bool IsDead => CurrentHealth == 0;
        public event Action<int> HealthChanged;
        public event Action<int> BleedingChanged;
        internal event Action DirectDamageTaken;
        double _nextBleedingTick;
        double _nextHealthRecovery;
        int _flashlightCondition = MaxFlashlightCondition;
        AudioSource _hurtAudio;

        internal float FlashlightCharge => _flashlightCondition / (float)MaxFlashlightCondition;
        internal DamageSource FlashlightBreakCause { get; private set; }
        internal bool CanRepairFlashlight => !IsDead && FlashlightCharge < FlashlightRepairThreshold;

        internal bool TryRepairFlashlight()
        {
            if (!CanRepairFlashlight)
                return false;

            _flashlightCondition = MaxFlashlightCondition;
            FlashlightBreakCause = DamageSource.Other;
            return true;
        }

        public void TakeDamage(int amount)
        {
            SetHealth(CurrentHealth - Mathf.Clamp(amount, 0, MaxHealth));
        }

        internal void TakeAttackDamage(int amount, DamageSource source = DamageSource.Melee)
        {
            if (amount <= 0 || IsDead)
                return;

            SetHealth(CurrentHealth - Mathf.Clamp(amount, 0, MaxHealth), isDirectDamage: true, source: source);
            if (_hurtClips == null || _hurtClips.Length == 0)
                return;

            var clip = _hurtClips[UnityEngine.Random.Range(0, _hurtClips.Length)];
            if (clip == null)
                return;

            if (_hurtAudio == null)
            {
                _hurtAudio = gameObject.AddComponent<AudioSource>();
                _hurtAudio.playOnAwake = false;
                _hurtAudio.spatialBlend = 0f;
            }
            _hurtAudio.PlayOneShot(clip);
        }

        public void SetHealth(int amount) => SetHealth(amount, isDirectDamage: true);

        void SetHealth(int amount, bool isDirectDamage, DamageSource source = DamageSource.Other)
        {
            int health = Mathf.Clamp(amount, 0, MaxHealth);
            if (IsDead || health == CurrentHealth) return;

            if (CurrentHealth == MaxHealth && health > 0)
                _nextHealthRecovery = Time.timeAsDouble + HealthRecoveryInterval;

            bool tookDirectDamage = isDirectDamage && health < CurrentHealth;
            if (tookDirectDamage && _flashlightCondition > 0)
            {
                _flashlightCondition = Mathf.Max(0,
                    _flashlightCondition - (CurrentHealth - health) * FlashlightDamagePerHealthPoint);
                if (_flashlightCondition == 0)
                    FlashlightBreakCause = source;
            }

            CurrentHealth = health;
            if (IsDead && Bleeding > 0)
                SetBleeding(0);
            HealthChanged?.Invoke(health);
            if (tookDirectDamage)
                DirectDamageTaken?.Invoke();
        }

        internal void AddBleeding(int amount)
        {
            if (amount <= 0 || IsDead)
                return;

            if (Bleeding == 0)
                _nextBleedingTick = Time.timeAsDouble + BleedingInterval;
            SetBleeding(Bleeding + amount);
        }

        void SetBleeding(int amount)
        {
            Bleeding = amount;
            BleedingChanged?.Invoke(amount);
        }

        void Update()
        {
            if (Time.timeScale == 0f)
                return;

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            bool damagePressed = keyboard?.hKey.wasPressedThisFrame == true;
            bool singleDamagePressed = keyboard?.jKey.wasPressedThisFrame == true;
            bool bleedingPressed = keyboard?.kKey.wasPressedThisFrame == true;
#else
            bool damagePressed = Input.GetKeyDown(KeyCode.H);
            bool singleDamagePressed = Input.GetKeyDown(KeyCode.J);
            bool bleedingPressed = Input.GetKeyDown(KeyCode.K);
#endif
            if (damagePressed)
                TakeDamage(TestDamage);
            if (singleDamagePressed)
                TakeDamage(1);
            if (bleedingPressed)
                AddBleeding(TestBleeding);

            double now = Time.timeAsDouble;
            if (Bleeding > 0 && now >= _nextBleedingTick)
            {
                _nextBleedingTick += BleedingInterval;
                SetBleeding(Bleeding - 1);
                SetHealth(CurrentHealth - 1, isDirectDamage: false);
            }

            if (!IsDead && CurrentHealth < MaxHealth && now >= _nextHealthRecovery)
            {
                _nextHealthRecovery += HealthRecoveryInterval;
                SetHealth(CurrentHealth + 1);
            }
        }
    }
}
