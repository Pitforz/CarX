using System.Collections;
using CarX.Gameplay;
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
        const float CloseDetectionDistance = 3f;
        const float SightDistance = 18f;
        const float SightHalfAngle = 55f;
        const float LightFringeWidth = 0.85f;
        const float VisibleLightLevel = 0.12f;
        const float LostSightDelay = 7f;
        const float AlertMemoryDuration = 40f;
        const float RepathInterval = 0.18f;
        const float SearchStepLength = 3f;
        const float MinSearchStep = 0.45f;
        const float SearchStallDelay = 0.7f;
        static readonly float[] SearchTurnAngles = { 45f, 70f, 90f, 110f };

        [SerializeField, Min(0.1f)] float _chaseSpeed = 3.3f;
        [SerializeField] Transform _leftShoulder;
        [SerializeField] Transform _rightShoulder;

        enum State { Idle, Alerted, Returning, Watching, Dead }

        readonly RaycastHit[] _sightHits = new RaycastHit[12];
        State _state;
        BanditPistol _bandit;
        NavMeshAgent _agent;
        BoxCollider _hitbox;
        PlayerHealth _player;
        PlayerPistol _pistol;
        CharacterController _playerCollider;
        Flashlight[] _playerFlashlights;
        Light[] _sceneLights;
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

        float AttackRange => _hasKnife ? KnifeAttackRange : UnarmedAttackRange;

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

            _hitbox = gameObject.AddComponent<BoxCollider>();
            _hitbox.center = new Vector3(0f, 0.9f, 0f);
            _hitbox.size = new Vector3(0.60f, 1.8f, 0.42f);
        }

        void Start()
        {
            InitializeNavigation();

            foreach (var player in FindObjectsByType<PlayerHealth>())
            {
                if (player.gameObject.scene != gameObject.scene)
                    continue;

                _player = player;
                _playerCollider = player.GetComponent<CharacterController>();
                _playerFlashlights = player.GetComponentsInChildren<Flashlight>(true);
                _pistol = player.GetComponent<PlayerPistol>();
                if (_pistol != null)
                    _pistol.ShotFired += HearShot;
                if (_bandit != null)
                    _bandit.Initialize(this, player, _playerCollider, _rightShoulder);
                break;
            }

            _sceneLights = FindObjectsByType<Light>();
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
            float distance = Vector3.Distance(transform.position, playerFeet);
            if (_bandit != null)
            {
                if (_bandit.isActiveAndEnabled)
                    _bandit.Tick(HitByPlayerBeam(), playerCenter);
                return;
            }

            bool hitByBeam = HitByPlayerBeam();
            if (hitByBeam)
                SetState(State.Alerted);

            float timeSinceContact = Time.time - _lastContactAt;
            bool remembersPlayer = timeSinceContact < AlertMemoryDuration &&
                (_state == State.Alerted || _state == State.Returning || _state == State.Watching);
            bool visible = hitByBeam || (remembersPlayer && CanSeePlayer(playerCenter, distance));
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
            Bounds bounds = _hitbox.bounds;
            Vector3 upperBodyOffset = Vector3.up * (bounds.extents.y * 0.72f);
            return CanSeeLitPoint(bounds.center) || CanSeeLitPoint(bounds.center + upperBodyOffset) ||
                CanSeeLitPoint(bounds.center - upperBodyOffset);

            bool CanSeeLitPoint(Vector3 point)
            {
                Vector3 viewport = camera.WorldToViewportPoint(point);
                return viewport.z >= camera.nearClipPlane && viewport.z <= camera.farClipPlane &&
                    viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f &&
                    HasClearLine(camera.transform.position, point) &&
                    IsLitByScene(point, includePlayerLights: true, fringe: 0f);
            }
        }

        bool CanSeePlayer(Vector3 playerCenter, float distance)
        {
            if (distance <= CloseDetectionDistance)
                return CanSeePlayerBody(playerCenter, requireLight: false);

            Vector3 eye = transform.position + Vector3.up * 1.55f;
            return IsInSightCone(playerCenter - eye) && CanSeePlayerBody(playerCenter, requireLight: true);
        }

        internal bool CanSeePlayerPoint(Vector3 eye, Vector3 point) =>
            IsInSightCone(point - eye) && HasClearLine(eye, point) && IsPlayerLit(point);

        bool IsInSightCone(Vector3 offset) => offset.sqrMagnitude <= SightDistance * SightDistance &&
            Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(offset, Vector3.up)) <= SightHalfAngle;

        bool CanSeePlayerBody(Vector3 center, bool requireLight)
        {
            Vector3 eye = transform.position + Vector3.up * 1.55f;
            float height = _playerCollider != null ? _playerCollider.bounds.extents.y * 0.65f : 0.5f;
            return CanSeePoint(center) || CanSeePoint(center + Vector3.up * height) ||
                CanSeePoint(center - Vector3.up * height);

            bool CanSeePoint(Vector3 point) =>
                HasClearLine(eye, point) && (!requireLight || IsPlayerLit(point));
        }

        bool HitByPlayerBeam()
        {
            Vector3 center = _hitbox.bounds.center;
            float halfHeight = _hitbox.bounds.extents.y * 0.8f;
            foreach (var flashlight in _playerFlashlights)
            {
                if (flashlight == null || !flashlight.IsOn)
                    continue;

                // Include the edge closest to the beam axis as well as the torso and ends.
                Vector3 origin = flashlight.transform.position;
                Vector3 direction = flashlight.transform.forward;
                float depth = Mathf.Max(0f, Vector3.Dot(center - origin, direction));
                Vector3 edge = _hitbox.ClosestPoint(origin + direction * depth);
                if (BeamReaches(center) || BeamReaches(center + Vector3.up * halfHeight) ||
                    BeamReaches(center - Vector3.up * halfHeight) || BeamReaches(edge))
                    return true;

                bool BeamReaches(Vector3 point) =>
                    flashlight.IsPointInMainBeam(point) && HasClearLine(origin, point);
            }
            return false;
        }

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

        internal void BeginSearchAt(Vector3 position, Vector3 direction)
        {
            _lastKnownPosition = position;
            BeginSearch(direction);
        }

        internal void SearchForPlayer()
        {
            if (_agent == null || !_agent.isOnNavMesh)
                return;

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
                    FaceMovement();
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
                bool left = TrySearchTurn(start, -1f, filter, out var leftDestination);
                bool right = TrySearchTurn(start, 1f, filter, out var rightDestination);
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

        bool TrySearchTurn(Vector3 start, float side, NavMeshQueryFilter filter, out Vector3 destination)
        {
            foreach (float angle in SearchTurnAngles)
            {
                Vector3 direction = Quaternion.AngleAxis(angle * side, Vector3.up) * _searchDirection;
                if (TrySearchStep(start, direction, filter, out destination))
                    return true;
            }

            destination = default;
            return false;
        }

        bool TrySearchStep(Vector3 start, Vector3 direction, NavMeshQueryFilter filter,
            out Vector3 destination, bool skipDirection = false)
        {
            destination = default;
            if (skipDirection)
                return false;

            Vector3 ahead = start + direction * SearchStepLength;
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
                    HasClearLine(start + chest, end.position + chest))
                {
                    destination = end.position;
                    return true;
                }
                ahead = Vector3.Lerp(start, ahead, 0.5f);
            }

            return false;
        }

        bool IsPlayerLit(Vector3 position)
        {
            foreach (var flashlight in _playerFlashlights)
                if (flashlight != null && flashlight.IsOn)
                    return true;

            return IsLitByScene(position, includePlayerLights: false, fringe: LightFringeWidth);
        }

        bool IsLitByScene(Vector3 position, bool includePlayerLights, float fringe)
        {
            foreach (var light in _sceneLights)
            {
                if (light == null || light.gameObject.scene != gameObject.scene ||
                    !light.isActiveAndEnabled || light.intensity <= 0f ||
                    (uint)light.renderingLayerMask == PlayerPistol.WeaponRenderingLayer ||
                    (uint)light.renderingLayerMask == Flashlight.WeaponSpillRenderingLayer ||
                    (!includePlayerLights && light.transform.IsChildOf(_player.transform.root)))
                    continue;
                if (light.type != LightType.Point && light.type != LightType.Spot)
                    continue;

                if (LightAtPoint(light, position, fringe) >= VisibleLightLevel &&
                    HasClearLine(light.transform.position, position))
                    return true;
            }
            return false;
        }

        static float LightAtPoint(Light light, Vector3 position, float fringe)
        {
            Vector3 offset = position - light.transform.position;
            float distance = offset.magnitude;
            float rangeWeight = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(light.range * 0.85f, light.range + fringe, distance));
            if (rangeWeight <= 0f)
                return 0f;

            float coneWeight = 1f;
            if (light.type == LightType.Spot)
            {
                Vector3 direction = light.transform.forward;
                float depth = Vector3.Dot(direction, offset);
                if (depth <= 0f)
                    return 0f;

                float beamRadius = depth * Mathf.Tan(light.spotAngle * 0.5f * Mathf.Deg2Rad);
                float radius = (offset - direction * depth).magnitude;
                coneWeight = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(beamRadius * 0.8f, beamRadius + fringe, radius));
            }

            return light.intensity * rangeWeight * coneWeight / Mathf.Max(1f, distance * distance);
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

        void HearShot(Vector3 position)
        {
            if (!isActiveAndEnabled || _state == State.Dead ||
                (transform.position - position).sqrMagnitude > ShotHearingDistance * ShotHearingDistance)
                return;

            if (_bandit != null)
            {
                _bandit.HearNoise(position);
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

            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction), 560f * Time.deltaTime);
            return true;
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
                if (visible && distance <= AttackRange && CanSeePlayerBody(playerCenter, requireLight: false))
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

        internal void TakeDamage(int damage)
        {
            if (_state == State.Dead)
                return;

            _health = Mathf.Max(0, _health - damage);
            if (_health > 0)
                return;

            StopMoving();
            SetState(State.Dead);
            if (_bandit != null)
                _bandit.StopCombat();
            if (_agent != null)
                _agent.enabled = false;
            _hitbox.enabled = false;
            if (_leftShoulder != null)
                _leftShoulder.localRotation = _leftRest;
            if (_rightShoulder != null)
                _rightShoulder.localRotation = _rightRest;
            StartCoroutine(Fall());
        }

        IEnumerator Fall()
        {
            const float duration = 0.4f;
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                FallPose(Mathf.SmoothStep(0f, 90f, elapsed / duration));
                yield return null;
            }
            FallPose(90f);

            void FallPose(float angle)
            {
                transform.SetPositionAndRotation(
                    startPosition + Vector3.up * (0.205f * Mathf.Sin(angle * Mathf.Deg2Rad)),
                    startRotation * Quaternion.Euler(angle, 0f, 0f));
            }
        }

        void OnDestroy()
        {
            if (_pistol != null)
                _pistol.ShotFired -= HearShot;
        }
    }
}
