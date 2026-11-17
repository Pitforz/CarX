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
        [Tooltip("Крысы. Пауза и громкость общие с мусором")]
        public bool rats;
        [Tooltip("Мусор. Пауза и громкость общие с крысами")]
        public bool trash;
        [Tooltip("Местная громкость событий этого участка. Умножается на громкость группы")]
        [Range(0f, 1f)] public float eventVolume = 0.7f;

        BoxCollider _box;
        AudioSource _oneShots;
        bool _inside;
        float _drip;
        float _fall;
        float _life;
        float _burst = 1f;
        int _burstKind;

        void Awake()
        {
            _box = GetComponent<BoxCollider>();
            _box.isTrigger = true;
            var director = TunnelAudioDirector.Current;
            _drip = Gap(director, d => d.dripGap, 0.3f, 1.4f);
            _fall = Gap(director, d => d.fallGap, 40f, 90f);
            _life = Gap(director, d => d.lifeGap, 8f, 18f);
        }

        void Start()
        {
            if (TunnelAudioDirector.Current == null) return;
            _oneShots = TunnelAudioDirector.Current.MakeSource("Section " + kind, TunnelBus.Pipe, 1f, false, 9f);
            _oneShots.transform.SetParent(transform, false);
            var voice = _oneShots.GetComponent<TunnelVoice>();
            voice.BaseVolume = eventVolume;
        }

        void OnDisable()
        {
            if (!_inside) return;
            _inside = false;
            if (TunnelAudioDirector.Current != null) TunnelAudioDirector.Current.ExitSection(kind);
        }

        void Update()
        {
            var director = TunnelAudioDirector.Current;
            // The ear is often a camera that is not parented to the body, so the box is the test.
            bool inside = director != null && director.Ear != null && Contains(director.Ear.position);
            if (inside != _inside)
            {
                _inside = inside;
                if (director != null)
                {
                    if (inside) director.EnterSection(kind);
                    else director.ExitSection(kind);
                }
            }
            if (!_inside || _oneShots == null) return;
            var clips = director != null ? director.clips : null;
            if (clips == null) return;
            RefreshBurst();
            float dt = Time.deltaTime;
            if (drips)
            {
                _drip -= dt;
                if (_drip <= 0f)
                {
                    _drip = Gap(director, d => d.dripGap, 0.35f, 2.4f);
                    _oneShots.transform.position = RandomPoint();
                    _burstKind = 1;
                    _burst = Random.Range(0.35f, 0.8f);
                    RefreshBurst();
                    TunnelAudioDirector.Play(_oneShots, clips.drip, 1f, Random.Range(0.75f, 1.35f));
                }
            }
            if (falls)
            {
                _fall -= dt;
                if (_fall <= 0f)
                {
                    _fall = Gap(director, d => d.fallGap, 45f, 110f);
                    if (director != null && !director.LoudBlocked)
                    {
                        var clip = Random.value < 0.5f ? clips.stone : clips.metal;
                        _oneShots.transform.position = RandomPoint();
                        _burstKind = 2;
                        _burst = 1f;
                        RefreshBurst();
                        if (TunnelAudioDirector.Play(_oneShots, clip, 1f, Random.Range(0.9f, 1.1f)))
                            director.MarkLoud();
                    }
                }
            }
            if (rats || trash)
            {
                _life -= dt;
                if (_life <= 0f)
                {
                    _life = Gap(director, d => d.lifeGap, 8f, 22f);
                    AudioClip clip = rats && trash
                        ? (Random.value < 0.5f ? clips.rats : clips.trash)
                        : (rats ? clips.rats : clips.trash);
                    _oneShots.transform.position = RandomPoint();
                    _burstKind = 3;
                    _burst = Random.Range(0.4f, 0.75f);
                    RefreshBurst();
                    TunnelAudioDirector.Play(_oneShots, clip, 1f, Random.Range(0.85f, 1.2f));
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

        bool Contains(Vector3 world)
        {
            var box = _box != null ? _box : GetComponent<BoxCollider>();
            if (box == null) return false;
            Vector3 local = transform.InverseTransformPoint(world) - box.center;
            Vector3 half = box.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        void RefreshBurst()
        {
            if (_oneShots == null || _burstKind == 0) return;
            var director = TunnelAudioDirector.Current;
            float group = 1f;
            if (director != null)
            {
                var rates = director.Active;
                group = _burstKind == 1 ? rates.dripVolume : _burstKind == 2 ? rates.fallVolume : rates.lifeVolume;
            }
            var voice = _oneShots.GetComponent<TunnelVoice>();
            if (voice == null) return;
            voice.BaseVolume = eventVolume * _burst * group;
            _oneShots.volume = voice.BaseVolume;
        }

        static float Gap(TunnelAudioDirector director, System.Func<TunnelAudioRates, Vector2> gap, float lo, float hi)
        {
            if (director == null) return Random.Range(lo, hi);
            return director.Wait(gap(director.Active));
        }

        void OnDrawGizmos()
        {
            var box = _box != null ? _box : GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = kind switch
            {
                TunnelPipeKind.WetNarrow => new Color(0.3f, 0.65f, 1f, 0.85f),
                TunnelPipeKind.Hall => new Color(0.55f, 0.85f, 0.4f, 0.85f),
                TunnelPipeKind.DeadEnd => new Color(0.85f, 0.4f, 0.7f, 0.85f),
                _ => new Color(0.95f, 0.85f, 0.35f, 0.85f),
            };
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
