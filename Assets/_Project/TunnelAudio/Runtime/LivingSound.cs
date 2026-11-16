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
        [Tooltip("Куда встать, если режим — следовать. Чужой префаб не меняется")]
        public Transform follow;
        [Range(0f, 1f)] public float chance = 0.65f;
        [Tooltip("Минимальная пауза между звуками, секунды")]
        public float minPause = 6f;
        [Range(0f, 1f)] public float volumeScale = 0.8f;
        [Tooltip("Как часто вместо обычного звука берутся крик или удар по трубе")]
        [Range(0f, 1f)] public float rareChance = 0.08f;

        AudioSource _source;
        float _wait;

        void Start()
        {
            if (volume == null) volume = GetComponent<BoxCollider>();
            if (TunnelAudioDirector.Current == null) return;
            _source = TunnelAudioDirector.Current.MakeSource("Living", TunnelBus.Pipe, 1f, false, 7f);
            _source.transform.SetParent(transform, false);
            _source.GetComponent<TunnelVoice>().BaseVolume = volumeScale;
            _wait = Random.Range(1f, minPause);
        }

        void Update()
        {
            if (_source == null) return;
            if (placement == LivingPlacement.FollowTransform && follow != null)
                transform.position = follow.position;
            _wait -= Time.deltaTime;
            if (_wait > 0f) return;
            var director = TunnelAudioDirector.Current;
            var clips = director != null ? director.clips : null;
            if (clips == null) { _wait = 2f; return; }
            if (Random.value > chance) { _wait = 1.4f; return; }

            bool rare = Random.value < rareChance && director != null && !director.LoudBlocked;
            AudioClip clip = rare ? clips.RareVoice() : clips.CommonVoice();
            if (clip == null) { _wait = 2f; return; }
            if (placement == LivingPlacement.RandomInVolume && volume != null)
                _source.transform.position = RandomIn(volume);
            else
                _source.transform.position = transform.position;

            float reach = 7f;
            if (clip == clips.scream) reach = 16f;
            else if (clip == clips.pipeHit) reach = 12f;
            _source.maxDistance = reach;
            if (TunnelAudioDirector.Play(_source, clip, 1f, Random.Range(0.92f, 1.08f)) && rare && director != null)
                director.MarkLoud();
            _wait = Random.Range(minPause, minPause * 2.4f);
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
}
