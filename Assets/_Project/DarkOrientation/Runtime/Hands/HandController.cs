using UnityEngine;

namespace CarX.DarkOrientation
{
    internal struct HandFrame
    {
        public Vector3 shoulder;
        public Quaternion yaw;
        public Quaternion aim;
        public Vector3 camPos;
        public Vector3 camFwd;
        public Camera cam;
        public Vector3 bodyForward;
        public Vector3 bodyPosition;
        public float dt;
        public float time;
    }

    internal enum HandEvent { None, TouchBegan, TouchEnded }

    /// <summary>
    /// One hand. While searching it wanders in front of the shoulder. On contact the fingertips
    /// stay on the surface and the contact point slides with the player, staying inside the view.
    /// A shared skeleton (sleeve model) is not deactivated: an unused arm is parked below the view.
    /// </summary>
    internal sealed class HandController
    {
        enum State { Hidden, Searching, Reaching, Holding }

        public readonly HandSide side;
        readonly DarkOrientationSettings _s;
        readonly Transform _body;
        readonly bool _ownsRoot;
        readonly float _seed;
        readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly float[] _curl = new float[5];

        State _state = State.Hidden;
        float _presence;
        bool _visible = true;
        bool _poseValid;
        float _stateTime;
        Vector3 _searchGoal;
        Vector3 _searchCurrent;
        Vector3 _searchVelocity;
        float _nextRetarget;
        Vector3 _palm;
        Vector3 _palmVelocity;
        Quaternion _handRotation = Quaternion.identity;
        Vector3 _fingerDir = Vector3.forward;
        TouchAnchor _anchor;
        Vector3 _lastAnchorPoint;
        float _regrabAt;
        float _offSurface;
        Vector3 _lastBody;
        bool _haveBody;
        float _surfaceDistance;
        float _wallFacing;
        float _settle;
        Vector3 _prevBody;
        Vector3 _heldPalm;
        Vector3 _heldFingers;
        Vector3 _heldPalmNormal;

        public HandRig Rig { get; }
        public bool HasContact => _state == State.Reaching || _state == State.Holding;
        public bool IsHolding => _state == State.Holding;
        public bool AtCorner { get; private set; }
        public bool ContactIsPerson { get; private set; }
        public Vector3 ContactNormal => _anchor.IsValid ? _anchor.Normal : Vector3.zero;
        public bool Visible => _visible && _presence > 0f;
        public float Presence => _presence;
        public Vector3 AnchorPoint => _anchor.Point;
        public TouchInfo LastTouch { get; private set; }
        public string DebugState => _state + (HasContact && _anchor.collider != null ? " on " + _anchor.collider.name : "") + $" ({_presence:0.00})";

        public HandController(HandSide side, HandRig rig, bool ownsRoot, DarkOrientationSettings settings, Transform body, float seed)
        {
            this.side = side;
            Rig = rig;
            _ownsRoot = ownsRoot;
            _s = settings;
            _body = body;
            _seed = seed;
            SetVisible(false);
        }

