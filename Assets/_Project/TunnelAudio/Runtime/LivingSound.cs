using UnityEngine;

namespace CarX.TunnelAudio
{
    /// <summary>
    /// A living sound with no words. Place it on a point, inside a volume, or on a scene object
    /// that follows an NPC. It does not edit NPC prefabs: assign a transform that already exists in the scene.
    /// </summary>
    [AddComponentMenu("CarX/Tunnel Audio/Living Sound")]
    public sealed class LivingSound : MonoBehaviour
    {
        public LivingPlacement placement = LivingPlacement.Point;
        [Tooltip("Для случайной точки внутри. Если пусто, берётся BoxCollider на этом объекте")]
        public BoxCollider volume;
        [Tooltip("За кем ехать. Префаб не меняется. Если задан, точка стоит на этом объекте. Поле is_talking на нём, на родителе или на ребёнке глушит звук")]
        public Transform follow;
        [Range(0f, 1f)] public float chance = 0.65f;
        [Tooltip("Личная пауза. 6 — как общий интервал голосов, 12 — вдвое реже. Общая частота ещё укорачивает паузу")]
        public float minPause = 6f;
        [Tooltip("Личная громкость. Умножается на общую громкость голосов")]
        [Range(0f, 1f)] public float volumeScale = 0.8f;
        [Tooltip("Как часто вместо обычного звука берутся крик или удар по трубе. Используется, только если список голосов ниже пуст")]
        [Range(0f, 1f)] public float rareChance = 0.08f;
        [Tooltip("Свой интервал на каждый звук. Пустой список — старый общий случайный голос. Крик и удар молчат, пока не прошла пауза громкого события")]
        public LivingVoiceRate[] voices = new LivingVoiceRate[0];
        [Tooltip("За сколько секунд затихнуть, когда у объекта is_talking стал true")]
        public float talkFadeSeconds = 0.4f;

        AudioSource _source;
        float _wait;
        float _talk = 1f;
        float[] _voiceWait;

        void Start()
        {
            if (volume == null) volume = GetComponent<BoxCollider>();
            var director = TunnelAudioDirector.Current;
            if (director == null) return;
            _source = director.MakeSource("Living", TunnelBus.Pipe, 1f, false, 7f);
            _source.transform.SetParent(transform, false);
            ApplyVolume(director);
            _wait = Random.Range(1f, Mathf.Max(1.2f, NextWait(director)));
            if (voices != null && voices.Length > 0)
            {
                _voiceWait = new float[voices.Length];
                for (int i = 0; i < voices.Length; i++)
                    _voiceWait[i] = voices[i] != null ? Random.Range(0.5f, Mathf.Max(0.6f, voices[i].gap.x)) : 2f;
            }
        }

        void Update()
        {
            if (_source == null) return;
            var directorNow = TunnelAudioDirector.Current;
            bool talking = TunnelTalkProbe.IsTalking(follow);
            float fade = Mathf.Max(0.05f, talkFadeSeconds);
            _talk = Mathf.MoveTowards(_talk, talking ? 0f : 1f, Time.deltaTime / fade);
            if (directorNow != null) ApplyVolume(directorNow);
            if (follow != null)
                transform.position = follow.position;
            if (talking || _talk < 0.02f) return;

            if (UsesOwnRates())
            {
                TickVoices(directorNow);
                return;
            }

            _wait -= Time.deltaTime;
            if (_wait > 0f) return;
            var director = directorNow;
            var clips = director != null ? director.clips : null;
            if (clips == null) { _wait = 2f; return; }
            if (Random.value > chance) { _wait = 1.4f; return; }

            bool rare = Random.value < rareChance && director != null && !director.LoudBlocked;
            AudioClip clip = rare ? clips.RareVoice() : clips.CommonVoice();
            if (clip == null) { _wait = 2f; return; }
            PlaceSource();
            float reach = 7f;
            if (clip == clips.scream) reach = 16f;
            else if (clip == clips.pipeHit) reach = 12f;
            _source.maxDistance = reach;
            if (TunnelAudioDirector.Play(_source, clip, 1f, Random.Range(0.92f, 1.08f)) && rare && director != null)
                director.MarkLoud();
            _wait = NextWait(director);
        }

        bool UsesOwnRates()
        {
            if (voices == null) return false;
            for (int i = 0; i < voices.Length; i++)
                if (voices[i] != null && voices[i].enabled) return true;
            return false;
        }

