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
        public const int MaxHealth = 100;
        const int TestDamage = 10;

        public int CurrentHealth { get; private set; } = MaxHealth;
        public bool IsDead => CurrentHealth == 0;
        public event Action<int> HealthChanged;

        public void TakeDamage(int amount)
        {
            SetHealth(CurrentHealth - Mathf.Clamp(amount, 0, MaxHealth));
        }

        public void SetHealth(int amount)
        {
            int health = Mathf.Clamp(amount, 0, MaxHealth);
            if (IsDead || health == CurrentHealth) return;

            CurrentHealth = health;
            HealthChanged?.Invoke(health);
        }

        void Update()
        {
            if (Time.timeScale == 0f)
                return;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current?.hKey.wasPressedThisFrame == true)
#else
            if (Input.GetKeyDown(KeyCode.H))
#endif
                TakeDamage(TestDamage);
        }
    }
}