        public HandEvent Tick(in HandFrame f, bool want, HandController other)
        {
            var result = HandEvent.None;
            if (Rig == null || !Rig.IsBound) return result;
            float sign = side == HandSide.Right ? 1f : -1f;
            float dt = f.dt;
            _stateTime += dt;

            _presence = Mathf.MoveTowards(_presence, want ? 1f : 0f, dt / (want ? _s.activateTime : _s.deactivateTime));
            if (!want && HasContact) result = Release(f.time);
            if (_presence <= 0f)
            {
                SetVisible(false);
                _state = State.Hidden;
                _poseValid = false;
                if (!_ownsRoot) Park(f, sign);
                return result;
            }
            SetVisible(true);
            if (_state == State.Hidden)
            {
                SetState(State.Searching);
                _regrabAt = f.time + _s.activateTime;
                PickSearchGoal(f.time, sign);
                _searchCurrent = _searchGoal;
                _searchVelocity = Vector3.zero;
            }

            if (_ownsRoot) Rig.transform.SetPositionAndRotation(f.shoulder, f.yaw);
            Vector3 shoulder = Rig.HasArm ? Rig.upperArm.position : f.shoulder;

            // The search wanders in camera space. The camera itself is not smoothed, so a turn does not leave the hand behind.
            _searchCurrent = SearchCircle(f.time, sign);
            // Free hands follow the view only up to the pitch limit. Past that they stay put.
            Vector3 viewUp = f.aim * Vector3.up;
            Vector3 viewFwd = f.aim * Vector3.forward;
            Vector3 searchPalm = f.camPos + f.aim * _searchCurrent;
            searchPalm = KeepOffWalls(f, searchPalm);
            Vector3 searchNormal = viewFwd;
            Vector3 searchFingers = FingersUp(searchNormal, viewUp);

            if (HasContact && !StillValid(f, shoulder)) result = Release(f.time);
            if (HasContact) SlideAnchor(f, shoulder);
            UpdateCorner(f);

            if (!HasContact && want && _presence > 0.6f && f.time >= _regrabAt && _settle < 0.2f
                && TryTouch(f, shoulder, ref searchPalm, other, out var anchor))
            {
                _anchor = anchor;
                _lastAnchorPoint = anchor.Point;
                _offSurface = 0f;
                _haveBody = false;
                LastTouch = new TouchInfo(anchor.collider, anchor.Point, anchor.Normal);
                SetState(State.Reaching);
                result = HandEvent.TouchBegan;
            }

            if (HasContact)
                PoseOnSurface(f, shoulder, out _heldPalm, out _heldPalmNormal, out _heldFingers);

            // Approach and release ease in camera space, so looking around never drags the hand off screen.
            _settle = Mathf.MoveTowards(_settle, HasContact ? 1f : 0f, dt / (HasContact ? 0.55f : 0.42f));
            float touch = Mathf.SmoothStep(0f, 1f, _settle);
            Vector3 desiredPalm = Vector3.Lerp(searchPalm, _heldPalm, touch);
            Vector3 palmNormal = Vector3.Slerp(searchNormal, _heldPalmNormal.sqrMagnitude > 0.5f ? _heldPalmNormal : searchNormal, touch);
            _fingerDir = Vector3.Slerp(searchFingers, _heldFingers.sqrMagnitude > 0.5f ? _heldFingers : searchFingers, touch).normalized;

            // Hands rise from below the view when the mode starts and sink back when it ends.
            Vector3 lowered = shoulder + f.yaw * new Vector3(sign * 0.12f, -0.48f, 0.2f);
            float rise = Mathf.SmoothStep(0f, 1f, _presence);
            desiredPalm = Vector3.Lerp(lowered, desiredPalm, rise);
            palmNormal = Vector3.Slerp(viewFwd, palmNormal, rise);

            Quaternion targetRotation = Rig.HandRotationFor(_fingerDir, palmNormal);
            if (_state == State.Reaching && _stateTime > _s.reachTime)
                SetState(State.Holding);
            // Glued to a surface: the hand stays on that point and does not chase the camera.
            // Everywhere else it cannot jump across the view faster than handMaxSpeed.
            bool glued = HasContact && _settle > 0.92f;
            if (!_poseValid)
            {
                _palm = desiredPalm;
                _handRotation = targetRotation;
                _prevBody = f.bodyPosition;
                _poseValid = true;
            }
            else if (glued)
            {
                _palm = desiredPalm;
                _handRotation = targetRotation;
            }
            else
            {
                _palm += f.bodyPosition - _prevBody;
                Vector3 step = desiredPalm - _palm;
                float maxStep = Mathf.Max(0.2f, _s.handMaxSpeed) * Mathf.Max(f.dt, 0.0001f);
                if (step.magnitude > maxStep) desiredPalm = _palm + step.normalized * maxStep;
                _palm = desiredPalm;
                _handRotation = Quaternion.RotateTowards(_handRotation, targetRotation, 240f * Mathf.Max(f.dt, 0.0001f));
            }
            _prevBody = f.bodyPosition;

            // Keep the hand out of the camera's near plane; the touch point itself does not move.
            Vector3 drawnPalm = _palm;
            float along = Vector3.Dot(drawnPalm - f.camPos, f.camFwd);
            if (along < _s.minCameraDistance) drawnPalm += f.camFwd * (_s.minCameraDistance - along);

            Rig.ResetPose();
            Vector3 wrist = drawnPalm - _handRotation * Rig.PalmPointLocal;
            Vector3 pole = shoulder + f.yaw * new Vector3(sign * 0.35f, -0.6f, -0.05f);
            Rig.SolvePose(wrist, pole, _handRotation);
            UpdateFingers(dt, f.time);
            return result;
        }

        public HandEvent BreakGrip(float time)
        {
            if (!HasContact) return HandEvent.None;
            AtCorner = false;
            return Release(time);
        }

