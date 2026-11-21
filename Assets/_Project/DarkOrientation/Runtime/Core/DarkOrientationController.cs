using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarX.DarkOrientation
{
    /// <summary>
    /// The single entry point of the dark-orientation mode. Needs only the player's body transform and the view camera;
    /// project-specific rules (flashlight, weapon, menus) come in through the three query delegates.
    /// Installs itself into scenes with a player (see <see cref="DarkOrientationBootstrap"/>) or can be placed by hand.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]
    [AddComponentMenu("CarX/Dark Orientation")]
    public sealed class DarkOrientationController : MonoBehaviour
    {
        public const int ZoneCount = 4;

        /// <summary>Raised once per controller after it is bound to a player; integrations hook their queries here.</summary>
        public static event Action<DarkOrientationController> Created;
        public static DarkOrientationController Current { get; private set; }

        [SerializeField] DarkOrientationSettings _settings;
        [Tooltip("Тело игрока; по умолчанию объект с тегом Player")]
        [SerializeField] Transform _body;
        [Tooltip("Камера игрока; по умолчанию Camera.main")]
        [SerializeField] Camera _viewCamera;
        [Tooltip("Свои руки вместо процедурных (необязательно)")]
        [SerializeField] HandRig _leftRig;
        [SerializeField] HandRig _rightRig;

        /// <summary>True while the toggle key must be ignored (menus, inventory).</summary>
        public Func<bool> InputBlockedQuery;
        /// <summary>True when the mode must not run (e.g. the flashlight is on): switches it off.</summary>
        public Func<bool> ForceOffQuery;
        /// <summary>Called when the player asks for the mode while <see cref="ForceOffQuery"/> holds (e.g. turns the flashlight off).</summary>
        public Action ClearForceOff;
        /// <summary>Project rule: which colliders are level walls, which are objects and which the hands ignore.</summary>
        public Func<Collider, TouchKind> KindQuery
        {
            get => _classifier.KindQuery;
            set { _classifier.KindQuery = value; _classifier.Clear(); }
        }
        /// <summary>True when a hand is busy (e.g. holds a weapon): that hand stays hidden and does not search.</summary>
        public Func<HandSide, bool> HandOccupiedQuery;
        /// <summary>Adds renderers that stay sharp and outlined like the hands (e.g. the weapon in the busy hand).</summary>
        public Action<List<Renderer>> ExtraSilhouettes;

        public event Action<bool> ActiveChanged;
        public event Action<HandSide, TouchInfo> TouchBegan;
        public event Action<HandSide, TouchInfo> TouchEnded;

        public bool IsActive { get; private set; }
        /// <summary>0..1, how much of the effect is on screen.</summary>
        public float Blend { get; private set; }
        /// <summary>0..1, how far the wider view of the mode is in. Faster than the dark picture and the hands.</summary>
        public float FovBlend { get; private set; }
        /// <summary>Degrees to add to the camera's field of view right now.</summary>
        public float FovBonus => _settings == null ? 0f : Mathf.SmoothStep(0f, 1f, FovBlend) * _settings.modeFovBonus;
        public bool InputBlocked { get; set; }
        public Transform Body => _body;
        public Camera ViewCamera => _viewCamera;
        public DarkOrientationSettings Settings => _settings;
        /// <summary>The weapon and its arms are put away while the dark picture is mostly on.</summary>
        public bool HidesWeapon => _settings != null && (Blend >= _settings.weaponHideBlend || _left?.Visible == true || _right?.Visible == true);
        /// <summary>The weapon cannot be used from the moment the mode is asked for until it is fully gone again.</summary>
        public bool BlocksWeapon => IsActive || Blend > 0f || _left?.Visible == true || _right?.Visible == true;

        internal readonly Vector4[] ZoneCenters = new Vector4[ZoneCount];
        internal readonly Vector4[] ZoneNormals = new Vector4[ZoneCount];
        internal Vector4 ZoneWeights;
        internal readonly List<Renderer> HandDraws = new();
        internal Material MaskMaterial;
        internal Material BlurMaterial;
        internal Material CompositeMaterial;

        struct Zone
        {
            public Vector3 center;
            public Vector3 normal;
            public float weight;
            public int owner;   // -1 = fading out, 0 = left hand, 1 = right hand
            public bool small;   // an object or a person: tight highlight
        }

        readonly Zone[] _zones = new Zone[ZoneCount];
        readonly HumanoidTouchProxyBuilder _proxies = new();
        readonly TouchClassifier _classifier = new();
        HandController _left;
        HandController _right;
        float _leftAssist;
        float _rightAssist;
        SleeveArms _sleeve;
        DarkOrientationRenderPass _pass;
        Transform _handRoot;
        bool _bound;
        bool _createdRaised;
        bool _subscribed;
        CharacterController _bodyController;
        Transform _bodyControllerOf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        void Awake()
        {
            if (_settings == null) _settings = DarkOrientationSettings.Load();
            _classifier.Settings = _settings;
            for (int i = 0; i < ZoneCount; i++) _zones[i].owner = -1;
        }

        void Start()
        {
            if (!_bound) Bind(_body, _viewCamera);
        }

        void OnEnable()
        {
            if (_subscribed) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            _subscribed = true;
            // After a script reload the hand objects are gone but the player link remains.
            if (Application.isPlaying && (_left == null || _right == null) && (_body != null || _viewCamera != null))
                Bind(_body, _viewCamera);
        }

        void OnDisable()
        {
            if (_subscribed) RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _subscribed = false;
            if (IsActive) SetActive(false);
            RestoreNearClip(_viewCamera);
            Blend = 0f;
            _left?.ForceHide();
            _right?.ForceHide();
            for (int i = 0; i < ZoneCount; i++) _zones[i] = new Zone { owner = -1 };
            ZoneWeights = Vector4.zero;
            _proxies.Clear();
            _classifier.Clear();
        }

        void OnDestroy()
        {
            _left?.Dispose();
            _right?.Dispose();
            if (_left != null && _left.Rig != null && _left.Rig != _leftRig) ProceduralHandBuilder.DestroyMeshes(_left.Rig);
            if (_right != null && _right.Rig != null && _right.Rig != _rightRig) ProceduralHandBuilder.DestroyMeshes(_right.Rig);
            CoreUtils.Destroy(MaskMaterial);
            CoreUtils.Destroy(BlurMaterial);
            CoreUtils.Destroy(CompositeMaterial);
            if (Current == this) Current = null;
        }

        /// <summary>Binds the mode to a player. Null arguments fall back to the Player-tagged object and Camera.main.</summary>
        public void Bind(Transform body, Camera viewCamera)
        {
            if (_settings == null) _settings = DarkOrientationSettings.Load();
            _classifier.Settings = _settings;
            _body = body != null ? body : DarkOrientationBootstrap.FindPlayer();
            _viewCamera = viewCamera != null ? viewCamera : Camera.main;
            if (!_bound || _left == null || _right == null)
            {
                if (_handRoot != null)
                {
                    Destroy(_handRoot.gameObject);
                    _handRoot = null;
                    _sleeve = null;
                    _left = null;
                    _right = null;
                    _bound = false;
                }
                if (!CreateMaterials())
                {
                    Debug.LogError("CarX DarkOrientation: shaders are missing or unsupported, the mode is disabled", this);
                    enabled = false;
                    return;
                }
                _handRoot = new GameObject("Hands").transform;
                _handRoot.SetParent(transform, false);
                _sleeve = SleeveArms.TryCreate(_settings, _handRoot);
                if (_sleeve != null)
                {
                    _left = new HandController(HandSide.Left, _sleeve.Left, false, _settings, _body, 13.7f, _classifier);
                    _right = new HandController(HandSide.Right, _sleeve.Right, false, _settings, _body, 71.3f, _classifier);
                }
                else
                {
                    _left = CreateHand(HandSide.Left, _leftRig);
                    _right = CreateHand(HandSide.Right, _rightRig);
                }
                _pass = new DarkOrientationRenderPass(this);
                _bound = true;
            }
            Current = this;
            if (!_createdRaised)
            {
                _createdRaised = true;
                Created?.Invoke(this);
            }
        }

        /// <summary>Replaces a hand with another rig (a real model). Pass null to go back to the procedural hand.</summary>
        public void SetHandRig(HandSide side, HandRig rig)
        {
            if (side == HandSide.Left) _leftRig = rig; else _rightRig = rig;
            if (!_bound) return;
            var old = side == HandSide.Left ? _left : _right;
            if (old != null)
            {
                old.ForceHide();
                old.Dispose();
                if (old.Rig != null && old.Rig.transform.IsChildOf(_handRoot))
                {
                    ProceduralHandBuilder.DestroyMeshes(old.Rig);
                    Destroy(old.Rig.gameObject);
                }
            }
            var hand = CreateHand(side, rig);
            if (side == HandSide.Left) _left = hand; else _right = hand;
        }

        public void Toggle() => SetActive(!IsActive);

        /// <summary>One-line state for logs and tests.</summary>
        public string DescribeState() =>
            $"active={IsActive} blend={Blend:0.00} left={_left?.DebugState} right={_right?.DebugState} zones={ZoneWeights:F2}";

        public void SetActive(bool on)
        {
            if (on && (!_bound || !enabled)) return;
            // Asking for the mode with the flashlight on puts the flashlight out first.
            if (on && ForceOff()) ClearForceOff?.Invoke();
            if (on && ForceOff()) return;
            if (IsActive == on) return;
            IsActive = on;
            ActiveChanged?.Invoke(on);
        }

        bool ForceOff() => ForceOffQuery != null && ForceOffQuery();
        bool IsInputBlocked() => InputBlocked || (InputBlockedQuery != null && InputBlockedQuery());
        bool IsHandOccupied(HandSide side) => HandOccupiedQuery != null && HandOccupiedQuery(side);

        bool CreateMaterials()
        {
            MaskMaterial = MakeMaterial(_settings.HandShader);
            BlurMaterial = MakeMaterial(_settings.BlurShader);
            CompositeMaterial = MakeMaterial(_settings.CompositeShader);
            return MaskMaterial != null && BlurMaterial != null && CompositeMaterial != null;
        }

        static Material MakeMaterial(Shader shader)
        {
            if (shader == null || !shader.isSupported) return null;
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        HandController CreateHand(HandSide side, HandRig custom)
        {
            HandRig rig = custom;
            bool owns = false;
            if (rig == null)
            {
                rig = ProceduralHandBuilder.Build(side, _settings.handScale, MaskMaterial, _handRoot);
                owns = true;
            }
            else if (!rig.IsBound)
            {
                rig.Bind();
            }
            return new HandController(side, rig, owns, _settings, _body, side == HandSide.Left ? 13.7f : 71.3f, _classifier);
        }

        void Update()
        {
            if (!_bound) return;
            if (IsActive && ForceOff()) SetActive(false);
            if (Time.timeScale == 0f || Time.deltaTime <= 0f) return;
            var keyboard = Keyboard.current;
            var key = _settings.toggleKey;
            if (keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame && !IsInputBlocked())
                Toggle();
        }

        void LateUpdate()
        {
            if (!_bound || _left == null || _right == null) return;
            if (_viewCamera == null)
            {
                _viewCamera = Camera.main;
                if (_viewCamera == null) return;
            }
            float dt = Time.deltaTime;
            FovBlend = Mathf.MoveTowards(FovBlend, IsActive ? 1f : 0f, dt / Mathf.Max(0.01f, _settings.modeFovTime));
            // The dark picture comes first and the hands rise only once it is full, so they are never seen in their
            // own colour, only as outlines. Leaving, the hands go down first and the picture clears after them.
            bool handsUp = _left.Visible || _right.Visible;
            bool effect = IsActive || handsUp;
            Blend = Mathf.MoveTowards(Blend, effect ? 1f : 0f,
                dt / (effect ? _settings.effectTime : _settings.deactivateTime));
            bool handsWanted = IsActive && Blend >= _settings.handsAfterEffect;
            bool wantLeft = handsWanted && !IsHandOccupied(HandSide.Left);
            bool wantRight = handsWanted && !IsHandOccupied(HandSide.Right);

            if (!IsActive && Blend <= 0f && !_left.Visible && !_right.Visible)
            {
                if (_proxies.Count > 0) _proxies.Clear();
                _classifier.Clear();
                UpdateZones(dt);
                return;
            }

            var cam = _viewCamera.transform;
            Vector3 camPos = cam.position;
            Vector3 camFwd = cam.forward;
            Vector3 bodyForward = Vector3.ProjectOnPlane(_body != null ? _body.forward : camFwd, Vector3.up);
            if (bodyForward.sqrMagnitude < 1e-6f) bodyForward = Vector3.ProjectOnPlane(cam.up, Vector3.up);
            bodyForward.Normalize();
            Quaternion yaw = Quaternion.LookRotation(bodyForward, Vector3.up);
            float pitch = Mathf.Asin(Mathf.Clamp(-camFwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            float handPitch = Mathf.Clamp(pitch, -_settings.handPitchLimit, _settings.handPitchLimit);
            var frame = new HandFrame
            {
                yaw = yaw,
                aim = yaw * Quaternion.Euler(handPitch, 0f, 0f),
                camPos = camPos,
                camFwd = camFwd,
                cam = _viewCamera,
                bodyForward = bodyForward,
                bodyPosition = _body != null ? _body.position : camPos,
                dt = dt,
                time = Time.time,
            };

            if (IsActive && _settings.npcProxies) _proxies.Tick(_body, frame.bodyPosition, _settings.npcProxyRange, Time.time);
            // Proxies hang on animated bones; queries must see this frame's pose.
            if (_proxies.Count > 0) Physics.SyncTransforms();

            Vector3 so = _settings.shoulderOffset;
            Vector3 leftShoulder = camPos + yaw * new Vector3(-so.x, so.y, so.z);
            Vector3 rightShoulder = camPos + yaw * so;
            if (_sleeve != null) _sleeve.Place(rightShoulder, leftShoulder, yaw);
            frame.shoulder = leftShoulder;
            Handle(_left, 0, _left.Tick(frame, wantLeft, _right));
            frame.shoulder = rightShoulder;
            Handle(_right, 1, _right.Tick(frame, wantRight, _left));
            UpdateHandover(frame);
            KeepHandsApart(frame);
            _left.FinishPose(frame);
            _right.FinishPose(frame);
            if (_sleeve != null)
            {
                bool shown = _left.Visible || _right.Visible;
                _sleeve.SetShown(shown);
                if (shown) _sleeve.Bake();
            }
            UpdateZones(dt);
            ApplyBodyGrip(dt, bodyForward);
        }

        /// <summary>
        /// A held wall tugs the player back until they pull hard enough to break it.
        /// At a corner, walking forward yaws the camera toward that hand.
        /// </summary>
        void ApplyBodyGrip(float dt, Vector3 bodyForward)
        {
            if (!IsActive || _body == null) return;
            if (_bodyController == null || _bodyControllerOf != _body)
            {
                _bodyController = _body.GetComponentInParent<CharacterController>();
                _bodyControllerOf = _body;
            }
            var cc = _bodyController;
            if (cc == null || !cc.enabled) return;
            Vector3 velocity = cc.velocity;
            float forward = Vector3.Dot(velocity, bodyForward);
            float yaw = 0f;
            Grip(_left, 0, velocity, forward, dt, cc, ref yaw);
            Grip(_right, 1, velocity, forward, dt, cc, ref yaw);
            if (Mathf.Abs(yaw) > 0.001f)
                cc.transform.Rotate(Vector3.up, yaw, Space.World);
        }

        void Grip(HandController hand, int owner, Vector3 velocity, float forward, float dt, CharacterController cc, ref float yaw)
        {
            if (hand == null) return;
            ref float assist = ref (hand.side == HandSide.Left ? ref _leftAssist : ref _rightAssist);
            if (!hand.IsHolding)
            {
                assist = 0f;
                return;
            }
            Vector3 n = Vector3.ProjectOnPlane(hand.ContactNormal, Vector3.up);
            if (n.sqrMagnitude < 0.05f) return;
            n.Normalize();
            // Only walls hold the player. People and props are just felt.
            if (hand.ContactIsObject) return;
            // Only motion straight off the wall is resisted. Forward and back along the wall are free.
            float leaving = Vector3.Dot(velocity, n);
            if (leaving > _settings.gripBreakSpeed)
            {
                assist = 0f;
                Handle(hand, owner, hand.BreakGrip(Time.time));
                return;
            }
            if (leaving > _settings.gripMinLeave)
                cc.Move(-n * (leaving * dt * _settings.gripStick));
            // One corner turns the view by up to a right angle, then the player looks for themselves.
            if (hand.AtCorner && forward > _settings.cornerMinForward && Mathf.Abs(assist) < _settings.cornerMaxTurn)
            {
                float step = (hand.side == HandSide.Left ? -1f : 1f) * _settings.cornerYawSpeed * dt;
                assist += step;
                yaw += step;
            }
        }

        // ------------------------------------------------------------------ the two hands together

        /// <summary>Horizontal angle of a point from the view direction, degrees; positive is to the right.</summary>
        static float Azimuth(in HandFrame f, Vector3 point)
        {
            Vector3 to = Vector3.ProjectOnPlane(point - f.camPos, Vector3.up);
            Vector3 fwd = Vector3.ProjectOnPlane(f.camFwd, Vector3.up);
            if (to.sqrMagnitude < 1e-6f || fwd.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(fwd, to, Vector3.up);
        }

        /// <summary>
        /// A held point that drifted into the other hand's half of the view (the player turned) goes over to the other hand,
        /// so the contact is not lost and the arms never cross. If the other hand cannot take it, this hand lets go.
        /// </summary>
        void UpdateHandover(in HandFrame f)
        {
            if (!IsActive) return;
            Exchange(_right, 1, _left, 0, f);
            Exchange(_left, 0, _right, 1, f);
            // Both hands hold and their points are in the wrong order: the one deeper on the wrong side lets go.
            if (_left.HasContact && _right.HasContact)
            {
                float l = Azimuth(f, _left.AnchorPoint), r = Azimuth(f, _right.AnchorPoint);
                if (l > r)
                {
                    if (l > -r) Handle(_left, 0, _left.BreakGrip(f.time));
                    else Handle(_right, 1, _right.BreakGrip(f.time));
                }
            }
        }

        void Exchange(HandController giver, int giverOwner, HandController taker, int takerOwner, in HandFrame f)
        {
            if (!giver.HasContact) return;
            float sign = giver.side == HandSide.Right ? 1f : -1f;
            float wrong = -Azimuth(f, giver.AnchorPoint) * sign;   // degrees into the other hand's half
            if (wrong < _settings.handoverAngle) return;

            bool takerHoldsWall = taker.HasContact && !taker.ContactIsObject;
            bool takerFree = !IsHandOccupied(taker.side) && taker.Visible && !takerHoldsWall;
            if (takerFree)
            {
                bool isObject = giver.ContactIsObject;
                var anchor = ShiftToward(giver.Anchor, taker.side, isObject);
                if (taker.HasContact) Handle(taker, takerOwner, taker.BreakGrip(f.time));
                var given = giver.LastTouch;
                giver.HandOff(f.time);
                TouchEnded?.Invoke(giver.side, given);
                if (taker.TakeOver(anchor, isObject, f.time) == HandEvent.TouchBegan)
                {
                    // The highlight stays on: its zone changes owner instead of fading out and in again.
                    for (int i = 0; i < ZoneCount; i++)
                        if (_zones[i].owner == giverOwner) _zones[i].owner = takerOwner;
                    TouchBegan?.Invoke(taker.side, taker.LastTouch);
                }
                else
                {
                    for (int i = 0; i < ZoneCount; i++)
                        if (_zones[i].owner == giverOwner) _zones[i].owner = -1;
                }
                return;
            }
            // The other hand already holds the wall, or carries the pistol: hold on up to the limit, then let go.
            if (takerHoldsWall || wrong > _settings.handoverMaxAngle)
                Handle(giver, giverOwner, giver.BreakGrip(f.time));
        }

        readonly RaycastHit[] _shiftHits = new RaycastHit[8];

        /// <summary>The taking hand lands a little to its own side of the point on the same wall, so the two palms do not overlap.</summary>
        TouchAnchor ShiftToward(TouchAnchor a, HandSide side, bool isObject)
        {
            if (isObject || _settings.handoverOffset <= 0f || !a.IsValid || _viewCamera == null) return a;
            Vector3 n = a.Normal, p = a.Point;
            Vector3 toward = Vector3.ProjectOnPlane(_viewCamera.transform.right * (side == HandSide.Right ? 1f : -1f), n);
            if (toward.sqrMagnitude < 1e-4f) return a;
            Vector3 q = p + toward.normalized * _settings.handoverOffset;
            int count = Physics.RaycastNonAlloc(q + n * 0.3f, -n, _shiftHits, _settings.handoverProbeDistance, _settings.TouchMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            TouchAnchor result = a;
            for (int i = 0; i < count; i++)
            {
                var h = _shiftHits[i];
                if (h.distance >= best || _classifier.Kind(h.collider) != TouchKind.Wall || Vector3.Angle(h.normal, n) > _settings.handoverMaxNormalAngle) continue;
                best = h.distance;
                result = TouchAnchor.Create(h.collider, h.point, h.normal);
            }
            return result;
        }

        /// <summary>The left palm is always drawn left of the right one, with a gap; they never pass through each other.</summary>
        void KeepHandsApart(in HandFrame f)
        {
            if (f.cam == null || !_left.NeedsPose || !_right.NeedsPose) return;
            Vector3 right = f.cam.transform.right;
            Vector3 l = _left.DrawnPalm, r = _right.DrawnPalm;
            float gap = Vector3.Dot(r - l, right);
            if (gap >= _settings.handGap) return;
            float d = (_settings.handGap - gap) * 0.5f;
            _left.DrawnPalm = l - right * d;
            _right.DrawnPalm = r + right * d;
        }

        float RaisedHands()
        {
            float raised = 1f;
            int counted = 0;
            if (!IsHandOccupied(HandSide.Left) && _left != null) { raised = Mathf.Min(raised, _left.Presence); counted++; }
            if (!IsHandOccupied(HandSide.Right) && _right != null) { raised = Mathf.Min(raised, _right.Presence); counted++; }
            return counted == 0 ? 1f : raised;
        }

        void Handle(HandController hand, int owner, HandEvent e)
        {
            if (e == HandEvent.TouchEnded)
            {
                for (int i = 0; i < ZoneCount; i++)
                    if (_zones[i].owner == owner) _zones[i].owner = -1;
                TouchEnded?.Invoke(hand.side, hand.LastTouch);
            }
            else if (e == HandEvent.TouchBegan)
            {
                int slot = -1;
                float lowest = float.MaxValue;
                for (int i = 0; i < ZoneCount; i++)
                {
                    if (_zones[i].owner == owner) _zones[i].owner = -1;
                    if (_zones[i].owner < 0 && _zones[i].weight < lowest)
                    {
                        lowest = _zones[i].weight;
                        slot = i;
                    }
                }
                if (slot >= 0) _zones[slot] = new Zone { center = hand.AnchorPoint, weight = 0f, owner = owner };
                TouchBegan?.Invoke(hand.side, hand.LastTouch);
            }
        }

        void UpdateZones(float dt)
        {
            for (int i = 0; i < ZoneCount; i++)
            {
                ref var z = ref _zones[i];
                var hand = z.owner == 0 ? _left : z.owner == 1 ? _right : null;
                if (hand != null && hand.HasContact)
                {
                    z.center = hand.AnchorPoint;
                    z.normal = hand.ContactNormal;
                    z.small = hand.ContactIsObject;
                }
                else if (hand != null) z.owner = -1;
                bool on = z.owner >= 0;
                z.weight = Mathf.MoveTowards(z.weight, on ? 1f : 0f, dt / (on ? _settings.zoneFadeIn : _settings.zoneFadeOut));
                ZoneCenters[i] = z.center;
                ZoneCenters[i].w = z.small ? 1f : 0f;
                ZoneNormals[i] = z.normal;
                ZoneWeights[i] = z.weight;
            }
        }

        float _savedNear = -1f, _appliedNear = -1f;

        /// <summary>
        /// While the hands are shown the view camera's near plane is pulled in, so looking up or down never slices the arms.
        /// Done right before rendering, after Cinemachine has written its lens for the frame; the old value comes back after.
        /// </summary>
        void ApplyNearClip(Camera cam)
        {
            if (cam == null || cam != _viewCamera) return;
            bool shown = _bound && _settings != null && _settings.handsNearClip > 0f
                && ((_left != null && _left.Visible) || (_right != null && _right.Visible));
            if (shown)
            {
                // Something else (Cinemachine) may write its own value every frame: take that as the one to restore.
                if (_savedNear < 0f || !Mathf.Approximately(cam.nearClipPlane, _appliedNear)) _savedNear = cam.nearClipPlane;
                _appliedNear = Mathf.Min(_savedNear, _settings.handsNearClip);
                cam.nearClipPlane = _appliedNear;
            }
            else RestoreNearClip(cam);
        }

        void RestoreNearClip(Camera cam)
        {
            if (_savedNear < 0f) return;
            if (cam != null && Mathf.Approximately(cam.nearClipPlane, _appliedNear)) cam.nearClipPlane = _savedNear;
            _savedNear = -1f;
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (cam.cameraType == CameraType.Game) ApplyNearClip(cam);
            if (!_bound || Blend <= 0.001f || cam != _viewCamera || cam.cameraType != CameraType.Game) return;
            if (!cam.TryGetComponent(out UniversalAdditionalCameraData data)) return;
            var renderer = data.scriptableRenderer;
            if (renderer == null) return;
            HandDraws.Clear();
            if (_sleeve != null)
            {
                if (_left.Visible || _right.Visible) _sleeve.CollectMasks(HandDraws);
            }
            else
            {
                if (_left.Visible) _left.Rig.CollectRenderers(HandDraws);
                if (_right.Visible) _right.Rig.CollectRenderers(HandDraws);
            }
            ExtraSilhouettes?.Invoke(HandDraws);
            _pass.Setup(_settings);
            renderer.EnqueuePass(_pass);
        }
    }
}
