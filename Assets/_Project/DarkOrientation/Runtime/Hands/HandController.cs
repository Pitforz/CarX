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
        readonly TouchClassifier _classify;
        readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly Collider[] _overlaps = new Collider[16];
        readonly float[] _curl = new float[5];

        State _state = State.Hidden;
        float _presence;
        bool _visible = true;
        bool _poseValid;
        float _stateTime;
        Vector3 _searchCurrent;
        Vector3 _palm;
        Vector3 _palmVelocity;
        Quaternion _handRotation = Quaternion.identity;
        Vector3 _fingerDir = Vector3.forward;
        TouchAnchor _anchor;
        Vector3 _lastAnchorPoint;
        float _regrabAt;
        float _holdUntil;
        Vector3 _lastBody;
        bool _haveBody;
        float _surfaceDistance;
        float _wallFacing;
        float _settle;
        Vector3 _prevBody;
        Vector3 _heldPalm;
        Vector3 _heldFingers;
        Vector3 _heldPalmNormal;
        Vector3 _slidePending;
        Vector3 _drawnPalm;
        Vector3 _pole;
        Vector3 _poseShoulder;
        bool _needsPose;
        CapsuleCollider _probe;

        public HandRig Rig { get; }
        public bool HasContact => _state == State.Reaching || _state == State.Holding;
        public bool IsHolding => _state == State.Holding;
        public bool AtCorner { get; private set; }
        /// <summary>The hand is on an object (person, prop), not on a level wall.</summary>
        public bool ContactIsObject { get; private set; }
        public Vector3 ContactNormal => _anchor.IsValid ? _anchor.Normal : Vector3.zero;
        public bool Visible => _visible && _presence > 0f;
        public float Presence => _presence;
        public Vector3 AnchorPoint => _anchor.Point;
        public TouchInfo LastTouch { get; private set; }
        internal TouchAnchor Anchor => _anchor;
        /// <summary>Where the palm will be drawn this frame: set by <see cref="Tick"/>, may be nudged before <see cref="FinishPose"/>.</summary>
        public Vector3 DrawnPalm { get => _drawnPalm; set => _drawnPalm = value; }
        public bool NeedsPose => _needsPose;
        public string DebugState => _state + (HasContact && _anchor.collider != null ? " on " + _anchor.collider.name : "") + $" ({_presence:0.00})";

        public HandController(HandSide side, HandRig rig, bool ownsRoot, DarkOrientationSettings settings, Transform body, float seed, TouchClassifier classify)
        {
            this.side = side;
            Rig = rig;
            _ownsRoot = ownsRoot;
            _s = settings;
            _body = body;
            _seed = seed;
            _classify = classify;
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
            _needsPose = false;
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
            if (IsHolding) SeekView(f, shoulder);
            UpdateCorner(f);

            if (!HasContact && want && _presence > _s.searchStartPresence && f.time >= _regrabAt && _settle < 0.2f
                && TryTouch(f, shoulder, ref searchPalm, other, out var anchor))
            {
                _anchor = anchor;
                StartHold(anchor, f.time);
                result = HandEvent.TouchBegan;
            }

            if (HasContact)
                PoseOnSurface(f, shoulder, out _heldPalm, out _heldPalmNormal, out _heldFingers);

            // Approach and release ease in camera space, so looking around never drags the hand off screen.
            _settle = Mathf.MoveTowards(_settle, HasContact ? 1f : 0f, dt / (HasContact ? _s.settleInTime : _s.settleOutTime));
            float touch = Mathf.SmoothStep(0f, 1f, _settle);
            Vector3 desiredPalm = Vector3.Lerp(searchPalm, _heldPalm, touch);
            Vector3 palmNormal = Vector3.Slerp(searchNormal, _heldPalmNormal.sqrMagnitude > 0.5f ? _heldPalmNormal : searchNormal, touch);
            _fingerDir = Vector3.Slerp(searchFingers, _heldFingers.sqrMagnitude > 0.5f ? _heldFingers : searchFingers, touch).normalized;

            // Hands rise from below the view when the mode starts and sink back when it ends.
            Vector3 lowered = shoulder + f.yaw * new Vector3(sign * _s.loweredOffset.x, _s.loweredOffset.y, _s.loweredOffset.z);
            float rise = Mathf.SmoothStep(0f, 1f, _presence);
            desiredPalm = Vector3.Lerp(lowered, desiredPalm, rise);
            palmNormal = Vector3.Slerp(viewFwd, palmNormal, rise);

            Quaternion targetRotation = Rig.HandRotationFor(_fingerDir, palmNormal);
            if (_state == State.Reaching && _stateTime > _s.reachTime)
                SetState(State.Holding);
            // Glued to a surface: the hand stays on that point and does not chase the camera.
            // Everywhere else it cannot jump across the view faster than handMaxSpeed.
            bool glued = HasContact && _settle > _s.gluedSettle;
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
                _handRotation = Quaternion.RotateTowards(_handRotation, targetRotation, _s.handTurnSpeed * Mathf.Max(f.dt, 0.0001f));
            }
            _prevBody = f.bodyPosition;

            // Keep the hand out of the camera's near plane. Pushing it forward would drive it into a close wall,
            // so it moves out to its side and only a little forward. The touch point itself does not move.
            Vector3 drawnPalm = _palm;
            float along = Vector3.Dot(drawnPalm - f.camPos, f.camFwd);
            if (along < _s.minCameraDistance)
            {
                float deficit = _s.minCameraDistance - along;
                Vector3 outward = f.cam != null ? f.cam.transform.right * sign : f.yaw * Vector3.right * sign;
                drawnPalm += f.camFwd * (deficit * _s.nearPushForward) + outward * (deficit * _s.nearPushSide);
            }

            // The pose is solved in FinishPose, after the controller has seen both hands.
            _drawnPalm = drawnPalm;
            _poseShoulder = shoulder;
            _pole = shoulder + f.yaw * new Vector3(sign * _s.elbowPole.x, _s.elbowPole.y, _s.elbowPole.z);
            _needsPose = true;
            return result;
        }

        /// <summary>Solves the arm for the drawn palm, after pushing the hand out of anything solid it went into.</summary>
        public void FinishPose(in HandFrame f)
        {
            if (!_needsPose) return;
            _needsPose = false;
            Vector3 palm = _s.handCollision ? SoftCollide(_drawnPalm) : _drawnPalm;
            Rig.ResetPose();
            Vector3 wrist = palm - _handRotation * Rig.PalmPointLocal;
            Rig.SolvePose(wrist, _pole, _handRotation);
            UpdateFingers(f.dt, f.time);
        }

        /// <summary>Takes a point the other hand is giving up (it crossed to this hand's side of the view).</summary>
        public HandEvent TakeOver(TouchAnchor anchor, bool isObject, float time)
        {
            if (!anchor.IsValid) return HandEvent.None;
            if (HasContact) Release(time);
            StartHold(anchor, time);
            ContactIsObject = isObject;
            return HandEvent.TouchBegan;
        }

        /// <summary>Lets go of the point without searching back at it (it was handed to the other hand).</summary>
        public void HandOff(float time)
        {
            if (!HasContact) return;
            AtCorner = false;
            Release(time);
        }

        public void Dispose()
        {
            if (_probe != null) Object.Destroy(_probe.gameObject);
            _probe = null;
        }

        void StartHold(TouchAnchor anchor, float time)
        {
            _anchor = anchor;
            _lastAnchorPoint = anchor.Point;
            _holdUntil = time + Random.Range(_s.objectHoldTime.x, Mathf.Max(_s.objectHoldTime.x, _s.objectHoldTime.y));
            _haveBody = false;
            _slidePending = Vector3.zero;
            LastTouch = new TouchInfo(anchor.collider, anchor.Point, anchor.Normal);
            SetState(State.Reaching);
        }

        // ------------------------------------------------------------------ soft collision

        /// <summary>
        /// The hand (palm and fingers) and the forearm are capsules. Whatever solid they went into pushes them out,
        /// a few passes so corners settle. Nothing else moves: no forces, no rigidbody.
        /// </summary>
        Vector3 SoftCollide(Vector3 palm)
        {
            EnsureProbe();
            Vector3 tipDir = _fingerDir.sqrMagnitude > 0.5f ? _fingerDir.normalized : Vector3.up;
            float tip = Mathf.Max(0.06f, Rig.TipReach);
            for (int pass = 0; pass < _s.softPasses; pass++)
            {
                Vector3 push = Resolve(palm - tipDir * 0.05f, palm + tipDir * tip, _s.handRadius);
                palm += push;
                Vector3 wrist = palm - tipDir * 0.06f;
                Vector3 arm = wrist - _poseShoulder;
                Vector3 armDir = arm.sqrMagnitude > 1e-4f ? arm.normalized : -tipDir;
                Vector3 armPush = Resolve(wrist - armDir * _s.forearmLength, wrist, _s.forearmRadius);
                palm += armPush;
                if ((push + armPush).sqrMagnitude < 1e-6f) break;
            }
            return palm;
        }

        Vector3 Resolve(Vector3 p0, Vector3 p1, float radius)
        {
            Vector3 axis = p1 - p0;
            float len = axis.magnitude;
            if (len < 1e-4f) return Vector3.zero;
            Vector3 dir = axis / len;
            Quaternion rot = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up);
            _probe.radius = radius;
            _probe.height = len + radius * 2f;
            int count = Physics.OverlapCapsuleNonAlloc(p0, p1, radius, _overlaps, _s.TouchMask, QueryTriggerInteraction.Ignore);
            Vector3 total = Vector3.zero;
            Vector3 mid = (p0 + p1) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                var c = _overlaps[i];
                if (!Solid(c)) continue;
                var ct = c.transform;
                if (Physics.ComputePenetration(_probe, mid + total, rot, c, ct.position, ct.rotation, out var outDir, out float outDist))
                    total += outDir * outDist;
            }
            return total;
        }

        bool Solid(Collider c)
        {
            if (c == null || c == _probe || c.isTrigger || c is CharacterController) return false;
            if (_body != null && c.transform.IsChildOf(_body)) return false;
            if (Rig != null && c.transform.IsChildOf(Rig.transform.root)) return false;
            // Hands collide with the simplified touch band and objects only, never with the detailed mesh behind the band.
            return _classify.Kind(c) != TouchKind.Ignore;
        }

        void EnsureProbe()
        {
            if (_probe != null) return;
            var go = new GameObject("DarkOrientation Hand Probe " + side) { layer = HumanoidTouchProxyBuilder.ProxyLayer, hideFlags = HideFlags.HideAndDontSave };
            go.transform.position = new Vector3(0f, -10000f, 0f);
            _probe = go.AddComponent<CapsuleCollider>();
            _probe.isTrigger = true;
            _probe.direction = 2;
            _probe.center = Vector3.zero;
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
            Vector3 lowered = shoulder + f.yaw * new Vector3(sign * _s.parkedOffset.x, _s.parkedOffset.y, _s.parkedOffset.z);
            Quaternion rot = Rig.HandRotationFor(f.yaw * Vector3.down, f.yaw * Vector3.forward);
            Rig.ResetPose();
            Vector3 wrist = lowered - rot * Rig.PalmPointLocal;
            Vector3 pole = shoulder + f.yaw * new Vector3(sign * _s.parkedPole.x, _s.parkedPole.y, _s.parkedPole.z);
            Rig.SolvePose(wrist, pole, rot);
            for (int i = 0; i < 5; i++)
                if (Rig.HasFinger((Finger)i)) Rig.SetCurl((Finger)i, _s.parkedCurl);
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
            float delay = ContactIsObject ? _s.objectRegrabDelay : _s.regrabDelay;
            _anchor = default;
            _slidePending = Vector3.zero;
            ContactIsObject = false;
            SetState(State.Searching);
            _regrabAt = time + delay;
            return HandEvent.TouchEnded;
        }

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
            float gap = _surfaceDistance <= _s.palmReach ? _s.palmGapNear : _s.palmGapFar;
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
            float speed = _s.searchSpeed + 0.35f * Mathf.Sin(_seed);
            float phase = time * speed + _seed;
            float radius = _s.searchRadius + 0.03f * (0.5f + 0.5f * Mathf.Sin(_seed * 1.7f));
            float drift = 0.02f * Mathf.Sin(time * 0.7f + _seed * 2.1f);
            return new Vector3(
                sign * (_s.searchSide + Mathf.Cos(phase) * _s.searchSwing + drift),
                Mathf.Sin(phase * 0.93f) * radius * 0.7f,
                _s.searchForward + 0.04f * Mathf.Sin(phase * 0.47f + 1.2f));
        }

        void UpdateCorner(in HandFrame f)
        {
            AtCorner = false;
            if (!IsHolding || !_anchor.IsValid || ContactIsObject) return;
            Vector3 n = _anchor.Normal;
            Vector3 forward = Vector3.ProjectOnPlane(f.bodyForward, n);
            if (forward.sqrMagnitude < 0.01f) return;
            forward.Normalize();
            Vector3 origin = _anchor.Point + forward * _s.cornerProbeAhead + n * _s.cornerProbeOut;
            if (!FindWall(origin, -n, _s.cornerProbeDistance, n, _s.cornerProbeAngle, out var hit))
                AtCorner = true;
            else if (Vector3.Angle(Flat(n), Flat(hit.normal)) > _s.cornerTurnAngle)
                AtCorner = true;
        }

        /// <summary>
        /// The hand on a wall keeps the touch where the player can see it. Along the same wall it moves toward the point
        /// closest to the view direction that the arm still reaches, on this hand's side of the view:
        /// facing the wall the hand presses in front, walking along it the arm stretches ahead along the way,
        /// turning away from it the arm straightens toward the view so the palm stays as close to sight as it can.
        /// </summary>
        void SeekView(in HandFrame f, Vector3 shoulder)
        {
            if (!_s.seekView || ContactIsObject || !_anchor.IsValid || f.cam == null) return;
            Vector3 n = _anchor.Normal;
            Vector3 tangent = Vector3.Cross(Vector3.up, n);
            if (tangent.sqrMagnitude < 1e-4f) return;
            tangent.Normalize();
            Vector3 p = _anchor.Point;
            float sign = side == HandSide.Right ? 1f : -1f;
            float reach = Mathf.Min(_s.reach, _s.releaseDistance) * 0.95f;
            float height = Mathf.Lerp(p.y, f.camPos.y - _s.seekHeightBelowEyes, 0.5f);

            Vector3 best = p;
            float bestScore = ViewScore(f, p, sign);
            int steps = Mathf.Max(1, _s.seekSteps);
            for (int k = -steps; k <= steps; k++)
            {
                if (k == 0) continue;
                Vector3 q = p + tangent * (_s.seekRange * k / steps);
                q.y = height;
                if (!FindWall(q + n * _s.slideProbeLift, -n, _s.slideProbeDistance, n, _s.slideAngle, out var hit)) continue;
                if ((hit.point - shoulder).magnitude > reach) continue;
                float score = ViewScore(f, hit.point, sign);
                if (score < bestScore - 0.5f)
                {
                    bestScore = score;
                    best = hit.point;
                }
            }
            Vector3 delta = best - p;
            if (delta.sqrMagnitude < 1e-6f) return;
            float maxStep = _s.seekSpeed * f.dt;
            Vector3 q2 = p + (delta.magnitude > maxStep ? delta.normalized * maxStep : delta);
            if (FindWall(q2 + n * _s.slideProbeLift, -n, _s.slideProbeDistance, n, _s.slideAngle, out var moved))
            {
                _anchor = TouchAnchor.Create(moved.collider, moved.point, moved.normal.sqrMagnitude > 1e-6f ? moved.normal.normalized : n);
                _lastAnchorPoint = _anchor.Point;
            }
        }

        /// <summary>Degrees from the view centre; points on the other hand's half of the view score badly.</summary>
        static float ViewScore(in HandFrame f, Vector3 point, float sign)
        {
            Vector3 to = point - f.camPos;
            float angle = Vector3.Angle(f.camFwd, to);
            float side = Vector3.Dot(to, f.cam.transform.right) * sign;
            return side < 0.03f ? angle + 90f : angle;
        }

        /// <summary>Moves the contact along the same surface toward where the hand naturally rests. False if the hand let go.</summary>
        bool SlideAnchor(in HandFrame f, Vector3 shoulder)
        {
            if (!_anchor.IsValid) return false;
            if (ContactIsObject)
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
            // A step that finds no wall (a seam, the edge of a piece) is not lost: it waits and goes with the next one,
            // so the hand never stays behind in a joint.
            Vector3 moved = f.bodyPosition - _lastBody;
            _lastBody = f.bodyPosition;
            _slidePending += Vector3.ProjectOnPlane(moved, n);
            if (_slidePending.sqrMagnitude > 1e-6f)
            {
                Vector3 dest = _anchor.Point + _slidePending;
                if (FindWall(dest + n * _s.slideProbeLift, -n, _s.slideProbeDistance, n, _s.slideAngle, out var hit))
                {
                    _anchor = TouchAnchor.Create(hit.collider, hit.point, hit.normal.sqrMagnitude > 1e-6f ? hit.normal.normalized : n);
                    _slidePending = Vector3.zero;
                }
            }
            _lastAnchorPoint = _anchor.Point;
            return true;
        }

        bool TryTouch(in HandFrame f, Vector3 shoulder, ref Vector3 desiredPalm, HandController other, out TouchAnchor anchor)
        {
            anchor = default;
            Vector3 toGoal = desiredPalm - shoulder;
            float goalDistance = toGoal.magnitude;
            if (goalDistance < 1e-3f) return false;
            Vector3 dir = toGoal / goalDistance;
            float distance = Mathf.Min(goalDistance + _s.probeRadius * 1.5f, _s.reach);
            int mask = _s.TouchMask | (1 << HumanoidTouchProxyBuilder.ProxyLayer);
            int count = Physics.SphereCastNonAlloc(shoulder, _s.probeRadius, dir, _hits, distance, mask, QueryTriggerInteraction.Collide);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = _hits[i];
                // Zero distance means the cast started inside that collider: no usable contact.
                if (h.distance <= 0f || h.distance >= bestDistance || !CanTouch(h.collider, h.normal)) continue;
                if (!FitsHand(f, h.point, h.normal)) continue;
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
                bool sameFace = Vector3.Angle(Flat(other.ContactNormal), Flat(normal)) < _s.sameFaceAngle;
                if (sameFace && Vector3.Distance(other.AnchorPoint, point) < _s.minAnchorSeparation)
                {
                    desiredPalm = shoulder + dir * Mathf.Max(0.05f, bestDistance);
                    return false;
                }
            }
            ContactIsObject = !_classify.IsWall(hit.collider);
            anchor = TouchAnchor.Create(hit.collider, point, normal);
            return true;
        }

        bool FindWall(Vector3 origin, Vector3 dir, float distance, Vector3 referenceNormal, float maxAngle, out RaycastHit hit)
        {
            hit = default;
            int count = Physics.RaycastNonAlloc(origin, dir, _hits, distance, _s.TouchMask, QueryTriggerInteraction.Ignore);
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

        /// <summary>Each hand takes a new point only on its own side of the view: the left hand never reaches across to the right.</summary>
        bool FitsHand(in HandFrame f, Vector3 point, Vector3 normal)
        {
            if (f.cam == null) return true;
            float sign = side == HandSide.Right ? 1f : -1f;
            return Vector3.Dot(point - f.camPos, f.cam.transform.right) * sign > _s.handSideMargin;
        }

        /// <summary>
        /// Any solid collider can be touched. Walls only on their sides (floor and ceiling are not felt);
        /// objects from the side or from above, never from below.
        /// </summary>
        bool CanTouch(Collider c, Vector3 normal)
        {
            if (!Touchable(c)) return false;
            return _classify.IsWall(c) ? Mathf.Abs(normal.y) <= _s.wallMaxNormalY : normal.y >= _s.objectMinNormalY;
        }

        /// <summary>Stops a searching hand from swinging through a solid surface. It does not drag a held hand back into frame.</summary>
        Vector3 KeepOffWalls(in HandFrame f, Vector3 palm)
        {
            Vector3 to = palm - f.camPos;
            float len = to.magnitude;
            if (len > 0.08f && Physics.Raycast(f.camPos, to / len, out var hit, len - 0.02f, _s.TouchMask, QueryTriggerInteraction.Ignore)
                && !hit.collider.isTrigger)
                palm = hit.point + hit.normal * _s.keepOffWallsGap;
            return palm;
        }

        bool IsWall(Collider c, Vector3 normal) =>
            Touchable(c) && !c.isTrigger && _classify.IsWall(c) && Mathf.Abs(normal.y) <= _s.wallMaxNormalY;

        bool Touchable(Collider c)
        {
            if (c == null) return false;
            bool proxy = c.TryGetComponent(out DarkTouchProxy _);
            if (c.isTrigger && !proxy) return false;
            if (_classify.Kind(c) == TouchKind.Ignore) return false;
            if (!proxy && ((1 << c.gameObject.layer) & _s.TouchMask) == 0) return false;
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
            // Objects are felt, not held: after a moment the hand lets go and takes the nearest comfortable spot again.
            if (ContactIsObject && (f.time >= _holdUntil || toAnchor.magnitude > _s.reach)) return false;

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
            float k = 1f - Mathf.Exp(-dt / _s.fingerSmooth);
            for (int i = 0; i < 5; i++)
            {
                var finger = (Finger)i;
                if (!Rig.HasFinger(finger)) continue;
                float idle = _s.idleCurl;
                // Facing the wall straight: fingers open and the palm lies flat. Along the wall: a light curl.
                float held = finger == Finger.Thumb
                    ? Mathf.Lerp(_s.thumbCurlAlong, _s.thumbCurlFacing, _wallFacing)
                    : Mathf.Lerp(_s.fingerCurlAlong, _s.fingerCurlFacing, _wallFacing);
                float target = Mathf.Lerp(idle, held, _settle);
                _curl[i] = Mathf.Lerp(_curl[i], target, k);
                Rig.SetCurl(finger, _curl[i]);
            }
        }
    }
}