        public void ForceHide()
        {
            if (HasContact) Release(0f);
            _presence = 0f;
            _state = State.Hidden;
            _poseValid = false;
            SetVisible(false);
        }

        void Park(in HandFrame f, float sign)
        {
            Vector3 shoulder = Rig.HasArm ? Rig.upperArm.position : f.shoulder;
            Vector3 lowered = shoulder + f.yaw * new Vector3(sign * 0.2f, -0.7f, -0.16f);
            Quaternion rot = Rig.HandRotationFor(f.yaw * Vector3.down, f.yaw * Vector3.forward);
            Rig.ResetPose();
            Vector3 wrist = lowered - rot * Rig.PalmPointLocal;
            Vector3 pole = shoulder + f.yaw * new Vector3(sign * 0.4f, -0.9f, -0.2f);
            Rig.SolvePose(wrist, pole, rot);
            for (int i = 0; i < 5; i++)
                if (Rig.HasFinger((Finger)i)) Rig.SetCurl((Finger)i, 0.45f);
        }

        void SetState(State s)
        {
            _state = s;
            _stateTime = 0f;
        }

        void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            if (_ownsRoot && Rig != null) Rig.gameObject.SetActive(visible);
        }

        HandEvent Release(float time)
        {
            if (_anchor.collider != null)
                LastTouch = new TouchInfo(_anchor.collider, _anchor.Point, _anchor.Normal);
            _anchor = default;
            ContactIsPerson = false;
            _offSurface = 0f;
            SetState(State.Searching);
            _regrabAt = time + _s.regrabDelay;
            return HandEvent.TouchEnded;
        }

        void PickSearchGoal(float time, float sign)
        {
            _nextRetarget = time + Random.Range(_s.retargetInterval.x, _s.retargetInterval.y);
            // Offsets are in camera space: outward, around the view centre, in front.
            _searchGoal = new Vector3(
                Random.Range(_s.searchSideRange.x, _s.searchSideRange.y) * sign,
                Random.Range(_s.searchHeightRange.x, _s.searchHeightRange.y),
                Random.Range(_s.searchDistance.x, _s.searchDistance.y));
        }

        /// <summary>A point in front of the camera, so the hand stays in view instead of hanging at shoulder height.</summary>
        static Vector3 ViewPoint(in HandFrame f, Vector3 cameraLocal, Vector3 shoulderFallback)
        {
            if (f.cam == null) return shoulderFallback + f.aim * cameraLocal;
            return f.cam.transform.position + f.cam.transform.rotation * cameraLocal;
        }

        float Wobble(float time, float offset) => (Mathf.PerlinNoise(time * 1.3f + _seed, offset) - 0.5f) * 2f;

        Vector3 Jitter(float time) => new(Wobble(time * 2.1f, 11f), Wobble(time * 2.1f, 23f), Wobble(time * 2.1f, 37f));

        /// <summary>Close to the surface the palm lies on it. Farther away the straight fingers reach it. The palm never turns face-up.</summary>
        void PoseOnSurface(in HandFrame f, Vector3 shoulder, out Vector3 desiredPalm, out Vector3 palmNormal, out Vector3 fingers)
        {
            Vector3 p = _anchor.Point;
            Vector3 n = _anchor.Normal;
            if (n.sqrMagnitude < 1e-6f) n = Vector3.up;
            n.Normalize();
            _surfaceDistance = Vector3.Distance(f.camPos, p);
            _wallFacing = Mathf.Clamp01(Vector3.Dot(f.bodyForward, -n));
            palmNormal = -n;
            Vector3 upOnWall = Vector3.ProjectOnPlane(Vector3.up, n);
            Vector3 along = Vector3.ProjectOnPlane(f.bodyForward, n);
            if (upOnWall.sqrMagnitude < 1e-6f) upOnWall = Vector3.up;
            if (along.sqrMagnitude < 1e-6f) along = upOnWall;
            Vector3 diagonal = (along.normalized + upOnWall.normalized).normalized;
            // Along the wall the fingers sit about 45° between the walk and the vertical.
            // Facing the wall, they stand up the surface and the palm lies flat.
            fingers = Vector3.Slerp(diagonal, upOnWall.normalized, _wallFacing);
            if (Vector3.Dot(fingers, Vector3.up) < 0f) fingers = -fingers;
            // Keep the mesh on the near side of the surface so the hand does not pass through it.
            float gap = _surfaceDistance <= _s.palmReach ? 0.06f : 0.045f;
            if (_surfaceDistance <= _s.palmReach)
                desiredPalm = p + n * (_s.palmOffset + gap);
            else
                desiredPalm = p - fingers * Mathf.Max(0.04f, Rig.TipReach) + n * (_s.palmOffset + gap);
        }

