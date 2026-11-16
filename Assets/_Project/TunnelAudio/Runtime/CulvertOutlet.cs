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
            if (profile == CulvertProfile.Casino && director.clips != null)
                StartCasino(director);
            else
                _bed.loop = false;
            ArmTraffic();
            _wait = Random.Range(4f, 12f);
        }

        void ArmTraffic()
        {
            if (traffic == null || TunnelAudioDirector.Current == null || TunnelAudioDirector.Current.clips == null) return;
            var clips = TunnelAudioDirector.Current.clips;
            traffic.followDirector = true;
            switch (profile)
            {
                case CulvertProfile.Cars:
                    // Horn is rare, quiet and only layered on top of a pass, never instead of it.
                    traffic.heavy = false;
                    traffic.Arm(clips.car, clips.carHorn, 0.07f, new Vector2(8f, 20f), 0.7f, 0.18f);
                    break;
                case CulvertProfile.Heavy:
                    traffic.heavy = true;
                    traffic.Arm(clips.dumpTruck, clips.machineryHorn, 0.2f, new Vector2(16f, 38f), 0.85f, 0.28f);
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
            var director = TunnelAudioDirector.Current;
            _wait = director != null ? director.Wait(director.Active.cityGap, director.Active.cityRate) : Random.Range(14f, 36f);
            TunnelAudioDirector.Play(_bed, clips.distantCity, 0.45f, Random.Range(0.92f, 1.05f));
        }

        void StartCasino(TunnelAudioDirector director)
        {
            var clips = director.clips;
            if (clips.casino != null)
            {
                _bed.clip = clips.casino;
                _bed.loop = true;
                _bed.Play();
            }
            if (clips.casinoAmbience == null) return;
            var room = director.MakeSource("Culvert Casino Room", TunnelBus.Street, 1f, true, hearDistance);
            room.transform.SetParent(transform, false);
            room.minDistance = 0.3f;
            var voice = room.GetComponent<TunnelVoice>();
            voice.BaseVolume = bedVolume * 0.85f;
            voice.Casino = true;
            room.clip = clips.casinoAmbience;
            room.Play();
        }
    }
}
