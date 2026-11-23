using System.Collections.Generic;
using CarX.NPC;
using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class LevelMusic : MonoBehaviour
    {
        enum Floor { None, First, Second }

        [SerializeField] Transform _firstFloor;
        [SerializeField] Transform _secondFloor;
        [SerializeField] NpcDialogue _simon;
        [SerializeField] AudioClip _firstTrack;
        [SerializeField] AudioClip _secondTrack;
        [SerializeField, Range(0f, 1f)] float _firstVolume = 0.25f;
        [SerializeField, Range(0f, 1f)] float _secondVolume = 0.28f;

        readonly Dictionary<Transform, Renderer[]> _residents = new();
        PlayerHealth _player;
        PlayerPistol _pistol;
        AudioSource _firstSource;
        AudioSource _secondSource;
        Floor _floor;
        int _worldLayers;
        bool _sawResident;
        float _quietSince;
        float _nextCheck;

        void Start()
        {
            foreach (var player in FindObjectsByType<PlayerHealth>())
            {
                if (player.gameObject.scene != gameObject.scene)
                    continue;
                _player = player;
                _pistol = player.GetComponent<PlayerPistol>();
                _worldLayers = Physics.DefaultRaycastLayers;
                if (player.gameObject.layer != 0)
                    _worldLayers &= ~(1 << player.gameObject.layer);
                break;
            }

            if (_firstFloor != null)
            {
                Collect<NpcDialogue>();
                Collect<WorkshopEncounter>();
                Collect<NpcIdleMotion>();
                Collect<ResidentModelPose>();
                Collect<SleepingResidentPose>();
                Collect<SleepingMeshBreathing>();
            }
            _firstSource = CreateSource(_firstTrack);
            _secondSource = CreateSource(_secondTrack);
        }

        void Collect<T>() where T : Component
        {
            foreach (var component in _firstFloor.GetComponentsInChildren<T>(true))
                if (!_residents.ContainsKey(component.transform))
                    _residents.Add(component.transform, component.GetComponentsInChildren<Renderer>(true));
        }

        AudioSource CreateSource(AudioClip clip)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.reverbZoneMix = 0f;
            source.volume = 0f;
            return source;
        }

        void Update()
        {
            if (_player == null)
                return;

            float now = Time.time;
            if (now >= _nextCheck)
            {
                _nextCheck = now + 0.12f;
                UpdateFloor(now);
                bool visible = _floor == Floor.First && SeesResident();
                if (visible || _sawResident)
                    _quietSince = now;
                _sawResident = visible;
            }

            bool first = !_player.IsDead && _floor == Floor.First && now - _quietSince >= 12f;
            bool second = !_player.IsDead && _floor == Floor.Second && _simon != null &&
                now >= _simon.IntroductionCompletedAt + 5f && (!_simon.IsAnswering || _secondSource.isPlaying);
            Fade(_firstSource, first, _firstVolume);
            Fade(_secondSource, second, _secondVolume);
        }

        void UpdateFloor(float now)
        {
            // Read the supporting geometry so starts inside a floor and stacked tunnels work too.
            Vector3 origin = _player.transform.position + Vector3.up * 0.35f;
            if (!Physics.Raycast(origin, Vector3.down, out var support, 2.2f, _worldLayers,
                QueryTriggerInteraction.Ignore))
                return;

            Floor next = _firstFloor != null && support.transform.IsChildOf(_firstFloor) ? Floor.First
                : _secondFloor != null && support.transform.IsChildOf(_secondFloor) ? Floor.Second : Floor.None;
            if (_floor == next)
                return;

            _floor = next;
            _quietSince = now;
            _sawResident = false;
        }

        bool SeesResident()
        {
            Camera camera = _pistol != null ? _pistol.ViewCamera : null;
            if (camera == null || !camera.isActiveAndEnabled)
                camera = Camera.main;
            if (camera == null)
                return true;

            foreach (var pair in _residents)
            {
                var resident = pair.Key;
                if (resident == null || !resident.gameObject.activeInHierarchy)
                    continue;

                foreach (var renderer in pair.Value)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                        continue;
                    if (InView(camera, resident, renderer.bounds.center))
                        return true;
                }
            }
            return false;
        }

        bool InView(Camera camera, Transform resident, Vector3 point)
        {
            Vector3 viewport = camera.WorldToViewportPoint(point);
            if (viewport.z <= camera.nearClipPlane || viewport.z > camera.farClipPlane ||
                viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                return false;

            return !Physics.Linecast(camera.transform.position, point, out var hit, _worldLayers,
                QueryTriggerInteraction.Ignore) || hit.transform.IsChildOf(resident);
        }

        static void Fade(AudioSource track, bool play, float volume)
        {
            if (track == null || track.clip == null || Time.deltaTime <= 0f)
                return;
            if (play && !track.isPlaying)
                track.Play();

            float duration = play ? 3.5f : 2f;
            track.volume = Mathf.MoveTowards(track.volume, play ? volume : 0f,
                Time.deltaTime * Mathf.Max(volume, 0.01f) / duration);
            if (!play && track.volume <= 0f && track.isPlaying)
                track.Stop();
        }

        void OnDisable()
        {
            if (_firstSource != null)
                _firstSource.Stop();
            if (_secondSource != null)
                _secondSource.Stop();
        }
    }
}
