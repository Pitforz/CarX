using UnityEngine;
using UnityEngine.InputSystem;

namespace CarX.Flow
{
    /// <summary>
    /// Камера стоит там, куда её поставили в сцене. Курсор у края экрана лишь немного доворачивает её.
    /// Точка кадра — это Transform самой Main Camera. Этот компонент его не перезаписывает в редакторе.
    /// </summary>
    [AddComponentMenu("CarX/Камера меню (лёгкий поворот)")]
    public class MenuCameraLook : MonoBehaviour
    {
        [Tooltip("Градусы влево-вправо, когда курсор у левого или правого края экрана.")]
        public float maxYaw = 3.5f;

        [Tooltip("Градусы вверх-вниз, когда курсор у верхнего или нижнего края.")]
        public float maxPitch = 2f;

        [Tooltip("Насколько быстро камера догоняет курсор.")]
        public float smooth = 6f;

        Quaternion _rest;
        Vector2 _offset;

        void Start()
        {
            _rest = transform.rotation;
        }

        void LateUpdate()
        {
            if (!Application.isPlaying) return;

            Vector2 target = Vector2.zero;
            if (Mouse.current != null && !SceneFlow.Busy)
            {
                Vector2 pointer = Mouse.current.position.ReadValue();
                float x = Mathf.Clamp((pointer.x / Mathf.Max(1f, Screen.width)) * 2f - 1f, -1f, 1f);
                float y = Mathf.Clamp((pointer.y / Mathf.Max(1f, Screen.height)) * 2f - 1f, -1f, 1f);
                target = new Vector2(x * maxYaw, -y * maxPitch);
            }

            float t = 1f - Mathf.Exp(-smooth * Time.deltaTime);
            _offset = Vector2.Lerp(_offset, target, t);
            transform.rotation = _rest * Quaternion.Euler(_offset.y, _offset.x, 0f);
        }
    }
}
