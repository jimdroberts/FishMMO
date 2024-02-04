using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
using KinematicCharacterController;

namespace KCCPredictionV2
{
	[RequireComponent(typeof(NetworkObject))]
	[RequireComponent(typeof(KinematicCharacterMotor))]
	[RequireComponent(typeof(KCCController))]
	[RequireComponent(typeof(KCCPlayer))]
	public class KCCPlayer : NetworkBehaviour
	{
		public KCCController CharacterController;
		public KCCCamera CharacterCamera;
		public KinematicCharacterMotor Motor;

		private const string MouseXInput = "Mouse X";
		private const string MouseYInput = "Mouse Y";
		private const string MouseScrollInput = "Mouse ScrollWheel";
		private const string HorizontalInput = "Horizontal";
		private const string VerticalInput = "Vertical";
		private const KeyCode JumpInput = KeyCode.Space;
		private const KeyCode CrouchInput = KeyCode.LeftControl;
		private const KeyCode RunInput = KeyCode.LeftShift;
		private const KeyCode ToggleFirstPersonInput = KeyCode.F9;

		private bool _jumpQueued = false;
		private bool _crouchInputActive = false;
		private bool _sprintInputActive = false;

		private void Awake()
		{
			Motor = gameObject.GetComponent<KinematicCharacterMotor>();

			CharacterController = gameObject.GetComponent<KCCController>();
			CharacterController.Motor = Motor;
			Motor.CharacterController = CharacterController;

			KCCPlayer player = gameObject.GetComponent<KCCPlayer>();
			player.CharacterController = CharacterController;
			player.Motor = Motor;

			Rigidbody rb = GetComponent<Rigidbody>();
			if (rb != null)
			{
				rb.isKinematic = true;
			}
		}

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		public override void OnStartClient()
		{
			base.OnStartClient();

			if (base.IsOwner)
			{
				Camera mc = Camera.main;
				if (mc != null)
				{
					CharacterCamera = mc.gameObject.GetComponent<KCCCamera>();
					if (CharacterCamera != null)
					{
						CharacterCamera.SetFollowTransform(CharacterController.CameraFollowPoint);
						CharacterCamera.IgnoredColliders.Clear();
						CharacterCamera.IgnoredColliders.AddRange(CharacterController.GetComponentsInChildren<Collider>());
					}
				}
			}
		}

		public override void OnStopNetwork()
		{
			base.OnStopNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
		}

		private void TimeManager_OnTick()
		{
			Replicate(HandleCharacterInput());
			if (base.IsServerStarted)
			{
				KinematicCharacterMotorState state = CharacterController.GetState();
				Reconcile(state);
			}
		}

		private KCCInputReplicateData HandleCharacterInput()
		{
			if (!base.IsOwner ||
				CharacterCamera == null)
			{
				return default;
			}

			int moveFlags = 0;
			if (_jumpQueued)
			{
				moveFlags.EnableBit(KCCMoveFlags.Jump);
				_jumpQueued = false;
			}
			if (_crouchInputActive)
			{
				moveFlags.EnableBit(KCCMoveFlags.Crouch);
			}
			if (_sprintInputActive)
			{
				moveFlags.EnableBit(KCCMoveFlags.Sprint);
			}

			return new KCCInputReplicateData(Input.GetAxis(VerticalInput),
											 Input.GetAxis(HorizontalInput),
											 moveFlags,
											 CharacterCamera.Transform.position,
											 CharacterCamera.Transform.rotation);
		}

		[Replicate]
		private void Replicate(KCCInputReplicateData input, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
		{
			if (state == ReplicateState.Future)
			{
				return;
			}

			CharacterController.SetInputs(ref input);

			// Simulate the KCC Motor
			float delta = (float)base.TimeManager.TickDelta;

			Motor.UpdatePhase1(delta);
			Motor.UpdatePhase2(delta);

			Motor.Transform.SetPositionAndRotation(Motor.TransientPosition, Motor.TransientRotation);
		}

		[Reconcile]
		private void Reconcile(KinematicCharacterMotorState rd, Channel channel = Channel.Unreliable)
		{
			CharacterController.ApplyState(rd);
		}

		private void Update()
		{
			if (!base.IsOwner)
			{
				return;
			}

			if (Input.GetKeyDown(JumpInput) && !CharacterController.IsJumping)
			{
				_jumpQueued = true;
			}

			_crouchInputActive = Input.GetKey(CrouchInput);

			_sprintInputActive = Input.GetKey(RunInput);
		}

		private void LateUpdate()
		{
			if (!base.IsOwner)
			{
				return;
			}
			if (CharacterCamera == null)
			{
				return;
			}

			HandleCameraInput();
		}

		private void HandleCameraInput()
		{
			// Handle rotating the camera along with physics movers
			if (Motor != null && CharacterCamera.RotateWithPhysicsMover && Motor.AttachedRigidbody != null)
			{
				PhysicsMover mover = Motor.AttachedRigidbody.GetComponent<PhysicsMover>();
				if (mover != null)
				{
					CharacterCamera.PlanarDirection = mover.RotationDeltaFromInterpolation * CharacterCamera.PlanarDirection;
					CharacterCamera.PlanarDirection = Vector3.ProjectOnPlane(CharacterCamera.PlanarDirection, Motor.CharacterUp).normalized;
				}
			}

			// Create the look input vector for the camera
			float mouseLookAxisUp = Input.GetAxis(MouseYInput);
			float mouseLookAxisRight = Input.GetAxis(MouseXInput);
			Vector3 lookInputVector = new Vector3(mouseLookAxisRight, mouseLookAxisUp, 0f);

			// Prevent moving the camera while the cursor isn't locked
			if (Cursor.lockState != CursorLockMode.Locked)
			{
				lookInputVector = Vector3.zero;
			}

			float scrollInput = 0.0f;
#if !UNITY_WEBGL
			// Input for zooming the camera (disabled in WebGL because it can cause problems)
			scrollInput = -Input.GetAxis(MouseScrollInput);
#endif

			// Apply inputs to the camera
			CharacterCamera.UpdateWithInput((float)base.TimeManager.TickDelta, scrollInput, lookInputVector);

			// Handle toggling zoom level
			if (Input.GetKeyDown(ToggleFirstPersonInput))
			{
				CharacterCamera.TargetDistance = (CharacterCamera.TargetDistance == 0f) ? CharacterCamera.DefaultDistance : 0f;
			}

			SetOrientationMethod(CharacterController.OrientationMethod);
		}

		[ServerRpc(RunLocally = true)]
		private void SetOrientationMethod(OrientationMethod method)
		{
			CharacterController.OrientationMethod = method;
		}
	}
}