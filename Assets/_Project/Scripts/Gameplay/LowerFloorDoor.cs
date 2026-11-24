using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class LowerFloorDoor : MonoBehaviour
    {
        [SerializeField] Transform _leftLeaf;
        [SerializeField] Transform _rightLeaf;
        [SerializeField, Min(0.1f)] float _openDuration = 1.15f;
        [SerializeField] bool _opensWithoutKey;

        Quaternion _leftClosed;
        Quaternion _rightClosed;
        float _opening;
        bool _openingStarted;

        internal bool CanInteract => !_openingStarted && _leftLeaf != null;
        internal bool OpeningStarted => _openingStarted;
        internal bool CanUnlock(PlayerInventory inventory) => _opensWithoutKey || inventory.HasLowerFloorKey;

        void Awake()
        {
            if (_leftLeaf != null)
                _leftClosed = _leftLeaf.localRotation;
            if (_rightLeaf != null)
                _rightClosed = _rightLeaf.localRotation;
        }

        internal bool TryOpen(PlayerInventory inventory)
        {
            if (!CanInteract || !CanUnlock(inventory))
                return false;

            _openingStarted = true;
            FloorDoorCheckpoint.Remember(this, inventory);
            return true;
        }

        internal void RestoreOpened()
        {
            _openingStarted = true;
            _opening = 1f;
            if (_leftLeaf != null)
                _leftLeaf.localRotation = _leftClosed * Quaternion.Euler(0f, -90f, 0f);
            if (_rightLeaf != null)
                _rightLeaf.localRotation = _rightClosed * Quaternion.Euler(0f, 90f, 0f);
        }

        void Update()
        {
            if (!_openingStarted || _opening >= 1f)
                return;

            _opening = Mathf.MoveTowards(_opening, 1f, Time.deltaTime / _openDuration);
            float angle = Mathf.SmoothStep(0f, 90f, _opening);
            _leftLeaf.localRotation = _leftClosed * Quaternion.Euler(0f, -angle, 0f);
            if (_rightLeaf != null)
                _rightLeaf.localRotation = _rightClosed * Quaternion.Euler(0f, angle, 0f);
        }
    }
}
