using System.Collections;
using CarX.Gameplay;
using StarterAssets;
using UnityEngine;
using UnityEngine.AI;

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

        enum State { Idle, Alerted, Returning, Watching, Dead }

        readonly RaycastHit[] _sightHits = new RaycastHit[12];
        NavMeshPath _combatPath;
        State _state;
        BanditPistol _bandit;
        CombatHumanoidAnimation _encounterAnimation;
        NavMeshAgent _agent;
        PlayerHealth _player;
        PlayerPistol _pistol;
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
        Quaternion _leftRest;
        Quaternion _rightRest;
        Transform _swingingArm;
        Quaternion _swingRest;
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

        float AttackRange => _hasKnife ? KnifeAttackRange : UnarmedAttackRange;
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

            float distance = Vector3.Distance(transform.position, playerFeet);
            float timeSinceContact = Time.time - _lastContactAt;
            bool remembersPlayer = timeSinceContact < AlertMemoryDuration &&
                (_state == State.Alerted || _state == State.Returning || _state == State.Watching);
            bool visible = CanSeePlayer(playerCenter, distance);
            if (visible)
            {
                SetState(State.Alerted);
                _lastKnownPosition = playerFeet;
                Vector3 velocity = _playerCollider != null ? _playerCollider.velocity : Vector3.zero;
                _lastSeenVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
                _lastContactAt = Time.time;
                _hadVisualContact = true;
            }
            else if (_state == State.Alerted)
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
                    else if (visible)
                    {
                        MoveTo(_lastKnownPosition, _chaseSpeed, 0.9f);
                        if (!FaceMovement())
                            Face(playerCenter - transform.position);
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
            if (elapsed >= AttackWindup && !_strikeDelivered)
            {
                _strikeDelivered = true;
                if (visible && distance <= AttackRange)
                {
                    _player.TakeAttackDamage(_hasKnife ? KnifeHitDamage : UnarmedHitDamage);
                    if (_hasKnife)
                        _player.AddBleeding(KnifeBleeding);
                }
            }
            if (elapsed >= AttackWindup + AttackRecovery)
            {
                _attacking = false;
                if (_swingingArm != null)
                    _swingingArm.localRotation = _swingRest;
                _swingingArm = null;
                return false;
            }

            if (visible)
                Face(playerCenter - transform.position);
            if (_swingingArm != null)
            {
                float angle = elapsed < 0.17f
                    ? Mathf.SmoothStep(0f, -32f, elapsed / 0.17f)
                    : elapsed < AttackWindup
                        ? Mathf.SmoothStep(-32f, 91f, (elapsed - 0.17f) / (AttackWindup - 0.17f))
                        : Mathf.SmoothStep(91f, 0f, (elapsed - AttackWindup) / AttackRecovery);
                _swingingArm.localRotation = _swingRest * Quaternion.Euler(-angle, 0f, 0f);
            }
            return true;
        }

        internal void TakeShot(RaycastHit hit, int bodyDamage, Vector3 shotOrigin)
        {
            if ((!isActiveAndEnabled && !_stagedDamageEnabled) || _state == State.Dead)
                return;

            bool headshot = _headHitbox != null && hit.collider == _headHitbox;
            _health = headshot ? 0 : Mathf.Max(0, _health - bodyDamage);
            if (_health > 0)
            {
                _encounterAnimation?.Flinch();
                _bandit?.ReactToHitInCover(shotOrigin, hit.point);
                return;
            }

            TakeFatalShot();
        }

        internal void TakeFatalShot()
        {
            if (!IsAlive)
                return;

            _health = 0;
            EnterDeadState();
            if (_encounterAnimation != null)
                _encounterAnimation.Fall(immediately: false);
            else
                StartCoroutine(Fall());
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

        internal void BeginEncounterCombat(Vector3 target, float? visibleHitChance, bool holdCover)
        {
            _stagedDamageEnabled = false;
            _home = transform.position;
            _homeFacing = transform.forward;
            _bandit.BeginEncounterCombat(target, visibleHitChance, holdCover);
            enabled = true;
        }

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
            if (_playerHud != null)
                _playerHud.UnregisterHostile(this);
            if (_playerMovement != null)
                _playerMovement.AudibleFootstep -= HearFootstep;
            if (_pistol != null)
                _pistol.ShotFired -= HearShot;
        }
    }
}
