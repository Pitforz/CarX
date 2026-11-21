using CarX.Gameplay;
using StarterAssets;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HostileNpc))]
    public sealed class BanditPistol : MonoBehaviour
    {
        internal const int MaxHealth = 40;
        internal const float LoudStepSpeed = 3.2f;
        const int MagazineSize = 12;
        const int Damage = 17;
        const int Bleeding = 3;
        const float AimTime = 0.8f;
        const float ShotInterval = 1f;
        const float PositionalShotInterval = 2.2f;
        const float PositionalCoverAdvance = 5.5f;
        const float SearchTime = 7f;
        const float ShotMemoryTime = 5f;
        const float CloseFootstepRange = 8f;
        const float FootstepReactionWindow = 1.4f;
        const float FootstepReactionCooldown = 3f;
        const float SquadPeekWindow = 3f;
        const float RepositionInterval = 1f;
        const float CoverSearchRadius = 8.5f;
        const float CoverRetryInterval = 2f;
        const float CoverMoveTimeout = 5f;
        const float HitReactionSpeedScale = 1.3f;
        const float HitCounterAimTime = 0.2f;
        const float HitCounterExtraClearance = 1f;
        const float HitRecoveryTime = 1.3f;
        const float PeekTimeout = 3f;
        const float PeekAimTime = 0.38f;
        const float PeekFollowupInterval = 0.8f;
        const float PeekTargetGrace = 0.58f;
        const float PeekAfterShotTime = 0.32f;
        const float PeekStepSpeedScale = 0.68f;
        const float LeanOutTime = 0.38f;
        const float LeanInTime = 0.3f;
        const float CoverWaitMemory = 12f;
        const float CoverWaitDistance = 7f;
        const float OpenFireWindow = 3f;
        const float PreferredCoverDistance = 10f;
        const float AssaultStoppingDistance = 5f;
        const float EyeHeight = 1.55f;
        const float LeanAngle = 10f;
        const float LeanPivotHeight = 0.98f;
        const float ReloadRetreatTimeout = 3.5f;
        const float VisibleHitChance = 0.60f;
        const float ShotRange = 36f;
        const float SightRange = 39f;
        const float AccuracyFalloffStart = 18f;
        const float LongRangeAccuracyMultiplier = 0.6f;
        const float FallbackReloadTime = 2.28f;
        static readonly Vector3 ChestPivot = new(0f, 1.52f, 0f);
        static readonly Vector3 GripReach = new(0f, 0f, 0.60f);
        static readonly Vector3 MuzzlePosition = new(0f, 0.015f, 0.145f);
        static readonly Vector3 LeftGripPosition = new(-0.04f, -0.045f, 0.02f);
        static readonly Vector3 RightGripPosition = new(0.035f, -0.06f, -0.02f);

        [SerializeField] Transform _weapon;
        [SerializeField] Transform _leanPivot;
        [SerializeField] AudioClip _shotClip;
        [SerializeField] AudioClip _reloadClip;

        enum State
        {
            Idle, Watching, Supporting, Shooting, Repositioning, TakingCover, InCover, Peeking, SteppingOut,
            Assaulting, RetreatingToReload, Reloading, RetreatingAfterHit, RecoveringInCover, Counterattacking,
            Returning
        }

        enum PeekPhase { MovingToEdge, Opening, Aiming, HoldingAfterShot, Closing }
        enum CoverAction { Peek, StepOut, Wait }

        readonly Vector3[] _targetPoints = new Vector3[5];
        int _targetPointCount;
        HostileNpc _npc;
        CombatHumanoidAnimation _stagedModel;
        BanditSquad _squad;
        PlayerHealth _player;
        PlayerStatusHud _hud;
        CharacterController _playerCollider;
        FirstPersonController _playerMovement;
        Transform _leftShoulder;
        Transform _rightShoulder;
        Quaternion _leftRest;
        Quaternion _rightRest;
        Quaternion _leanRest = Quaternion.identity;
        Vector3 _leanRestPosition;
        GameObject _effects;
        AudioSource _shotAudio;
        AudioSource _reloadAudio;
        AudioSource _shoutAudio;
        Light _flash;
        Vector3 _initialFacing;
        Vector3 _lastAimPoint;
        Vector3 _lastSeenPosition;
        BanditCover _cover;
        Vector3 _hidePosition;
        Vector3 _peekPosition;
        Vector3 _openFirePosition;
        Vector3 _reloadPosition;
        bool _peekWithLean;
        bool _peekHasFired;
        bool _holdCoverInEncounter;
        bool _openingShotPending;
        Vector3? _heardPosition;
        Vector3 _lastHeardShotPosition;
        int _rounds = MagazineSize;
        int _peekShotsLeft;
        int _repeatedPeeksLeft;
        int _squadContactVersion;
        float _lastSeenAt = float.NegativeInfinity;
        float _lastHeardAt = float.NegativeInfinity;
        float _lastHeardShotAt = float.NegativeInfinity;
        float _loudApproachUntil = float.NegativeInfinity;
        float _nextFootstepReactionAt;
        float _squadPeekUntil = float.NegativeInfinity;
        float _nextCoverTryAt;
        float _coverActionAt;
        float _peekAimStartedAt;
        float _movingAimStartedAt = -1f;
        float _nextSupportMoveAt;
        float _nextRepositionAt;
        float _fireAt;
        float _nextRoundAt;
        float _visibleHitChance = VisibleHitChance;
        float _reloadEndsAt;
        float _reloadRetreatEndsAt;
        float _flashUntil;
        float _armAngle;
        float _leanProgress;
        float _leanSide;
        PeekPhase _peekPhase;
        CoverAction _coverAction;
        State _state;

        internal bool IsEngaged => isActiveAndEnabled &&
            _state != State.Idle && _state != State.Returning;

        Transform WeaponPivot => _leanPivot != null ? _leanPivot : transform;
        Vector3 ChestPosition => WeaponPivot.TransformPoint(ChestPivot);
        internal Vector3 StagedMuzzlePosition => _stagedModel != null ? _stagedModel.Muzzle.position :
            _weapon != null ? _weapon.TransformPoint(MuzzlePosition) : transform.TransformPoint(new Vector3(0f, 1.45f, 0.5f));

        internal void AttachStagedModel(CombatHumanoidAnimation model)
        {
            _stagedModel = model;
            if (_weapon != null)
                _weapon.gameObject.SetActive(false);
        }

        internal void ConfigureGrip(Transform leftShoulder, Quaternion leftRest, Transform rightShoulder,
            Quaternion rightRest)
        {
            _leftShoulder = leftShoulder;
            _rightShoulder = rightShoulder;
            _leftRest = leftRest;
            _rightRest = rightRest;
            if (_weapon != null && _weapon.parent != WeaponPivot)
                _weapon.SetParent(WeaponPivot, false);
            if (_leanPivot != null)
            {
                _leanRest = _leanPivot.localRotation;
                _leanRestPosition = _leanPivot.localPosition;
            }
            SetWeaponAngle(0f);
        }

        internal void Initialize(HostileNpc npc, PlayerHealth player, CharacterController playerCollider)
        {
            _npc = npc;
            _squad = GetComponentInParent<BanditSquad>();
            _player = player;
            _hud = player.GetComponent<PlayerStatusHud>();
            _playerCollider = playerCollider;
            _playerMovement = player.GetComponent<FirstPersonController>();
            _initialFacing = transform.forward;

            EnsureWeaponEffects();
            _reloadAudio = AddAudioSource(24f);
        }

        void EnsureWeaponEffects()
        {
            if (_effects != null)
            {
                if (_stagedModel != null && _effects.transform.parent != _stagedModel.Muzzle)
                    _effects.transform.SetParent(_stagedModel.Muzzle, false);
                return;
            }

            _effects = new GameObject("Bandit Weapon Effects");
            if (_stagedModel != null)
                _effects.transform.SetParent(_stagedModel.Muzzle, false);
            else
            {
                _effects.transform.SetParent(_weapon != null ? _weapon : transform, false);
                _effects.transform.localPosition = _weapon != null ? MuzzlePosition : new Vector3(0f, 1.45f, 0.5f);
            }
            _shotAudio = AddAudioSource(ShotRange + 8f);
        }

        internal void SetStagedWeaponVisible(bool visible)
        {
            if (_stagedModel != null)
                _stagedModel.SetWeaponVisible(visible);
            else if (_weapon != null)
                _weapon.gameObject.SetActive(visible);
        }

        internal void PlayStagedShot(bool playAudio = true)
        {
            EnsureWeaponEffects();
            PlayShotEffects(Time.time, playAudio);
        }

        internal void AimStagedWeapon(Vector3 target, bool smooth = false,
            float raiseDuration = CombatHumanoidAnimation.DefaultAimRaiseTime)
        {
            PoseStagedWeapon(GetAimAngle(target), smooth);
            _stagedModel?.TrackAim(target, raiseDuration: raiseDuration);
        }

        internal void LowerStagedWeapon(bool smooth = false, bool relaxed = false)
        {
            PoseStagedWeapon(0f, smooth);
            _stagedModel?.TrackAim(null, relaxed);
        }

        void PoseStagedWeapon(float angle, bool smooth)
        {
            if (smooth)
                RaiseWeapon(angle);
            else
                SetWeaponAngle(angle);
        }

        internal void BeginEncounterCombat(Vector3 target, float? visibleHitChance, bool holdCover)
        {
            float now = Time.time;
            _visibleHitChance = visibleHitChance ?? VisibleHitChance;
            _holdCoverInEncounter = holdCover;
            _openingShotPending = holdCover;
            _lastAimPoint = target;
            _lastSeenPosition = target - Vector3.up * 1.4f;
            _lastSeenAt = now;
            _state = holdCover ? State.Watching : State.Shooting;
            _fireAt = holdCover ? now + AimTime : now;
            _nextRoundAt = now;
            _nextCoverTryAt = holdCover ? now : now + 2f;
            SetStagedWeaponVisible(true);
            AimStagedWeapon(target);
        }

        internal void Tick(Vector3 playerCenter)
        {
            float now = Time.time;

            UpdateLean();
            UpdateTargetPoints(playerCenter);
            bool visible = TryGetVisibleTarget(out Vector3 aimPoint);
            if (visible)
            {
                _lastAimPoint = aimPoint;
                _lastSeenPosition = _player.transform.position;
                _lastSeenAt = now;
                _heardPosition = null;
                if (_squad != null)
                    _squad.ReportSight(this, _player, _lastSeenPosition, aimPoint, now, !_holdCoverInEncounter);
            }
            else if (_squad != null && _squad.HasContact(now) &&
                (_squad.LastSeenAt > _lastSeenAt || _squad.ContactVersion != _squadContactVersion))
            {
                _lastSeenAt = _squad.LastSeenAt;
                _lastSeenPosition = _squad.LastSeenPosition;
                _lastAimPoint = _squad.LastAimPoint;
                if (_state == State.Idle || _state == State.Returning)
                    StopToWatch();
            }

            bool newSquadContact = _squad != null && _squad.HasContact(now) &&
                _squadContactVersion != _squad.ContactVersion;
            if (newSquadContact)
            {
                _squadContactVersion = _squad.ContactVersion;
                if (_state != State.Peeking && _state != State.Shooting)
                    _squadPeekUntil = now + SquadPeekWindow;
            }

            if (_state == State.RetreatingAfterHit || _state == State.RecoveringInCover)
            {
                UpdateHitRetreat(now);
                return;
            }
            if (_state == State.Counterattacking)
            {
                if (!UpdateReload(now))
                    UpdateStepOut(now, visible, HitCounterAimTime);
                return;
            }

            if (!visible && !newSquadContact && _heardPosition.HasValue &&
                _lastHeardAt >= _lastSeenAt && now - _lastHeardAt < FootstepReactionWindow)
            {
                _lastSeenPosition = _heardPosition.Value;
                _lastAimPoint = _lastSeenPosition + Vector3.up * 1.4f;
            }

            if (TryOpeningShot(now, visible) || HandleImmediateReaction(now, visible, newSquadContact))
                return;

            if (_holdCoverInEncounter)
            {
                UpdatePositionalCombat(now, visible);
                return;
            }

            if (!visible && _rounds > 0 && _state != State.Peeking &&
                TryGetRecentShot(now, out Vector3 shotPosition))
            {
                if (_state != State.InCover)
                {
                    ReleaseCover();
                    StopToWatch();
                }
                AimAt(shotPosition + Vector3.up * 1.4f);
                return;
            }

            bool advance = true;
            bool assaultOrdered = _squad != null && _squad.isActiveAndEnabled
                ? _squad.HasAssaultOrder(_player, now, out advance)
                : BanditSquad.CanRush(_player) && (visible || (_state == State.Assaulting && now - _lastSeenAt < SearchTime));
            if (_rounds > 0 && assaultOrdered && (advance || (_state != State.Peeking && _state != State.SteppingOut)))
            {
                UpdateAssault(now, visible, advance);
                return;
            }

            if (_state == State.Peeking)
            {
                UpdateCover(now, visible);
                return;
            }

            if (UpdateReload(now))
                return;

            if (_state == State.SteppingOut)
            {
                UpdateStepOut(now, visible);
                return;
            }

            if (_state == State.Assaulting)
                StopToWatch();

            if (_state == State.Supporting && visible)
            {
                FinishMovingFire(now, CoverRetryInterval);
                return;
            }

            if (_state == State.TakingCover || _state == State.InCover)
            {
                UpdateCover(now, visible);
                return;
            }

            if (visible)
            {
                if (now >= _nextCoverTryAt && TryTakeCover(now))
                    return;

                if (_state != State.Shooting && _state != State.Repositioning)
                {
                    _fireAt = Mathf.Max(_fireAt, now + AimTime);
                    _state = State.Shooting;
                    _npc.StopMoving();
                }
                AimAt(aimPoint);
                if (!CanFire(now))
                    return;

                if (!HasClearShot())
                {
                    RepositionForShot(now, aimPoint);
                    return;
                }

                if (_state == State.Repositioning)
                {
                    _state = State.Shooting;
                    _nextRepositionAt = 0f;
                    _npc.StopMoving();
                }
                Fire(now);
                return;
            }

            if (_state == State.Repositioning)
            {
                if (now < _nextRepositionAt)
                    return;
                StopToWatch();
            }

            if (_state == State.Shooting)
                StopToWatch();

            bool heardRecently = _heardPosition.HasValue && now - _lastHeardAt < SearchTime;
            if (heardRecently && (_state == State.Idle || _state == State.Returning))
                StopToWatch();

            switch (_state)
            {
                case State.Watching:
                case State.Supporting:
                    if (now - _lastSeenAt < SearchTime)
                        UpdateFiringSupport(now);
                    else if (heardRecently)
                    {
                        StopToWatch();
                        _npc.Face(_heardPosition.Value - transform.position);
                        RaiseWeapon(48f);
                    }
                    else
                    {
                        _npc.StopMoving();
                        _state = State.Returning;
                    }
                    break;
                case State.Returning:
                    if (_npc.ReturnHome())
                        _state = State.Idle;
                    break;
                default:
                    _npc.Face(_initialFacing);
                    RaiseWeapon(0f);
                    break;
            }
        }

        bool TryOpeningShot(float now, bool visible)
        {
            if (!_openingShotPending)
                return false;

            _openingShotPending = false;
            if (visible)
            {
                AimStagedWeapon(_lastAimPoint);
                if (HasClearShot())
                {
                    Fire(now, PositionalShotInterval);
                    return true;
                }
            }

            if (_squad == null || !_squad.TryUseOpeningWarning())
                return false;

            SetWeaponAngle(112f);
            Fire(now, PositionalShotInterval, damagePlayer: false);
            return true;
        }

        bool HandleImmediateReaction(float now, bool visible, bool newSquadContact)
        {
            if (_rounds == 0 || _state == State.Reloading || _state == State.RetreatingToReload)
                return false;

            bool closeApproach = now <= _loudApproachUntil && now >= _nextFootstepReactionAt;
            if (_state == State.InCover && now + 0.65f >= _nextRoundAt &&
                (closeApproach || now <= _squadPeekUntil))
            {
                AimAt(_lastAimPoint);
                if (_cover != null && _cover.IsReservedBy(this) && TryBeginPeek(now, quick: true))
                {
                    _squadPeekUntil = float.NegativeInfinity;
                    return true;
                }
            }

            if (newSquadContact && !visible)
                AimAt(_lastAimPoint);

            if (!closeApproach || !visible || now < _nextRoundAt ||
                _state == State.TakingCover || _state == State.InCover ||
                (_state == State.Peeking && (_peekPhase == PeekPhase.Closing || _peekPhase == PeekPhase.HoldingAfterShot)))
                return false;

            _npc.Face(_lastAimPoint - transform.position);
            AimStagedWeapon(_lastAimPoint);
            if (!HasClearShot())
                return false;

            Fire(now, _holdCoverInEncounter ? PositionalShotInterval : ShotInterval);
            _loudApproachUntil = float.NegativeInfinity;
            _nextFootstepReactionAt = now + FootstepReactionCooldown;
            _npc.StopMoving();
            if (_state == State.Peeking)
            {
                _peekHasFired = true;
                _peekPhase = PeekPhase.HoldingAfterShot;
                _coverActionAt = now + PeekAfterShotTime;
            }
            else
                _state = State.Shooting;
            return true;
        }

        void UpdatePositionalCombat(float now, bool visible)
        {
            if (!visible && TryGetRecentShot(now, out var shotPosition))
            {
                _lastSeenPosition = shotPosition;
                _lastAimPoint = shotPosition + Vector3.up * 1.4f;
            }

            if (_state == State.Peeking)
            {
                UpdateCover(now, visible);
                return;
            }
            if (UpdateReload(now))
                return;
            if (_state == State.TakingCover || _state == State.InCover)
            {
                UpdateCover(now, visible);
                return;
            }

            if (now >= _nextCoverTryAt && TryTakeCover(now))
                return;

            AimAt(_lastAimPoint);
            if (!visible)
            {
                if (_state != State.Repositioning || now >= _nextRepositionAt)
                    StopToWatch();
                return;
            }

            if (!HasClearShot())
            {
                RepositionForShot(now, _lastAimPoint);
                return;
            }

            if (_state != State.Shooting)
            {
                _npc.StopMoving();
                _state = State.Shooting;
                _fireAt = Mathf.Max(_fireAt, now + AimTime);
            }
            if (CanFire(now))
                Fire(now, PositionalShotInterval);
        }

        bool CanFire(float now) => _rounds > 0 && now >= _fireAt;

        internal void ReactToHitInCover(Vector3 shotOrigin, Vector3 impactPoint)
        {
            if (!isActiveAndEnabled || _npc == null || _player == null || _player.IsDead)
                return;

            BanditCover cover = _state switch
            {
                State.InCover or State.TakingCover => _cover,
                State.Peeking when _peekPhase == PeekPhase.Closing || !_peekHasFired => _cover,
                State.Idle or State.Watching => FindCoverAtCurrentPosition(shotOrigin),
                _ => null
            };
            if (cover == null || (!cover.IsAvailable && !cover.IsReservedBy(this)) ||
                (_state == State.TakingCover && !cover.ProtectsFrom(shotOrigin, transform.position)))
                return;

            cover.GetPositions(shotOrigin, false, out var hide, out var left, out var right);
            Vector3 side = cover.transform.right;
            float hitSide = Vector3.Dot(impactPoint - transform.position, side);
            if (Mathf.Abs(hitSide) < 0.05f)
                hitSide = Vector3.Dot(shotOrigin - transform.position, side);

            float now = Time.time;
            float oppositeSide = -Mathf.Sign(hitSide);
            Vector3 oppositeEdge = oppositeSide < 0f ? left : right;
            Vector3 widerExit = oppositeEdge + side * (oppositeSide * HitCounterExtraClearance);
            if (_rounds > 0 && Random.value < 0.5f &&
                (TryMoveToCoverExit(hide, oppositeEdge, shotOrigin, HitReactionSpeedScale, out var counterPosition) ||
                    TryMoveToCoverExit(hide, widerExit, shotOrigin, HitReactionSpeedScale, out counterPosition)))
            {
                _lastAimPoint = shotOrigin;
                _fireAt = Mathf.Max(now, _nextRoundAt);
                _openingShotPending = false;
                BeginStepOut(now, counterPosition, State.Counterattacking);
                return;
            }

            Vector3 awayFromShot = Vector3.ProjectOnPlane(hide - shotOrigin, Vector3.up).normalized;

            if (!TryMoveToHiddenPoint(hide + side * (oppositeSide * 0.65f)) &&
                !TryMoveToHiddenPoint(hide + awayFromShot * 0.55f) &&
                !TryMoveToHiddenPoint(hide))
                return;

            if (_cover != cover)
                ReleaseCover();
            _cover = cover;
            _cover.Reserve(this);
            _state = State.RetreatingAfterHit;
            _coverActionAt = now + CoverMoveTimeout;

            bool TryMoveToHiddenPoint(Vector3 point)
            {
                if (!_npc.TryGetReachablePosition(point, 0.45f, out var destination) ||
                    !cover.ProtectsFrom(shotOrigin, destination))
                    return false;

                // Check both sides of the torso so the new position hides the exposed shoulder too.
                Vector3 torsoSide = Vector3.Cross(Vector3.up, destination - shotOrigin).normalized * 0.36f;
                if (!cover.ProtectsFrom(shotOrigin, destination + torsoSide) ||
                    !cover.ProtectsFrom(shotOrigin, destination - torsoSide) ||
                    !_npc.TryMoveToFiringPosition(destination, HitReactionSpeedScale))
                    return false;

                _hidePosition = destination;
                return true;
            }
        }

        BanditCover FindCoverAtCurrentPosition(Vector3 shotOrigin)
        {
            BanditCover bestCover = null;
            float bestDistance = CoverSearchRadius * CoverSearchRadius;
            foreach (var cover in BanditCover.Active)
            {
                if (cover.gameObject.scene != gameObject.scene || !cover.IsAvailable)
                    continue;

                float distance = (cover.transform.position - transform.position).sqrMagnitude;
                if (distance >= bestDistance || !cover.ProtectsFrom(shotOrigin, transform.position))
                    continue;

                bestCover = cover;
                bestDistance = distance;
            }
            return bestCover;
        }

        void UpdateHitRetreat(float now)
        {
            RaiseWeapon(15f);
            if (_cover != null && _cover.IsReservedBy(this))
            {
                if (_state == State.RetreatingAfterHit && AtPosition(_hidePosition, 0.2f))
                {
                    _npc.StopMoving();
                    _state = State.RecoveringInCover;
                    _coverActionAt = now + HitRecoveryTime;
                }

                if (now < _coverActionAt)
                    return;
            }

            ReleaseCover();
            StopToWatch();
            _fireAt = Mathf.Max(_fireAt, now + AimTime);
        }

        internal void Shout(string subtitle, string voicePath)
        {
            const float subtitleDuration = 3f;
            if (_hud != null)
                _hud.ShowSubtitle(subtitle, duration: subtitleDuration);

            var clip = DialogueVoice.GetStoryClip(voicePath);
            string spoken = NpcSpeechState.SpokenText(subtitle);
            var speech = NpcSpeechState.GetOrAdd(this);
            if (clip == null)
            {
                speech.BeginLine(spoken, subtitleDuration);
                return;
            }

            if (_shoutAudio == null)
                _shoutAudio = DialogueVoice.CreateSource(transform, "Bandit Voice", minDistance: 4f, maxDistance: 42f);
            _shoutAudio.clip = clip;
            _shoutAudio.Play();
            speech.BeginLine(spoken, clip.length, _shoutAudio, clip);
        }

        void UpdateFiringSupport(float now)
        {
            _movingAimStartedAt = -1f;
            _npc.Face(_lastAimPoint - transform.position);
            RaiseWeapon(48f);
            if (now >= _nextCoverTryAt && TryTakeCover(now))
                return;

            if (now < _nextSupportMoveAt)
                return;

            _nextSupportMoveAt = now + RepositionInterval;
            if (_npc.TryRepositionForShot(_lastAimPoint, CanWatchFrom))
            {
                _state = State.Supporting;
                return;
            }

            StopToWatch();

            bool CanWatchFrom(Vector3 feet)
            {
                Vector3 eye = feet + Vector3.up * EyeHeight;
                return (_lastAimPoint - eye).sqrMagnitude <= ShotRange * ShotRange &&
                    _npc.HasClearLine(eye, _lastAimPoint);
            }
        }

        void UpdateAssault(float now, bool visible, bool advance)
        {
            if (!advance)
            {
                AimAt(_lastAimPoint);
                return;
            }

            if (_state != State.Assaulting)
            {
                ReleaseCover();
                _state = State.Assaulting;
                _movingAimStartedAt = -1f;
            }

            if (now - _lastSeenAt >= SearchTime || AtPosition(_lastSeenPosition, AssaultStoppingDistance + 0.3f) ||
                !_npc.TryAdvanceOnPlayer(_lastSeenPosition, AssaultStoppingDistance))
                _npc.StopMoving();

            AimAndFireWhileMoving(now, visible);
        }

        void AimAndFireWhileMoving(float now, bool visible, float aimTime = AimTime, float interval = ShotInterval)
        {
            AimAt(_lastAimPoint);
            if (!visible || !HasClearShot())
            {
                _movingAimStartedAt = -1f;
                return;
            }

            if (_movingAimStartedAt < 0f)
                _movingAimStartedAt = now;
            if (now - _movingAimStartedAt >= aimTime && CanFire(now))
                Fire(now, interval);
        }

        void FinishMovingFire(float now, float openFireWindow, float aimTime = AimTime)
        {
            _npc.StopMoving();
            _state = State.Shooting;
            float aimStartedAt = _movingAimStartedAt < 0f ? now : _movingAimStartedAt;
            _fireAt = Mathf.Max(_fireAt, aimStartedAt + aimTime);
            _nextCoverTryAt = now + openFireWindow;
            _movingAimStartedAt = -1f;
        }

        bool UpdateReload(float now)
        {
            if (_rounds > 0)
                return false;

            if (_state == State.Reloading && now >= _reloadEndsAt)
            {
                _rounds = MagazineSize;
                _reloadEndsAt = 0f;
                _nextCoverTryAt = now;
                ReleaseCover();
                _state = State.Watching;
                return false;
            }

            if (_state != State.RetreatingToReload && _state != State.Reloading)
            {
                if (TryFindReloadPosition(now, out _reloadPosition))
                {
                    _state = State.RetreatingToReload;
                    _reloadRetreatEndsAt = now + ReloadRetreatTimeout;
                }
                else
                    StartReload(now);
            }

            if (_state == State.RetreatingToReload && (AtPosition(_reloadPosition) || now >= _reloadRetreatEndsAt))
                StartReload(now);

            _npc.Face(_lastAimPoint - transform.position);
            RaiseWeapon(_state == State.Reloading ? 48f : 15f);
            return true;
        }

        bool TryFindReloadPosition(float now, out Vector3 position)
        {
            if (_cover != null && _cover.IsReservedBy(this) && _cover.ProtectsFrom(CoverThreatPoint, _hidePosition) &&
                _npc.TryMoveToFiringPosition(_hidePosition))
            {
                position = _hidePosition;
                return true;
            }

            ReleaseCover();
            if (TryTakeCover(now, forReload: true))
            {
                position = _hidePosition;
                return true;
            }

            return _npc.TryRetreatFrom(_lastSeenPosition, out position);
        }

        void StartReload(float now)
        {
            _npc.StopMoving();
            _state = State.Reloading;
            _reloadEndsAt = now + (_reloadClip != null ? _reloadClip.length : FallbackReloadTime);
            if (_reloadClip != null)
                _reloadAudio.PlayOneShot(_reloadClip, 0.7f);
        }

        Vector3 CoverThreatPoint => new Vector3(_lastAimPoint.x,
            Mathf.Max(_lastAimPoint.y, _lastSeenPosition.y + 1.45f), _lastAimPoint.z);

        bool TryTakeCover(float now, bool forReload = false)
        {
            _nextCoverTryAt = now + CoverRetryInterval;
            float distanceToPlayer = Vector3.Distance(transform.position, _lastSeenPosition);
            float minimumCoverDistance = _holdCoverInEncounter
                ? Mathf.Max(0f, distanceToPlayer - PositionalCoverAdvance)
                : forReload || BanditSquad.CanRush(_player) ? 0f : Mathf.Min(PreferredCoverDistance, distanceToPlayer);
            Vector3 threat = CoverThreatPoint;
            BanditCover bestCover = null;
            Vector3 bestHide = default;
            Vector3 bestPeek = default;
            bool bestLean = false;
            float bestScore = CoverSearchRadius * CoverSearchRadius;
            foreach (var cover in BanditCover.Active)
            {
                if (cover.gameObject.scene != gameObject.scene || !cover.IsAvailable)
                    continue;

                if (_leanPivot != null && !forReload)
                {
                    cover.GetPositions(threat, true, out var leanHide, out var leanLeft, out var leanRight);
                    ConsiderSpot(cover, leanHide, leanLeft, true);
                    ConsiderSpot(cover, leanHide, leanRight, true);
                }

                cover.GetPositions(threat, false, out var hide, out var left, out var right);
                if (forReload)
                    ConsiderSpot(cover, hide, hide, false);
                else
                {
                    ConsiderSpot(cover, hide, left, false);
                    ConsiderSpot(cover, hide, right, false);
                }
            }

            if (bestCover == null || !_npc.TryMoveToFiringPosition(bestHide))
                return false;

            _cover = bestCover;
            _cover.Reserve(this);
            _hidePosition = bestHide;
            _peekPosition = bestPeek;
            _peekWithLean = bestLean;
            _repeatedPeeksLeft = 0;
            _state = State.TakingCover;
            _coverActionAt = now + CoverMoveTimeout;
            return true;

            void ConsiderSpot(BanditCover cover, Vector3 hide, Vector3 peek, bool lean)
            {
                float score = (hide - transform.position).sqrMagnitude;
                if (!forReload)
                    score += 0.15f * (peek - transform.position).sqrMagnitude + (lean ? 0f : 5f);
                if (score >= bestScore)
                    return;

                Vector3 walkableHide;
                Vector3 walkablePeek = default;
                if (forReload)
                {
                    if (!_npc.TryGetReachablePosition(hide, 0.45f, out walkableHide))
                        return;
                }
                else if (!_npc.TryGetCoverRoute(hide, peek, out walkableHide, out walkablePeek) ||
                    !CanSeeFromPeek(walkableHide, walkablePeek, lean, _lastAimPoint))
                    return;

                if ((walkableHide - _lastSeenPosition).sqrMagnitude < minimumCoverDistance * minimumCoverDistance ||
                    !cover.ProtectsFrom(threat, walkableHide))
                    return;

                bestCover = cover;
                bestHide = walkableHide;
                bestPeek = walkablePeek;
                bestLean = lean;
                bestScore = score;
            }
        }

        void UpdateCover(float now, bool visible)
        {
            bool waiting = _state == State.InCover && _coverAction == CoverAction.Wait;
            float contactMemory = waiting ? CoverWaitMemory : SearchTime;
            if (_cover == null || !_cover.IsReservedBy(this) ||
                (!_holdCoverInEncounter && now - _lastSeenAt >= contactMemory) ||
                (visible && !_cover.ProtectsFrom(CoverThreatPoint, _hidePosition)))
            {
                LeaveCover(now, visible);
                return;
            }

            if (_state == State.Peeking && _peekPhase != PeekPhase.MovingToEdge && _peekPhase != PeekPhase.Closing)
                AimAt(_lastAimPoint);
            else
            {
                _npc.Face(_lastAimPoint - transform.position);
                RaiseWeapon(48f);
            }
            switch (_state)
            {
                case State.TakingCover:
                    if (AtPosition(_hidePosition))
                        BeginCoverPause(now);
                    else if (now >= _coverActionAt)
                        LeaveCover(now, visible);
                    break;
                case State.InCover:
                    UpdateInCover(now, visible);
                    break;
                case State.Peeking:
                    UpdatePeek(now, visible);
                    break;
            }
        }

        void BeginCoverPause(float now)
        {
            _npc.StopMoving();
            _state = State.InCover;
            if (_holdCoverInEncounter)
            {
                _coverActionAt = now + Random.Range(3.8f, 6.2f);
                _coverAction = CoverAction.Peek;
                return;
            }
            _coverActionAt = now + Random.Range(0.8f, 1.5f);
            if (_repeatedPeeksLeft > 0)
            {
                _repeatedPeeksLeft--;
                _coverAction = CoverAction.Peek;
                return;
            }

            float choice = Random.value;
            if (choice < 0.55f)
            {
                _coverAction = CoverAction.Peek;
                _repeatedPeeksLeft = Random.Range(1, 3);
            }
            else
                _coverAction = choice < 0.8f || !AtPosition(_lastSeenPosition, PreferredCoverDistance)
                    ? CoverAction.StepOut : CoverAction.Wait;
        }

        void UpdateInCover(float now, bool visible)
        {
            if (now < _coverActionAt || !CanFire(now))
                return;

            if (_coverAction == CoverAction.Wait)
            {
                bool freshContact = now - _lastSeenAt <= 1.5f;
                bool seenClose = freshContact && AtPosition(_lastSeenPosition, CoverWaitDistance);
                bool heardClose = _heardPosition.HasValue && now - _lastHeardAt <= 1.5f &&
                    AtPosition(_heardPosition.Value, CoverWaitDistance);
                bool targetRetreated = freshContact && !AtPosition(_lastSeenPosition, PreferredCoverDistance);
                if (!seenClose && !heardClose && !targetRetreated)
                    return;
                if ((seenClose || heardClose) && TryStepOut(now))
                    return;
            }
            else if (_coverAction == CoverAction.StepOut && TryStepOut(now))
                return;

            if (!TryBeginPeek(now))
            {
                LeaveCover(now, visible);
            }
        }

        bool TryBeginPeek(float now, bool quick = false)
        {
            float speed = quick ? 1.2f : PeekStepSpeedScale;
            if (quick && TryMoveToWideCoverPosition(speed, out var widerPeek))
            {
                _peekPosition = widerPeek;
                _peekWithLean = false;
            }
            else if (!_npc.TryMoveToFiringPosition(_peekPosition, speed))
                return false;

            _state = State.Peeking;
            _peekPhase = PeekPhase.MovingToEdge;
            _peekHasFired = false;
            _coverActionAt = now + (quick ? 1.6f : PeekTimeout);
            return true;
        }

        bool TryStepOut(float now)
        {
            if (!TryMoveToWideCoverPosition(1f, out var destination))
                return false;

            BeginStepOut(now, destination, State.SteppingOut);
            return true;
        }

        void BeginStepOut(float now, Vector3 destination, State state)
        {
            _openFirePosition = destination;
            ReleaseCover();
            _state = state;
            _coverActionAt = now + PeekTimeout;
            _movingAimStartedAt = -1f;
        }

        void UpdateStepOut(float now, bool visible, float aimTime = AimTime)
        {
            AimAndFireWhileMoving(now, visible, aimTime, _holdCoverInEncounter ? PositionalShotInterval : ShotInterval);
            if (_rounds == 0 || _player.IsDead || (now < _coverActionAt && !AtPosition(_openFirePosition)))
                return;

            if (visible)
                FinishMovingFire(now, OpenFireWindow, aimTime);
            else
            {
                _npc.StopMoving();
                _state = State.Watching;
                _nextCoverTryAt = now + RepositionInterval;
            }
        }

        void UpdatePeek(float now, bool visible)
        {
            switch (_peekPhase)
            {
                case PeekPhase.MovingToEdge:
                    if (AtPosition(_peekPosition, 0.22f))
                    {
                        _npc.StopMoving();
                        Vector3 facing = Vector3.ProjectOnPlane(_lastAimPoint - transform.position, Vector3.up);
                        Vector3 right = facing.sqrMagnitude > 0.0025f
                            ? Vector3.Cross(Vector3.up, facing.normalized) : transform.right;
                        _leanSide = GetPeekSide(_peekPosition - _hidePosition, right);
                        _peekPhase = PeekPhase.Opening;
                    }
                    else if (now >= _coverActionAt && !TryWidenPeek(now))
                        _peekPhase = PeekPhase.Closing;
                    break;
                case PeekPhase.Opening:
                    bool canShoot = visible && HasClearShot();
                    if (!_peekWithLean || _leanProgress >= 1f || canShoot)
                    {
                        _peekPhase = PeekPhase.Aiming;
                        _peekAimStartedAt = canShoot ? now : -1f;
                        _peekShotsLeft = _holdCoverInEncounter ? 1 : Random.Range(2, 4);
                        _coverActionAt = now + PeekAimTime + PeekTargetGrace;
                    }
                    break;
                case PeekPhase.Aiming:
                    if (now >= _coverActionAt)
                    {
                        if (_peekHasFired || !TryWidenPeek(now))
                            _peekPhase = PeekPhase.Closing;
                        break;
                    }

                    if (!visible || !HasClearShot())
                    {
                        _peekAimStartedAt = -1f;
                        break;
                    }

                    if (_peekAimStartedAt < 0f)
                        _peekAimStartedAt = now;
                    if (now - _peekAimStartedAt >= PeekAimTime && CanFire(now))
                    {
                        bool lastShot = --_peekShotsLeft == 0 || _rounds == 1;
                        Fire(now, _holdCoverInEncounter ? PositionalShotInterval :
                            lastShot ? ShotInterval : PeekFollowupInterval);
                        _peekHasFired = true;
                        if (lastShot)
                        {
                            _peekPhase = PeekPhase.HoldingAfterShot;
                            _coverActionAt = now + PeekAfterShotTime;
                        }
                        else
                            _coverActionAt = now + PeekFollowupInterval + PeekTargetGrace;
                    }
                    break;
                case PeekPhase.HoldingAfterShot:
                    if (now >= _coverActionAt)
                        _peekPhase = PeekPhase.Closing;
                    break;
                case PeekPhase.Closing:
                    if (_leanProgress <= 0f)
                        ReturnToCover(now, visible);
                    break;
            }
        }

        bool AtPosition(Vector3 position, float tolerance = 0.3f) =>
            (transform.position - position).sqrMagnitude <= tolerance * tolerance;

        bool TryWidenPeek(float now)
        {
            if (!_peekWithLean || !TryMoveToWideCoverPosition(PeekStepSpeedScale, out var position))
                return false;

            _peekPosition = position;
            _peekWithLean = false;
            _peekPhase = PeekPhase.MovingToEdge;
            _coverActionAt = now + PeekTimeout;
            return true;
        }

        bool TryMoveToWideCoverPosition(float speedScale, out Vector3 position)
        {
            _cover.GetPositions(CoverThreatPoint, false, out var hide, out var left, out var right);
            bool preferLeft = Vector3.Dot(_peekPosition - _hidePosition, left - hide) > 0f;
            return TryMoveToCoverExit(_hidePosition, preferLeft ? left : right, _lastAimPoint, speedScale, out position) ||
                TryMoveToCoverExit(_hidePosition, preferLeft ? right : left, _lastAimPoint, speedScale, out position);
        }

        bool TryMoveToCoverExit(Vector3 hide, Vector3 exit, Vector3 aimPoint, float speedScale, out Vector3 destination) =>
            _npc.TryGetCoverRoute(hide, exit, out _, out destination) &&
            CanSeeFromPeek(hide, destination, false, aimPoint) && _npc.TryMoveToFiringPosition(destination, speedScale);

        bool CanSeeFromPeek(Vector3 hide, Vector3 peek, bool lean, Vector3 aimPoint)
        {
            Vector3 facing = Vector3.ProjectOnPlane(aimPoint - peek, Vector3.up);
            Quaternion rotation = facing.sqrMagnitude > 0.0025f ? Quaternion.LookRotation(facing) : transform.rotation;
            float side = lean ? GetPeekSide(peek - hide, rotation * Vector3.right) : 0f;
            Quaternion rest = rotation * _leanRest;
            Quaternion pose = rest * Quaternion.AngleAxis(-LeanAngle * side, Vector3.forward);
            Vector3 waist = Vector3.up * LeanPivotHeight;
            Vector3 feet = peek + rest * waist - pose * waist;
            Vector3 eye = feet + pose * (Vector3.up * EyeHeight);
            if ((lean && !_npc.HasClearLine(peek + Vector3.up * EyeHeight, eye)) ||
                !_npc.HasClearLine(eye, aimPoint))
                return false;

            Vector3 chest = feet + pose * ChestPivot;
            float aimAngle = AimAngleFromOffset(Quaternion.Inverse(pose) * (aimPoint - chest));
            Vector3 muzzle = chest + pose * (WeaponRotation(aimAngle) * (GripReach + MuzzlePosition));
            return (aimPoint - muzzle).sqrMagnitude <= ShotRange * ShotRange &&
                HasWeaponClearance(peek, chest, muzzle) && _npc.HasClearLine(muzzle, aimPoint);
        }

        void UpdateLean()
        {
            if (_leanPivot == null)
                return;

            bool exposed = _state == State.Peeking && _peekWithLean &&
                (_peekPhase == PeekPhase.Opening || _peekPhase == PeekPhase.Aiming || _peekPhase == PeekPhase.HoldingAfterShot);
            if (!exposed && _leanProgress <= 0f)
                return;

            _leanProgress = Mathf.MoveTowards(_leanProgress, exposed ? 1f : 0f,
                Time.deltaTime / (exposed ? LeanOutTime : LeanInTime));
            ApplyCoverLean(_leanSide * Mathf.SmoothStep(0f, 1f, _leanProgress));
        }

        void ApplyCoverLean(float side)
        {
            Quaternion rotation = _leanRest * Quaternion.AngleAxis(-LeanAngle * side, Vector3.forward);
            Vector3 waist = Vector3.up * LeanPivotHeight;
            _leanPivot.SetLocalPositionAndRotation(_leanRestPosition + _leanRest * waist - rotation * waist, rotation);
            _stagedModel?.SetCoverLean(side);
        }

        static float GetPeekSide(Vector3 outward, Vector3 right) =>
            Mathf.Clamp(Vector3.Dot(outward.normalized, right) * 1.4f, -1f, 1f);

        bool HasClearShot()
        {
            Vector3 muzzle = StagedMuzzlePosition;
            return HasWeaponClearance(transform.position, ChestPosition, muzzle) && CanHitPlayer(muzzle);
        }

        void ReturnToCover(float now, bool visible)
        {
            if (!_npc.TryMoveToFiringPosition(_hidePosition, PeekStepSpeedScale))
            {
                LeaveCover(now, visible);
                return;
            }
            _state = State.TakingCover;
            _coverActionAt = now + CoverMoveTimeout;
        }

        void LeaveCover(float now, bool visible)
        {
            ReleaseCover();
            _npc.StopMoving();
            _nextCoverTryAt = now + CoverRetryInterval;
            if (visible)
            {
                _state = State.Shooting;
                _fireAt = Mathf.Max(_fireAt, now + AimTime);
            }
            else
                StopToWatch();
        }

        void ReleaseCover()
        {
            if (_cover != null)
                _cover.Release(this);
            _cover = null;
        }

        void AimAt(Vector3 aimPoint)
        {
            _npc.Face(aimPoint - transform.position);
            AimStagedWeapon(aimPoint, smooth: true);
        }

        float GetAimAngle(Vector3 target) => AimAngleFromOffset(WeaponPivot.InverseTransformPoint(target) - ChestPivot);

        static float AimAngleFromOffset(Vector3 offset)
        {
            float pitch = Mathf.Atan2(offset.y, new Vector2(offset.x, offset.z).magnitude) * Mathf.Rad2Deg;
            return 90f + Mathf.Clamp(pitch, -25f, 25f);
        }

        bool HasWeaponClearance(Vector3 feet, Vector3 chest, Vector3 muzzle)
        {
            feet.y = chest.y;
            return _npc.HasClearLine(feet, chest) && _npc.HasClearLine(chest, muzzle);
        }

        void RepositionForShot(float now, Vector3 aimPoint)
        {
            _state = State.Repositioning;
            if (now < _nextRepositionAt)
                return;

            _nextRepositionAt = now + RepositionInterval;
            Quaternion inverse = Quaternion.Inverse(transform.rotation);
            Vector3 chestOffset = inverse * (ChestPosition - transform.position);
            Vector3 muzzleOffset = inverse * (StagedMuzzlePosition - transform.position);
            if (!_npc.TryRepositionForShot(aimPoint, CanShootFrom, 1.25f) &&
                (_holdCoverInEncounter || !_npc.TryRepositionForShot(aimPoint, CanShootFrom)))
                _npc.StopMoving();

            bool CanShootFrom(Vector3 feet)
            {
                Vector3 facing = Vector3.ProjectOnPlane(aimPoint - feet, Vector3.up);
                Quaternion rotation = facing.sqrMagnitude > 0.0025f
                    ? Quaternion.LookRotation(facing) : transform.rotation;
                Vector3 candidateChest = feet + rotation * chestOffset;
                Vector3 candidateMuzzle = feet + rotation * muzzleOffset;
                return (aimPoint - candidateMuzzle).sqrMagnitude <= ShotRange * ShotRange &&
                    HasWeaponClearance(feet, candidateChest, candidateMuzzle) &&
                    _npc.HasClearLine(candidateMuzzle, aimPoint);
            }
        }

        void StopToWatch()
        {
            _npc.StopMoving();
            _state = State.Watching;
            _movingAimStartedAt = -1f;
        }

        internal void HearNoise(Vector3 feet, Vector3 velocity)
        {
            if (!isActiveAndEnabled || _player == null || _player.IsDead)
                return;

            _heardPosition = feet;
            _lastHeardAt = Time.time;
            Vector3 towardBandit = Vector3.ProjectOnPlane(transform.position - feet, Vector3.up);
            if (velocity.sqrMagnitude >= LoudStepSpeed * LoudStepSpeed &&
                towardBandit.sqrMagnitude <= CloseFootstepRange * CloseFootstepRange &&
                Vector3.Dot(velocity.normalized, towardBandit.normalized) >= 0.25f)
                _loudApproachUntil = _lastHeardAt + FootstepReactionWindow;
        }

        internal void HearShot(Vector3 position)
        {
            if (!isActiveAndEnabled)
                return;

            _lastHeardShotPosition = position;
            _lastHeardShotAt = Time.time;
            _squad?.ReportShot(position, _lastHeardShotAt);
        }

        bool TryGetRecentShot(float now, out Vector3 position)
        {
            if (_squad != null && _squad.isActiveAndEnabled && _squad.LastHeardShotAt > _lastHeardShotAt)
            {
                position = _squad.LastHeardShotPosition;
                return _squad.LastHeardShotAt >= _lastSeenAt && now - _squad.LastHeardShotAt <= ShotMemoryTime;
            }

            position = _lastHeardShotPosition;
            return _lastHeardShotAt >= _lastSeenAt && now - _lastHeardShotAt <= ShotMemoryTime;
        }

        void Fire(float now, float interval = ShotInterval, bool damagePlayer = true)
        {
            _fireAt = _nextRoundAt = now + interval;
            _rounds--;
            PlayShotEffects(now);
            if (!damagePlayer)
                return;

            if (_visibleHitChance < 1f)
            {
                float distance = Vector3.Distance(_effects.transform.position, _lastAimPoint);
                float accuracy = Mathf.Lerp(1f, LongRangeAccuracyMultiplier,
                    Mathf.InverseLerp(AccuracyFalloffStart, ShotRange, distance));
                if (Random.value >= _visibleHitChance * accuracy)
                    return;
            }

            _player.TakeAttackDamage(Damage, PlayerHealth.DamageSource.Gunfire);
            _player.AddBleeding(Bleeding);
        }

        void PlayShotEffects(float now, bool playAudio = true)
        {
            _stagedModel?.Recoil();
            if (playAudio && _shotClip != null)
                _shotAudio.PlayOneShot(_shotClip, 0.8f);
            Flash(now);
        }

        void UpdateTargetPoints(Vector3 fallbackCenter)
        {
            CapsuleCollider hitbox = _playerMovement != null ? _playerMovement.BulletHitbox : null;
            if (hitbox == null || !hitbox.enabled)
            {
                float height = _playerCollider != null ? _playerCollider.bounds.extents.y * 0.65f : 0.5f;
                Vector3 offset = Vector3.up * height;
                _targetPoints[0] = fallbackCenter;
                _targetPoints[1] = fallbackCenter + offset;
                _targetPoints[2] = fallbackCenter - offset;
                _targetPointCount = 3;
                return;
            }

            // Sample inside the capsule so a clear line to a point also intersects the hitbox.
            Transform pose = hitbox.transform;
            Vector3 center = hitbox.center;
            Vector3 bodyOffset = Vector3.up * (hitbox.height * 0.325f);
            Vector3 shoulders = center + Vector3.up * (hitbox.height * 0.5f - hitbox.radius * 0.6f);
            Vector3 sideOffset = Vector3.right * (hitbox.radius * 0.8f);
            _targetPoints[0] = pose.TransformPoint(center);
            _targetPoints[1] = pose.TransformPoint(center + bodyOffset);
            _targetPoints[2] = pose.TransformPoint(shoulders + sideOffset);
            _targetPoints[3] = pose.TransformPoint(shoulders - sideOffset);
            _targetPoints[4] = pose.TransformPoint(center - bodyOffset);
            _targetPointCount = _targetPoints.Length;
        }

        bool TryGetVisibleTarget(out Vector3 target)
        {
            Vector3 neutralEye = transform.position + Vector3.up * EyeHeight;
            Vector3 eye = _leanPivot != null ? _leanPivot.TransformPoint(Vector3.up * EyeHeight) : neutralEye;
            if (_leanProgress > 0f && _stagedModel != null)
                eye = _stagedModel.HeadPosition - Vector3.up * 0.035f;
            if (_leanProgress > 0f && !_npc.HasClearLine(neutralEye, eye))
            {
                target = default;
                return false;
            }
            for (int i = 0; i < _targetPointCount; i++)
            {
                Vector3 point = _targetPoints[i];
                if (_npc.CanSeePlayerPoint(eye, point, SightRange) && IsOnPlayerSideOfCover(point))
                {
                    target = point;
                    return true;
                }
            }

            target = default;
            return false;
        }

        bool CanHitPlayer(Vector3 muzzle)
        {
            for (int i = 0; i < _targetPointCount; i++)
            {
                Vector3 point = _targetPoints[i];
                Vector3 offset = point - muzzle;
                if (offset.sqrMagnitude <= ShotRange * ShotRange &&
                    _npc.HasClearLine(muzzle, point) && IsOnPlayerSideOfCover(point))
                    return true;
            }

            return false;
        }

        bool IsOnPlayerSideOfCover(Vector3 point)
        {
            // Reject any edge of the trigger that crosses a wall during the lean.
            Transform player = _player.transform;
            Vector3 bodyAxis = _playerCollider != null
                ? player.TransformPoint(_playerCollider.center) : player.position;
            bodyAxis.y = point.y;
            return _npc.HasClearLine(bodyAxis, point);
        }

        void RaiseWeapon(float target) => SetWeaponAngle(Mathf.MoveTowards(_armAngle, target, 260f * Time.deltaTime));

        static Quaternion WeaponRotation(float angle) => Quaternion.Euler(90f - angle, 0f, 0f);

        void SetWeaponAngle(float angle)
        {
            _armAngle = angle;
            if (_weapon == null)
                return;

            Quaternion rotation = WeaponRotation(angle);
            _weapon.SetLocalPositionAndRotation(ChestPivot + rotation * GripReach, rotation);
            if (_stagedModel != null)
            {
                _stagedModel.TrackAim(angle >= 65f ? _weapon.position + _weapon.forward * ShotRange : null);
                return;
            }
            AimShoulderAtGrip(_leftShoulder, _leftRest, _weapon.TransformPoint(LeftGripPosition));
            AimShoulderAtGrip(_rightShoulder, _rightRest, _weapon.TransformPoint(RightGripPosition));
        }

        static void AimShoulderAtGrip(Transform shoulder, Quaternion rest, Vector3 grip)
        {
            if (shoulder == null || shoulder.parent == null)
                return;

            Vector3 direction = shoulder.parent.InverseTransformPoint(grip) - shoulder.localPosition;
            if (direction.sqrMagnitude > 0.0001f)
                shoulder.localRotation = Quaternion.FromToRotation(rest * Vector3.down, direction) * rest;
        }

        AudioSource AddAudioSource(float audibleRange)
        {
            var source = _effects.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.minDistance = 2f;
            source.maxDistance = audibleRange;
            source.rolloffMode = AudioRolloffMode.Linear;
            return source;
        }

        void Flash(float now)
        {
            if (_flash == null)
            {
                _flash = _effects.AddComponent<Light>();
                _flash.type = LightType.Point;
                _flash.color = new Color(1f, 0.73f, 0.42f);
                _flash.range = 2.8f;
                _flash.intensity = 9f;
            }
            _flash.enabled = true;
            _flashUntil = now + 0.06f;
        }

        void LateUpdate()
        {
            if (_flash != null && _flash.enabled && Time.time >= _flashUntil)
                _flash.enabled = false;
        }

        internal void StopCombat()
        {
            ReleaseCover();
            _state = State.Idle;
            _nextRepositionAt = 0f;
            _nextCoverTryAt = 0f;
            _nextSupportMoveAt = 0f;
            _movingAimStartedAt = -1f;
            _heardPosition = null;
            _lastHeardShotAt = float.NegativeInfinity;
            _openingShotPending = false;
            _loudApproachUntil = float.NegativeInfinity;
            _squadPeekUntil = float.NegativeInfinity;
            _reloadEndsAt = 0f;
            _leanProgress = 0f;
            if (_leanPivot != null)
                ApplyCoverLean(0f);
            LowerStagedWeapon();
            if (_npc != null)
                _npc.StopMoving();
            if (_flash != null)
                _flash.enabled = false;
            if (_shotAudio != null)
                _shotAudio.Stop();
            if (_reloadAudio != null)
                _reloadAudio.Stop();
            if (_shoutAudio != null)
                _shoutAudio.Stop();
        }

        void OnDisable() => StopCombat();
    }
}
