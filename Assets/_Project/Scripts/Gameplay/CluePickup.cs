using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(SpriteRenderer))]
    public sealed class CluePickup : MonoBehaviour
    {
        static readonly Color RestingTint = new Color(0.85f, 0.83f, 0.79f);

        [SerializeField] ClueNote _note;
        SpriteRenderer _paper;

        internal ClueNote Note => _note;

        void Awake()
        {
            _paper = GetComponent<SpriteRenderer>();
            if (_note != null)
                _paper.sprite = _note.Paper;
            SetHighlighted(false);
        }

        internal void SetHighlighted(bool highlighted) =>
            _paper.color = highlighted ? Color.white : RestingTint;
    }
}
