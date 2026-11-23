using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CarX.Gameplay;
using StarterAssets;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10)]
    public sealed class HostileNpc : MonoBehaviour
    {
        const int MaxHealth = 30;
        const int UnarmedHitDamage = 10;
        const int KnifeHitDamage = 13;
        const int KnifeBleeding = 2;
        const float UnarmedAttackRange = 1.5f;
        const float ResidentPunchRange = 1.1f;
        const float KnifeAttackRange = 1.6f;
        const float AttackInterval = 1.2f;
        const float AttackWindup = 0.32f;
        const float AttackRecovery = 0.24f;
        const float ShotHearingDistance = 15f;
        const float BanditShotHearingDistance = 36f;
        const float FootstepHearingDistance = 6f;
        const float CloseDetectionDistance = 3f;
        const float SightDistance = 18f;
        const float SightHalfAngle = 55f;
        const float LostSightDelay = 7f;
        const float TrackInDarknessDuration = 3f;
        const float AllySupportRadius = 10f;
        const int AllySupportLimit = 2;
        const float AllySupportStoppingDistance = 2.6f;
        const float AlertMemoryDuration = 40f;
        const float RepathInterval = 0.18f;
        const float SearchStepLength = 3f;
        const float MinSearchStep = 0.45f;
        const float SearchStallDelay = 0.7f;
        static readonly float[] SearchTurnAngles = { 45f, 70f, 90f, 110f };
        static readonly float[] FiringPositionAngles = { 90f, -90f, 135f, -135f, 180f };

        [SerializeField, Min(0.1f)] float _chaseSpeed = 3.3f;
        [SerializeField] Transform _leftShoulder;
        [SerializeField] Transform _rightShoulder;
        [SerializeField] BoxCollider _bodyHitbox;
        [SerializeField] Collider _headHitbox;
        [SerializeField] GameObject _combatModel;
        [SerializeField] GameObject _combatPistol;
        [SerializeField] RuntimeAnimatorController _combatLocomotion;
        [SerializeField] GameObject _residentModel;
        [SerializeField] GameObject _alternateResidentModel;
        [SerializeField] GameObject _residentKnifeModel;
        [SerializeField] TextAsset _residentWalkAnimation;
        [SerializeField] TextAsset _residentAttackAnimation;
        [SerializeField] TextAsset _residentStandingAnimation;
        [SerializeField] bool _provokedByFlashlight;

        enum State { Idle, Alerted, Returning, Watching, Dead }

        readonly RaycastHit[] _sightHits = new RaycastHit[12];
        readonly List<HostileNpc> _calledAllies = new(AllySupportLimit);
        NavMeshPath _combatPath;
        State _state;
        BanditPistol _bandit;
        CombatHumanoidAnimation _encounterAnimation;
        NavMeshAgent _agent;
        PlayerHealth _player;
        PlayerPistol _pistol;
        Flashlight _playerFlashlight;
        ResidentModelPose _residentPose;
        static AnimationClip _humanoidDeathClip;
        PlayableGraph _deathGraph;
        bool _scriptedMovement;
        ResidentChaseVoice _chaseVoice;
        PlayerStatusHud _playerHud;
        FirstPersonController _playerMovement;
        CharacterController _playerCollider;
        TextMesh _alertMark;
        Vector3 _home;
        Vector3 _homeFacing;
        Vector3 _returnDirection;
        Vector3 _lastKnownPosition;
        Vector3 _lastSeenVelocity;
        Vector3 _searchDirection;
        Vector3 _searchProgressPosition;
        Vector3 _allySupportPoint;
        Quaternion _leftRest;
        Quaternion _rightRest;
        Transform _swingingArm;
        Quaternion _swingRest;
        bool _provoked;
        bool _assistingAlly;
        float _darknessStartedAt = -1f;
        int _health = MaxHealth;
        bool _hasKnife;
        bool _useLeftArm;
        float _lastContactAt;
        bool _hadVisualContact;
        bool _searchingBeyondLastKnown;
        bool _hasSearchStep;
        float _searchProgressAt;
        float _nextSearchStepAt;
        float _nextRepathAt;
        float _nextAttackAt;
        float _attackStartedAt;
        bool _attacking;
        bool _strikeDelivered;
        bool _stagedDamageEnabled;

        float AttackRange => _hasKnife ? KnifeAttackRange : _residentPose != null ? ResidentPunchRange : UnarmedAttackRange;
        float SwingWindup => _residentPose == null ? AttackWindup : _hasKnife ? 0.43f : 0.39f;
        float SwingRecovery => _residentPose == null ? AttackRecovery : _hasKnife ? 0.38f : 0.34f;
        internal bool IsEngaged => isActiveAndEnabled && _state != State.Dead &&
            (_bandit != null ? _bandit.IsEngaged : _state == State.Alerted);
        internal bool IsAlive => _state != State.Dead;
        internal Vector3 HeadPosition => _encounterAnimation != null ? _encounterAnimation.HeadPosition :
            _headHitbox is SphereCollider head ? head.transform.TransformPoint(head.center) :
            _headHitbox != null ? _headHitbox.bounds.center : transform.position + Vector3.up * 1.7f;

        void Awake()
        {
            _home = transform.position;
            _homeFacing = transform.forward;
            _bandit = GetComponent<BanditPistol>();
            if (_bandit != null)
                _health = BanditPistol.MaxHealth;
            if (_leftShoulder != null)
                _leftRest = _leftShoulder.localRotation;
            if (_rightShoulder != null)
            {
                _rightRest = _rightShoulder.localRotation;
                _hasKnife = _rightShoulder.Find("RightArm/KnifePlaceholder") != null;
            }
            _bandit?.ConfigureGrip(_leftShoulder, _leftRest, _rightShoulder, _rightRest);

            if (_bodyHitbox == null)
            {
                _bodyHitbox = gameObject.AddComponent<BoxCollider>();
                _bodyHitbox.center = new Vector3(0f, 0.9f, 0f);
                _bodyHitbox.size = new Vector3(0.60f, 1.8f, 0.42f);
            }
        }

        void Start()
        {
            if (_state == State.Dead)
                return;

            AttachCombatModel();
            AttachResidentModel();
            InitializeNavigation();

            foreach (var player in FindObjectsByType<PlayerHealth>())
            {
                if (player.gameObject.scene != gameObject.scene)
                    continue;

                _player = player;
                _playerCollider = player.GetComponent<CharacterController>();
                _playerHud = player.GetComponent<PlayerStatusHud>();
                if (_playerHud != null)
                    _playerHud.RegisterHostile(this);
                _playerMovement = player.GetComponent<FirstPersonController>();
                if (_playerMovement != null)
                    _playerMovement.AudibleFootstep += HearFootstep;
                _pistol = player.GetComponent<PlayerPistol>();
                if (_provokedByFlashlight)
                    _playerFlashlight = Flashlight.FindForPlayer(player.transform, _pistol?.ViewCamera);
                if (_pistol != null)
                    _pistol.ShotFired += HearShot;
                if (_bandit != null)
                    _bandit.Initialize(this, player, _playerCollider);
                break;
            }

            var mark = new GameObject("NPC Alert");
            mark.transform.SetParent(transform, false);
            mark.transform.localPosition = Vector3.up * 2.06f;
            _alertMark = mark.AddComponent<TextMesh>();
            _alertMark.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mark.GetComponent<MeshRenderer>().sharedMaterial = _alertMark.font.material;
            _alertMark.text = "!";
            _alertMark.anchor = TextAnchor.MiddleCenter;
            _alertMark.alignment = TextAlignment.Center;
            _alertMark.fontSize = 64;
            _alertMark.characterSize = 0.10f;
            _alertMark.fontStyle = FontStyle.Bold;
            _alertMark.color = new Color(1f, 0.42f, 0.12f);
            mark.SetActive(false);
        }

        void InitializeNavigation()
        {
            if (_scriptedMovement)
                return;

            var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            if (NavMesh.SamplePosition(_home, out var start, 1.5f, filter))
            {
                transform.position = _home = start.position;
                _agent = gameObject.AddComponent<NavMeshAgent>();
                _agent.speed = _chaseSpeed;
                _agent.radius = 0.38f;
                _agent.height = 1.8f;
                _agent.acceleration = 12f;
                _agent.updateRotation = false;
                _agent.autoTraverseOffMeshLink = false;
                _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                if (_bandit != null && _agent.isOnNavMesh)
                    _agent.isStopped = true;
            }
            else
            {
                Debug.LogWarning("NPC has no walkable NavMesh near its starting position.", this);
            }
        }

        void Update()
        {
            if (_state == State.Dead || Time.deltaTime <= 0f || _player == null)
                return;

            if (_player.IsDead)
            {
                StopMoving();
                if (_bandit != null)
                    _bandit.StopCombat();
                SetState(State.Idle);
                return;
            }

            Vector3 playerFeet = _player.transform.position;
            Vector3 playerCenter = _playerCollider != null
                ? _playerCollider.bounds.center : playerFeet + Vector3.up * 0.9f;
            if (_bandit != null)
            {
                if (_bandit.isActiveAndEnabled)
                    _bandit.Tick(playerCenter);
                return;
            }

            bool illuminated = _provokedByFlashlight && IsLitByFlashlight();
            if (illuminated)
                _chaseVoice ??= ResidentChaseVoice.ForPlayer(_player);
            if (_provokedByFlashlight && !TrackFlashlightTarget(illuminated))
            {
                if (_assistingAlly)
                    MoveToAssistAlly();
                return;
            }

            float distance = Vector3.Distance(transform.position, playerFeet);
            float timeSinceContact = Time.time - _lastContactAt;
            bool remembersPlayer = timeSinceContact < AlertMemoryDuration &&
                (_state == State.Alerted || _state == State.Returning || _state == State.Watching);
            bool visible = CanSeePlayer(playerCenter, distance);
            bool knowsPlayerPosition = illuminated || visible;
            if (knowsPlayerPosition)
            {
                _assistingAlly = false;
                SetState(State.Alerted);
                _lastKnownPosition = playerFeet;
                Vector3 velocity = _playerCollider != null ? _playerCollider.velocity : Vector3.zero;
                _lastSeenVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
                _lastContactAt = Time.time;
                _hadVisualContact = true;
            }
            else if (_state == State.Alerted && !_assistingAlly)
            {
                if (_hadVisualContact)
                {
                    _hadVisualContact = false;
                    BeginSearch(_lastSeenVelocity);
                }
                if (Time.time - _lastContactAt >= LostSightDelay)
                    SetState(State.Returning);
            }

            if (UpdateSwing(distance, visible, playerCenter))
                return;

            switch (_state)
            {
                case State.Alerted:
                    if (visible && distance <= AttackRange)
                    {
                        StopMoving();
                        Face(playerCenter - transform.position);
                        if (Time.time >= _nextAttackAt)
                            BeginSwing();
                    }
                    else if (knowsPlayerPosition)
                    {
                        if (distance > AttackRange)
                            _chaseVoice?.TryShout(transform);
                        MoveTo(_lastKnownPosition, _chaseSpeed, 0.9f);
                        if (!FaceMovement())
                            Face(playerCenter - transform.position);
                    }
                    else if (_assistingAlly)
                    {
                        MoveToAssistAlly();
                    }
                    else
                    {
                        SearchForPlayer();
                    }
                    break;
                case State.Returning:
                    MoveTo(_home, _chaseSpeed * 0.65f, 0.1f);
                    if (FaceMovement())
                        _returnDirection = _agent.desiredVelocity;
                    if (HasArrived())
                    {
                        if (_returnDirection.sqrMagnitude > 0f)
                            _homeFacing = -_returnDirection;
                        StopMoving();
                        SetState(remembersPlayer ? State.Watching : State.Idle);
                    }
                    break;
                case State.Watching:
                    Face(_homeFacing);
                    if (!remembersPlayer)
                        SetState(State.Idle);
                    break;
                case State.Idle:
                    Face(_homeFacing);
                    break;
            }
        }

        void LateUpdate()
        {
            if (_residentPose != null && _state != State.Dead)
            {
                float speed = _agent != null && _agent.isOnNavMesh ? _agent.velocity.magnitude : 0f;
                _residentPose.PoseMelee(speed, _attacking ? Time.time - _attackStartedAt : -1f,
                    SwingWindup, SwingRecovery, _useLeftArm, _state == State.Alerted && !_assistingAlly);
            }
            if (_alertMark == null)
                return;

            bool showAlert = (_state == State.Alerted || _state == State.Returning || _state == State.Watching) &&
                Time.time - _lastContactAt < AlertMemoryDuration;
            if (_bandit != null)
                showAlert = _state != State.Dead && _bandit.IsEngaged;
            Camera playerCamera = _pistol != null ? _pistol.ViewCamera : null;
            showAlert = showAlert && playerCamera != null && IsVisibleToPlayer(playerCamera);
            if (_alertMark.gameObject.activeSelf != showAlert)
                _alertMark.gameObject.SetActive(showAlert);

            if (showAlert)
                _alertMark.transform.rotation = playerCamera.transform.rotation;
        }

        bool IsVisibleToPlayer(Camera camera)
        {
            Bounds bounds = _bodyHitbox.bounds;
            Vector3 upperBodyOffset = Vector3.up * (bounds.extents.y * 0.72f);
            return IsPointVisible(bounds.center) || IsPointVisible(bounds.center + upperBodyOffset) ||
                IsPointVisible(bounds.center - upperBodyOffset) ||
                (_headHitbox != null && IsPointVisible(_headHitbox.bounds.center));

            bool IsPointVisible(Vector3 point)
            {
                Vector3 viewport = camera.WorldToViewportPoint(point);
                return viewport.z >= camera.nearClipPlane && viewport.z <= camera.farClipPlane &&
                    viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f &&
                    HasClearLine(camera.transform.position, point);
            }
        }

        bool CanSeePlayer(Vector3 playerCenter, float distance)
        {
            Vector3 eye = transform.position + Vector3.up * 1.55f;
            if (distance > CloseDetectionDistance && !IsInSightCone(playerCenter - eye))
                return false;

            float height = _playerCollider != null ? _playerCollider.bounds.extents.y * 0.65f : 0.5f;
            Vector3 offset = Vector3.up * height;
            return HasClearLine(eye, playerCenter) || HasClearLine(eye, playerCenter + offset) ||
                HasClearLine(eye, playerCenter - offset);
        }

        bool TrackFlashlightTarget(bool illuminated)
        {
            if (!_provoked)
            {
                if (!illuminated)
                    return false;
                _provoked = true;
            }

            if (_playerFlashlight != null && _playerFlashlight.isActiveAndEnabled && _playerFlashlight.IsSwitchedOn)
            {
                _darknessStartedAt = -1f;
                return true;
            }

            if (_darknessStartedAt < 0f)
                _darknessStartedAt = Time.time;
            if (Time.time - _darknessStartedAt < TrackInDarknessDuration)
                return true;

            _provoked = false;
            _chaseVoice = null;
            _darknessStartedAt = -1f;
            _hadVisualContact = false;
            _lastContactAt = float.NegativeInfinity;
            _lastKnownPosition = Vector3.zero;
            _lastSeenVelocity = Vector3.zero;
            _searchDirection = Vector3.zero;
            _hasSearchStep = false;
            _searchingBeyondLastKnown = false;
            EndSwing();
            SetState(State.Idle);
            return false;
        }

        bool IsLitByFlashlight()
        {
            if (_playerFlashlight == null || !_playerFlashlight.isActiveAndEnabled || !_playerFlashlight.IsSwitchedOn)
                return false;

            Transform lightSource = _playerFlashlight.transform;
            Vector3 origin = lightSource.position;
            Vector3 forward = lightSource.forward;
            float range = _playerFlashlight.WorldLightRange;
            return IsHitboxLit(_bodyHitbox, origin, forward, range) || IsHitboxLit(_headHitbox, origin, forward, range);
        }

        bool IsHitboxLit(Collider hitbox, Vector3 origin, Vector3 forward, float range)
        {
            if (hitbox == null || !hitbox.enabled)
                return false;

            Bounds bounds = hitbox.bounds;
            if ((bounds.ClosestPoint(origin) - origin).sqrMagnitude > range * range)
                return false;

            if (IsLitPoint(origin, bounds.center))
                return true;

            // Sample the surface nearest the beam axis across the hitbox's depth for glancing hits.
            Vector3 absoluteForward = new Vector3(Mathf.Abs(forward.x), Mathf.Abs(forward.y), Mathf.Abs(forward.z));
            float depth = Vector3.Dot(forward, bounds.center - origin);
            float halfDepth = Vector3.Dot(absoluteForward, bounds.extents);
            float start = Mathf.Max(0f, depth - halfDepth);
            float end = Mathf.Min(range, depth + halfDepth);
            if (end <= start)
                return false;

            const int depthIntervals = 4;
            for (int i = 0; i <= depthIntervals; i++)
            {
                Vector3 axisPoint = origin + forward * Mathf.Lerp(start, end, i / (float)depthIntervals);
                if (IsLitPoint(origin, hitbox.ClosestPoint(axisPoint)))
                    return true;
            }

            // The nearest surface can be hidden by cover while another side catches the light.
            if (hitbox is BoxCollider box)
            {
                Vector3 halfSize = box.size * 0.5f;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3((corner & 1) == 0 ? -halfSize.x : halfSize.x,
                        (corner & 2) == 0 ? -halfSize.y : halfSize.y,
                        (corner & 4) == 0 ? -halfSize.z : halfSize.z);
                    if (IsLitPoint(origin, box.transform.TransformPoint(box.center + offset)))
                        return true;
                }
            }

            return false;
        }

        bool IsLitPoint(Vector3 origin, Vector3 point) => _playerFlashlight.IlluminatesPoint(point) &&
            HasClearLine(origin, point);

        internal bool CanSeePlayerPoint(Vector3 eye, Vector3 point, float sightDistance) =>
            IsInSightCone(point - eye, sightDistance) && HasClearLine(eye, point);

        bool IsInSightCone(Vector3 offset, float sightDistance = SightDistance) => offset.sqrMagnitude <= sightDistance * sightDistance &&
            Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(offset, Vector3.up)) <= SightHalfAngle;

        void BeginSearch(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                direction = Vector3.ProjectOnPlane(_lastKnownPosition - transform.position, Vector3.up);
            _searchDirection = direction.sqrMagnitude >= 0.01f ? direction.normalized : transform.forward;
            _searchingBeyondLastKnown = false;
            _hasSearchStep = false;
            _searchProgressPosition = transform.position;
            _searchProgressAt = Time.time;
            _nextSearchStepAt = Time.time;
            _nextRepathAt = 0f;
            MoveTo(_lastKnownPosition, _chaseSpeed, 0.16f);
        }

        void SearchForPlayer()
        {
            if (_agent == null || !_agent.isOnNavMesh)
            {
                Face(_lastKnownPosition - transform.position);
                return;
            }

            if ((transform.position - _searchProgressPosition).sqrMagnitude >= 0.04f)
            {
                _searchProgressPosition = transform.position;
                _searchProgressAt = Time.time;
            }
            bool stalled = Time.time - _searchProgressAt >= SearchStallDelay;
            bool reachedPathEnd = !_agent.pathPending &&
                _agent.remainingDistance <= _agent.stoppingDistance + 0.1f;

            if (!_searchingBeyondLastKnown)
            {
                if (!reachedPathEnd && !stalled)
                {
                    MoveTo(_lastKnownPosition, _chaseSpeed, 0.16f);
                    if (!FaceMovement())
                        Face(_lastKnownPosition - transform.position);
                    return;
                }
                _searchingBeyondLastKnown = true;
            }

            if (_hasSearchStep && !reachedPathEnd && !stalled)
            {
                FaceMovement();
                return;
            }

            if (Time.time < _nextSearchStepAt)
                return;

            _nextSearchStepAt = Time.time + RepathInterval;
            var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
            Vector3 start = _agent.nextPosition;
            if (!TrySearchStep(start, _searchDirection, filter, out var destination, stalled))
            {
                bool left = TrySearchTurn(start, _searchDirection, -1f, filter, out var leftDestination);
                bool right = TrySearchTurn(start, _searchDirection, 1f, filter, out var rightDestination);
                if (!left && !right)
                {
                    _hasSearchStep = false;
                    StopMoving();
                    Face(_searchDirection);
                    return;
                }

                destination = left && (!right || Random.value < 0.5f)
                    ? leftDestination : rightDestination;
            }

            _searchDirection = Vector3.ProjectOnPlane(destination - start, Vector3.up).normalized;
            _agent.speed = _chaseSpeed;
            _agent.stoppingDistance = 0.12f;
            _agent.isStopped = false;
            _hasSearchStep = _agent.SetDestination(destination);
            _searchProgressPosition = transform.position;
            _searchProgressAt = Time.time;
            Face(_searchDirection);
        }

        bool TrySearchTurn(Vector3 start, Vector3 forward, float side, NavMeshQueryFilter filter, out Vector3 destination)
        {
            foreach (float angle in SearchTurnAngles)
            {
                Vector3 direction = Quaternion.AngleAxis(angle * side, Vector3.up) * forward;
                if (TrySearchStep(start, direction, filter, out destination))
                    return true;
            }

            destination = default;
            return false;
        }

        internal bool TryRepositionForShot(Vector3 aimPoint, System.Predicate<Vector3> canShootFrom,
            float stepLength = SearchStepLength)
        {
            if (_agent == null || !_agent.isOnNavMesh)
                return false;

            var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
            Vector3 start = _agent.nextPosition;
            Vector3 direction = Vector3.ProjectOnPlane(aimPoint - start, Vector3.up).normalized;
            if (direction == Vector3.zero)
                direction = transform.forward;

            foreach (float angle in FiringPositionAngles)
            {
                Vector3 step = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                if (TrySearchStep(start, step, filter, out var destination, accept: CanFireFromDistance,
                    stepLength: stepLength) &&
                    TryMoveToFiringPosition(destination))
                    return true;
            }

            return false;

            bool CanFireFromDistance(Vector3 destination) =>
                Vector3.Dot(destination - start, direction) <= 0.15f && canShootFrom(destination);
        }

        internal bool TryGetCoverRoute(Vector3 hide, Vector3 peek, out Vector3 hidePosition, out Vector3 peekPosition)
        {
            hidePosition = peekPosition = default;
            if (!TryGetReachablePosition(hide, 0.45f, out var walkableHide))
                return false;

            var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
            if (!NavMesh.SamplePosition(peek, out var peekHit, 0.45f, filter) ||
                NavMesh.Raycast(walkableHide, peekHit.position, out _, filter))
                return false;

            hidePosition = walkableHide;
            peekPosition = peekHit.position;
            return true;
        }

        internal bool TryGetReachablePosition(Vector3 near, float radius, out Vector3 position)
        {
            position = default;
            if (_agent == null || !_agent.isOnNavMesh)
                return false;

            var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
            if (!NavMesh.SamplePosition(near, out var hit, radius, filter))
                return false;

            _combatPath ??= new NavMeshPath();
            if (!_agent.CalculatePath(hit.position, _combatPath) || _combatPath.status != NavMeshPathStatus.PathComplete)
                return false;

            position = hit.position;
            return true;
        }

        internal bool TryRetreatFrom(Vector3 threat, out Vector3 destination)
        {
            destination = default;
            if (_agent == null || !_agent.isOnNavMesh)
                return false;

            var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
            Vector3 start = _agent.nextPosition;
            Vector3 away = Vector3.ProjectOnPlane(start - threat, Vector3.up).normalized;
            if (away == Vector3.zero)
                away = -transform.forward;

            if (TrySearchStep(start, away, filter, out destination) ||
                TrySearchTurn(start, away, -1f, filter, out destination) ||
                TrySearchTurn(start, away, 1f, filter, out destination))
                return TryMoveToFiringPosition(destination);

            return false;
        }

        internal bool TryMoveToFiringPosition(Vector3 position, float speedScale = 1f)
        {
            if (_agent == null || !_agent.isOnNavMesh || !_agent.SetDestination(position))
                return false;

            _agent.speed = _chaseSpeed * speedScale;
            _agent.stoppingDistance = 0.12f;
            _agent.isStopped = false;
            return true;
        }

        internal bool TryAdvanceOnPlayer(Vector3 position, float stoppingDistance)
        {
            if (_agent == null || !_agent.isOnNavMesh)
                return false;

            MoveTo(position, _chaseSpeed, stoppingDistance);
            return !_agent.isStopped && (_agent.pathPending ||
                (_agent.hasPath && _agent.pathStatus == NavMeshPathStatus.PathComplete && !HasArrived()));
        }

        bool TrySearchStep(Vector3 start, Vector3 direction, NavMeshQueryFilter filter,
            out Vector3 destination, bool skipDirection = false, System.Predicate<Vector3> accept = null,
            float stepLength = SearchStepLength)
        {
            destination = default;
            if (skipDirection)
                return false;

            Vector3 ahead = start + direction * stepLength;
            if (NavMesh.Raycast(start, ahead, out var edge, filter))
            {
                float clearance = Vector3.Dot(edge.position - start, direction) - 0.12f;
                ahead = start + direction * Mathf.Max(0f, clearance);
            }

            Vector3 chest = Vector3.up * 0.75f;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (Vector3.Dot(ahead - start, direction) < MinSearchStep)
                    break;
                if (NavMesh.SamplePosition(ahead, out var end, 0.7f, filter) &&
                    Vector3.Dot(end.position - start, direction) >= MinSearchStep &&
                    !NavMesh.Raycast(start, end.position, out _, filter) &&
                    HasClearLine(start + chest, end.position + chest) &&
                    (accept == null || accept(end.position)))
                {
                    destination = end.position;
                    return true;
                }
                ahead = Vector3.Lerp(start, ahead, 0.5f);
            }

            return false;
        }

        internal bool HasClearLine(Vector3 from, Vector3 to)
        {
            Vector3 offset = to - from;
            float distance = offset.magnitude;
            if (distance < 0.001f)
                return true;

            var ray = new Ray(from, offset / distance);
            int count = Physics.RaycastNonAlloc(ray, _sightHits, distance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var hits = _sightHits;
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(ray, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            for (int i = 0; i < count; i++)
            {
                Transform obstacle = hits[i].collider.transform;
                if (!obstacle.IsChildOf(transform) && !obstacle.IsChildOf(_player.transform))
                    return false;
            }
            return true;
        }

        void HearFootstep(Vector3 position, Vector3 velocity)
        {
            float range = FootstepHearingDistance;
            if (_bandit != null && velocity.sqrMagnitude >= BanditPistol.LoudStepSpeed * BanditPistol.LoudStepSpeed)
                range = velocity.sqrMagnitude >= 25f ? 18f : 13f;
            HearNoise(position, range, footstepVelocity: velocity);
        }

        void HearShot(Vector3 position) =>
            HearNoise(position, _bandit != null ? BanditShotHearingDistance : ShotHearingDistance, isShot: true);

        void HearNoise(Vector3 position, float range, bool isShot = false, Vector3 footstepVelocity = default)
        {
            if (!isActiveAndEnabled || _state == State.Dead || _player == null || _player.IsDead ||
                (_provokedByFlashlight && (!_provoked || _assistingAlly)) ||
                (transform.position - position).sqrMagnitude > range * range)
                return;

            if (_bandit != null)
            {
                if (isShot)
                    _bandit.HearShot(position);
                else
                    _bandit.HearNoise(position, footstepVelocity);
                return;
            }

            _lastKnownPosition = position;
            _lastContactAt = Time.time;
            _hadVisualContact = false;
            BeginSearch(position - transform.position);
            SetState(State.Alerted);
        }

        void MoveTo(Vector3 destination, float speed, float stoppingDistance)
        {
            if (_agent == null || !_agent.isOnNavMesh)
                return;

            _agent.speed = speed;
            _agent.stoppingDistance = stoppingDistance;
            if (Time.time < _nextRepathAt)
                return;

            _nextRepathAt = Time.time + RepathInterval;
            var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
            if (!NavMesh.SamplePosition(destination, out var target, 1f, filter))
            {
                StopMoving();
                return;
            }
            _agent.isStopped = false;
            if (!_agent.hasPath || (_agent.destination - target.position).sqrMagnitude > 0.04f)
                _agent.SetDestination(target.position);
        }

        bool HasArrived()
        {
            return _agent != null && _agent.isOnNavMesh && !_agent.isStopped && !_agent.pathPending &&
                _agent.pathStatus == NavMeshPathStatus.PathComplete &&
                _agent.remainingDistance <= _agent.stoppingDistance + 0.08f;
        }

        internal bool ReturnHome()
        {
            if (_agent == null || !_agent.isOnNavMesh)
                return true;

            MoveTo(_home, _chaseSpeed * 0.65f, 0.1f);
            if (!HasArrived())
            {
                FaceMovement();
                return false;
            }

            StopMoving();
            return true;
        }

        bool FaceMovement()
        {
            return _agent != null && _agent.isOnNavMesh && Face(_agent.desiredVelocity);
        }

        internal bool Face(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0025f)
                return false;

            TurnTowards(Quaternion.LookRotation(direction), 420f);
            return true;
        }

        void TurnTowards(Quaternion facing, float turnSpeed)
        {
            float dt = Time.deltaTime;
            float ease = 1f - Mathf.Exp(-10f * dt);
            Quaternion eased = Quaternion.Slerp(transform.rotation, facing, ease);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, eased, turnSpeed * dt);
        }

        internal void StopMoving()
        {
            if (_agent == null || !_agent.isOnNavMesh || _agent.isStopped)
                return;

            _agent.isStopped = true;
            _agent.ResetPath();
            _nextRepathAt = 0f;
        }

        void SetState(State state)
        {
            if (_state == state)
                return;

            _state = state;
            _nextRepathAt = 0f;
            if (state == State.Returning)
                _returnDirection = Vector3.zero;
            if (state == State.Idle)
                StopMoving();
        }

        void BeginSwing()
        {
            _attacking = true;
            _attackStartedAt = Time.time;
            _nextAttackAt = Time.time + AttackInterval;
            _strikeDelivered = false;
            _useLeftArm = !_hasKnife && !_useLeftArm;
            _swingingArm = _useLeftArm ? _leftShoulder : _rightShoulder;
            _swingRest = _useLeftArm ? _leftRest : _rightRest;
        }

        bool UpdateSwing(float distance, bool visible, Vector3 playerCenter)
        {
            if (!_attacking)
                return false;

            float elapsed = Time.time - _attackStartedAt;
            float windup = SwingWindup;
            float recovery = SwingRecovery;
            if (elapsed >= windup && !_strikeDelivered)
            {
                _strikeDelivered = true;
                if (visible && distance <= AttackRange)
                {
                    _player.TakeAttackDamage(_hasKnife ? KnifeHitDamage : UnarmedHitDamage);
                    if (_hasKnife)
                        _player.AddBleeding(KnifeBleeding);
                }
            }
            if (elapsed >= windup + recovery)
            {
                EndSwing();
                return false;
            }

            if (visible)
                Face(playerCenter - transform.position);
            if (_swingingArm != null)
            {
                float angle = elapsed < 0.17f
                    ? Mathf.SmoothStep(0f, -32f, elapsed / 0.17f)
                    : elapsed < windup
                        ? Mathf.SmoothStep(-32f, 91f, (elapsed - 0.17f) / (windup - 0.17f))
                        : Mathf.SmoothStep(91f, 0f, (elapsed - windup) / recovery);
                _swingingArm.localRotation = _swingRest * Quaternion.Euler(-angle, 0f, 0f);
            }
            return true;
        }

        void EndSwing()
        {
            _attacking = false;
            if (_swingingArm != null)
                _swingingArm.localRotation = _swingRest;
            _swingingArm = null;
        }

        internal void TakeShot(RaycastHit hit, int bodyDamage, Vector3 shotOrigin)
        {
            if ((!isActiveAndEnabled && !_stagedDamageEnabled) || _state == State.Dead)
                return;

            if (_provokedByFlashlight && _player != null && !_player.IsDead)
                CallNearbyAllies(shotOrigin);

            bool headshot = _headHitbox != null && hit.collider == _headHitbox;
            _health = headshot ? 0 : Mathf.Max(0, _health - bodyDamage);
            if (_health > 0)
            {
                _encounterAnimation?.Flinch();
                _bandit?.ReactToHitInCover(shotOrigin, hit.point);
                return;
            }

            TakeFatalShot(shotOrigin);
        }

        void CallNearbyAllies(Vector3 shotOrigin)
        {
            Vector3 position = transform.position;
            int availableSlots = AllySupportLimit - _calledAllies.Count;
            if (availableSlots > 0)
            {
                var nearbyAllies = FindObjectsByType<HostileNpc>()
                    .Where(ally => ally != this && ally._provokedByFlashlight && ally.isActiveAndEnabled && ally.IsAlive &&
                        ally.gameObject.scene == gameObject.scene && !_calledAllies.Contains(ally) &&
                        !ally._assistingAlly && !(ally._provoked && ally._state == State.Alerted && ally._hadVisualContact))
                    .Select(ally => (ally, distanceSquared: (ally.transform.position - shotOrigin).sqrMagnitude))
                    .Where(candidate => candidate.distanceSquared <= AllySupportRadius * AllySupportRadius)
                    .OrderBy(candidate => candidate.distanceSquared)
                    .Take(availableSlots);

                foreach (var candidate in nearbyAllies)
                {
                    candidate.ally.AssistAllyAt(position);
                    _calledAllies.Add(candidate.ally);
                }
            }

            _provoked = true;
            _assistingAlly = false;
            _lastContactAt = Time.time;
            _lastKnownPosition = _player.transform.position;
            _hadVisualContact = false;
            SetState(State.Alerted);
            BeginSearch(_lastKnownPosition - position);
        }

        void AssistAllyAt(Vector3 position)
        {
            _provoked = true;
            _assistingAlly = true;
            _allySupportPoint = position;
            _lastContactAt = Time.time;
            StopMoving();
            SetState(State.Alerted);
        }

        void MoveToAssistAlly()
        {
            bool hasAgent = _agent != null && _agent.isOnNavMesh;
            bool reachedRouteEnd = hasAgent && !_agent.pathPending && _agent.hasPath &&
                _agent.remainingDistance <= _agent.stoppingDistance + 0.08f;
            if (!hasAgent || reachedRouteEnd)
            {
                _assistingAlly = false;
                StopMoving();
                if (reachedRouteEnd)
                    Face(_allySupportPoint - transform.position);
                if (_provoked)
                    SetState(State.Watching);
                return;
            }

            MoveTo(_allySupportPoint, _chaseSpeed, AllySupportStoppingDistance);
            FaceMovement();
        }

        internal void TakeFatalShot(Vector3? shotOrigin = null)
        {
            if (!IsAlive)
                return;

            _health = 0;
            EnterDeadState();
            if (_encounterAnimation != null)
                _encounterAnimation.Fall(immediately: false, shotOrigin: shotOrigin);
            else if (!TryPlayHumanoidDeath())
                StartCoroutine(Fall());
        }

        bool TryPlayHumanoidDeath()
        {
            var animator = _residentPose != null ? _residentPose.GetComponentInChildren<Animator>() : null;
            if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
                return false;

            _humanoidDeathClip ??= Resources.LoadAll<AnimationClip>("CarX/NpcDeathFromRight")
                .FirstOrDefault(clip => clip.humanMotion && clip.name == "NpcDeathFromRight");
            if (_humanoidDeathClip == null)
                return false;

            _residentPose.enabled = false;
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _deathGraph = PlayableGraph.Create("NPC Death");
            _deathGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var motion = AnimationClipPlayable.Create(_deathGraph, _humanoidDeathClip);
            motion.SetApplyFootIK(false);
            var output = AnimationPlayableOutput.Create(_deathGraph, "Death Animation", animator);
            output.SetSourcePlayable(motion);
            _deathGraph.Play();
            StartCoroutine(PlayDeathMotion(motion, _humanoidDeathClip.length));
            return true;
        }

        IEnumerator PlayDeathMotion(AnimationClipPlayable motion, float duration)
        {
            float elapsed = 0f;
            while (_deathGraph.IsValid())
            {
                motion.SetTime(elapsed);
                _deathGraph.Evaluate(0f);
                if (elapsed >= duration)
                    yield break;
                yield return null;
                elapsed = Mathf.Min(duration, elapsed + Time.deltaTime);
            }
        }

        void EnterDeadState()
        {
            StopMoving();
            SetState(State.Dead);
            if (TryGetComponent<NpcSpeechState>(out var speech))
                speech.SetTalking(false);
            if (_bandit != null)
                _bandit.StopCombat();
            if (_agent != null)
                _agent.enabled = false;
            _bodyHitbox.enabled = false;
            if (_headHitbox != null)
                _headHitbox.enabled = false;
            if (_bandit == null)
            {
                if (_leftShoulder != null)
                    _leftShoulder.localRotation = _leftRest;
                if (_rightShoulder != null)
                    _rightShoulder.localRotation = _rightRest;
            }
        }

        internal void LeaveBodyForEncounter()
        {
            if (_state == State.Dead)
                return;

            _health = 0;
            EnterDeadState();
            if (_encounterAnimation != null)
                _encounterAnimation.Fall(immediately: true);
            else
                SetFallenPose(transform.position, transform.rotation, 90f);
        }

        internal void StageForEncounter(Color headColor, Material fallbackMaterial)
        {
            enabled = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer.sharedMaterial == null)
                    renderer.sharedMaterial = fallbackMaterial;
            if (_headHitbox == null || !_headHitbox.TryGetComponent<Renderer>(out var head))
                return;

            if (fallbackMaterial != null)
                head.sharedMaterial = fallbackMaterial;
            var properties = new MaterialPropertyBlock();
            head.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", headColor);
            properties.SetColor("_Color", headColor);
            head.SetPropertyBlock(properties);
        }

        internal void UseScriptedMovement()
        {
            _scriptedMovement = true;
            if (_agent != null)
                _agent.enabled = false;
        }

        internal void AttachCombatModel(GameObject modelPrefab = null, GameObject pistolPrefab = null,
            RuntimeAnimatorController locomotion = null)
        {
            if (_encounterAnimation != null || _bandit == null)
                return;

            modelPrefab ??= _combatModel;
            pistolPrefab ??= _combatPistol;
            locomotion ??= _combatLocomotion;
            if (modelPrefab == null || pistolPrefab == null || locomotion == null)
                return;

            var placeholders = GetComponentsInChildren<Renderer>(true);
            var model = Instantiate(modelPrefab, transform, false);
            var animation = model.AddComponent<CombatHumanoidAnimation>();
            if (!animation.Bind(transform, _headHitbox as SphereCollider, pistolPrefab, locomotion))
            {
                Debug.LogWarning($"{name} requires a humanoid model and a pistol muzzle.", this);
                Destroy(model);
                return;
            }

            foreach (var placeholder in placeholders)
                placeholder.enabled = false;
            _encounterAnimation = animation;
            _bandit.AttachStagedModel(animation);
            animation.SetWeaponVisible(true);
        }

        void AttachResidentModel()
        {
            if (_residentModel == null || _bandit != null)
                return;

            Transform knife = _hasKnife ? _rightShoulder.Find("RightArm/KnifePlaceholder") : null;
            var placeholders = GetComponentsInChildren<Renderer>(true);
            var modelPrefab = _alternateResidentModel != null && Random.value < 0.5f
                ? _alternateResidentModel : _residentModel;
            var model = Instantiate(modelPrefab, transform, false);
            _residentPose = model.AddComponent<ResidentModelPose>();
            foreach (var placeholder in placeholders)
                if (knife == null || placeholder.transform != knife)
                    placeholder.enabled = false;
            _residentPose.EnableMelee(knife, _residentKnifeModel);
            _residentPose.SetMeleeAnimations(_residentWalkAnimation, _residentAttackAnimation, _residentStandingAnimation);

            if (_headHitbox == null)
            {
                var head = gameObject.AddComponent<SphereCollider>();
                head.center = Vector3.up * 1.68f;
                head.radius = 0.14f;
                _headHitbox = head;
                _bodyHitbox.center = Vector3.up * 0.77f;
                _bodyHitbox.size = new Vector3(0.60f, 1.54f, 0.42f);
            }
        }

        internal void SetStagedWeaponVisible(bool visible) => _bandit.SetStagedWeaponVisible(visible);

        internal CombatHumanoidAnimation EncounterAnimation => _encounterAnimation;

        internal void SetStagedDamageEnabled(bool enabled) => _stagedDamageEnabled = enabled;

        internal void PlayStagedShot(bool playAudio = true) => _bandit.PlayStagedShot(playAudio);

        internal void ReactToStagedShot() => _encounterAnimation?.Flinch();

        internal void LowerStagedWeapon()
        {
            if (_bandit != null)
                _bandit.LowerStagedWeapon();
            else if (_rightShoulder != null)
                _rightShoulder.localRotation = _rightRest;
        }

        internal void AimForEncounter(Vector3 target, bool smooth = false,
            float raiseDuration = CombatHumanoidAnimation.DefaultAimRaiseTime)
        {
            FaceForEncounter(target, smooth);
            _bandit?.AimStagedWeapon(target, smooth, raiseDuration);
        }

        internal void SpeakAtForEncounter(Vector3 target)
        {
            FaceForEncounter(target, smooth: true);
            _bandit?.LowerStagedWeapon(smooth: true, relaxed: true);
        }

        internal void FaceForEncounter(Vector3 target, bool smooth = false)
        {
            Vector3 offset = target - transform.position;
            Vector3 direction = Vector3.ProjectOnPlane(offset, Vector3.up);
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion aim = Quaternion.LookRotation(direction);
                if (smooth)
                    TurnTowards(aim, 360f);
                else
                    transform.rotation = aim;
            }
        }

        internal void BeginEncounterCombat(Vector3 target, float? visibleHitChance, bool holdCover,
            float openingCoverSeconds = 0f)
        {
            _stagedDamageEnabled = false;
            _home = transform.position;
            _homeFacing = transform.forward;
            _bandit.BeginEncounterCombat(target, visibleHitChance, holdCover, openingCoverSeconds);
            enabled = true;
        }

        internal void EndOpeningCover() => _bandit.EndOpeningCover();

        IEnumerator Fall()
        {
            const float duration = 0.4f;
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                SetFallenPose(startPosition, startRotation, Mathf.SmoothStep(0f, 90f, elapsed / duration));
                yield return null;
            }
            SetFallenPose(startPosition, startRotation, 90f);
        }

        void SetFallenPose(Vector3 startPosition, Quaternion startRotation, float angle)
        {
            transform.SetPositionAndRotation(
                startPosition + Vector3.up * (0.205f * Mathf.Sin(angle * Mathf.Deg2Rad)),
                startRotation * Quaternion.Euler(angle, 0f, 0f));
        }

        void OnDestroy()
        {
            if (_deathGraph.IsValid())
                _deathGraph.Destroy();
            if (_playerHud != null)
                _playerHud.UnregisterHostile(this);
            if (_playerMovement != null)
                _playerMovement.AudibleFootstep -= HearFootstep;
            if (_pistol != null)
                _pistol.ShotFired -= HearShot;
        }
    }
}
