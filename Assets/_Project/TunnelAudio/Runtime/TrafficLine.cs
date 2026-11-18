using UnityEngine;

namespace CarX.TunnelAudio
{
    /// <summary>
    /// A pass above the tunnel. An invisible source runs from point to point,
    /// so the sound arrives from the front, crosses the grate and leaves. Gaps are uneven.
    /// </summary>
    [AddComponentMenu("CarX/Tunnel Audio/Traffic Line")]
    public sealed class TrafficLine : MonoBehaviour
    {
        public Transform[] points = new Transform[0];
        public float speed = 9f;
        [Tooltip("Тяжёлый проезд берёт свои интервал и шанс сигнала")]
        public bool heavy;
        [Tooltip("Интервал и шанс сигнала читаются с общих настроек и с зоны, где стоит ухо")]
        public bool followDirector = true;

        AudioClip _pass;
        AudioClip _blare;
        float _blareChance;
        float _blareVolume;
        Vector2 _gap;
        float _volume;
        AudioSource _source;
        AudioSource _horn;
        float _hornIn;
        float _wait;
        int _leg;
        float _along;
        bool _armed;
        Transform _opening;

        public void BindOpening(Transform opening, float far)
        {
            _opening = opening;
            Tune(_source, far);
            Tune(_horn, far);
        }

        void Tune(AudioSource source, float far)
        {
            if (source == null) return;
            source.minDistance = 1.2f;
            source.maxDistance = Mathf.Max(source.maxDistance, far);
            var voice = source.GetComponent<TunnelVoice>();
            if (voice != null && _opening != null) voice.Opening = _opening;
        }

        public void Arm(AudioClip pass, AudioClip blare, float blareChance, Vector2 gap, float volume, float blareVolume = 0.22f)
        {
            _pass = pass;
            _blare = blare;
            _blareChance = blareChance;
            _blareVolume = blareVolume;
            _gap = gap;
            _volume = volume;
            _armed = _pass != null || _blare != null;
            _hornIn = -1f;
            _wait = NextGap();
            if (TunnelAudioDirector.Current == null) return;
            if (_source == null)
            {
                _source = TunnelAudioDirector.Current.MakeSource("Traffic", TunnelBus.Street, 1f, false, 18f);
                _source.minDistance = 2f;
                _source.GetComponent<TunnelVoice>().BaseVolume = volume;
            }
            if (_horn == null && blare != null)
            {
                _horn = TunnelAudioDirector.Current.MakeSource("Traffic Horn", TunnelBus.Street, 1f, false, 26f);
                _horn.minDistance = 8f;
                var hornVoice = _horn.GetComponent<TunnelVoice>();
                hornVoice.BaseVolume = volume * blareVolume;
            }
        }

        void Update()
        {
            if (!_armed || points == null || points.Length < 2 || _source == null) return;
            ApplyVolumes();
            if (_wait > 0f)
            {
                _wait -= Time.deltaTime;
                if (_wait <= 0f) Begin();
                return;
            }
            var from = points[_leg].position;
            var to = points[_leg + 1].position;
            float length = Vector3.Distance(from, to);
            _along += speed * Time.deltaTime;
            if (length < 0.05f || _along >= length)
            {
                _leg++;
                _along = 0f;
                if (_leg >= points.Length - 1)
                {
                    _wait = NextGap();
                    _source.Stop();
                    return;
                }
                from = points[_leg].position;
                to = points[_leg + 1].position;
                length = Mathf.Max(0.05f, Vector3.Distance(from, to));
            }
            var place = Vector3.Lerp(from, to, length < 0.05f ? 0f : _along / length);
            _source.transform.position = place;
            if (_horn != null) _horn.transform.position = place;
            if (_hornIn > 0f)
            {
                _hornIn -= Time.deltaTime;
                if (_hornIn <= 0f && _blare != null)
                {
                    _hornIn = -1f;
                    TunnelAudioDirector.Play(_horn, _blare, 1f, Random.Range(0.92f, 1.02f));
                }
            }
        }

        void Begin()
        {
            _leg = 0;
            _along = 0f;
            _hornIn = -1f;
            if (_pass == null)
            {
                _wait = NextGap();
                return;
            }
            _source.clip = _pass;
            _source.loop = true;
            _source.pitch = Random.Range(0.94f, 1.04f);
            ApplyVolumes();
            _source.transform.position = points[0].position;
            _source.Play();
            if (_horn != null && _blare != null && Random.value < HornChance())
                _hornIn = Random.Range(0.8f, 2.4f);
        }

        float NextGap()
        {
            var director = TunnelAudioDirector.Current;
            if (!followDirector || director == null)
                return Random.Range(_gap.x, Mathf.Max(_gap.x, _gap.y));
            var rates = director.Active;
            return director.Wait(heavy ? rates.heavyGap : rates.carGap);
        }

        void ApplyVolumes()
        {
            float pass = PassVolume();
            var voice = _source != null ? _source.GetComponent<TunnelVoice>() : null;
            if (voice != null) voice.BaseVolume = pass;
            if (_horn == null) return;
            var horn = _horn.GetComponent<TunnelVoice>();
            if (horn != null) horn.BaseVolume = pass * HornScale();
        }

        float PassVolume()
        {
            var director = TunnelAudioDirector.Current;
            if (!followDirector || director == null) return _volume;
            float group = heavy ? director.Active.heavyVolume : director.Active.carVolume;
            return _volume * group;
        }

        float HornScale()
        {
            var director = TunnelAudioDirector.Current;
            if (!followDirector || director == null) return _blareVolume;
            return director.Active.hornVolume;
        }

        float HornChance()
        {
            var director = TunnelAudioDirector.Current;
            if (!followDirector || director == null) return _blareChance;
            return heavy ? director.Active.heavyHornChance : director.Active.carHornChance;
        }

        void OnDrawGizmos()
        {
            if (points == null || points.Length < 2) return;
            Gizmos.color = heavy ? new Color(1f, 0.45f, 0.15f, 0.9f) : new Color(0.3f, 0.8f, 1f, 0.9f);
            for (int i = 0; i < points.Length - 1; i++)
            {
                if (points[i] == null || points[i + 1] == null) continue;
                Gizmos.DrawLine(points[i].position, points[i + 1].position);
                Gizmos.DrawWireSphere(points[i].position, 0.15f);
            }
        }
    }
}
