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

        public HandRig Rig { get; }
        public bool HasContact => _state == State.Reaching || _state == State.Holding;
        public bool IsHolding => _state == State.Holding;
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

            if (f.time >= _nextRetarget) PickSearchGoal(f.time, sign);
            _searchCurrent = Vector3.SmoothDamp(_searchCurrent, _searchGoal, ref _searchVelocity, _s.searchSmoothTime, Mathf.Infinity, dt);

            if (HasContact && !StillValid(f, shoulder)) result = Release(f.time);
            if (HasContact && !SlideAnchor(f, shoulder))
                result = HandEvent.TouchEnded;

            Vector3 desiredPalm;
            Vector3 palmNormal;
            float smoothTime;
            if (HasContact)
            {
                PoseOnSurface(f, shoulder, out desiredPalm, out palmNormal);
                smoothTime = _state == State.Reaching ? _s.reachTime : 0.08f;
            }
            else
            {
                desiredPalm = ViewPoint(f, _searchCurrent + Jitter(f.time) * _s.searchJitter, shoulder);
                if (want && _presence > 0.6f && f.time >= _regrabAt && TryTouch(shoulder, ref desiredPalm, other, out var anchor))
                {
                    _anchor = anchor;
                    _lastAnchorPoint = anchor.Point;
                    _offSurface = 0f;
                    _haveBody = false;
                    LastTouch = new TouchInfo(anchor.collider, anchor.Point, anchor.Normal);
                    SetState(State.Reaching);
                    result = HandEvent.TouchBegan;
                }
                Quaternion pose = f.aim * Quaternion.Euler(-18f + Wobble(f.time, 3.1f) * 7f, sign * 8f + Wobble(f.time, 7.7f) * 5f, sign * 6f);
                _fingerDir = pose * Vector3.forward;
                palmNormal = pose * Vector3.down;
                smoothTime = _s.searchSmoothTime * 0.4f;
            }

            // Hands rise from below the view when the mode starts and sink back when it ends.
            Vector3 lowered = shoulder + f.yaw * new Vector3(sign * 0.12f, -0.48f, -0.02f);
            desiredPalm = Vector3.Lerp(lowered, desiredPalm, Mathf.SmoothStep(0f, 1f, _presence));

            Quaternion targetRotation = Rig.HandRotationFor(_fingerDir, palmNormal);
            if (!_poseValid)
            {
                _palm = lowered;
                _palmVelocity = Vector3.zero;
                _handRotation = targetRotation;
                _poseValid = true;
            }
            _palm = Vector3.SmoothDamp(_palm, desiredPalm, ref _palmVelocity, smoothTime, Mathf.Infinity, dt);
            if (_state == State.Reaching && ((_palm - desiredPalm).sqrMagnitude < 1e-4f || _stateTime > _s.reachTime * 4f))
                SetState(State.Holding);
            _handRotation = Quaternion.Slerp(_handRotation, targetRotation, 1f - Mathf.Exp(-dt / (HasContact ? 0.06f : 0.1f)));

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

        /// <summary>Fingertips on the surface, palm lifted off it. Fingers point along the way the player is moving.</summary>
        void PoseOnSurface(in HandFrame f, Vector3 shoulder, out Vector3 desiredPalm, out Vector3 palmNormal)
        {
            Vector3 p = _anchor.Point;
            Vector3 n = _anchor.Normal;
            if (n.sqrMagnitude < 1e-6f) n = Vector3.up;
            n.Normalize();

            Vector3 travel = f.bodyForward;
            if (_haveBody && f.dt > 0f)
            {
                Vector3 step = (f.bodyPosition - _lastBody) / f.dt;
                if (step.sqrMagnitude > 0.04f) travel = step;
            }
            _lastBody = f.bodyPosition;
            _haveBody = true;

            Vector3 along = Vector3.ProjectOnPlane(travel, n);
            if (along.sqrMagnitude < 0.01f) along = Vector3.ProjectOnPlane(f.aim * Vector3.forward, n);
            if (along.sqrMagnitude < 1e-6f) along = Vector3.Cross(n, Vector3.up);
            along.Normalize();
            Vector3 away = Vector3.ProjectOnPlane(p - shoulder, n);
            if (away.sqrMagnitude > 0.01f && Vector3.Dot(along, away) < 0f)
                along = -along;

            float tilt = _s.fingertipTilt;
            _fingerDir = (along * (1f - tilt) - n * tilt).normalized;
            palmNormal = -n;
            float tip = Mathf.Max(0.04f, Rig.TipReach * 0.92f);
            desiredPalm = p - _fingerDir * tip + n * _s.palmOffset;
        }

        /// <summary>Moves the contact along the same surface toward where the hand naturally rests. False if the hand let go.</summary>
        bool SlideAnchor(in HandFrame f, Vector3 shoulder)
        {
            if (!_anchor.IsValid) return false;
            var col = _anchor.collider;
            Vector3 n = _anchor.Normal;
            if (n.sqrMagnitude < 1e-6f) n = Vector3.up;
            n.Normalize();
            float sign = side == HandSide.Right ? 1f : -1f;
            Vector3 home = ViewPoint(f, new Vector3(_s.slideHome.x * sign, _s.slideHome.y, _s.slideHome.z), shoulder);

            if (!Project(col, home, n, out var hit))
            {
                _offSurface += f.dt;
                if (_offSurface > 0.12f)
                {
                    Release(f.time);
                    return false;
                }
                _lastAnchorPoint = _anchor.Point;
                return true;
            }
            _offSurface = 0f;

            Vector3 target = hit.point;
            Vector3 normal = hit.normal.sqrMagnitude > 1e-6f ? hit.normal.normalized : n;
            if (f.cam != null && !InsideView(f.cam, target, _s.viewMargin))
            {
                Vector3 vp = f.cam.WorldToViewportPoint(target);
                if (vp.z < 0.08f)
                {
                    Release(f.time);
                    return false;
                }
                var clamped = f.cam.ViewportToWorldPoint(new Vector3(
                    Mathf.Clamp(vp.x, _s.viewMargin, 1f - _s.viewMargin),
                    Mathf.Clamp(vp.y, _s.viewMargin, 1f - _s.viewMargin),
                    vp.z));
                if (!Project(col, clamped, normal, out var pulled))
                {
                    Release(f.time);
                    return false;
                }
                target = pulled.point;
                if (pulled.normal.sqrMagnitude > 1e-6f) normal = pulled.normal.normalized;
            }

            Vector3 current = _anchor.Point;
            Vector3 delta = Vector3.ProjectOnPlane(target - current, normal);
            bool inView = f.cam == null || InsideView(f.cam, current, _s.viewMargin);
            if (delta.magnitude >= _s.slideDeadzone || !inView)
            {
                float t = 1f - Mathf.Exp(-f.dt / Mathf.Max(0.01f, _s.slideSmoothTime));
                Vector3 next = !inView ? Vector3.Lerp(current, target, t) : current + delta * t;
                _anchor = TouchAnchor.Create(col, next, normal);
            }
            _lastAnchorPoint = _anchor.Point;
            return true;
        }

        bool Project(Collider col, Vector3 near, Vector3 normal, out RaycastHit hit)
        {
            hit = default;
            Vector3 origin = near + normal * 0.5f;
            if (HitSame(col, origin, -normal, 1f, out hit)) return true;
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

        bool TryTouch(Vector3 shoulder, ref Vector3 desiredPalm, HandController other, out TouchAnchor anchor)
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
                if (h.distance <= 0f || h.distance >= bestDistance || !Touchable(h.collider)) continue;
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
            if (other != null && other.HasContact && Vector3.Distance(other.AnchorPoint, point) < _s.minAnchorSeparation)
            {
                desiredPalm = shoulder + dir * Mathf.Max(0.05f, bestDistance);
                return false;
            }
            anchor = TouchAnchor.Create(hit.collider, point, normal);
            return true;
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
            if (toAnchor.magnitude > _s.releaseDistance) return false;

            Vector3 flat = Vector3.ProjectOnPlane(toAnchor, Vector3.up);
            if (flat.sqrMagnitude > 0.0025f && Vector3.Angle(f.bodyForward, flat) > _s.releaseAngle) return false;

            // The player's own walking is covered by the distance check; this catches objects that move away fast.
            // Sliding updates the stored point afterwards, so this delta is the collider's motion, not the slide.
            if (f.dt > 0f)
            {
                float speed = (p - _lastAnchorPoint).magnitude / f.dt;
                if (speed > _s.maxAnchorSpeed) return false;
            }

            Vector3 target = p + _anchor.Normal * 0.03f;
            Vector3 d = target - shoulder;
            float length = d.magnitude;
            if (length > 0.08f)
            {
                int count = Physics.RaycastNonAlloc(shoulder, d / length, _hits, length - 0.04f, _s.touchLayers.value, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var c = _hits[i].collider;
                    if (c == _anchor.collider || (_body != null && c.transform.IsChildOf(_body))) continue;
                    return false;
                }
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
                float target;
                if (HasContact) target = finger == Finger.Thumb ? 0.2f : ContactCurl(finger);
                else if (_state == State.Searching) target = 0.12f + 0.1f * (Wobble(time * 0.6f, 50f + i * 9f) * 0.5f + 0.5f);
                else target = 0.25f;
                _curl[i] = Mathf.Lerp(_curl[i], target, k);
                Rig.SetCurl(finger, _curl[i]);
            }
        }

        /// <summary>Fingers stay mostly straight. They curl only where the surface falls away under the tip.</summary>
        float ContactCurl(Finger finger)
        {
            if (!_anchor.IsValid) return 0.14f;
            Vector3 n = _anchor.Normal;
            float length = Rig.FingerLength(finger);
            if (length < 0.01f) return 0.14f;
            Vector3 probe = Rig.FingerBase(finger) + _fingerDir * (length * 0.85f) + n * 0.04f;
            if (!_anchor.collider.Raycast(new Ray(probe, -n), out var hit, 0.04f + length)) return 0.16f;
            float drop = Vector3.Dot(_anchor.Point - hit.point, n);
            if (drop <= 0.004f) return 0.12f;
            float angle = Mathf.Atan2(drop, length * 0.85f) * Mathf.Rad2Deg;
            return Mathf.Clamp(0.12f + angle / 200f, 0.12f, 0.42f);
        }
    }
}
