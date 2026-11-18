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
        const int Damage = 17;
        const int Bleeding = 3;
        const float AimTime = 1.2f;
        const float ShotInterval = 1f;
        const float SearchTime = 7f;
        const float VisibleHitChance = 0.60f;
        const float BlindHitChance = 0.40f;
        const float ShotRange = 18f;
        const float BlindFireHalfAngle = 22f;
        const float FootstepRange = 6f;
        const float FootstepInterval = 0.4f;
        const float ListenTime = 2f;
        const float FallbackReloadTime = 2.28f;
        static readonly Vector3 MuzzlePosition = new(0f, 0.015f, 0.145f);

        [SerializeField] Transform _weapon;
        [SerializeField] AudioClip _shotClip;
        [SerializeField] AudioClip _reloadClip;

        enum State { Idle, Shooting, Suppressing, Searching, Returning }

        readonly Vector3[] _targetPoints = new Vector3[5];
        int _targetPointCount;
        HostileNpc _npc;
        PlayerHealth _player;
        CharacterController _playerCollider;
        FirstPersonController _playerMovement;
        Transform _shoulder;
        Quaternion _restRotation;
        GameObject _effects;
        AudioSource _shotAudio;
        AudioSource _reloadAudio;
        Light _flash;
        Vector3 _initialFacing;
        Vector3 _lastAimPoint;
        Vector3 _lastSeenPosition;
        Vector3 _lastSeenDirection;
        Vector3 _heardPosition;
        int _rounds = PlayerPistol.MagazineSize;
        int _blindShotsLeft;
        float _searchEndsAt;
        float _fireAt;
        float _reloadEndsAt;
        float _listenUntil;
        float _nextFootstepAt;
        float _flashUntil;
        float _armAngle;
        State _state;

        internal bool IsEngaged => isActiveAndEnabled &&
            (_state == State.Shooting || _state == State.Suppressing || _state == State.Searching);

        internal void Initialize(HostileNpc npc, PlayerHealth player, CharacterController playerCollider,
            Transform shoulder)
        {
            _npc = npc;
            _player = player;
            _playerCollider = playerCollider;
            _playerMovement = player.GetComponent<FirstPersonController>();
            _shoulder = shoulder;
            _restRotation = shoulder != null ? shoulder.localRotation : Quaternion.identity;
            _initialFacing = transform.forward;

            _effects = new GameObject("Bandit Weapon Effects");
            _effects.transform.SetParent(_weapon != null ? _weapon : transform, false);
            _effects.transform.localPosition = _weapon != null ? MuzzlePosition : new Vector3(0f, 1.45f, 0.5f);
            _shotAudio = AddAudioSource();
            _reloadAudio = AddAudioSource();
        }

        internal void Tick(bool hitByBeam, Vector3 playerCenter)
        {
            float now = Time.time;
            if (_flash != null && _flash.enabled && now >= _flashUntil)
                _flash.enabled = false;
            if (_reloadEndsAt > 0f && now >= _reloadEndsAt)
            {
                _rounds = PlayerPistol.MagazineSize;
                _reloadEndsAt = 0f;
            }

            UpdateTargetPoints(playerCenter);
            bool seesPlayer = TryGetVisibleTarget(out Vector3 aimPoint);
            if (seesPlayer || hitByBeam)
            {
                if (_state != State.Shooting)
                {
                    if (_state != State.Suppressing)
                        _fireAt = now + AimTime;
                    _state = State.Shooting;
                    _npc.StopMoving();
                }
                _lastAimPoint = aimPoint;
                _lastSeenPosition = _player.transform.position;
                _lastSeenDirection = _playerCollider != null ? _playerCollider.velocity : Vector3.zero;
                _listenUntil = 0f;
                AimAt(aimPoint);
                if (CanFire(now))
                    Fire(now, true, aimPoint);
                return;
            }

            if (_state == State.Shooting)
            {
                _state = State.Suppressing;
                _blindShotsLeft = Random.Range(1, 4);
            }

            switch (_state)
            {
                case State.Suppressing:
                    AimAt(_lastAimPoint);
                    if (CanFire(now))
                    {
                        Fire(now, false, _lastAimPoint);
                        if (--_blindShotsLeft == 0 && !_player.IsDead)
                        {
                            _state = State.Searching;
                            _searchEndsAt = now + SearchTime;
                            _npc.BeginSearchAt(_lastSeenPosition, _lastSeenDirection);
                        }
                    }
                    break;
                case State.Searching:
                    RaiseWeapon(0f);
                    if (now < _searchEndsAt)
                        _npc.SearchForPlayer();
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
                    ListenForFootsteps(now);
                    _npc.Face(now < _listenUntil ? _heardPosition - transform.position : _initialFacing);
                    RaiseWeapon(0f);
                    break;
            }
        }

        bool CanFire(float now) => _reloadEndsAt <= 0f && now >= _fireAt;

        void AimAt(Vector3 aimPoint)
        {
            _npc.Face(aimPoint - transform.position);
            Vector3 fromShoulder = aimPoint - (_shoulder != null
                ? _shoulder.position : transform.position + Vector3.up * 1.5f);
            float pitch = Mathf.Atan2(fromShoulder.y, Vector3.ProjectOnPlane(fromShoulder, Vector3.up).magnitude) *
                Mathf.Rad2Deg;
            RaiseWeapon(_reloadEndsAt > 0f ? 48f : 90f + Mathf.Clamp(pitch, -25f, 25f));
        }

        void ListenForFootsteps(float now)
        {
            if (now < _nextFootstepAt ||
                (_playerMovement != null && _playerMovement.IsWalkingQuietly) ||
                _playerCollider == null || !_playerCollider.isGrounded ||
                Vector3.ProjectOnPlane(_playerCollider.velocity, Vector3.up).sqrMagnitude < 0.09f ||
                (_player.transform.position - transform.position).sqrMagnitude > FootstepRange * FootstepRange)
                return;

            _nextFootstepAt = now + FootstepInterval;
            HearNoise(_player.transform.position);
        }

        internal void HearNoise(Vector3 feet)
        {
            if (!isActiveAndEnabled || _player == null || _player.IsDead)
                return;

            _heardPosition = feet + Vector3.up * 0.95f;
            _listenUntil = Time.time + ListenTime;
        }

        void Fire(float now, bool visible, Vector3 aimPoint)
        {
            _fireAt = now + ShotInterval;
            _rounds--;
            if (_shotClip != null)
                _shotAudio.PlayOneShot(_shotClip, 0.8f);
            Flash(now);

            Vector3 muzzle = _effects.transform.position;
            Vector3 shoulder = _shoulder != null ? _shoulder.position : transform.position + Vector3.up * 1.5f;
            if (_npc.HasClearLine(shoulder, muzzle) && CanHitPlayer(muzzle, visible, aimPoint) &&
                Random.value < (visible ? VisibleHitChance : BlindHitChance))
            {
                _player.TakeAttackDamage(Damage);
                _player.AddBleeding(Bleeding);
            }

            if (_rounds == 0 && !_player.IsDead)
            {
                _reloadEndsAt = now + (_reloadClip != null ? _reloadClip.length : FallbackReloadTime);
                if (_reloadClip != null)
                    _reloadAudio.PlayOneShot(_reloadClip, 0.7f);
            }
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
            Vector3 eye = transform.position + Vector3.up * 1.55f;
            for (int i = 0; i < _targetPointCount; i++)
            {
                Vector3 point = _targetPoints[i];
                if (_npc.CanSeePlayerPoint(eye, point) && IsOnPlayerSideOfCover(point))
                {
                    target = point;
                    return true;
                }
            }

            target = _targetPoints[0];
            return false;
        }

        bool CanHitPlayer(Vector3 muzzle, bool visible, Vector3 aimPoint)
        {
            for (int i = 0; i < _targetPointCount; i++)
            {
                Vector3 point = _targetPoints[i];
                Vector3 offset = point - muzzle;
                if (offset.sqrMagnitude <= ShotRange * ShotRange &&
                    (visible || Vector3.Angle(aimPoint - muzzle, offset) <= BlindFireHalfAngle) &&
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

        void RaiseWeapon(float target)
        {
            if (_shoulder == null)
                return;

            _armAngle = Mathf.MoveTowards(_armAngle, target, 230f * Time.deltaTime);
            _shoulder.localRotation = _restRotation * Quaternion.Euler(-_armAngle, 0f, 0f);
        }

        AudioSource AddAudioSource()
        {
            var source = _effects.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.minDistance = 2f;
            source.maxDistance = 24f;
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

        internal void StopCombat()
        {
            _state = State.Idle;
            if (_npc != null)
                _npc.StopMoving();
            if (_flash != null)
                _flash.enabled = false;
            if (_shotAudio != null)
                _shotAudio.Stop();
            if (_reloadAudio != null)
                _reloadAudio.Stop();
        }

        void OnDisable() => StopCombat();
    }
}
