using UnityEngine;

namespace CarX.Gameplay
{
    [CreateAssetMenu(menuName = "CarX/Clue Note")]
    public sealed class ClueNote : ScriptableObject
    {
        [SerializeField] string _title;
        [SerializeField] Sprite _paper;
        [SerializeField] AudioClip _readVoice;

        internal string Title => _title;
        internal Sprite Paper => _paper;
        internal AudioClip ReadVoice => _readVoice;
    }
}
