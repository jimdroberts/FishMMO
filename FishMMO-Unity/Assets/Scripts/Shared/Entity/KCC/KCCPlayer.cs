using FishNet;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Managing.Logging;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Client;
using KinematicCharacterController;

namespace FishMMO.Shared
{
	public class KCCPlayer : NetworkBehaviour
	{
		public KCCController CharacterController;
		public KCCCamera CharacterCamera;
		public KinematicCharacterMotor Motor;

		//Quang: Old input system member
		private const string MouseXInput = "Mouse X";
		private const string MouseYInput = "Mouse Y";
		private const string MouseScrollInput = "Mouse ScrollWheel";
		private const string HorizontalInput = "Horizontal";
		private const string VerticalInput = "Vertical";
		private const string JumpInput = "Jump";
		private const string CrouchInput = "Crouch";
		private const string RunInput = "Run";
		private const string ToggleFirstPersonInput = "ToggleFirstPerson";

		private bool _jumpQueued = false;
		private bool _crouchInputActive = false;
		private bool _sprintInputActive = false;

		void Awake()
		{
			KinematicCharacterSystem.EnsureCreation();

			//Quang: Using manual simultion instead of KCC auto simulation
			KinematicCharacterSystem.Settings.AutoSimulation = false;
		}

		public override void OnStartNetwork()
		{
			//Quang: Subscribe to tick event, this will replace FixedUpdate
			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		public override void OnStopNetwork()
		{
			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
		}

#if !UNITY_SERVER
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
			//Quang: The remote client objects must not have movement related logic code, destroy it. Network transform will handle the movements
			/*else
			{
				KinematicCharacterMotor motor = GetComponent<KinematicCharacterMotor>();
				if (motor != null)
				{
					motor.enabled = false;
				}
				KCCController controller = GetComponent<KCCController>();
				if (controller != null)
				{
					controller.enabled = false;
				}
				Rigidbody rb = GetComponent<Rigidbody>();
				if (rb != null)
				{
					rb.isKinematic = true;
				}
			}*/
		}
#endif

		private void TimeManager_OnTick()
		{
			KCCInputReplicateData characterInput = HandleCharacterInput();
			Replicate(characterInput);
			if (base.IsServerStarted)
			{
				KinematicCharacterMotorState state = CharacterController.GetState();
				Reconcile(state);
			}
		}

		[Client(Logging = LoggingType.Off)]
		private bool CanUpdateInput()
		{
			return !InputManager.MouseMode;
		}

		private KCCInputReplicateData HandleCharacterInput()
		{
			if (!base.IsOwner)
			{
				return default;
			}

			// we can't change input if the UI is open or if the mouse cursor is enabled
			if (!CanUpdateInput())
			{
				KCCInputReplicateData characterInputs = default;

				// always handle rotation
				characterInputs.CameraRotation = CharacterCamera.Transform.rotation;

				return characterInputs;
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

			return new KCCInputReplicateData(InputManager.GetAxis(VerticalInput),
											 InputManager.GetAxis(HorizontalInput),
											 moveFlags,
											 CharacterCamera.Transform.position,
											 CharacterCamera.Transform.rotation);
		}

		[ReplicateV2]
		private void Replicate(KCCInputReplicateData input, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
		{
			CharacterController.SetInputs(ref input);

			SimulateMotor((float)base.TimeManager.TickDelta);
		}

		private void SimulateMotor(float deltaTime)
		{
			Motor.UpdatePhase1(deltaTime);
			Motor.UpdatePhase2(deltaTime);

			Motor.Transform.SetPositionAndRotation(Motor.TransientPosition, Motor.TransientRotation);
		}

		[ReconcileV2]
		private void Reconcile(KinematicCharacterMotorState rd, Channel channel = Channel.Unreliable)
		{
			//Quang: Note - KCCMotorState has Rigidbody field, this component is not serialized, 
			// and doesn't have to be reconciled, so we build a new Reconcile data that exclude Rigidbody field
			CharacterController.ApplyState(rd);
		}

		private void Update()
		{
			if (!base.IsOwner)
			{
				return;
			}

			if (InputManager.GetKeyDown(JumpInput) && !CharacterController.IsJumping)
			{
				_jumpQueued = true;
			}

			_crouchInputActive = InputManager.GetKey(CrouchInput);

			_sprintInputActive = InputManager.GetKey(RunInput);
		}

		private void LateUpdate()
		{
			HandleCameraInput();
		}

		private void HandleCameraInput()
		{
			if (!base.IsOwner)
			{
				return;
			}
			if (CharacterCamera == null)
			{
				return;
			}
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
			float mouseLookAxisUp = InputManager.GetAxis(MouseYInput);
			float mouseLookAxisRight = InputManager.GetAxis(MouseXInput);
			Vector3 lookInputVector = new Vector3(mouseLookAxisRight, mouseLookAxisUp, 0f);

			// Prevent moving the camera while the cursor isn't locked
			if (Cursor.lockState != CursorLockMode.Locked)
			{
				lookInputVector = Vector3.zero;
			}

			float scrollInput = 0.0f;
#if !UNITY_WEBGL
			if (CanUpdateInput())
			{
				// Input for zooming the camera (disabled in WebGL because it can cause problems)
				scrollInput = -InputManager.GetAxis(MouseScrollInput);
			}
#endif

			if (CharacterCamera == null)
			{
				return;
			}

			// Apply inputs to the camera
			CharacterCamera.UpdateWithInput((float)base.TimeManager.TickDelta, scrollInput, lookInputVector);

			// Handle toggling zoom level
			if (InputManager.GetKeyDown(ToggleFirstPersonInput))
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