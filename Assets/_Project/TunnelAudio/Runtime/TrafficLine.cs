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
        Vector2 _gap;
        float _volume;
        AudioSource _source;
        float _wait;
        int _leg;
        float _along;
        bool _armed;

        public void Arm(AudioClip pass, AudioClip blare, float blareChance, Vector2 gap, float volume)
        {
            _pass = pass;
            _blare = blare;
            _blareChance = blareChance;
            _gap = gap;
            _volume = volume;
            _armed = _pass != null || _blare != null;
            _wait = Random.Range(gap.x, Mathf.Max(gap.x, gap.y));
            if (TunnelAudioDirector.Current != null && _source == null)
            {
                _source = TunnelAudioDirector.Current.MakeSource("Traffic", TunnelBus.Street, 1f, false, 18f);
                _source.minDistance = 2f;
                var voice = _source.GetComponent<TunnelVoice>();
                voice.BaseVolume = volume;
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
            _source.transform.position = Vector3.Lerp(from, to, length < 0.05f ? 0f : _along / length);
        }

        void Begin()
        {
            _leg = 0;
            _along = 0f;
            bool blare = _blare != null && (_pass == null || Random.value < _blareChance);
            var clip = blare ? _blare : _pass;
            if (clip == null) { _wait = Random.Range(_gap.x, Mathf.Max(_gap.x, _gap.y)); return; }
            _source.clip = clip;
            _source.loop = !blare;
            _source.pitch = Random.Range(0.94f, 1.04f);
            var voice = _source.GetComponent<TunnelVoice>();
            if (voice != null) voice.BaseVolume = _volume * (blare ? 0.55f : 1f);
            _source.transform.position = points[0].position;
            _source.Play();
        }
    }
}
