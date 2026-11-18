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
        [Tooltip("Насколько глубоко в тоннеле глушить улицу. 0 — везде ясно, 1 — вдали как из-под земли. У самой решётки глушение не включается")]
        [Range(0f, 1f)] public float streetMuffle = 0.72f;
        [Tooltip("Насколько резать казино в глубине. У решётки голоса почти не режутся")]
        [Range(0f, 1f)] public float voiceCut = 0.92f;
        [Tooltip("За сколько секунд гул и эхо переходят к другому участку")]
        [Range(3f, 6f)] public float transitionSeconds = 4.5f;
        [Tooltip("Громкость гула трубы. Слышна сразу. Смена участка по-прежнему перетекает плавно")]
        [Range(0f, 1f)] public float rumbleVolume = 0.45f;

        [Header("Эхо трубы")]
        [Tooltip("Хвост сухого участка, секунды. Мокрый длиннее примерно в 1,9, зал в 3,3, тупик в 2,5. Снаружи берётся половина сухого")]
        public float reverbSeconds = 0.85f;

        [Header("Стена между ухом и источником")]
        [Tooltip("Частота среза, когда путь свободен, Гц")]
        public float openCutoff = 18000f;
        [Tooltip("Ближе этого, м, звук решётки ясный и громкий")]
        public float grateClear = 3.5f;
        [Tooltip("Дальше этого, м, уличный звук уже как из-под земли и пропадает")]
        public float soilFar = 26f;
        [Tooltip("Срез сверху глубоко в тоннеле, Гц. Бетон тут ни при чём: только дистанция до своей решётки")]
        public float soilCutoff = 380f;
        [Tooltip("Частота среза, когда между ухом и звуком стена, Гц")]
        public float occlusionCutoff = 700f;
        [Tooltip("Пауза после громкого события, секунды")]
        public float loudPause = 4.5f;

        [Header("Частота событий")]
        [Tooltip("Общие интервалы. Зона на карте подменяет их, пока ухо внутри")]
        public TunnelAudioRates rates = new TunnelAudioRates();

        [Header("Гул из улицы, если слоты гула пустые")]
        [Tooltip("Доля зацикленного проезда в гуле. 0 — только город")]
        [Range(0f, 1f)] public float carBedMix = 0.3f;
        [Range(0.4f, 1f)] public float rumblePitch = 0.82f;
        [Range(0.4f, 1f)] public float carBedPitch = 0.55f;
        [Tooltip("Срез сверху у проезда, Гц")]
        public float carBedLowHz = 130f;
        [Tooltip("Срез снизу у проезда, Гц")]
        public float carBedHighHz = 35f;
        [Tooltip("Срез сверху гула из улицы, Гц. Это сухой участок. Мокрый и тупик сами темнее, зал между ними")]
        public float rumbleLowHz = 520f;
        [Tooltip("Срез снизу гула, Гц. Держи ниже среза сверху")]
        public float rumbleHighHz = 40f;
        [Tooltip("Задержка эха гула, мс. Зал и тупик сами длиннее. Хвост трубы звучит поверх")]
        public float echoDelayMs = 110f;
        [Tooltip("Сколько эхо гула повторяется. Тип участка чуть сдвигает")]
        [Range(0f, 1f)] public float echoDecay = 0.42f;
        [Tooltip("Доля эха гула. 0 — без этого эха, хвост трубы остаётся")]
        [Range(0f, 1f)] public float echoWet = 0.34f;

        [HideInInspector] public int mixVersion;

        readonly List<TunnelVoice> _voices = new();
        AudioSource[] _rumbles;
        AudioSource _carBed;
        AudioReverbZone _reverb;
        Transform _ear;
        TunnelAudioZone _zone;
        TunnelPipeKind _kind = TunnelPipeKind.DryWide;
        bool _inside;
        float _decay = 1.2f;
        float _loudUntil;
        readonly AudioClip[] _boundRumble = new AudioClip[4];
        readonly bool[] _synth = new bool[4];
        readonly float[] _level = new float[4];
        float _carLevel;
        bool _heard;

        public bool LoudBlocked => Time.time < _loudUntil;
        public Transform Ear => _ear;
        public TunnelAudioZone ActiveZone => _zone;
        public TunnelAudioRates Active
        {
            get
            {
                Repair();
                var zone = _zone;
                if (zone != null && zone.replaceRates && zone.rates != null)
                {
                    zone.rates.Repair();
                    return zone.rates;
                }
                return rates;
            }
        }

        void Awake()
        {
            Current = this;
            Repair();
            _rumbles = new AudioSource[4];
            for (int i = 0; i < 4; i++)
            {
                _rumbles[i] = MakeSource("Rumble " + (TunnelPipeKind)i, TunnelBus.Pipe, 0f, true, 0f, false);
                var echo = _rumbles[i].gameObject.AddComponent<AudioEchoFilter>();
                echo.dryMix = 1f;
                echo.wetMix = 0f;
                echo.delay = 80f;
                echo.decayRatio = 0.3f;
            }
            _carBed = MakeSource("Rumble Traffic", TunnelBus.Pipe, 0f, true, 0f, false);
            var carEcho = _carBed.gameObject.AddComponent<AudioEchoFilter>();
            carEcho.dryMix = 1f;
            carEcho.wetMix = 0.28f;
            carEcho.delay = 110f;
            carEcho.decayRatio = 0.45f;
            var zone = new GameObject("Pipe Reverb");
            zone.transform.SetParent(transform, false);
            _reverb = zone.AddComponent<AudioReverbZone>();
            _reverb.reverbPreset = AudioReverbPreset.User;
            _reverb.minDistance = 1f;
            _reverb.maxDistance = 50f;
            ApplyReverb(ReverbOf(TunnelPipeKind.DryWide), 1f);
            FindEar();
        }

        void OnValidate() => Repair();

        bool _repairing;

        public void Repair()
        {
            if (_repairing) return;
            _repairing = true;
            if (rates == null) rates = new TunnelAudioRates();
            rates.Repair();
            if (mixVersion < 2)
            {
                reverbSeconds = 0.85f;
                rumbleLowHz = 520f;
                rumbleHighHz = 40f;
                echoDelayMs = 110f;
                echoDecay = 0.42f;
                echoWet = 0.34f;
                if (rumblePitch <= 0f) rumblePitch = 0.82f;
                if (carBedPitch <= 0f) carBedPitch = 0.55f;
                if (carBedLowHz < 20f) carBedLowHz = 130f;
                if (carBedHighHz < 10f) carBedHighHz = 35f;
                if (carBedMix <= 0f) carBedMix = 0.3f;
                mixVersion = 2;
            }
            _repairing = false;
        }

        /// <summary>Seconds until the next event. Master shortens every gap in the active block.</summary>
        public float Wait(Vector2 gap)
        {
            float scale = Mathf.Max(0.05f, Active.master);
            float lo = Mathf.Max(0.05f, gap.x);
            float hi = Mathf.Max(lo, gap.y);
            return Random.Range(lo, hi) / scale;
        }

        float ReverbOf(TunnelPipeKind kind) => reverbSeconds * kind switch
        {
            TunnelPipeKind.WetNarrow => 1.9f,
            TunnelPipeKind.Hall => 3.3f,
            TunnelPipeKind.DeadEnd => 2.5f,
            _ => 1f,
        };

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        void LateUpdate()
        {
            FindEar();
            if (_ear == null) return;
            _zone = TunnelAudioZone.Pick(_ear.position);
            _reverb.transform.position = _ear.position;
            FollowEar();
            FadeRumble();
            FadeReverb();
            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                if (_voices[i] == null) { _voices.RemoveAt(i); continue; }
                Shade(_voices[i]);
            }
        }

        public void MarkLoud() => _loudUntil = Time.time + loudPause;

        readonly List<TunnelPipeKind> _sections = new();

        public void EnterSection(TunnelPipeKind kind)
        {
            _sections.Add(kind);
            _kind = kind;
            _inside = true;
        }

        public void ExitSection(TunnelPipeKind kind)
        {
            int at = _sections.LastIndexOf(kind);
            if (at >= 0) _sections.RemoveAt(at);
            if (_sections.Count == 0) _inside = false;
            else _kind = _sections[_sections.Count - 1];
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

        void FollowEar()
        {
            for (int i = 0; i < 4; i++)
                if (_rumbles[i] != null) _rumbles[i].transform.position = _ear.position;
            if (_carBed != null) _carBed.transform.position = _ear.position;
        }

        void FadeRumble()
        {
            if (clips == null || _rumbles == null) return;
            AudioClip bed = clips.distantCity != null ? clips.distantCity : clips.car;
            float step = Time.deltaTime / Mathf.Max(0.01f, transitionSeconds);
            if (_inside && !_heard)
            {
                _level[(int)_kind] = 1f;
                _heard = true;
            }
            for (int i = 0; i < 4; i++)
            {
                var dedicated = clips.Rumble((TunnelPipeKind)i);
                var clip = dedicated != null ? dedicated : bed;
                bool synth = dedicated == null && clip != null;
                if (_boundRumble[i] != clip)
                {
                    _boundRumble[i] = clip;
                    _rumbles[i].clip = clip;
                    if (_rumbles[i].isPlaying) _rumbles[i].Stop();
                }
                _synth[i] = synth;
                _rumbles[i].pitch = synth ? rumblePitch : 1f;
                PaintRumble(i, synth);
                bool on = _inside && (int)_kind == i && clip != null;
                _level[i] = Mathf.MoveTowards(_level[i], on ? 1f : 0f, step);
                _rumbles[i].volume = _level[i] * rumbleVolume;
                Hold(_rumbles[i], clip);
            }
            FadeCarBed(step);
        }

        void FadeCarBed(float step)
        {
            if (_carBed == null) return;
            var clip = clips != null ? clips.car : null;
            if (_carBed.clip != clip)
            {
                _carBed.clip = clip;
                if (_carBed.isPlaying) _carBed.Stop();
            }
            bool synthOn = _inside && _synth[(int)_kind] && clip != null && carBedMix > 0.001f;
            _carLevel = Mathf.MoveTowards(_carLevel, synthOn ? 1f : 0f, step);
            _carBed.volume = _carLevel * rumbleVolume * carBedMix;
            _carBed.pitch = carBedPitch;
            var low = _carBed.GetComponent<AudioLowPassFilter>();
            var high = _carBed.GetComponent<AudioHighPassFilter>();
            if (low != null) low.cutoffFrequency = Mathf.Clamp(carBedLowHz, 40f, 2000f);
            if (high != null) high.cutoffFrequency = Mathf.Clamp(carBedHighHz, 10f, 400f);
            Hold(_carBed, clip);
        }

        static void Hold(AudioSource source, AudioClip clip)
        {
            if (source == null) return;
            if (source.volume <= 0.001f || clip == null)
            {
                if (source.isPlaying) source.Stop();
                return;
            }
            if (!source.isPlaying) source.Play();
        }

        void PaintRumble(int index, bool synth)
        {
            var source = _rumbles[index];
            var low = source.GetComponent<AudioLowPassFilter>();
            var high = source.GetComponent<AudioHighPassFilter>();
            var echo = source.GetComponent<AudioEchoFilter>();
            if (low != null) low.cutoffFrequency = synth ? LowOf(index) : openCutoff;
            if (high != null) high.cutoffFrequency = synth ? HighOf(index) : 12f;
            if (echo == null) return;
            echo.delay = synth ? DelayOf(index) : 80f;
            echo.decayRatio = synth ? DecayOf(index) : 0.2f;
            echo.wetMix = synth ? WetOf(index) : 0f;
            echo.dryMix = 1f;
        }

        float LowOf(int index)
        {
            float scale = index switch { 1 => 0.5f, 2 => 0.69f, 3 => 0.37f, _ => 1f };
            return Mathf.Max(40f, rumbleLowHz * scale);
        }

        float HighOf(int index)
        {
            float scale = index switch { 1 => 0.75f, 2 => 0.9f, 3 => 0.6f, _ => 1f };
            return Mathf.Clamp(rumbleHighHz * scale, 10f, 200f);
        }

        float DelayOf(int index)
        {
            float scale = index switch { 0 => 0.75f, 1 => 1.25f, 2 => 2f, _ => 1.6f };
            return Mathf.Clamp(echoDelayMs * scale, 10f, 1800f);
        }

        float DecayOf(int index)
        {
            float scale = index switch { 0 => 0.65f, 1 => 1f, 2 => 1.35f, _ => 1.55f };
            return Mathf.Clamp01(echoDecay * scale);
        }

        float WetOf(int index)
        {
            float scale = index switch { 0 => 0.6f, 1 => 1f, 2 => 1.35f, _ => 1.2f };
            return Mathf.Clamp01(echoWet * scale);
        }

        void FadeReverb()
        {
            float target = _inside ? ReverbOf(_kind) : reverbSeconds * 0.5f;
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
            float bury = 0f;
            if (voice.Bus == TunnelBus.Street)
            {
                bury = OpeningDepth(voice);
                float mud = bury * streetMuffle;
                volume *= Mathf.Lerp(1f, 0.16f, mud);
                cutoff = Mathf.Lerp(openCutoff, soilCutoff, mud);
                source.reverbZoneMix = Mathf.Lerp(0.08f, 0.85f, mud);
            }
            if (voice.Casino)
            {
                float cut = voiceCut * bury;
                volume *= Mathf.Lerp(1f, 0.4f, cut);
                cutoff = Mathf.Min(cutoff, Mathf.Lerp(openCutoff, 1600f, cut));
                high = Mathf.Lerp(50f, 260f, cut);
            }
            if (voice.Bus != TunnelBus.Street && Blocked(source.transform.position))
                cutoff = Mathf.Min(cutoff, occlusionCutoff);
            source.volume = volume;
            voice.Low.cutoffFrequency = cutoff;
            voice.High.cutoffFrequency = high;
        }

        float OpeningDepth(TunnelVoice voice)
        {
            var place = voice.Opening != null ? voice.Opening : voice.Source.transform;
            float distance = Vector3.Distance(_ear.position, place.position);
            return Mathf.InverseLerp(grateClear, Mathf.Max(grateClear + 1f, soilFar), distance);
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
        public Transform Opening;
        public float BaseVolume = 1f;
    }
}
