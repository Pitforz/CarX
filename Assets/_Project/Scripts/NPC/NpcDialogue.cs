using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [AddComponentMenu("CarX/NPC Dialogue")]
    [RequireComponent(typeof(NpcSpeechState))]
    public sealed class NpcDialogue : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float _interactionDistance = 2.5f;
        [SerializeField] string _displayName;
        [SerializeField] string _firstQuestion = "Вопрос один";
        [SerializeField] string _secondQuestion = "Вопрос номер два";
        [SerializeField] string _closeOption = "Закрыть";

        internal string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        internal string FirstQuestion => _firstQuestion;
        internal string SecondQuestion => _secondQuestion;
        internal string CloseOption => _closeOption;
        internal bool CanTalk => isActiveAndEnabled && GetComponentInParent<HostileNpc>() == null;
        internal float InteractionDistance => _interactionDistance;

        void Awake() => NpcSpeechState.GetOrAdd(this);
    }
}
