using UnityEngine;

namespace CarX.TunnelAudio
{
    public enum TunnelBus { Pipe, Street }

    public enum TunnelPipeKind { DryWide, WetNarrow, Hall, DeadEnd }

    public enum CulvertProfile { Silence, Cars, Heavy, Casino }

    public enum LivingPlacement { Point, RandomInVolume, FollowTransform }

    /// <summary>
    /// All clip slots for the tunnel bed. An empty slot is skipped, so the scene
    /// can be built before any wav files exist. Drop files onto these fields later.
    /// </summary>
    [CreateAssetMenu(menuName = "CarX/Tunnel Audio Clips", fileName = "TunnelAudioClips")]
    public sealed class TunnelAudioClips : ScriptableObject
    {
        [Header("Гул трубы, четыре характера")]
        public AudioClip rumbleDryWide;
        public AudioClip rumbleWetNarrow;
        public AudioClip rumbleHall;
        public AudioClip rumbleDeadEnd;

        [Header("События участка")]
        public AudioClip drip;
        public AudioClip stone;
        public AudioClip metal;
        public AudioClip rats;
        public AudioClip trash;

        [Header("Улица над решёткой")]
        public AudioClip distantCity;
        public AudioClip car;
        public AudioClip carHorn;
        public AudioClip dumpTruck;
        public AudioClip machineryHorn;
        [Tooltip("Игровой зал. Играет вместе с фоном казино")]
        public AudioClip casino;
        [Tooltip("Фон казино. Играет вместе с игровым залом")]
        public AudioClip casinoAmbience;

        [Header("Живые звуки, без слов")]
        public AudioClip cough;
        public AudioClip groan;
        [Tooltip("Плач, не хрип")]
        public AudioClip cry;
        public AudioClip moan;
        public AudioClip mutter;
        public AudioClip sob;
        public AudioClip breath;
        public AudioClip scream;
        public AudioClip pipeHit;

        public AudioClip Rumble(TunnelPipeKind kind) => kind switch
        {
            TunnelPipeKind.DryWide => rumbleDryWide,
            TunnelPipeKind.WetNarrow => rumbleWetNarrow,
            TunnelPipeKind.Hall => rumbleHall,
            _ => rumbleDeadEnd,
        };

        public AudioClip Any(params AudioClip[] options)
        {
            int count = 0;
            for (int i = 0; i < options.Length; i++)
                if (options[i] != null) count++;
            if (count == 0) return null;
            int pick = Random.Range(0, count);
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] == null) continue;
                if (pick == 0) return options[i];
                pick--;
            }
            return null;
        }

        public AudioClip CommonVoice() => Any(cough, groan, cry, moan, mutter, sob, breath);

        public AudioClip RareVoice() => Any(scream, pipeHit);

        public AudioClip ByVoice(LivingVoice voice) => voice switch
        {
            LivingVoice.Cough => cough,
            LivingVoice.Groan => groan,
            LivingVoice.Cry => cry,
            LivingVoice.Moan => moan,
            LivingVoice.Mutter => mutter,
            LivingVoice.Sob => sob,
            LivingVoice.Breath => breath,
            LivingVoice.Scream => scream,
            _ => pipeHit,
        };
    }
}
