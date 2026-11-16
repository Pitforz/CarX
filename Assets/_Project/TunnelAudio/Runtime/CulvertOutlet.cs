using UnityEngine;

namespace CarX.TunnelAudio
{
    /// <summary>A grate in the tunnel ceiling. The profile is chosen per grate; casino is only the grate marked Casino.</summary>
    [AddComponentMenu("CarX/Tunnel Audio/Culvert")]
    public sealed class CulvertOutlet : MonoBehaviour
    {
        public CulvertProfile profile = CulvertProfile.Silence;
        public TrafficLine traffic;
        [Tooltip("Дальше пары метров решётку почти не слышно")]
        public float hearDistance = 2.3f;
        [Range(0f, 1f)] public float bedVolume = 0.35f;

        AudioSource _bed;
        float _wait;

        void Start()
        {
            var director = TunnelAudioDirector.Current;
            if (director == null) return;
            _bed = director.MakeSource("Culvert " + profile, TunnelBus.Street, 1f, profile == CulvertProfile.Casino, hearDistance);
            _bed.transform.SetParent(transform, false);
            _bed.minDistance = 0.3f;
            var voice = _bed.GetComponent<TunnelVoice>();
            voice.BaseVolume = bedVolume;
            voice.Casino = profile == CulvertProfile.Casino;
            if (profile == CulvertProfile.Casino && director.clips != null && director.clips.casino != null)
            {
                _bed.clip = director.clips.casino;
                _bed.loop = true;
                _bed.Play();
            }
            else
                _bed.loop = false;
            ArmTraffic();
            _wait = Random.Range(4f, 12f);
        }

        void ArmTraffic()
        {
            if (traffic == null || TunnelAudioDirector.Current == null || TunnelAudioDirector.Current.clips == null) return;
            var clips = TunnelAudioDirector.Current.clips;
            switch (profile)
            {
                case CulvertProfile.Cars:
                    traffic.Arm(clips.car, clips.carHorn, 0.16f, new Vector2(5f, 14f), 0.7f);
                    break;
                case CulvertProfile.Heavy:
                    traffic.Arm(clips.dumpTruck, clips.machineryHorn, 0.35f, new Vector2(16f, 38f), 0.85f);
                    break;
            }
        }

        void Update()
        {
            if (profile != CulvertProfile.Silence || _bed == null) return;
            var clips = TunnelAudioDirector.Current != null ? TunnelAudioDirector.Current.clips : null;
            if (clips == null || clips.distantCity == null) return;
            _wait -= Time.deltaTime;
            if (_wait > 0f) return;
            _wait = Random.Range(14f, 36f);
            TunnelAudioDirector.Play(_bed, clips.distantCity, 0.45f, Random.Range(0.92f, 1.05f));
        }
    }
}