        void TickVoices(TunnelAudioDirector director)
        {
            var clips = director != null ? director.clips : null;
            if (clips == null || _voiceWait == null) return;
            if (_voiceWait.Length != voices.Length)
            {
                var next = new float[voices.Length];
                for (int i = 0; i < next.Length; i++)
                    next[i] = i < _voiceWait.Length ? _voiceWait[i] : 1f;
                _voiceWait = next;
            }
            for (int i = 0; i < voices.Length; i++)
            {
                var slot = voices[i];
                if (slot == null || !slot.enabled) continue;
                _voiceWait[i] -= Time.deltaTime;
                if (_voiceWait[i] > 0f) continue;
                var clip = clips.ByVoice(slot.voice);
                bool rare = slot.voice == LivingVoice.Scream || slot.voice == LivingVoice.PipeHit;
                if (clip == null || (rare && director != null && director.LoudBlocked))
                {
                    _voiceWait[i] = 1.5f;
                    continue;
                }
                PlaceSource();
                _source.maxDistance = slot.voice == LivingVoice.Scream ? 16f : slot.voice == LivingVoice.PipeHit ? 12f : 7f;
                var voice = _source.GetComponent<TunnelVoice>();
                if (voice != null)
                {
                    float group = director != null ? director.Active.voiceVolume : 1f;
                    voice.BaseVolume = volumeScale * group * slot.volume * _talk;
                }
                if (TunnelAudioDirector.Play(_source, clip, 1f, Random.Range(0.92f, 1.08f)) && rare && director != null)
                    director.MarkLoud();
                float lo = Mathf.Max(0.2f, slot.gap.x);
                float hi = Mathf.Max(lo, slot.gap.y);
                float master = director != null ? Mathf.Max(0.05f, director.Active.master) : 1f;
                _voiceWait[i] = Random.Range(lo, hi) / master;
            }
        }

        void PlaceSource()
        {
            if (follow == null && placement == LivingPlacement.RandomInVolume && volume != null)
                _source.transform.position = RandomIn(volume);
            else
                _source.transform.position = transform.position;
        }

        void ApplyVolume(TunnelAudioDirector director)
        {
            var voice = _source.GetComponent<TunnelVoice>();
            if (voice == null) return;
            float group = director != null ? director.Active.voiceVolume : 1f;
            voice.BaseVolume = volumeScale * group * _talk;
        }

        float NextWait(TunnelAudioDirector director)
        {
            float personal = Mathf.Max(0.05f, minPause) / 6f;
            if (director == null) return Random.Range(minPause, minPause * 2.4f);
            return director.Wait(director.Active.voiceGap) * personal;
        }

        static Vector3 RandomIn(BoxCollider box)
        {
            Vector3 half = box.size * 0.5f;
            Vector3 local = box.center + new Vector3(
                Random.Range(-half.x, half.x),
                Random.Range(-half.y, half.y),
                Random.Range(-half.z, half.z));
            return box.transform.TransformPoint(local);
        }
    }

    public enum LivingVoice
    {
        Cough,
        Groan,
        Cry,
        Moan,
        Mutter,
        Sob,
        Breath,
        Scream,
        PipeHit
    }

    /// <summary>How often one living sound repeats on a follower.</summary>
    [System.Serializable]
    public sealed class LivingVoiceRate
    {
        public LivingVoice voice = LivingVoice.Cry;
        public bool enabled = true;
        [Tooltip("Секунды между повторами. Делится на общую частоту")]
        public Vector2 gap = new Vector2(10f, 24f);
        [Range(0f, 1f)] public float volume = 1f;
    }

    static class TunnelTalkProbe
    {
        static readonly System.Collections.Generic.Dictionary<System.Type, System.Func<Component, bool>> Getters =
            new System.Collections.Generic.Dictionary<System.Type, System.Func<Component, bool>>();

        public static bool IsTalking(Transform target)
        {
            if (target == null) return false;
            if (Any(target)) return true;
            if (target.parent != null && Any(target.parent)) return true;
            for (int i = 0; i < target.childCount; i++)
                if (Any(target.GetChild(i))) return true;
            return false;
        }

        static bool Any(Transform node)
        {
            var parts = node.GetComponents<Component>();
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part == null) continue;
                var read = Reader(part.GetType());
                if (read != null && read(part)) return true;
            }
            return false;
        }

        static System.Func<Component, bool> Reader(System.Type type)
        {
            System.Func<Component, bool> read;
            if (Getters.TryGetValue(type, out read)) return read;
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;
            System.Reflection.FieldInfo field = null;
            foreach (var name in Names)
            {
                field = type.GetField(name, flags);
                if (field != null && field.FieldType == typeof(bool)) break;
                field = null;
            }
            if (field != null)
            {
                var captured = field;
                read = component =>
                {
                    try { return (bool)captured.GetValue(component); }
                    catch { return false; }
                };
            }
            else
            {
                System.Reflection.PropertyInfo property = null;
                foreach (var name in Names)
                {
                    property = type.GetProperty(name, flags);
                    if (property != null && property.PropertyType == typeof(bool) && property.GetGetMethod(true) != null) break;
                    property = null;
                }
                if (property != null)
                {
                    var captured = property;
                    read = component =>
                    {
                        try { return (bool)captured.GetValue(component, null); }
                        catch { return false; }
                    };
                }
            }
            Getters[type] = read;
            return read;
        }

        static readonly string[] Names = { "is_talking", "IsTalking", "isTalking" };
    }
}