        /// <summary>Fingers always point upward. The twist around the arm is fixed by that, so the hand cannot roll.</summary>
        static Vector3 FingersUp(Vector3 palmNormal, Vector3 upHint)
        {
            if (palmNormal.sqrMagnitude < 1e-6f) palmNormal = Vector3.forward;
            palmNormal.Normalize();
            Vector3 fingers = Vector3.ProjectOnPlane(upHint, palmNormal);
            if (fingers.sqrMagnitude < 1e-4f) fingers = Vector3.ProjectOnPlane(Vector3.up, palmNormal);
            if (fingers.sqrMagnitude < 1e-4f) fingers = Vector3.up;
            fingers.Normalize();
            if (Vector3.Dot(fingers, Vector3.up) < 0f) fingers = -fingers;
            return fingers;
        }

        /// <summary>A slow circle in front of this hand's side of the view, with a little irregular drift.</summary>
        Vector3 SearchCircle(float time, float sign)
        {
            float speed = 1.85f + 0.35f * Mathf.Sin(_seed);
            float phase = time * speed + _seed;
            float radius = 0.09f + 0.03f * (0.5f + 0.5f * Mathf.Sin(_seed * 1.7f));
            float drift = 0.02f * Mathf.Sin(time * 0.7f + _seed * 2.1f);
            return new Vector3(
                sign * (0.32f + Mathf.Cos(phase) * 0.16f + drift),
                Mathf.Sin(phase * 0.93f) * radius * 0.7f,
                0.62f + 0.04f * Mathf.Sin(phase * 0.47f + 1.2f));
        }

        /// <summary>Palms face the wall or the floor, never the ceiling.</summary>
        static Vector3 LockPalm(Vector3 palmNormal)
        {
            if (palmNormal.sqrMagnitude < 1e-6f) return Vector3.down;
            palmNormal.Normalize();
            if (Vector3.Dot(palmNormal, Vector3.up) > 0.12f)
            {
                palmNormal = Vector3.ProjectOnPlane(palmNormal, Vector3.up);
                if (palmNormal.sqrMagnitude < 1e-4f) palmNormal = Vector3.forward;
                palmNormal = (palmNormal.normalized + Vector3.down * 0.35f).normalized;
            }
            return palmNormal;
        }

        void UpdateCorner(in HandFrame f)
        {
            AtCorner = false;
            if (!IsHolding || !_anchor.IsValid) return;
            Vector3 n = _anchor.Normal;
            Vector3 forward = Vector3.ProjectOnPlane(f.bodyForward, n);
            if (forward.sqrMagnitude < 0.01f) return;
            forward.Normalize();
            Vector3 origin = _anchor.Point + forward * 0.45f + n * 0.35f;
            if (!FindWall(origin, -n, 0.9f, n, 22f, out var hit))
                AtCorner = true;
            else if (Vector3.Angle(Flat(n), Flat(hit.normal)) > 32f)
                AtCorner = true;
        }

        /// <summary>Moves the contact along the same surface toward where the hand naturally rests. False if the hand let go.</summary>
        bool SlideAnchor(in HandFrame f, Vector3 shoulder)
        {
            if (!_anchor.IsValid) return false;
            if (ContactIsPerson)
            {
                _lastAnchorPoint = _anchor.Point;
                return true;
            }
            Vector3 n = _anchor.Normal;
            if (n.sqrMagnitude < 1e-6f) n = Vector3.up;
            n.Normalize();
            if (!_haveBody)
            {
                _lastBody = f.bodyPosition;
                _haveBody = true;
                _lastAnchorPoint = _anchor.Point;
                return true;
            }
            // The contact rides with the player along the wall. Looking around does not move it.
            Vector3 moved = f.bodyPosition - _lastBody;
            _lastBody = f.bodyPosition;
            Vector3 alongMove = Vector3.ProjectOnPlane(moved, n);
            if (alongMove.sqrMagnitude > 1e-6f)
            {
                Vector3 dest = _anchor.Point + alongMove;
                if (FindWall(dest + n * 0.35f, -n, 0.8f, n, 22f, out var hit))
                    _anchor = TouchAnchor.Create(hit.collider, hit.point, hit.normal.sqrMagnitude > 1e-6f ? hit.normal.normalized : n);
            }
            _lastAnchorPoint = _anchor.Point;
            return true;
        }

