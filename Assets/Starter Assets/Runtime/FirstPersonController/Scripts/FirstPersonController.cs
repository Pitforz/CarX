using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class FirstPersonController : MonoBehaviour
	{
		[Header("Player")]
		[Tooltip("Move speed of the character in m/s")]
		public float MoveSpeed = 4.0f;
		[SerializeField, Range(0.1f, 1f)] private float _backwardSpeedMultiplier = 0.65f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;
		[Tooltip("Mouse smoothing time in seconds; keeps the full input angle")]
		[SerializeField, Min(0f)] private float _lookSmoothingTime = 0.045f;
		[Tooltip("Acceleration and deceleration")]
		public float SpeedChangeRate = 10.0f;
		[SerializeField, Min(0f)] private float _slowWalkSpeed = 1.6f;

#if ENABLE_INPUT_SYSTEM
		[Header("Keyboard")]
		[SerializeField] private Key _crouchKey = Key.LeftCtrl;
		[SerializeField] private Key _alternateCrouchKey = Key.RightCtrl;
		[SerializeField] private Key _slowWalkKey = Key.LeftShift;
		[SerializeField] private Key _alternateSlowWalkKey = Key.RightShift;
		[SerializeField] private Key _leanRightKey = Key.C;
		[SerializeField] private Key _leanLeftKey = Key.Z;
#endif

		[Space(10)]
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -15.0f;
		[SerializeField, Min(0f)] private float _jumpHeight = 0.8f;
		[SerializeField, Min(0f)] private float _crouchJumpHeight = 0.35f;

		[Header("Crouch")]
		[SerializeField, Min(0f)] private float _crouchHeight = 1.2f;
		[SerializeField, Min(0f)] private float _crouchSpeed = 2f;
		[SerializeField, Min(0f)] private float _crouchCameraDrop = 0.35f;
		[SerializeField, Min(0.01f)] private float _crouchTransitionTime = 0.28f;
		[SerializeField] private Transform _capsuleModel;

		[Header("Lean")]
		[SerializeField, Min(0f)] private float _leanDistance = 0.53f;
		[SerializeField, Range(0f, 75f)] private float _leanAngle = 24.5f;
		[SerializeField, Min(0.01f)] private float _leanTransitionTime = 0.5f;
		[SerializeField, Min(0.01f)] private float _leanRollSmoothTime = 0.08f;
		private const float LeanProbeRadius = 0.12f;
		private const float CameraWallMargin = 0.02f;
		private const float LeanClearanceSmoothTime = 0.16f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.5f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[SerializeField, Min(0.01f)] private float _verticalCameraSmoothTime = 0.08f;

		[Header("Vertical Look Limits")]
		[FormerlySerializedAs("BottomClamp")]
		[Tooltip("Upper pitch limit in degrees. Move toward zero to restrict looking up.")]
		[SerializeField, Range(-90f, 0f)] private float _lookUpLimit = -90f;
		[FormerlySerializedAs("TopClamp")]
		[Tooltip("Lower pitch limit in degrees. Move toward zero to restrict looking down.")]
		[SerializeField, Range(0f, 90f)] private float _lookDownLimit = 90f;

		// cinemachine
		private float _cinemachineTargetPitch;
		private const float LookRecoilDuration = 0.04f;
		private Vector2 _remainingLookRecoil;
		private float _lookRecoilTimeRemaining;
		private Vector2 _aimSwayDegrees;

		// player
		private Vector2 _planarVelocity;
		private Vector2 _planarAcceleration;
		private Vector2 _pendingLookRotation;
		private Vector2 _lookVelocity;
		private bool _controlsFrozen;
		private bool _movementLocked;
		private int _controlsResumedFrame = -1;
		private bool _encounterLookActive;
		private bool _limitEncounterLook;
		private float _encounterLookYaw;
		private float _encounterLookCenterYaw;
		private float _encounterLookCenterPitch;
		private float _encounterLookInputWeight = 1f;
		private Vector3 _encounterEntryFocus;
		private Vector3 _encounterEntryVelocity;
		private Quaternion _encounterEntryRotation;
		private float _encounterEntryPitch;
		private float _encounterEntryElapsed;
		private const float EncounterEntryDuration = 0.55f;
		private const float EncounterBrakeDuration = 0.24f;
		private const float EncounterLookYawLimit = 42f;
		private const float EncounterLookPitchLimit = 18f;
		private float _verticalVelocity;
		private float _audibleStepDistance;
		private const float TerminalVelocity = -53f;
		private const float DistancePerAudibleStep = 1.35f;

#if ENABLE_INPUT_SYSTEM
		private PlayerInput _playerInput;
#endif
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;
		private Vector3 _neutralCameraPosition;
		private float _crouchProgress;
		private float _crouchVelocity;
		private float _standingHeight;
		private Vector3 _standingCenter;
		private Vector3 _standingCameraPosition;
		private float _smoothedCameraFollowY;
		private float _cameraFollowVelocity;
		private const float MaxCameraFollowOffset = 0.12f;
		private Vector3 _standingModelPosition;
		private Vector3 _standingModelScale;
		private Quaternion _standingModelRotation;
		private float _leanProgress;
		private float _leanVelocity;
		private float _leanRoll;
		private float _leanRollVelocity;
		private float _leanWallCorrection;
		private float _leanWallCorrectionVelocity;
		private float _cameraWallCorrection;
		private float _cameraWallCorrectionVelocity;
		private float _leanDirection;
		public CapsuleCollider BulletHitbox { get; private set; }
		public float LeanAmount => Mathf.Abs(_leanRoll);
		public float MouseSensitivityScale { get; set; } = 1f;
		public float LookSensitivityMultiplier { get; set; } = 1f;
		public float MovementSpeedMultiplier { get; set; } = 1f;
		public bool ControlSprintEnabled { get; set; }
		public bool IsAiming { get; set; }
		public bool IsWalkingQuietly { get; private set; }
		public bool LookInputBlocked { get; set; }
		public bool IsSettlingEncounterEntry => _encounterLookActive && _encounterEntryElapsed < EncounterEntryDuration;
		public Quaternion EncounterLookCenterRotation => transform.rotation *
			Quaternion.Euler(_encounterLookCenterPitch, _encounterLookCenterYaw, 0f);
		public event System.Action<Vector3, Vector3> AudibleFootstep;

		public void SetControlsFrozen(bool frozen)
		{
			if (_controlsFrozen == frozen)
				return;

			_controlsFrozen = frozen;
			_pendingLookRotation = _lookVelocity = Vector2.zero;
			if (!frozen)
				_controlsResumedFrame = Time.frameCount;
		}

		public void SetMovementLocked(bool locked)
		{
			_movementLocked = locked;
			if (!locked)
				return;

			_planarVelocity = _planarAcceleration = Vector2.zero;
			_crouchVelocity = 0f;
			_audibleStepDistance = 0f;
		}

		public void BeginEncounterLook(Vector3 point)
		{
			_encounterLookActive = true;
			_limitEncounterLook = true;
			_encounterLookCenterYaw = 0f;
			_encounterLookYaw = ClampEncounterYaw(_aimSwayDegrees.x);
			_encounterLookInputWeight = 1f;
			_encounterEntryFocus = point;
			_encounterEntryRotation = transform.rotation;
			_encounterEntryVelocity = Vector3.ProjectOnPlane(_controller.velocity, Vector3.up);
			_encounterEntryElapsed = 0f;
			_pendingLookRotation = _lookVelocity = Vector2.zero;
			_planarVelocity = _planarAcceleration = Vector2.zero;
			_audibleStepDistance = 0f;
			_remainingLookRecoil = Vector2.zero;
			_lookRecoilTimeRemaining = 0f;
			_cinemachineTargetPitch = Mathf.Clamp(_cinemachineTargetPitch + _aimSwayDegrees.y,
				_lookUpLimit, _lookDownLimit);
			_encounterEntryPitch = _encounterLookCenterPitch = _cinemachineTargetPitch;
			if (CinemachineCameraTarget != null)
				CinemachineCameraTarget.transform.localRotation = GetViewRotation(LeanRollRotation);
		}

		public void EndEncounterLook()
		{
			if (!_encounterLookActive)
				return;

			transform.Rotate(Vector3.up * Mathf.Lerp(_encounterLookCenterYaw, _encounterLookYaw, _encounterLookInputWeight));
			_cinemachineTargetPitch = Mathf.Lerp(_encounterLookCenterPitch, _cinemachineTargetPitch, _encounterLookInputWeight);
			_encounterLookActive = false;
			_encounterLookYaw = 0f;
			_pendingLookRotation = _lookVelocity = Vector2.zero;
			_controlsResumedFrame = Time.frameCount;
			if (CinemachineCameraTarget != null)
				CinemachineCameraTarget.transform.localRotation = GetViewRotation(LeanRollRotation);
		}

		public void AllowFreeEncounterLook() => _limitEncounterLook = false;

		public void SetEncounterLookRotation(Quaternion worldRotation, float inputWeight)
		{
			if (!_encounterLookActive || CinemachineCameraTarget == null)
				return;

			_encounterEntryElapsed = EncounterEntryDuration;
			Vector3 forward = Quaternion.Inverse(transform.rotation) * (worldRotation * Vector3.forward);
			float yawOffset = _encounterLookYaw - _encounterLookCenterYaw;
			float pitchOffset = _cinemachineTargetPitch - _encounterLookCenterPitch;
			_encounterLookCenterYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
			_encounterLookCenterPitch = Mathf.Clamp(-Mathf.Atan2(forward.y,
				new Vector2(forward.x, forward.z).magnitude) * Mathf.Rad2Deg, _lookUpLimit, _lookDownLimit);
			_encounterLookYaw = ClampEncounterYaw(_encounterLookCenterYaw + yawOffset);
			_cinemachineTargetPitch = Mathf.Clamp(_encounterLookCenterPitch + pitchOffset, CurrentLookUp, CurrentLookDown);
			_encounterLookInputWeight = inputWeight;
			CinemachineCameraTarget.transform.localRotation = GetViewRotation(LeanRollRotation);
		}

		public void AddLookRecoil(float upwardDegrees, float sidewaysDegrees)
		{
			var recoil = new Vector2(sidewaysDegrees, -upwardDegrees);
			if (!isActiveAndEnabled || recoil == Vector2.zero)
				return;

			_remainingLookRecoil += recoil;
			_lookRecoilTimeRemaining = LookRecoilDuration;
		}

		public void SetAimSway(Vector2 offsetDegrees)
		{
			if (_aimSwayDegrees.Equals(offsetDegrees))
				return;

			_aimSwayDegrees = offsetDegrees;
			if (!_controlsFrozen && CinemachineCameraTarget != null)
				CinemachineCameraTarget.transform.localRotation = GetViewRotation(LeanRollRotation);
		}

		private Quaternion LeanRollRotation =>
			Quaternion.AngleAxis(-_leanAngle * _leanRoll, Vector3.forward);
		private float CurrentLookUp => _encounterLookActive && _limitEncounterLook
			? Mathf.Max(_lookUpLimit, _encounterLookCenterPitch - EncounterLookPitchLimit) : _lookUpLimit;
		private float CurrentLookDown => _encounterLookActive && _limitEncounterLook
			? Mathf.Min(_lookDownLimit, _encounterLookCenterPitch + EncounterLookPitchLimit) : _lookDownLimit;
		private float ClampEncounterYaw(float yaw) => _limitEncounterLook
			? Mathf.Clamp(yaw, -EncounterLookYawLimit, EncounterLookYawLimit) : yaw;

		private Quaternion GetViewRotation(Quaternion bodyRoll)
		{
			if (_encounterLookActive)
			{
				float encounterPitch = Mathf.Lerp(_encounterLookCenterPitch, _cinemachineTargetPitch, _encounterLookInputWeight);
				float encounterYaw = Mathf.Lerp(_encounterLookCenterYaw, _encounterLookYaw, _encounterLookInputWeight);
				return Quaternion.Euler(encounterPitch, encounterYaw, 0f) * bodyRoll;
			}

			float pitch = Mathf.Clamp(_cinemachineTargetPitch + _aimSwayDegrees.y, _lookUpLimit, _lookDownLimit);
			return Quaternion.Euler(pitch, _aimSwayDegrees.x, 0f) * bodyRoll;
		}

		private bool IsCurrentDeviceMouse
		{
			get
			{
				#if ENABLE_INPUT_SYSTEM
				string scheme = _playerInput.currentControlScheme;
				return !_input.lookFromTouch && (scheme == null || scheme == "KeyboardMouse");
				#else
				return !_input.lookFromTouch;
				#endif
			}
		}

		private void Awake()
		{
			// get a reference to our main camera
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
		}

		private void Start()
		{
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
			_playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

			_standingHeight = _controller.height;
			_standingCenter = _controller.center;
			var hitboxObject = new GameObject("Player Bullet Hitbox");
			hitboxObject.layer = LayerMask.NameToLayer("Ignore Raycast");
			hitboxObject.transform.SetParent(transform, false);
			hitboxObject.transform.localPosition = _standingCenter;
			BulletHitbox = hitboxObject.AddComponent<CapsuleCollider>();
			BulletHitbox.isTrigger = true;
			BulletHitbox.radius = _controller.radius;
			BulletHitbox.height = _standingHeight;
			_smoothedCameraFollowY = transform.position.y;
			if (CinemachineCameraTarget != null)
				_standingCameraPosition = CinemachineCameraTarget.transform.localPosition;

			if (_capsuleModel != null)
			{
				_standingModelPosition = _capsuleModel.localPosition;
				_standingModelScale = _capsuleModel.localScale;
				_standingModelRotation = _capsuleModel.localRotation;
			}

		}

		private void OnEnable()
		{
			CinemachineCore.CameraUpdatedEvent.AddListener(KeepCameraOutsideWalls);
			if (BulletHitbox != null)
				BulletHitbox.enabled = true;
		}

		private void OnDisable()
		{
			CinemachineCore.CameraUpdatedEvent.RemoveListener(KeepCameraOutsideWalls);
			if (BulletHitbox != null)
				BulletHitbox.enabled = false;
			_pendingLookRotation = _lookVelocity = Vector2.zero;
			_remainingLookRecoil = Vector2.zero;
			_lookRecoilTimeRemaining = 0f;
			SetAimSway(Vector2.zero);
			_planarVelocity = _planarAcceleration = Vector2.zero;
			_audibleStepDistance = 0f;
		}

		private void Update()
		{
			if (_controlsFrozen || _controlsResumedFrame == Time.frameCount ||
				Time.timeScale == 0f || Time.deltaTime <= 0f)
				return;

			if (_encounterLookActive)
			{
				UpdateEncounterEntry(Time.deltaTime);
				ApplyPendingLookRecoil(Time.deltaTime);
				CameraRotation();
				UpdateCameraPose(0f);
				return;
			}

#if ENABLE_INPUT_SYSTEM
			var keyboard = Keyboard.current;
			bool leanRight = IsKeyHeld(keyboard, _leanRightKey);
			bool leanLeft = IsKeyHeld(keyboard, _leanLeftKey);
			bool jumpPressed = keyboard?.spaceKey.wasPressedThisFrame == true;
#else
			bool leanRight = Input.GetKey(KeyCode.C);
			bool leanLeft = Input.GetKey(KeyCode.Z);
			bool jumpPressed = Input.GetKeyDown(KeyCode.Space);
#endif
			float desiredLean = _movementLocked ? 0f : (leanRight ? 1f : 0f) - (leanLeft ? 1f : 0f);
			if (!_movementLocked)
				UpdateCrouch();
			GroundedCheck();
			UpdateVerticalVelocity(jumpPressed && !_movementLocked && !LookInputBlocked);
			ApplyPendingLookRecoil(Time.deltaTime);
			CameraRotation();
			Move(leanRight || leanLeft || Mathf.Abs(_leanProgress) > 0.01f);
			UpdateCameraPose(desiredLean);
		}

		private void UpdateEncounterEntry(float deltaTime)
		{
			GroundedCheck();
			UpdateVerticalVelocity(false);

			float previous = _encounterEntryElapsed;
			_encounterEntryElapsed = Mathf.Min(previous + deltaTime, EncounterEntryDuration);
			float coast = EncounterBrakeDistance(_encounterEntryElapsed) - EncounterBrakeDistance(previous);
			CollisionFlags collisions = _controller.Move(_encounterEntryVelocity * coast +
				Vector3.up * (_verticalVelocity * deltaTime));
			if ((collisions & CollisionFlags.Above) != 0 && _verticalVelocity > 0f)
				_verticalVelocity = 0f;

			if (previous >= EncounterEntryDuration || CinemachineCameraTarget == null)
				return;

			Vector3 offset = _encounterEntryFocus - CinemachineCameraTarget.transform.position;
			Vector3 forward = Vector3.ProjectOnPlane(offset, Vector3.up);
			if (forward.sqrMagnitude < 0.001f)
				return;

			float weight = Mathf.SmoothStep(0f, 1f, _encounterEntryElapsed / EncounterEntryDuration);
			transform.rotation = Quaternion.Slerp(_encounterEntryRotation, Quaternion.LookRotation(forward), weight);
			float aimPitch = Mathf.Clamp(-Mathf.Atan2(offset.y, forward.magnitude) * Mathf.Rad2Deg,
				_lookUpLimit, _lookDownLimit);
			float pitchOffset = _cinemachineTargetPitch - _encounterLookCenterPitch;
			_encounterLookCenterPitch = Mathf.Lerp(_encounterEntryPitch, aimPitch, weight);
			_cinemachineTargetPitch = Mathf.Clamp(_encounterLookCenterPitch + pitchOffset,
				CurrentLookUp, CurrentLookDown);
		}

		private static float EncounterBrakeDistance(float elapsed)
		{
			float t = Mathf.Clamp01(elapsed / EncounterBrakeDuration);
			return EncounterBrakeDuration * (t - t * t * t * (1f - 0.5f * t));
		}

		private void UpdateCameraPose(float desiredLean)
		{
			if (CinemachineCameraTarget == null || Time.deltaTime <= 0f)
				return;

			// Smooth the view while the capsule responds to collisions immediately.
			float playerY = transform.position.y;
			_smoothedCameraFollowY = Mathf.SmoothDamp(_smoothedCameraFollowY, playerY,
				ref _cameraFollowVelocity, _verticalCameraSmoothTime);
			_smoothedCameraFollowY = Mathf.Clamp(_smoothedCameraFollowY,
				playerY - MaxCameraFollowOffset, playerY + MaxCameraFollowOffset);

			var cameraPosition = _standingCameraPosition - Vector3.up * (_crouchCameraDrop * _crouchProgress);
			var target = CinemachineCameraTarget.transform;
			Vector3 followOffset = Vector3.up * (_smoothedCameraFollowY - playerY);
			if (target.parent != null)
				followOffset = target.parent.InverseTransformVector(followOffset);
			target.localPosition = cameraPosition + followOffset;

			_neutralCameraPosition = target.position;
			float lean = UpdateLean(desiredLean);
			// Position must respect walls immediately; rolling the view can still ease into the safe pose.
			_leanRoll = Mathf.SmoothDamp(_leanRoll, lean, ref _leanRollVelocity, _leanRollSmoothTime);
			Vector3 leanOffset = transform.right * (_leanDistance * lean);
			Quaternion bodyRoll = LeanRollRotation;
			target.position += leanOffset;
			target.localRotation = GetViewRotation(bodyRoll);

			// The torso moves with the lean while the lower end stays closer to the feet.
			BulletHitbox.transform.SetLocalPositionAndRotation(
				_controller.center + transform.InverseTransformVector(leanOffset) * 0.5f, bodyRoll);

			if (_capsuleModel != null)
				_capsuleModel.localRotation = bodyRoll * _standingModelRotation;
		}

		private float UpdateLean(float desiredLean)
		{
			_leanProgress = Mathf.SmoothDamp(_leanProgress, desiredLean, ref _leanVelocity,
				_leanTransitionTime / 3f);
			float lean = _leanProgress;
			if (Mathf.Abs(lean - desiredLean) < 0.001f && Mathf.Abs(_leanVelocity) < 0.01f)
			{
				_leanProgress = lean = desiredLean;
				_leanVelocity = 0f;
			}
			float distance = Mathf.Abs(lean) * _leanDistance;
			float side = Mathf.Sign(lean);
			if (distance <= 0f || side != _leanDirection)
			{
				_leanWallCorrection = _leanWallCorrectionVelocity = 0f;
				_cameraWallCorrection = _cameraWallCorrectionVelocity = 0f;
				_leanDirection = side;
			}
			if (distance <= 0f)
				return 0f;

			Vector3 direction = transform.right * side;
			float safeDistance = GetCameraTravel(_neutralCameraPosition, LeanProbeRadius, direction, distance);
			float travel = SmoothCameraTravel(distance, safeDistance, ref _leanWallCorrection,
				ref _leanWallCorrectionVelocity);
			return side * travel / _leanDistance;
		}

		private void KeepCameraOutsideWalls(CinemachineBrain brain)
		{
			if ((_leanProgress == 0f && Mathf.Abs(_leanRoll) < 0.001f) || CinemachineCameraTarget == null
				|| brain.OutputCamera == null || brain.OutputCamera.gameObject != _mainCamera)
				return;

			var camera = brain.OutputCamera;
			Vector3 origin = _neutralCameraPosition;
			Vector3 cameraPosition = camera.transform.position;
			// A sphere enclosing the near plane stays valid when the view rolls or turns.
			Vector3 nearCorner = camera.ViewportToWorldPoint(new Vector3(1f, 1f, camera.nearClipPlane));
			float radius = Vector3.Distance(nearCorner, cameraPosition) + CameraWallMargin;
			if (CameraOverlapsWall(origin, radius))
				origin = transform.TransformPoint(_controller.center);

			Vector3 displacement = cameraPosition - origin;
			float distance = displacement.magnitude;
			if (distance <= 0f)
				return;

			Vector3 direction = displacement / distance;
			float safeDistance = GetCameraTravel(origin, radius, direction, distance);
			float travel = SmoothCameraTravel(distance, safeDistance, ref _cameraWallCorrection,
				ref _cameraWallCorrectionVelocity);
			camera.transform.position = origin + direction * travel;
		}

		private static float SmoothCameraTravel(float distance, float safeDistance,
			ref float correction, ref float correctionVelocity)
		{
			float requiredCorrection = distance - safeDistance;
			// Retract immediately at a wall and ease outward as the obstruction clears.
			if (requiredCorrection >= correction)
			{
				correction = requiredCorrection;
				correctionVelocity = 0f;
			}
			else if (Time.deltaTime > 0f)
			{
				correction = Mathf.SmoothDamp(correction, requiredCorrection,
					ref correctionVelocity, LeanClearanceSmoothTime);
			}

			return Mathf.Clamp(distance - correction, 0f, safeDistance);
		}

		private bool CameraOverlapsWall(Vector3 position, float radius)
		{
			foreach (var collider in Physics.OverlapSphere(position, radius,
				Physics.AllLayers, QueryTriggerInteraction.Ignore))
			{
				if (!collider.transform.IsChildOf(transform))
					return true;
			}

			return false;
		}

		private float GetCameraTravel(Vector3 origin, float radius, Vector3 direction, float distance)
		{
			var hits = Physics.SphereCastAll(origin, radius, direction, distance,
				Physics.AllLayers, QueryTriggerInteraction.Ignore);
			foreach (var hit in hits)
			{
				if (hit.collider.transform.IsChildOf(transform))
					continue;

				distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - CameraWallMargin));
			}

			return distance;
		}

		private bool IsControlHeld()
		{
#if ENABLE_INPUT_SYSTEM
			var keyboard = Keyboard.current;
			return IsKeyHeld(keyboard, _crouchKey) || IsKeyHeld(keyboard, _alternateCrouchKey);
#else
			return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
		}

		private void UpdateCrouch()
		{
			bool wantsToCrouch = !ControlSprintEnabled && IsControlHeld();
			bool blockedAbove = _crouchProgress > 0f && (!wantsToCrouch || _crouchVelocity < 0f)
				&& !HasStandingClearance();
			bool stayCrouched = wantsToCrouch || blockedAbove;
			float targetProgress = stayCrouched ? 1f : 0f;
			// Keep transition velocity when the key changes, but never keep rising into a ceiling.
			if (blockedAbove && _crouchVelocity < 0f)
				_crouchVelocity = 0f;
			float progress = Mathf.SmoothDamp(_crouchProgress, targetProgress,
				ref _crouchVelocity, _crouchTransitionTime * 0.5f);
			if (Mathf.Abs(progress - targetProgress) < 0.0001f && Mathf.Abs(_crouchVelocity) < 0.001f)
			{
				progress = targetProgress;
				_crouchVelocity = 0f;
			}
			if (progress == _crouchProgress)
				return;

			_crouchProgress = progress;
			float crouchingHeight = Mathf.Clamp(_crouchHeight, _controller.radius * 2f, _standingHeight);
			float height = Mathf.Lerp(_standingHeight, crouchingHeight, progress);
			float heightDifference = _standingHeight - height;
			_controller.height = height;
			_controller.center = _standingCenter - Vector3.up * (heightDifference * 0.5f);
			BulletHitbox.height = height;
			if (CinemachineCameraTarget == null)
				BulletHitbox.transform.localPosition = _controller.center;

			if (_capsuleModel != null)
			{
				_capsuleModel.localPosition = _standingModelPosition - Vector3.up * (heightDifference * 0.5f);
				var modelScale = _standingModelScale;
				modelScale.y *= height / _standingHeight;
				_capsuleModel.localScale = modelScale;
			}
		}

		private bool HasStandingClearance()
		{
			// Check the volume swept by the upper hemisphere as the capsule grows.
			float radius = _controller.radius;
			Vector3 crouchingTop = _controller.center + Vector3.up * (_controller.height * 0.5f - radius);
			Vector3 standingTop = _standingCenter + Vector3.up * (_standingHeight * 0.5f - radius);
			Vector3 scale = transform.lossyScale;
			float worldRadius = radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
			float skinWidth = _controller.skinWidth * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
			var overlaps = Physics.OverlapCapsule(transform.TransformPoint(crouchingTop),
				transform.TransformPoint(standingTop), Mathf.Max(0.001f, worldRadius - skinWidth),
				Physics.AllLayers, QueryTriggerInteraction.Ignore);

			foreach (var obstacle in overlaps)
			{
				if (obstacle.transform.IsChildOf(transform))
					continue;
				if (!Physics.GetIgnoreLayerCollision(gameObject.layer, obstacle.gameObject.layer))
					return false;
			}

			return true;
		}

		private void GroundedCheck()
		{
			// set sphere position, with offset
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
		}

		private void ApplyPendingLookRecoil(float deltaTime)
		{
			if (_lookRecoilTimeRemaining <= 0f)
				return;

			// Ease out the kick and commit it to the look angles so the player has to correct it.
			float timeRemaining = Mathf.Max(0f, _lookRecoilTimeRemaining - deltaTime);
			float remainingFraction = timeRemaining / _lookRecoilTimeRemaining;
			Vector2 recoil = _remainingLookRecoil * (1f - remainingFraction * remainingFraction);
			_remainingLookRecoil -= recoil;
			_lookRecoilTimeRemaining = timeRemaining;
			if (_encounterLookActive)
			{
				_encounterLookYaw = ClampEncounterYaw(_encounterLookYaw + recoil.x);
				_cinemachineTargetPitch = Mathf.Clamp(_cinemachineTargetPitch + recoil.y, CurrentLookUp, CurrentLookDown);
			}
			else
			{
				_cinemachineTargetPitch = Mathf.Clamp(_cinemachineTargetPitch + recoil.y, _lookUpLimit, _lookDownLimit);
				transform.Rotate(Vector3.up * recoil.x);
			}
		}

		private void CameraRotation()
		{
			if (LookInputBlocked)
			{
				_pendingLookRotation = _lookVelocity = Vector2.zero;
				return;
			}

			float deltaTime = Time.deltaTime;
			if (deltaTime <= 0f)
				return;

			// Mouse input is already a per-frame delta. Keep the remaining angle and angular velocity
			// between frames so starts, stops and direction changes do not reset the smoothing curve.
			float inputScale = RotationSpeed * LookSensitivityMultiplier * (_input.lookFromTouch ? deltaTime : 1f);
			if (IsCurrentDeviceMouse)
				inputScale *= MouseSensitivityScale;
			_pendingLookRotation += _input.look * inputScale;
			float lookUpLimit = CurrentLookUp;
			float lookDownLimit = CurrentLookDown;
			if (_encounterLookActive)
				_pendingLookRotation.x = ClampEncounterYaw(_encounterLookYaw + _pendingLookRotation.x) - _encounterLookYaw;
			_pendingLookRotation.y = Mathf.Clamp(_cinemachineTargetPitch + _pendingLookRotation.y,
				lookUpLimit, lookDownLimit) - _cinemachineTargetPitch;

			Vector2 rotation;
			if (_lookSmoothingTime > 0f)
			{
				rotation = new Vector2(
					Mathf.SmoothDamp(0f, _pendingLookRotation.x, ref _lookVelocity.x,
						_lookSmoothingTime, Mathf.Infinity, deltaTime),
					Mathf.SmoothDamp(0f, _pendingLookRotation.y, ref _lookVelocity.y,
						_lookSmoothingTime, Mathf.Infinity, deltaTime));
			}
			else
			{
				rotation = _pendingLookRotation;
				_lookVelocity = Vector2.zero;
			}
			_pendingLookRotation -= rotation;
			float pitch = _cinemachineTargetPitch + rotation.y;
			_cinemachineTargetPitch = Mathf.Clamp(pitch, lookUpLimit, lookDownLimit);
			if (pitch != _cinemachineTargetPitch)
			{
				_pendingLookRotation.y = 0f;
				_lookVelocity.y = 0f;
			}
			if (_encounterLookActive)
			{
				float yaw = _encounterLookYaw + rotation.x;
				_encounterLookYaw = ClampEncounterYaw(yaw);
				if (yaw != _encounterLookYaw)
					_pendingLookRotation.x = _lookVelocity.x = 0f;
			}
			else
				transform.Rotate(Vector3.up * rotation.x);
		}

		private void Move(bool leaning)
		{
#if ENABLE_INPUT_SYSTEM
			var keyboard = Keyboard.current;
			bool slowWalk = IsKeyHeld(keyboard, _slowWalkKey) || IsKeyHeld(keyboard, _alternateSlowWalkKey);
#else
			bool slowWalk = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
			IsWalkingQuietly = slowWalk;
			float targetSpeed = Mathf.Lerp(MoveSpeed, _crouchSpeed, _crouchProgress);
			if (slowWalk || leaning || IsAiming) targetSpeed = Mathf.Min(targetSpeed, _slowWalkSpeed);
			else if (ControlSprintEnabled && _crouchProgress < 0.05f && IsControlHeld())
				targetSpeed *= 1.65f;

			Vector2 moveInput = _input.analogMovement
				? Vector2.ClampMagnitude(_input.move, 1f)
				: _input.move.normalized;
			if (_movementLocked)
				moveInput = Vector2.zero;
			if (moveInput.y < 0f)
				targetSpeed *= _backwardSpeedMultiplier;
			Vector2 targetVelocity = moveInput * (targetSpeed * MovementSpeedMultiplier);
			// Smooth the whole local velocity, including a release to zero and changes of direction.
			// Collision-reduced controller.velocity must not restart acceleration every frame at a wall.
			_planarVelocity = Vector2.SmoothDamp(_planarVelocity, targetVelocity,
				ref _planarAcceleration, 1f / Mathf.Max(0.01f, SpeedChangeRate));
			if (targetVelocity.sqrMagnitude == 0f && _planarVelocity.sqrMagnitude < 0.000001f
				&& _planarAcceleration.sqrMagnitude < 0.0001f)
			{
				_planarVelocity = _planarAcceleration = Vector2.zero;
			}

			Vector3 velocity = transform.right * _planarVelocity.x + transform.forward * _planarVelocity.y
				+ Vector3.up * _verticalVelocity;
			Vector3 previousPosition = transform.position;
			CollisionFlags collisions = _controller.Move(velocity * Time.deltaTime);
			if ((collisions & CollisionFlags.Above) != 0 && _verticalVelocity > 0f)
				_verticalVelocity = 0f;
			ReportFootstep(previousPosition);
		}

		private void ReportFootstep(Vector3 previousPosition)
		{
			if (IsWalkingQuietly || !_controller.isGrounded)
			{
				_audibleStepDistance = 0f;
				return;
			}

			Vector3 movement = transform.position - previousPosition;
			movement.y = 0f;
			_audibleStepDistance += movement.magnitude;
			if (_audibleStepDistance < DistancePerAudibleStep)
				return;

			_audibleStepDistance %= DistancePerAudibleStep;
			AudibleFootstep?.Invoke(transform.position, movement / Time.deltaTime);
		}

		private void UpdateVerticalVelocity(bool jumpPressed)
		{
			if (_controller.isGrounded && _verticalVelocity <= 0f)
			{
				float height = Mathf.Lerp(_jumpHeight, _crouchJumpHeight, _crouchProgress);
				_verticalVelocity = jumpPressed ? Mathf.Sqrt(2f * Mathf.Max(0f, -Gravity) * height) : -2f;
				return;
			}

			_verticalVelocity = Mathf.Max(TerminalVelocity, _verticalVelocity + Gravity * Time.deltaTime);
		}

#if ENABLE_INPUT_SYSTEM
		private static bool IsKeyHeld(Keyboard keyboard, Key key)
		{
			return keyboard != null && key != Key.None && keyboard[key].isPressed;
		}
#endif

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}
	}
}
