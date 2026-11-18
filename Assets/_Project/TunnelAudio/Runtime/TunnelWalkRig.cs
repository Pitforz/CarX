using UnityEngine;
using UnityEngine.InputSystem;

namespace CarX.TunnelAudio
{
    /// <summary>
    /// A listener for the test scene only: walk with WASD, look with the mouse.
    /// It is not the game player and does not use the player prefab.
    /// </summary>
    [AddComponentMenu("CarX/Tunnel Audio/Walk Rig")]
    public sealed class TunnelWalkRig : MonoBehaviour
    {
        public float walkSpeed = 2.4f;
        public float lookSpeed = 0.08f;
        public Transform view;

        CharacterController _body;
        float _yaw;
        float _pitch;

        void Awake()
        {
            _body = GetComponent<CharacterController>();
            if (view == null && Camera.main != null) view = Camera.main.transform;
            _yaw = transform.eulerAngles.y;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }
            if (Cursor.lockState != CursorLockMode.Locked) return;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * lookSpeed;
                _pitch = Mathf.Clamp(_pitch - delta.y * lookSpeed, -80f, 80f);
            }
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (view != null) view.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            Vector2 move = Vector2.zero;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
            }
            Vector3 wish = transform.rotation * new Vector3(move.x, 0f, move.y);
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            wish.y = -2f;
            if (_body != null) _body.Move(wish * walkSpeed * Time.deltaTime);
            else transform.position += wish * walkSpeed * Time.deltaTime;
        }
    }

    /// <summary>Moves a scene object back and forth. Used so a living sound can follow something without editing an NPC prefab.</summary>
    [AddComponentMenu("CarX/Tunnel Audio/Pacer")]
    public sealed class TunnelPacer : MonoBehaviour
    {
        public Vector3 from;
        public Vector3 to;
        public float speed = 0.55f;
        float _time;

        void Update()
        {
            _time += Time.deltaTime * speed;
            transform.position = Vector3.Lerp(from, to, Mathf.PingPong(_time, 1f));
        }
    }
}
