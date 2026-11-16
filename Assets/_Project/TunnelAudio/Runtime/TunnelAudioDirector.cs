using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace CarX.TunnelAudio
{
    /// <summary>
    /// One per scene. Holds the shared sliders, the pipe reverb, the rumble crossfade
    /// and the wall low-pass. Music, footsteps and the player's own sounds are not here.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("CarX/Tunnel Audio/Director")]
    public sealed class TunnelAudioDirector : MonoBehaviour
    {
        public static TunnelAudioDirector Current { get; private set; }

        [Tooltip("Сюда потом кладутся wav. Пустой слот просто не играет")]
        public TunnelAudioClips clips;

        [Header("Шины")]
        public AudioMixerGroup pipeGroup;
        public AudioMixerGroup streetGroup;

        [Header("Общие ползунки")]
        [Tooltip("Насколько глушить улицу у решётки. 0 — сухо и громко, 1 — почти замуровано")]
        [Range(0f, 1f)] public float streetMuffle = 0.72f;
        [Tooltip("Насколько резать голоса сверху. 1 — на грани слышимости")]
        [Range(0f, 1f)] public float voiceCut = 0.92f;
        [Tooltip("За сколько секунд гул и эхо переходят к другому участку")]
        [Range(3f, 6f)] public float transitionSeconds = 4.5f;
        [Tooltip("Громкость гула трубы")]
        [Range(0f, 1f)] public float rumbleVolume = 0.45f;

        [Header("Эхо по типу трубы, секунды хвоста")]
        public float reverbDry = 0.85f;
        public float reverbWet = 1.6f;
        public float reverbHall = 2.8f;
        public float reverbDead = 2.15f;

        [Header("Стена между ухом и источником")]
        [Tooltip("Частота среза, когда путь свободен, Гц")]
        public float openCutoff = 18000f;
        [Tooltip("Частота среза улицы у решётки, Гц")]
        public float streetCutoff = 1300f;
        [Tooltip("Частота среза, когда между ухом и звуком стена, Гц")]
        public float occlusionCutoff = 700f;
        [Tooltip("Пауза после громкого события, секунды")]
        public float loudPause = 4.5f;

        readonly List<TunnelVoice> _voices = new();
        AudioSource[] _rumbles;
        AudioReverbZone _reverb;
        Transform _ear;
        TunnelPipeKind _kind = TunnelPipeKind.DryWide;
        bool _inside;
        float _decay = 1.2f;
        float _loudUntil;
        readonly AudioClip[] _boundRumble = new AudioClip[4];

        public bool LoudBlocked => Time.time < _loudUntil;
        public Transform Ear => _ear;

        void Awake()
        {
            Current = this;
            _rumbles = new AudioSource[4];
            for (int i = 0; i < 4; i++)
                _rumbles[i] = MakeSource("Rumble " + (TunnelPipeKind)i, TunnelBus.Pipe, 0f, true, 0f, false);
            var zone = new GameObject("Pipe Reverb");
            zone.transform.SetParent(transform, false);
            _reverb = zone.AddComponent<AudioReverbZone>();
            _reverb.reverbPreset = AudioReverbPreset.User;
            _reverb.minDistance = 1f;
            _reverb.maxDistance = 40f;
            ApplyReverb(reverbDry, 1f);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        void LateUpdate()
        {
            FindEar();
            if (_ear == null) return;
            _reverb.transform.position = _ear.position;
            FadeRumble();
            FadeReverb();
            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                if (_voices[i] == null) { _voices.RemoveAt(i); continue; }
                Shade(_voices[i]);
            }
        }

        public void MarkLoud() => _loudUntil = Time.time + loudPause;

        public void EnterSection(TunnelPipeKind kind)
        {
            _kind = kind;
            _inside = true;
        }

        public void ExitSection(TunnelPipeKind kind)
        {
            if (_kind == kind) _inside = false;
        }

        public void Register(TunnelVoice voice)
        {
            if (voice != null && !_voices.Contains(voice)) _voices.Add(voice);
        }

        public AudioSource MakeSource(string sourceName, TunnelBus bus, float spatial, bool loop, float maxDistance, bool track = true)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = spatial;
            source.dopplerLevel = 0.15f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = spatial > 0.5f ? 0.4f : 1f;
            source.maxDistance = maxDistance > 0.1f ? maxDistance : 20f;
            source.outputAudioMixerGroup = bus == TunnelBus.Street ? streetGroup : pipeGroup;
            source.reverbZoneMix = bus == TunnelBus.Street ? 0.16f : 1f;
            var low = go.AddComponent<AudioLowPassFilter>();
            low.cutoffFrequency = openCutoff;
            var high = go.AddComponent<AudioHighPassFilter>();
            high.cutoffFrequency = 10f;
            var voice = go.AddComponent<TunnelVoice>();
            voice.Source = source;
            voice.Low = low;
            voice.High = high;
            voice.Bus = bus;
            if (track) Register(voice);
            return source;
        }

        public static bool Play(AudioSource source, AudioClip clip, float volume, float pitch)
        {
            if (source == null || clip == null) return false;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume);
            return true;
        }

        void FindEar()
        {
            if (_ear != null) return;
            var listener = FindFirstObjectByType<AudioListener>();
            if (listener != null) _ear = listener.transform;
        }

        void FadeRumble()
        {
            if (clips == null) return;
            for (int i = 0; i < 4; i++)
            {
                var clip = clips.Rumble((TunnelPipeKind)i);
                if (_boundRumble[i] == clip) continue;
                _boundRumble[i] = clip;
                _rumbles[i].clip = clip;
                if (clip != null && !_rumbles[i].isPlaying) _rumbles[i].Play();
                if (clip == null) _rumbles[i].Stop();
            }
            float step = Time.deltaTime / Mathf.Max(0.01f, transitionSeconds);
            for (int i = 0; i < 4; i++)
            {
                bool on = _inside && (int)_kind == i && _rumbles[i].clip != null;
                float target = on ? rumbleVolume : 0f;
                _rumbles[i].volume = Mathf.MoveTowards(_rumbles[i].volume, target, step * rumbleVolume);
            }
        }

        void FadeReverb()
        {
            float target = _kind switch
            {
                TunnelPipeKind.DryWide => reverbDry,
                TunnelPipeKind.WetNarrow => reverbWet,
                TunnelPipeKind.Hall => reverbHall,
                _ => reverbDead,
            };
            if (!_inside) target = reverbDry * 0.5f;
            float step = Time.deltaTime * 2f / Mathf.Max(0.01f, transitionSeconds);
            _decay = Mathf.MoveTowards(_decay, target, step);
            ApplyReverb(_decay, Mathf.InverseLerp(0.4f, 3f, _decay));
        }

        void ApplyReverb(float decay, float amount)
        {
            _reverb.decayTime = decay;
            _reverb.decayHFRatio = Mathf.Lerp(0.7f, 0.35f, amount);
            _reverb.reflectionsDelay = 0.02f;
            _reverb.reverbDelay = Mathf.Lerp(0.03f, 0.06f, amount);
            _reverb.room = (int)Mathf.Lerp(-200, -600, amount);
            _reverb.roomHF = (int)Mathf.Lerp(-100, -800, amount);
            _reverb.reflections = (int)Mathf.Lerp(-300, -800, amount);
            _reverb.reverb = (int)Mathf.Lerp(200, 800, amount);
            _reverb.density = Mathf.Lerp(70f, 100f, amount);
            _reverb.diffusion = Mathf.Lerp(70f, 100f, amount);
        }

        void Shade(TunnelVoice voice)
        {
            var source = voice.Source;
            if (source == null) return;
            float volume = voice.BaseVolume;
            float cutoff = openCutoff;
            float high = 12f;
            if (voice.Bus == TunnelBus.Street)
            {
                volume *= Mathf.Lerp(1f, 0.18f, streetMuffle);
                cutoff = Mathf.Lerp(openCutoff, streetCutoff, streetMuffle);
            }
            if (voice.Casino)
            {
                volume *= Mathf.Lerp(1f, 0.08f, voiceCut);
                cutoff = Mathf.Min(cutoff, Mathf.Lerp(4000f, 900f, voiceCut));
                high = Mathf.Lerp(80f, 420f, voiceCut);
            }
            if (Blocked(source.transform.position))
                cutoff = Mathf.Min(cutoff, occlusionCutoff);
            source.volume = volume;
            voice.Low.cutoffFrequency = cutoff;
            voice.High.cutoffFrequency = high;
        }

        bool Blocked(Vector3 world)
        {
            Vector3 from = _ear.position;
            Vector3 to = world - from;
            float distance = to.magnitude;
            if (distance < 0.2f) return false;
            if (!Physics.Raycast(from, to / distance, out var hit, distance - 0.15f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            Vector3 flat = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
            // The ceiling above a grate is the street muffling, not an extra wall.
            return flat.sqrMagnitude > 0.25f;
        }
    }

    /// <summary>One playing voice the director muffles. Created with the source, not placed by hand.</summary>
    [DisallowMultipleComponent]
    public sealed class TunnelVoice : MonoBehaviour
    {
        public AudioSource Source;
        public AudioLowPassFilter Low;
        public AudioHighPassFilter High;
        public TunnelBus Bus;
        public bool Casino;
        public float BaseVolume = 1f;
    }
}
