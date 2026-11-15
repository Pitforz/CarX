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
        /// <summary>True when the mode must not run (e.g. the flashlight is on): blocks switching on and switches it off.</summary>
        public Func<bool> ForceOffQuery;
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
        public bool InputBlocked { get; set; }
        public Transform Body => _body;
        public Camera ViewCamera => _viewCamera;
        public DarkOrientationSettings Settings => _settings;

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
            public bool person;
        }

        readonly Zone[] _zones = new Zone[ZoneCount];
        readonly HumanoidTouchProxyBuilder _proxies = new();
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        void Awake()
        {
            if (_settings == null) _settings = DarkOrientationSettings.Load();
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
            Blend = 0f;
            _left?.ForceHide();
            _right?.ForceHide();
            for (int i = 0; i < ZoneCount; i++) _zones[i] = new Zone { owner = -1 };
            ZoneWeights = Vector4.zero;
            _proxies.Clear();
        }

        void OnDestroy()
        {
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
                    _left = new HandController(HandSide.Left, _sleeve.Left, false, _settings, _body, 13.7f);
                    _right = new HandController(HandSide.Right, _sleeve.Right, false, _settings, _body, 71.3f);
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
            if (on && (!_bound || !enabled || ForceOff())) return;
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
            return new HandController(side, rig, owns, _settings, _body, side == HandSide.Left ? 13.7f : 71.3f);
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
            bool wantLeft = IsActive && !IsHandOccupied(HandSide.Left);
            bool wantRight = IsActive && !IsHandOccupied(HandSide.Right);
            if (_sleeve == null)
            {
                Blend = Mathf.MoveTowards(Blend, IsActive ? 1f : 0f,
                    dt / (IsActive ? _settings.activateTime : _settings.deactivateTime));
            }
            else
            {
                // Hands come up in their own colour first. The dark picture and the outline follow.
                float raised = RaisedHands();
                bool effect = IsActive && raised >= _settings.effectAfterHands;
                Blend = Mathf.MoveTowards(Blend, effect ? 1f : 0f,
                    dt / (effect ? _settings.effectTime : _settings.deactivateTime));
                wantLeft = !IsHandOccupied(HandSide.Left) && (IsActive || Blend > 0.02f);
                wantRight = !IsHandOccupied(HandSide.Right) && (IsActive || Blend > 0.02f);
            }

            if (!IsActive && Blend <= 0f && !_left.Visible && !_right.Visible)
            {
                if (_proxies.Count > 0) _proxies.Clear();
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
            var frame = new HandFrame
            {
                yaw = yaw,
                aim = yaw * Quaternion.Euler(pitch * _settings.pitchFollow, 0f, 0f),
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
            var cc = _body.GetComponentInParent<CharacterController>();
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
            // People are not a wall: the hand may rest on them, but it does not drag the player.
            if (hand.ContactIsPerson) return;
            // Only motion straight off the wall is resisted. Forward and back along the wall are free.
            float leaving = Vector3.Dot(velocity, n);
            if (leaving > _settings.gripBreakSpeed)
            {
                assist = 0f;
                Handle(hand, owner, hand.BreakGrip(Time.time));
                return;
            }
            if (leaving > 0.2f)
                cc.Move(-n * (leaving * dt * _settings.gripStick));
            // One corner turns the view by up to a right angle, then the player looks for themselves.
            if (hand.AtCorner && forward > 0.35f && Mathf.Abs(assist) < 80f)
            {
                float step = (hand.side == HandSide.Left ? -1f : 1f) * _settings.cornerYawSpeed * dt;
                assist += step;
                yaw += step;
            }
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
                    z.person = hand.ContactIsPerson;
                }
                else if (hand != null) z.owner = -1;
                bool on = z.owner >= 0;
                z.weight = Mathf.MoveTowards(z.weight, on ? 1f : 0f, dt / (on ? _settings.zoneFadeIn : _settings.zoneFadeOut));
                ZoneCenters[i] = z.center;
                ZoneCenters[i].w = z.person ? 1f : 0f;
                ZoneNormals[i] = z.normal;
            }
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
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
