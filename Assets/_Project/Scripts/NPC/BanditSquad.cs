using CarX.Gameplay;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [AddComponentMenu("CarX/Bandit Squad")]
    public sealed class BanditSquad : MonoBehaviour
    {
        internal const int LowHealthThreshold = 30;
        const float ContactMemory = 8f;
        const float ContactBreakForPeek = 0.75f;
        const float AssaultWarningTime = 0.7f;
        float _assaultStartsAt = float.PositiveInfinity;
        int _openingWarningsLeft;

        internal float LastSeenAt { get; private set; } = float.NegativeInfinity;
        internal Vector3 LastSeenPosition { get; private set; }
        internal Vector3 LastAimPoint { get; private set; }
        internal int ContactVersion { get; private set; }
        internal float LastHeardShotAt { get; private set; } = float.NegativeInfinity;
        internal Vector3 LastHeardShotPosition { get; private set; }
        internal static bool CanRush(PlayerHealth player) => !player.IsDead && player.CurrentHealth <= LowHealthThreshold;

        internal bool HasContact(float now) => isActiveAndEnabled && now - LastSeenAt < ContactMemory;

        internal void ReportShot(Vector3 position, float now)
        {
            if (!isActiveAndEnabled)
                return;

            LastHeardShotPosition = position;
            LastHeardShotAt = now;
        }

        internal void ReportSight(BanditPistol observer, PlayerHealth player, Vector3 position, Vector3 aim,
            float now, bool canOrderAssault)
        {
            if (!isActiveAndEnabled)
                return;

            bool newContact = !HasContact(now);
            if (now - LastSeenAt >= ContactBreakForPeek)
                ContactVersion++;
            if (newContact)
                _assaultStartsAt = float.PositiveInfinity;

            LastSeenAt = now;
            LastSeenPosition = position;
            LastAimPoint = aim;
            if (canOrderAssault && CanRush(player) && float.IsPositiveInfinity(_assaultStartsAt))
            {
                _assaultStartsAt = now + AssaultWarningTime;
                observer.Shout(newContact ? "Бандит: Вон он! Вместе, вперёд!" : "Бандит: Вместе! Вперёд!",
                    newContact ? "Bandit/SpottedRush" : "Bandit/Rush");
            }
            else if (newContact)
                observer.Shout("Бандит: Вон он! Всем сюда!", "Bandit/Spotted");
        }

        internal bool HasAssaultOrder(PlayerHealth player, float now, out bool advance)
        {
            if (!CanRush(player) || !HasContact(now))
                _assaultStartsAt = float.PositiveInfinity;

            advance = now >= _assaultStartsAt;
            return !float.IsPositiveInfinity(_assaultStartsAt);
        }

        internal void PrepareOpeningWarnings() => _openingWarningsLeft = Random.Range(1, 3);

        internal bool TryUseOpeningWarning()
        {
            if (_openingWarningsLeft <= 0)
                return false;

            _openingWarningsLeft--;
            return true;
        }
    }
}
