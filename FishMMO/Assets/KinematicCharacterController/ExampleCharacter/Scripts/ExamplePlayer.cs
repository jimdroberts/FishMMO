using FishNet;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Managing.Logging;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Client;

namespace KinematicCharacterController.Examples
{
	public class ExamplePlayer : NetworkBehaviour
	{
		public ExampleCharacterController Character;
		public ExampleCharacterCamera CharacterCamera;

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

			//Quang: Subscribe to tick event, this will replace FixedUpdate
			InstanceFinder.TimeManager.OnTick += TimeManager_OnTick;
		}

		void OnDestroy()
		{
			if (InstanceFinder.TimeManager != null)
			{
				InstanceFinder.TimeManager.OnTick -= TimeManager_OnTick;
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
					CharacterCamera = mc.gameObject.GetComponent<ExampleCharacterCamera>();
					if (CharacterCamera != null)
					{
						CharacterCamera.SetFollowTransform(Character.CameraFollowPoint);
						CharacterCamera.IgnoredColliders.Clear();
						CharacterCamera.IgnoredColliders.AddRange(Character.GetComponentsInChildren<Collider>());
					}
				}
			}
			//Quang: The remote object must not have movement related logic code, destroy it. Network transform will handle the movements
			else if (!base.IsServer)
			{
				KinematicCharacterMotor motor = GetComponent<KinematicCharacterMotor>();
				if (motor != null)
				{
					motor.enabled = false;
				}
				ExampleCharacterController controller = GetComponent<ExampleCharacterController>();
				if (controller != null)
				{
					controller.enabled = false;
				}
				Rigidbody rb = GetComponent<Rigidbody>();
				if (rb != null)
				{
					rb.isKinematic = true;
				}
			}
		}

		[Client(Logging = LoggingType.Off)]
		private bool CanUpdateInput()
		{
			return !UIManager.ControlHasFocus() && !UIManager.InputControlHasFocus() && !InputManager.MouseMode;
		}

		private void TimeManager_OnTick()
		{
			if (base.IsOwner)
			{
				Reconcile(default, false);
				HandleCharacterInput(out ExampleCharacterInputReplicateData inputData);
				Replicate(inputData, false);
			}
			if (base.IsServer)
			{
				Replicate(default, true);
				KinematicCharacterMotorState state = Character.GetState();
				Reconcile(state, true);
			}
		}

		[Replicate]
		private void Replicate(ExampleCharacterInputReplicateData input, bool asServer, Channel channel = Channel.Unreliable, bool replaying = false)
		{
			Character.SetInputs(ref input);

			SimulateMotor((float)base.TimeManager.TickDelta);
		}

		[Reconcile]
		private void Reconcile(KinematicCharacterMotorState rd, bool asServer, Channel channel = Channel.Unreliable)
		{
			//Quang: Note - KCCMotorState has Rigidbody field, this component is not serialized, 
			// and doesn't have to be reconciled, so we build a new Reconcile data that exclude Rigidbody field
			Character.ApplyState(rd);
		}

		private void SimulateMotor(float deltaTime)
		{
			Character.Motor.UpdatePhase1(deltaTime);
			Character.Motor.UpdatePhase2(deltaTime);

			Character.Motor.Transform.SetPositionAndRotation(Character.Motor.TransientPosition, Character.Motor.TransientRotation);
		}

		private void Update()
		{
			if (!base.IsOwner)
			{
				return;
			}

			if (InputManager.GetKeyDown(JumpInput) && !Character.IsJumping)
			{
				_jumpQueued = true;
			}
			
			_crouchInputActive = InputManager.GetKey(CrouchInput);

			_sprintInputActive = InputManager.GetKey(RunInput);
		}

		private void LateUpdate()
		{
			if (!base.IsOwner)
			{
				return;
			}
			// Handle rotating the camera along with physics movers
			if (CharacterCamera != null && CharacterCamera.RotateWithPhysicsMover && Character.Motor.AttachedRigidbody != null)
			{
				PhysicsMover mover = Character.Motor.AttachedRigidbody.GetComponent<PhysicsMover>();
				if (mover != null)
				{
					CharacterCamera.PlanarDirection = mover.RotationDeltaFromInterpolation * CharacterCamera.PlanarDirection;
					CharacterCamera.PlanarDirection = Vector3.ProjectOnPlane(CharacterCamera.PlanarDirection, Character.Motor.CharacterUp).normalized;
				}
			}

			HandleCameraInput();
		}

		private void HandleCameraInput()
		{
			if (!base.IsOwner) return;

			// Create the look input vector for the camera
			float mouseLookAxisUp = InputManager.GetAxis(MouseYInput);
			float mouseLookAxisRight = InputManager.GetAxis(MouseXInput);
			Vector3 lookInputVector = new Vector3(mouseLookAxisRight, mouseLookAxisUp, 0f);

			// Prevent moving the camera while the cursor isn't locked
			if (Cursor.lockState != CursorLockMode.Locked)
			{
				lookInputVector = Vector3.zero;
			}

			// Input for zooming the camera (disabled in WebGL because it can cause problems)
			float scrollInput = -InputManager.GetAxis(MouseScrollInput);
#if UNITY_WEBGL
			scrollInput = 0f;
#endif

			// Apply inputs to the camera
			CharacterCamera.UpdateWithInput((float)base.TimeManager.TickDelta, scrollInput, lookInputVector);

			// Handle toggling zoom level
			if (InputManager.GetKeyDown(ToggleFirstPersonInput))
			{
				CharacterCamera.TargetDistance = (CharacterCamera.TargetDistance == 0f) ? CharacterCamera.DefaultDistance : 0f;
			}

			SetOrientationMethod(Character.OrientationMethod);
		}

		[ServerRpc(RunLocally = true)]
		private void SetOrientationMethod(OrientationMethod method)
		{
			Character.OrientationMethod = method;
		}

		private void HandleCharacterInput(out ExampleCharacterInputReplicateData characterInputs)
		{
			characterInputs = default;

			// always handle rotation
			characterInputs.CameraRotation = CharacterCamera.Transform.rotation;

			// we can't change input if the UI is open
			if (!CanUpdateInput())
			{
				return;
			}

			// Build the CharacterInputs struct
			characterInputs.MoveAxisForward = InputManager.GetAxis(VerticalInput);
			characterInputs.MoveAxisRight = InputManager.GetAxis(HorizontalInput);
			characterInputs.JumpDown = _jumpQueued;
			characterInputs.CrouchActive = _crouchInputActive;
			characterInputs.SprintActive = _sprintInputActive;

			// Reset the queued inputs
			_jumpQueued = false;
		}
	}
}