using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    [AddComponentMenu("CarX/Can Tripwire")]
    public sealed class CanTripwire : MonoBehaviour
    {
        const float CrossingMargin = 0.035f;

        [SerializeField] AudioClip _rattleClip;

        BoxCollider _trigger;
        CharacterController _player;
        float _lastSide;
        AudioSource _audio;

        void Awake()
        {
            _trigger = GetComponent<BoxCollider>();
            if (_rattleClip == null)
                return;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = _rattleClip;
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.minDistance = 1f;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!isActiveAndEnabled || other is not CharacterController player ||
                !player.CompareTag("Player"))
                return;

            _player = player;
            _lastSide = Mathf.Sign(LocalFeet(player).z - _trigger.center.z);
        }

        void OnTriggerStay(Collider other)
        {
            if (isActiveAndEnabled && other == _player)
                CheckCrossing();
        }

        void OnTriggerExit(Collider other)
        {
            if (!isActiveAndEnabled || other != _player)
                return;

            CheckCrossing();
            _player = null;
        }

        void OnDisable() => _player = null;

        Vector3 LocalFeet(CharacterController player)
        {
            Vector3 feet = player.center - Vector3.up * (player.height * 0.5f);
            return transform.InverseTransformPoint(player.transform.TransformPoint(feet));
        }

        void CheckCrossing()
        {
            Vector3 feet = LocalFeet(_player) - _trigger.center;
            if (Mathf.Abs(feet.z) <= CrossingMargin || _lastSide * feet.z > 0f)
                return;

            _lastSide = Mathf.Sign(feet.z);
            Vector3 halfSize = _trigger.size * 0.5f;
            if (Mathf.Abs(feet.x) > halfSize.x || feet.y > halfSize.y ||
                !_player.TryGetComponent<PlayerHealth>(out var health) || health.IsDead)
                return;

            if (_player.TryGetComponent<PlayerStatusHud>(out var hud))
                hud.ShowSubtitle("звук");
            if (_audio != null)
                _audio.Play();
        }
    }
}
