using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CarX.Flow
{
    /// <summary>
    /// После паузы телефон звонит и мигает лампочкой. Чуть позже сама модель переливается светлее-темнее.
    /// Пробел или клик по телефону запускает катсцену. Обводки нет.
    /// </summary>
    [AddComponentMenu("CarX/Звонок телефона в меню")]
    public class MenuPhoneRing : MonoBehaviour
    {
        [Tooltip("Катсцена, которая запустится по пробелу или клику по телефону.")]
        public CutsceneSequence sequence;

        [Tooltip("Секунды тишины после входа, потом звонок и лампочка.")]
        public float ringAfterSeconds = 4f;

        [Tooltip("Секунды после начала звонка, потом пульсация материала.")]
        public float glowAfterRingSeconds = 2.5f;

        [Tooltip("Звук звонка. Перетащите свой клип сюда.")]
        public AudioClip ringClip;

        [Tooltip("Лампочка на телефоне: сфера и точечный свет. Поставьте её сами.")]
        public Transform led;

        [Tooltip("Как часто мигает лампочка, раз в секунду.")]
        public float ledBlinkHz = 2.2f;

        [Tooltip("Как часто переливается материал телефона.")]
        public float glowHz = 0.85f;

        [Tooltip("Насколько светлее становится модель на пике пульсации.")]
        public float glowAmount = 0.55f;

        Renderer _phone;
        Material _mat;
        Color _baseColor;
        Color _emission;
        bool _hasEmission;
        AudioSource _audio;
        Light _ledLight;
        Renderer _ledRenderer;
        Texture2D _hoverCursor;
        float _enteredAt;
        float _ringedAt;
        bool _ringing;
        bool _glowing;
        bool _answered;
        bool _overPhone;

        void Awake()
        {
            _phone = GetComponent<Renderer>();
            if (_phone == null) _phone = GetComponentInChildren<Renderer>();
            if (_phone != null)
            {
                _mat = _phone.material;
                _baseColor = _mat.HasProperty("_BaseColor") ? _mat.GetColor("_BaseColor") : Color.white;
                _hasEmission = _mat.HasProperty("_EmissionColor");
                if (_hasEmission)
                {
                    _mat.EnableKeyword("_EMISSION");
                    _emission = _mat.GetColor("_EmissionColor");
                }
            }

            if (led == null) led = transform.Find("Phone LED");
            if (led != null)
            {
                _ledLight = led.GetComponentInChildren<Light>(true);
                _ledRenderer = led.GetComponentInChildren<Renderer>(true);
                SetLed(false);
            }

            EnsureCollider();
            _hoverCursor = MakeCursor();
        }

        void OnEnable()
        {
            _enteredAt = Time.unscaledTime;
        }

        void OnDestroy()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            if (_hoverCursor != null) Destroy(_hoverCursor);
        }

        void Update()
        {
            if (_answered || SceneFlow.Busy)
            {
                if (_overPhone)
                {
                    Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                    _overPhone = false;
                }
                return;
            }

            if (!_ringing)
            {
                if (Time.unscaledTime - _enteredAt >= ringAfterSeconds) StartRinging();
                return;
            }

            float sinceRing = Time.unscaledTime - _ringedAt;
            BlinkLed(sinceRing);
            if (!_glowing && sinceRing >= glowAfterRingSeconds) _glowing = true;
            if (_glowing) PulseMaterial(sinceRing - glowAfterRingSeconds);

            bool over = IsPointerOverPhone();
            if (over != _overPhone)
            {
                _overPhone = over;
                Cursor.SetCursor(over ? _hoverCursor : null, over ? new Vector2(8f, 4f) : Vector2.zero, CursorMode.Auto);
            }

            if (SpacePressed() || PhoneClicked()) Answer();
        }

        void StartRinging()
        {
            _ringing = true;
            _ringedAt = Time.unscaledTime;

            _audio = GetComponent<AudioSource>();
            if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = ringClip;
            _audio.loop = true;
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0.65f;
            _audio.volume = 0.85f;
            _audio.minDistance = 0.4f;
            _audio.maxDistance = 8f;
            if (_audio.clip != null) _audio.Play();
        }

        void BlinkLed(float time)
        {
            bool on = Mathf.Repeat(time * ledBlinkHz, 1f) < 0.45f;
            SetLed(on);
        }

        void SetLed(bool on)
        {
            if (_ledLight != null) _ledLight.enabled = on;
            if (_ledRenderer != null) _ledRenderer.enabled = on;
        }

        void PulseMaterial(float time)
        {
            if (_mat == null) return;
            float wave = 0.5f + 0.5f * Mathf.Sin(time * glowHz * Mathf.PI * 2f);
            Color tint = Color.Lerp(_baseColor, _baseColor * (1f + glowAmount) + Color.white * 0.15f, wave);
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", tint);
            if (_hasEmission)
                _mat.SetColor("_EmissionColor", Color.Lerp(_emission, Color.white * 0.35f, wave));
        }

        void Answer()
        {
            _answered = true;
            if (_audio != null) _audio.Stop();
            SetLed(false);
            if (_mat != null)
            {
                if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", _baseColor);
                if (_hasEmission) _mat.SetColor("_EmissionColor", _emission);
            }
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            if (sequence == null)
            {
                Debug.LogError("MenuPhoneRing: не назначена катсцена.");
                return;
            }
            SceneFlow.Play(sequence);
        }

        bool SpacePressed()
        {
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        }

        bool PhoneClicked()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return false;
            if (OverUi()) return false;
            return IsPointerOverPhone();
        }

        bool IsPointerOverPhone()
        {
            if (Mouse.current == null) return false;
            var cam = Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            return Physics.Raycast(ray, out RaycastHit hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
                && hit.collider.transform.IsChildOf(transform);
        }

        static bool OverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(-1);
        }

        void EnsureCollider()
        {
            if (_phone == null || _phone.GetComponent<Collider>() != null) return;
            var filter = _phone.GetComponent<MeshFilter>();
            var box = _phone.gameObject.AddComponent<BoxCollider>();
            if (filter != null && filter.sharedMesh != null)
            {
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size;
            }
        }

        static Texture2D MakeCursor()
        {
            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    tex.SetPixel(x, y, clear);

            void Dot(int x, int y, Color c)
            {
                if ((uint)x < s && (uint)y < s) tex.SetPixel(x, y, c);
            }

            Color white = Color.white;
            Color edge = new Color(0.08f, 0.08f, 0.08f, 1f);
            int[] xs = { 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 8, 9, 10, 11, 12, 7, 8, 9, 6, 7 };
            int[] ys = { 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 12, 12, 12, 12, 11, 11, 11, 10, 10 };
            for (int i = 0; i < xs.Length; i++)
            {
                Dot(xs[i] - 1, ys[i], edge);
                Dot(xs[i] + 1, ys[i], edge);
                Dot(xs[i], ys[i] - 1, edge);
                Dot(xs[i], ys[i] + 1, edge);
            }
            for (int i = 0; i < xs.Length; i++) Dot(xs[i], ys[i], white);
            tex.Apply();
            return tex;
        }
    }
}
