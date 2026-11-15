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
        public Vector3 bodyForward;
        public Vector3 bodyPosition;
        public float dt;
        public float time;
    }

    internal enum HandEvent { None, TouchBegan, TouchEnded }

    /// <summary>
    /// One hand: wanders in front of the shoulder, sphere-casts towards where it is heading, and on contact
    /// keeps the palm on the touched point (stored in the collider's space) until the player walks away.
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

        public HandRig Rig { get; }
        public bool HasContact => _state == State.Reaching || _state == State.Holding;
        public bool IsHolding => _state == State.Holding;
        public bool Visible => _visible && _presence > 0f;
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

            Vector3 desiredPalm;
            Vector3 palmNormal;
            float smoothTime;
            if (HasContact)
            {
                Vector3 p = _anchor.Point;
                Vector3 n = _anchor.Normal;
                desiredPalm = p + n * _s.palmOffset;
                palmNormal = -n;
                Vector3 away = (desiredPalm - shoulder).normalized + Vector3.up * 0.5f;
                _fingerDir = Vector3.ProjectOnPlane(away, n);
                if (_fingerDir.sqrMagnitude < 1e-4f) _fingerDir = Vector3.ProjectOnPlane(f.yaw * Vector3.forward, n);
                _fingerDir.Normalize();
                smoothTime = _state == State.Reaching ? _s.reachTime : 0.03f;
            }
            else
            {
                desiredPalm = shoulder + f.aim * _searchCurrent + Jitter(f.time) * _s.searchJitter;
                if (want && _presence > 0.6f && f.time >= _regrabAt && TryTouch(shoulder, ref desiredPalm, other, out var anchor))
                {
                    _anchor = anchor;
                    _lastAnchorPoint = anchor.Point;
                    LastTouch = new TouchInfo(anchor.collider, anchor.Point, anchor.Normal);
                    SetState(State.Reaching);
                    result = HandEvent.TouchBegan;
                }
                Quaternion pose = f.aim * Quaternion.Euler(-50f + Wobble(f.time, 3.1f) * 8f, sign * 12f + Wobble(f.time, 7.7f) * 6f, 0f);
                _fingerDir = pose * Vector3.forward;
                palmNormal = pose * Vector3.down;
                smoothTime = _s.searchSmoothTime * 0.4f;
            }

            // Hands rise from below the view when the mode starts and sink back when it ends.
            Vector3 lowered = shoulder + f.yaw * new Vector3(-sign * 0.03f, -0.32f, 0.14f);
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
            SetState(State.Searching);
            _regrabAt = time + _s.regrabDelay;
            return HandEvent.TouchEnded;
        }

        void PickSearchGoal(float time, float sign)
        {
            _nextRetarget = time + Random.Range(_s.retargetInterval.x, _s.retargetInterval.y);
            _searchGoal = new Vector3(
                Random.Range(_s.searchSideRange.x, _s.searchSideRange.y) * sign,
                Random.Range(_s.searchHeightRange.x, _s.searchHeightRange.y),
                Random.Range(_s.searchDistance.x, _s.searchDistance.y));
        }

        float Wobble(float time, float offset) => (Mathf.PerlinNoise(time * 1.3f + _seed, offset) - 0.5f) * 2f;

        Vector3 Jitter(float time) => new(Wobble(time * 2.1f, 11f), Wobble(time * 2.1f, 23f), Wobble(time * 2.1f, 37f));

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
            if (f.dt > 0f)
            {
                float speed = (p - _lastAnchorPoint).magnitude / f.dt;
                _lastAnchorPoint = p;
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
                if (HasContact) target = finger == Finger.Thumb ? 0.25f : ContactCurl(finger);
                else if (_state == State.Searching) target = 0.14f + 0.12f * (Wobble(time * 0.6f, 50f + i * 9f) * 0.5f + 0.5f);
                else target = 0.3f;
                _curl[i] = Mathf.Lerp(_curl[i], target, k);
                Rig.SetCurl(finger, _curl[i]);
            }
        }

        /// <summary>Curls a finger so its tip follows a surface that falls away under it (pipes, edges).</summary>
        float ContactCurl(Finger finger)
        {
            if (!_anchor.IsValid) return 0.2f;
            Vector3 n = _anchor.Normal;
            float length = Rig.FingerLength(finger);
            Vector3 probe = Rig.FingerBase(finger) + _fingerDir * (length * 0.7f) + n * 0.05f;
            if (!_anchor.collider.Raycast(new Ray(probe, -n), out var hit, 0.05f + length)) return 0.55f;
            float drop = Vector3.Dot(_anchor.Point - hit.point, n);
            if (drop <= 0f) return 0.05f;
            float angle = Mathf.Atan2(drop, length * 0.7f) * Mathf.Rad2Deg;
            return Mathf.Clamp01(0.05f + angle / 110f);
        }
    }
}
