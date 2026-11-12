using UnityEngine;

namespace CarX.Gameplay
{
    /// <summary>Hand flashlight: F toggles it. Slight lag behind the camera makes it feel hand-held.</summary>
    [RequireComponent(typeof(Light))]
    [AddComponentMenu("CarX/Flashlight")]
    public class Flashlight : MonoBehaviour
    {
        [Tooltip("Включён при старте")]
        public bool startOn = true;
        [Tooltip("Насколько фонарь запаздывает за взглядом (0 = жёстко)")]
        [Range(0f, 0.9f)] public float sway = 0.55f;

        Light _light;
        Quaternion _smoothed;

        void Awake()
        {
            _light = GetComponent<Light>();
            _light.enabled = startOn;
        }

        void OnEnable()
        {
            _smoothed = transform.parent != null ? transform.parent.rotation : transform.rotation;
        }

        void Update()
        {
            bool toggle = false;
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) toggle = true;
#else
            if (Input.GetKeyDown(KeyCode.F)) toggle = true;
#endif
            if (toggle && _light != null) _light.enabled = !_light.enabled;
        }

        void LateUpdate()
        {
            if (transform.parent == null || sway <= 0f) return;
            var target = transform.parent.rotation;
            float k = 1f - Mathf.Pow(sway, Time.deltaTime * 30f);
            _smoothed = Quaternion.Slerp(_smoothed, target, k);
            transform.rotation = _smoothed;
        }
    }
}
