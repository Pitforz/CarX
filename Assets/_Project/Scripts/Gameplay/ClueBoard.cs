using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class ClueBoard : MonoBehaviour
    {
        [SerializeField] Sprite[] _documents;
        [SerializeField] AudioSource[] _readingAmbience;

        internal bool CanRead => _documents is { Length: 3 } &&
            _documents[0] != null && _documents[1] != null && _documents[2] != null;

        internal bool TryRead(PlayerMenus menus) =>
            CanRead && menus != null && menus.ShowClues(_documents, ambience: _readingAmbience);
    }
}
