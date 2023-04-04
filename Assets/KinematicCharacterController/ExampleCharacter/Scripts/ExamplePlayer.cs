using FishNet;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Managing.Logging;
using FishNet.Transporting;
using UnityEngine;
using Client;

namespace KinematicCharacterController.Examples
{
	public struct ReconcileData : IReconcileData
	{
		public Vector3 Position;
		public Quaternion Rotation;
		public Vector3 BaseVelocity;

		public bool MustUnground;
		public float MustUngroundTime;
		public bool LastMovementIterationFoundAnyGround;
		public CharacterTransientGroundingReport GroundingStatus;
		public Vector3 AttachedRigidbodyVelocity;

		private uint _tick;
		public void Dispose() { }
		public uint GetTick() => _tick;
		public void SetTick(uint value) => _tick = value;
	}

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
				Destroy(GetComponent<KinematicCharacterMotor>());
				Destroy(GetComponent<ExampleCharacterController>());
				GetComponent<Rigidbody>().isKinematic = true;
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
				HandleCharacterInput(out PlayerCharacterInputs inputData);
				Replicate(inputData, false);
			}
			if (base.IsServer)
			{
				Replicate(default, true);
				KinematicCharacterMotorState state = Character.Motor.GetState();
				Reconcile(TranslateReconcileData(state), true);
			}
			if (!base.IsOwner && !base.IsServer)
				GetComponent<Rigidbody>().isKinematic = true;
		}

		[Replicate]
		private void Replicate(PlayerCharacterInputs input, bool asServer, Channel channel = Channel.Unreliable, bool replaying = false)
		{
			Character.SetInputs(ref input);

			//Quang: When Fishnet reconcile, it will run this Replicate method as replay in order for redo missing input,
			// so we have to simulate KCC tick manually here, but only when replaying
			if (replaying)
			{
				KinematicCharacterSystem.SimulateThisTick((float)base.TimeManager.TickDelta);
			}
		}

		[Reconcile]
		private void Reconcile(ReconcileData rd, bool asServer, Channel channel = Channel.Unreliable)
		{
			//Quang: Note - KCCMotorState has Rigidbody field, this component is not serialized, 
			// and doesn't have to be reconciled, so we build a new Reconcile data that exclude Rigidbody field
			Character.Motor.ApplyState(TranslateStateData(rd));
		}
		private ReconcileData TranslateReconcileData(KinematicCharacterMotorState state)
		{
			ReconcileData rd = new ReconcileData();
			rd.Position = state.Position;
			rd.Rotation = state.Rotation;
			rd.BaseVelocity = state.BaseVelocity;

			rd.MustUnground = state.MustUnground;
			rd.MustUngroundTime = state.MustUngroundTime;
			rd.LastMovementIterationFoundAnyGround = state.LastMovementIterationFoundAnyGround;
			rd.GroundingStatus = state.GroundingStatus;
			rd.AttachedRigidbodyVelocity = state.AttachedRigidbodyVelocity;

			return rd;
		}

		private KinematicCharacterMotorState TranslateStateData(ReconcileData rd)
		{
			KinematicCharacterMotorState state = new KinematicCharacterMotorState();
			state.Position = rd.Position;
			state.Rotation = rd.Rotation;
			state.BaseVelocity = rd.BaseVelocity;

			state.MustUnground = rd.MustUnground;
			state.MustUngroundTime = rd.MustUngroundTime;
			state.LastMovementIterationFoundAnyGround = rd.LastMovementIterationFoundAnyGround;
			state.GroundingStatus = rd.GroundingStatus;
			state.AttachedRigidbodyVelocity = rd.AttachedRigidbodyVelocity;

			return state;
		}

		private void LateUpdate()
		{
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
			CharacterCamera.UpdateWithInput(Time.deltaTime, scrollInput, lookInputVector);

			// Handle toggling zoom level
			if (Input.GetMouseButtonDown(1))
			{
				CharacterCamera.TargetDistance = (CharacterCamera.TargetDistance == 0f) ? CharacterCamera.DefaultDistance : 0f;
			}

			if (CharacterCamera.TargetDistance == 0f)
				SetOrientationMethod(OrientationMethod.TowardsCamera);
			else
				SetOrientationMethod(OrientationMethod.TowardsMovement);
		}

		[ServerRpc(RunLocally = true)]
		private void SetOrientationMethod(OrientationMethod method)
		{
			Character.OrientationMethod = method;
		}

		private void HandleCharacterInput(out PlayerCharacterInputs characterInputs)
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

			//Quang: Should add jump queued function in order for not missing input, here I use get key for quick demo, should not use in final project
			characterInputs.JumpDown = Input.GetKeyDown(KeyCode.Space);

			characterInputs.CrouchDown = Input.GetKeyDown(KeyCode.C);
			characterInputs.CrouchUp = Input.GetKeyUp(KeyCode.C);
		}
	}
}