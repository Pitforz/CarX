using UnityEngine;

namespace CarX.TunnelAudio
{
    /// <summary>
    /// A stretch of tunnel. Sets the rumble and the reverb, and plays drips, falls, rats and trash
    /// only inside this volume. Put a trigger collider on the same object.
    /// </summary>
    [AddComponentMenu("CarX/Tunnel Audio/Section")]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TunnelSection : MonoBehaviour
    {
        public TunnelPipeKind kind = TunnelPipeKind.WetNarrow;
        public bool drips = true;
        public bool falls;
        public bool rats;
        public bool trash;
        [Range(0f, 1f)] public float eventVolume = 0.7f;

        BoxCollider _box;
        AudioSource _oneShots;
        bool _inside;
        float _drip;
        float _fall;
        float _rat;
        float _trashWait;

        void Awake()
        {
            _box = GetComponent<BoxCollider>();
            _box.isTrigger = true;
            _drip = Random.Range(0.3f, 1.4f);
            _fall = Random.Range(40f, 90f);
            _rat = Random.Range(8f, 18f);
            _trashWait = Random.Range(10f, 24f);
        }

        void Start()
        {
            if (TunnelAudioDirector.Current == null) return;
            _oneShots = TunnelAudioDirector.Current.MakeSource("Section " + kind, TunnelBus.Pipe, 1f, false, 9f);
            _oneShots.transform.SetParent(transform, false);
            var voice = _oneShots.GetComponent<TunnelVoice>();
            voice.BaseVolume = eventVolume;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsEar(other)) return;
            _inside = true;
            if (TunnelAudioDirector.Current != null) TunnelAudioDirector.Current.EnterSection(kind);
        }

        void OnTriggerExit(Collider other)
        {
            if (!IsEar(other)) return;
            _inside = false;
            if (TunnelAudioDirector.Current != null) TunnelAudioDirector.Current.ExitSection(kind);
        }

        void Update()
        {
            if (!_inside || _oneShots == null) return;
            var director = TunnelAudioDirector.Current;
            var clips = director != null ? director.clips : null;
            if (clips == null) return;
            float dt = Time.deltaTime;
            if (drips)
            {
                _drip -= dt;
                if (_drip <= 0f)
                {
                    _drip = Random.Range(0.35f, 2.4f);
                    _oneShots.transform.position = RandomPoint();
                    TunnelAudioDirector.Play(_oneShots, clips.drip, Random.Range(0.35f, 0.8f), Random.Range(0.75f, 1.35f));
                }
            }
            if (falls)
            {
                _fall -= dt;
                if (_fall <= 0f)
                {
                    _fall = Random.Range(45f, 110f);
                    if (director != null && !director.LoudBlocked)
                    {
                        var clip = Random.value < 0.5f ? clips.stone : clips.metal;
                        _oneShots.transform.position = RandomPoint();
                        if (TunnelAudioDirector.Play(_oneShots, clip, 1f, Random.Range(0.9f, 1.1f)))
                            director.MarkLoud();
                    }
                }
            }
            if (rats)
            {
                _rat -= dt;
                if (_rat <= 0f)
                {
                    _rat = Random.Range(7f, 20f);
                    _oneShots.transform.position = RandomPoint();
                    TunnelAudioDirector.Play(_oneShots, clips.rats, Random.Range(0.4f, 0.75f), Random.Range(0.85f, 1.2f));
                }
            }
            if (trash)
            {
                _trashWait -= dt;
                if (_trashWait <= 0f)
                {
                    _trashWait = Random.Range(9f, 26f);
                    _oneShots.transform.position = RandomPoint();
                    TunnelAudioDirector.Play(_oneShots, clips.trash, Random.Range(0.35f, 0.7f), Random.Range(0.9f, 1.15f));
                }
            }
        }

        Vector3 RandomPoint()
        {
            Vector3 half = _box.size * 0.5f;
            Vector3 local = _box.center + new Vector3(
                Random.Range(-half.x, half.x),
                Random.Range(-half.y, half.y),
                Random.Range(-half.z, half.z));
            return transform.TransformPoint(local);
        }

        static bool IsEar(Collider other) =>
            other.GetComponentInParent<AudioListener>() != null || other.GetComponentInChildren<AudioListener>() != null;
    }
}
