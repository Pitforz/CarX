using Unity.Cinemachine;
using UnityEngine;
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
		[Tooltip("Sprint speed of the character in m/s")]
		public float SprintSpeed = 6.0f;
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

		[Header("Crouch")]
		[SerializeField, Min(0f)] private float _crouchHeight = 1.2f;
		[SerializeField, Min(0f)] private float _crouchSpeed = 2f;
		[SerializeField, Min(0f)] private float _crouchCameraDrop = 0.35f;
		[SerializeField, Min(0.01f)] private float _crouchTransitionTime = 0.28f;
		[SerializeField] private Transform _capsuleModel;

		[Header("Lean")]
		[SerializeField, Min(0f)] private float _leanDistance = 0.62f;
		[SerializeField, Range(0f, 75f)] private float _leanAngle = 30f;
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
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 90.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -90.0f;

		// cinemachine
		private float _cinemachineTargetPitch;

		// player
		private Vector2 _planarVelocity;
		private Vector2 _planarAcceleration;
		private Vector2 _pendingLookRotation;
		private Vector2 _lookVelocity;
		private float _verticalVelocity;
		private const float TerminalVelocity = -53f;

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
		public float LeanAmount { get; private set; }
		public float MouseSensitivityScale { get; set; } = 1f;
		public float LookSensitivityMultiplier { get; set; } = 1f;
		public bool LookInputBlocked { get; set; }

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
		}

		private void OnDisable()
		{
			CinemachineCore.CameraUpdatedEvent.RemoveListener(KeepCameraOutsideWalls);
			_pendingLookRotation = _lookVelocity = Vector2.zero;
			_planarVelocity = _planarAcceleration = Vector2.zero;
		}

		private void Update()
		{
			if (Time.timeScale == 0f || Time.deltaTime <= 0f)
				return;

#if ENABLE_INPUT_SYSTEM
			var keyboard = Keyboard.current;
			bool leanRight = IsKeyHeld(keyboard, _leanRightKey);
			bool leanLeft = IsKeyHeld(keyboard, _leanLeftKey);
#else
			bool leanRight = Input.GetKey(KeyCode.C);
			bool leanLeft = Input.GetKey(KeyCode.Z);
#endif
			float desiredLean = (leanRight ? 1f : 0f) - (leanLeft ? 1f : 0f);
			UpdateCrouch();
			GroundedCheck();
			ApplyGravity();
			CameraRotation();
			Move(leanRight || leanLeft || Mathf.Abs(_leanProgress) > 0.01f);
			UpdateCameraPose(desiredLean);
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
			LeanAmount = Mathf.Abs(_leanRoll);
			target.position += transform.right * (_leanDistance * lean);
			target.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0f, 0f)
				* Quaternion.AngleAxis(-_leanAngle * _leanRoll, Vector3.forward);

			if (_capsuleModel != null)
				_capsuleModel.localRotation = Quaternion.AngleAxis(-_leanAngle * _leanRoll, Vector3.forward)
					* _standingModelRotation;
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

		private void UpdateCrouch()
		{
#if ENABLE_INPUT_SYSTEM
			var keyboard = Keyboard.current;
			bool wantsToCrouch = IsKeyHeld(keyboard, _crouchKey) || IsKeyHeld(keyboard, _alternateCrouchKey);
#else
			bool wantsToCrouch = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
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
			_pendingLookRotation.y = Mathf.Clamp(_cinemachineTargetPitch + _pendingLookRotation.y,
				BottomClamp, TopClamp) - _cinemachineTargetPitch;

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
			_cinemachineTargetPitch = Mathf.Clamp(pitch, BottomClamp, TopClamp);
			if (pitch != _cinemachineTargetPitch)
			{
				_pendingLookRotation.y = 0f;
				_lookVelocity.y = 0f;
			}
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
			float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;
			targetSpeed = Mathf.Lerp(targetSpeed, _crouchSpeed, _crouchProgress);
			if (slowWalk || leaning) targetSpeed = Mathf.Min(targetSpeed, _slowWalkSpeed);

			Vector2 moveInput = _input.analogMovement
				? Vector2.ClampMagnitude(_input.move, 1f)
				: _input.move.normalized;
			Vector2 targetVelocity = moveInput * targetSpeed;
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
			_controller.Move(velocity * Time.deltaTime);
		}

		private void ApplyGravity()
		{
			_verticalVelocity = Grounded
				? -2f
				: Mathf.Max(TerminalVelocity, _verticalVelocity + Gravity * Time.deltaTime);
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
