using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PlayerDeathFall : MonoBehaviour
    {
        const float FallDuration = 0.64f;
        const float FloorProbeDistance = 3f;
        const float CameraClearance = 0.025f;

        [SerializeField] AudioClip _impactClip;

        AudioSource _impactAudio;
        Coroutine _fall;
        Camera _camera;
        CinemachineBrain _brain;
        bool _brainWasEnabled;
        Vector3 _startPosition;
        Quaternion _startRotation;

        internal void Play(Camera camera, Action onLanded)
        {
            if (_fall == null)
                _fall = StartCoroutine(Fall(camera, onLanded));
        }

        IEnumerator Fall(Camera camera, Action onLanded)
        {
            _camera = camera;
            Vector3 endPosition = default;
            Quaternion endRotation = Quaternion.identity;
            if (_camera != null)
            {
                _camera.transform.GetPositionAndRotation(out _startPosition, out _startRotation);
                _brain = _camera.GetComponent<CinemachineBrain>();
                if (_brain != null)
                {
                    _brainWasEnabled = _brain.enabled;
                    _brain.enabled = false;
                }

                endPosition = FindFloorPose(_camera);
                Vector3 forward = Vector3.ProjectOnPlane(_startRotation * Vector3.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.001f)
                    forward = transform.forward;
                endRotation = Quaternion.LookRotation(forward) * Quaternion.Euler(16f, 0f, -76f);
            }

            double startedAt = Time.unscaledTimeAsDouble;
            float progress;
            do
            {
                progress = Mathf.Clamp01((float)((Time.unscaledTimeAsDouble - startedAt) / FallDuration));
                if (_camera != null)
                {
                    float drop = progress * progress;
                    float roll = Mathf.SmoothStep(0f, 1f, progress);
                    _camera.transform.SetPositionAndRotation(
                        Vector3.Lerp(_startPosition, endPosition, drop),
                        Quaternion.Slerp(_startRotation, endRotation, roll));
                }
                if (progress < 1f)
                    yield return null;
            }
            while (progress < 1f);

            PlayImpact();
            _fall = null;
            onLanded?.Invoke();
        }

        Vector3 FindFloorPose(Camera camera)
        {
            Vector3 nearCorner = camera.ViewportToWorldPoint(new Vector3(1f, 1f, camera.nearClipPlane));
            float radius = Vector3.Distance(_startPosition, nearCorner) + CameraClearance;
            int layers = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);
            if (Physics.SphereCast(_startPosition, radius, Vector3.down, out var floor,
                    FloorProbeDistance, layers, QueryTriggerInteraction.Ignore))
                return _startPosition + Vector3.down * Mathf.Max(0f, floor.distance - CameraClearance);

            var controller = GetComponent<CharacterController>();
            float feetHeight = controller != null ? controller.bounds.min.y : transform.position.y;
            float drop = Mathf.Clamp(_startPosition.y - feetHeight - radius, 0f, FloorProbeDistance);
            return _startPosition + Vector3.down * drop;
        }

        void PlayImpact()
        {
            if (_impactClip == null)
                return;

            if (_impactAudio == null)
            {
                _impactAudio = gameObject.AddComponent<AudioSource>();
                _impactAudio.playOnAwake = false;
                _impactAudio.spatialBlend = 0f;
                _impactAudio.ignoreListenerPause = true;
            }
            _impactAudio.PlayOneShot(_impactClip, 0.75f);
        }

        void OnDisable()
        {
            if (_fall != null)
            {
                StopCoroutine(_fall);
                _fall = null;
            }
            if (_impactAudio != null)
                _impactAudio.Stop();
            if (_camera != null)
                _camera.transform.SetPositionAndRotation(_startPosition, _startRotation);
            if (_brain != null)
                _brain.enabled = _brainWasEnabled;
            _camera = null;
            _brain = null;
        }

        void OnDestroy()
        {
            if (_impactAudio != null)
                Destroy(_impactAudio);
        }
    }
}
