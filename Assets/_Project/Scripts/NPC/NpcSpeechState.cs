using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    public sealed class NpcSpeechState : MonoBehaviour
    {
        [Tooltip("True while this NPC is delivering a line. Ambient voice emitters can use this to avoid overlap.")]
        public bool is_talking;

        // RequireComponent does not backfill components on existing scene objects.
        internal static NpcSpeechState GetOrAdd(Component npc) =>
            npc.TryGetComponent<NpcSpeechState>(out var state) ? state : npc.gameObject.AddComponent<NpcSpeechState>();

        internal void SetTalking(bool talking) => is_talking = talking && isActiveAndEnabled;

        void OnDisable() => is_talking = false;
    }
}
