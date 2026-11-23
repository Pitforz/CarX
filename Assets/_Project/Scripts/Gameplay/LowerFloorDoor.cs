using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class LowerFloorDoor : MonoBehaviour
    {
        [SerializeField] Transform _leftLeaf;
        [SerializeField] Transform _rightLeaf;
        [SerializeField, Min(0.1f)] float _openDuration = 1.15f;

        Quaternion _leftClosed;
        Quaternion _rightClosed;
        float _opening;
        bool _openingStarted;

        internal bool CanInteract => !_openingStarted && _leftLeaf != null && _rightLeaf != null;

        void Awake()
        {
            if (_leftLeaf != null && _rightLeaf != null)
            {
                _leftClosed = _leftLeaf.localRotation;
                _rightClosed = _rightLeaf.localRotation;
            }
        }

        internal bool TryOpen(PlayerInventory inventory)
        {
            if (!CanInteract || !inventory.HasLowerFloorKey)
                return false;

            _openingStarted = true;
            return true;
        }

        void Update()
        {
            if (!_openingStarted || _opening >= 1f)
                return;

            _opening = Mathf.MoveTowards(_opening, 1f, Time.deltaTime / _openDuration);
            float angle = Mathf.SmoothStep(0f, 90f, _opening);
            _leftLeaf.localRotation = _leftClosed * Quaternion.Euler(0f, -angle, 0f);
            _rightLeaf.localRotation = _rightClosed * Quaternion.Euler(0f, angle, 0f);
        }
    }
}
