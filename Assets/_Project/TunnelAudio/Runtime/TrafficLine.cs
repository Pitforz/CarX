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
            _wait = Random.Range(gap.x, Mathf.Max(gap.x, gap.y));
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
                    _wait = Random.Range(_gap.x, Mathf.Max(_gap.x, _gap.y));
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
                _wait = Random.Range(_gap.x, Mathf.Max(_gap.x, _gap.y));
                return;
            }
            _source.clip = _pass;
            _source.loop = true;
            _source.pitch = Random.Range(0.94f, 1.04f);
            var voice = _source.GetComponent<TunnelVoice>();
            if (voice != null) voice.BaseVolume = _volume;
            _source.transform.position = points[0].position;
            _source.Play();
            if (_horn != null && _blare != null && Random.value < _blareChance)
                _hornIn = Random.Range(0.8f, 2.4f);
        }
    }
}