        bool Project(Collider col, Vector3 near, Vector3 normal, out RaycastHit hit)
        {
            hit = default;
            Vector3 origin = near + normal * 0.5f;
            if (HitSame(col, origin, -normal, 1.6f, out hit)) return true;
            Vector3 from = _anchor.Point + normal * 0.08f;
            Vector3 dir = near - from;
            float len = dir.magnitude;
            if (len < 0.02f) return false;
            return HitSame(col, from, dir / len, len + 0.2f, out hit);
        }

        bool HitSame(Collider col, Vector3 origin, Vector3 dir, float distance, out RaycastHit hit)
        {
            hit = default;
            if (!col.isTrigger && col.Raycast(new Ray(origin, dir), out hit, distance)) return true;
            int count = Physics.RaycastNonAlloc(origin, dir, _hits, distance, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].collider != col || _hits[i].distance >= best) continue;
                hit = _hits[i];
                best = hit.distance;
                found = true;
            }
            return found;
        }

        static bool InsideView(Camera cam, Vector3 point, float margin)
        {
            Vector3 vp = cam.WorldToViewportPoint(point);
            return vp.z > 0.08f && vp.x >= margin && vp.x <= 1f - margin && vp.y >= margin && vp.y <= 1f - margin;
        }

        bool TryTouch(in HandFrame f, Vector3 shoulder, ref Vector3 desiredPalm, HandController other, out TouchAnchor anchor)
        {
            anchor = default;
            Vector3 toGoal = desiredPalm - shoulder;
            float goalDistance = toGoal.magnitude;
            if (goalDistance < 1e-3f) return false;
            Vector3 dir = toGoal / goalDistance;
            float distance = Mathf.Min(goalDistance + _s.probeRadius * 1.5f, _s.reach);
            int mask = _s.touchLayers.value | (1 << HumanoidTouchProxyBuilder.ProxyLayer);
            int count = Physics.SphereCastNonAlloc(shoulder, _s.probeRadius, dir, _hits, distance, mask, QueryTriggerInteraction.Collide);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = _hits[i];
                // Zero distance means the cast started inside that collider: no usable contact.
                if (h.distance <= 0f || h.distance >= bestDistance || !CanTouch(h.collider, h.normal)) continue;
                if (!IsPerson(h.collider) && !FitsHand(f, h.point, h.normal)) continue;
                if (IsPerson(h.collider) && f.cam != null)
                {
                    float sign = side == HandSide.Right ? 1f : -1f;
                    float pointSide = Vector3.Dot(h.point - f.camPos, f.cam.transform.right) * sign;
                    if (pointSide < -0.08f) continue;
                }
                best = i;
                bestDistance = h.distance;
            }
            if (best < 0) return false;

            var hit = _hits[best];
            Vector3 point = hit.point, normal = hit.normal;
            if (hit.collider.Raycast(new Ray(hit.point + hit.normal * 0.05f, -hit.normal), out var exact, 0.1f))
            {
                point = exact.point;
                normal = exact.normal;
            }
            if (other != null && other.HasContact)
            {
                bool sameFace = Vector3.Angle(Flat(other.ContactNormal), Flat(normal)) < 28f;
                if (sameFace && Vector3.Distance(other.AnchorPoint, point) < 0.28f)
                {
                    desiredPalm = shoulder + dir * Mathf.Max(0.05f, bestDistance);
                    return false;
                }
            }
            ContactIsPerson = IsPerson(hit.collider);
            anchor = TouchAnchor.Create(hit.collider, point, normal);
            return true;
        }

        bool FindWall(Vector3 origin, Vector3 dir, float distance, Vector3 referenceNormal, float maxAngle, out RaycastHit hit)
        {
            hit = default;
            int count = Physics.RaycastNonAlloc(origin, dir, _hits, distance, _s.touchLayers.value, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            Vector3 reference = Flat(referenceNormal);
            for (int i = 0; i < count; i++)
            {
                var h = _hits[i];
                if (h.distance <= 0.001f || h.distance >= best || !IsWall(h.collider, h.normal)) continue;
                if (Vector3.Angle(reference, Flat(h.normal)) > maxAngle) continue;
                hit = h;
                best = h.distance;
                found = true;
            }
            return found;
        }

        static Vector3 Flat(Vector3 n)
        {
            Vector3 p = Vector3.ProjectOnPlane(n, Vector3.up);
            return p.sqrMagnitude > 1e-4f ? p.normalized : n.normalized;
        }

        /// <summary>A corner gives each hand its own face: the left face to the left hand, the right face to the right.</summary>
        bool FitsHand(in HandFrame f, Vector3 point, Vector3 normal)
        {
            if (f.cam == null) return true;
            Vector3 right = f.cam.transform.right;
            float sign = side == HandSide.Right ? 1f : -1f;
            float pointSide = Vector3.Dot(point - f.camPos, right) * sign;
            float faceSide = Vector3.Dot(Flat(normal), right) * sign;
            return pointSide > 0.02f || faceSide > 0.28f;
        }

        bool IsPerson(Collider c)
        {
            if (c == null) return false;
            if (c.GetComponent<DarkTouchProxy>() != null) return true;
            return c.GetComponentInParent<Animator>() != null;
        }

        bool CanTouch(Collider c, Vector3 normal) => Touchable(c) && (IsPerson(c) || IsWall(c, normal));

        /// <summary>Stops a searching hand from swinging through a solid surface. It does not drag a held hand back into frame.</summary>
        Vector3 KeepOffWalls(in HandFrame f, Vector3 palm)
        {
            Vector3 to = palm - f.camPos;
            float len = to.magnitude;
            if (len > 0.08f && Physics.Raycast(f.camPos, to / len, out var hit, len - 0.02f, _s.touchLayers.value, QueryTriggerInteraction.Ignore)
                && !hit.collider.isTrigger)
                palm = hit.point + hit.normal * 0.07f;
            return palm;
        }

        bool IsWall(Collider c, Vector3 normal)
        {
            if (!Touchable(c) || c.isTrigger) return false;
            if (Mathf.Abs(normal.y) > 0.42f) return false;
            if (c.GetComponentInParent<Animator>() != null) return false;
            Vector3 size = c.bounds.size;
            return size.y >= 1.35f && Mathf.Max(size.x, size.z) >= 0.85f;
        }

        bool Touchable(Collider c)
        {
            if (c == null) return false;
            bool proxy = c.TryGetComponent(out DarkTouchProxy _);
            if (c.isTrigger && !proxy) return false;
            if (!proxy && ((1 << c.gameObject.layer) & _s.touchLayers.value) == 0) return false;
            if (_body != null && c.transform.IsChildOf(_body)) return false;
            return c.GetComponentInParent<DarkTouchIgnore>() == null;
        }

        bool StillValid(in HandFrame f, Vector3 shoulder)
        {
            if (!_anchor.IsValid) return false;
            Vector3 p = _anchor.Point;
            Vector3 toAnchor = p - shoulder;
            // Looking around does not drop the hold. Only walking out of reach does.
            if (toAnchor.magnitude > _s.releaseDistance) return false;
            if (!CanTouch(_anchor.collider, _anchor.Normal)) return false;

            // The player's own walking is covered by the distance check; this catches objects that move away fast.
            // Sliding updates the stored point afterwards, so this delta is the collider's motion, not the slide.
            if (f.dt > 0f)
            {
                float speed = (p - _lastAnchorPoint).magnitude / f.dt;
                if (speed > _s.maxAnchorSpeed) return false;
            }
            return true;
        }

        void UpdateFingers(float dt, float time)
        {
            float k = 1f - Mathf.Exp(-dt / 0.08f);
            for (int i = 0; i < 5; i++)
            {
                var finger = (Finger)i;
                if (!Rig.HasFinger(finger)) continue;
                float idle = 0.16f;
                // Facing the wall straight: fingers open and the palm lies flat. Along the wall: a light curl.
                float held = finger == Finger.Thumb
                    ? Mathf.Lerp(0.22f, 0.06f, _wallFacing)
                    : Mathf.Lerp(0.2f, 0.0f, _wallFacing);
                float target = Mathf.Lerp(idle, held, _settle);
                _curl[i] = Mathf.Lerp(_curl[i], target, k);
                Rig.SetCurl(finger, _curl[i]);
            }
        }

        bool FarContact() => _surfaceDistance > _s.palmReach;

        static float Feel(float time, int finger) => 0.5f + 0.5f * Mathf.Sin(time * 2.4f + finger * 0.8f);
    }
}
