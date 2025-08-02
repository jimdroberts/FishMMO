using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
using System;
using KinematicCharacterController;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Networked player controller for KCC-based movement, prediction, and camera handling.
	/// Manages input replication, platform tracking, and region membership.
	/// </summary>
	public class KCCPlayer : NetworkBehaviour
	{
		/// <summary>
		/// The character controller for movement and state logic.
		/// </summary>
		public KCCController CharacterController;
		/// <summary>
		/// The camera controller for third-person view.
		/// </summary>
		public KCCCamera CharacterCamera;
		/// <summary>
		/// The motor for kinematic character movement.
		/// </summary>
		public KinematicCharacterMotor Motor;

		/// <summary>
		/// Delegate for handling character input (owner only).
		/// </summary>
		public Func<KCCInputReplicateData> OnHandleCharacterInput;

		/// <summary>
		/// The current platform the player is standing on (for moving platforms).
		/// </summary>
		private KCCPlatform currentPlatform;
		/// <summary>
		/// The last known position of the current platform.
		/// </summary>
		private Vector3 lastPlatformPosition;

		/// <summary>
		/// Sets the current platform and updates last position for velocity calculation.
		/// </summary>
		/// <param name="platform">The platform to set.</param>
		public void SetPlatform(KCCPlatform platform)
		{
			currentPlatform = platform;
			if (currentPlatform != null)
			{
				lastPlatformPosition = currentPlatform.transform.position;
			}
		}

		/// <summary>
		/// Tracks all regions the player is currently inside.
		/// </summary>
		public HashSet<Region> CurrentRegions { get; private set; } = new HashSet<Region>();

		/// <summary>
		/// Initializes motor, controller, and rigidbody settings on awake.
		/// </summary>
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

		/// <summary>
		/// Called when the network starts. Subscribes to tick events for input replication.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		/// <summary>
		/// Called when the network stops. Unsubscribes from tick events.
		/// </summary>
		public override void OnStopNetwork()
		{
			base.OnStopNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
		}

#if !UNITY_SERVER
		/// <summary>
		/// Called when the client starts. Sets up camera following and ignored colliders for the owner.
		/// </summary>
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
#endif

		/// <summary>
		/// Called on each network tick. Handles input replication and reconciliation.
		/// </summary>
		private void TimeManager_OnTick()
		{
			KCCInputReplicateData kCCInputReplicateData = !base.IsOwner || OnHandleCharacterInput == null ? default : OnHandleCharacterInput();
			Replicate(kCCInputReplicateData);
			CreateReconcile();
		}

		/// <summary>
		/// Creates a reconcile state for server-side movement correction.
		/// </summary>
		public override void CreateReconcile()
		{
			if (base.IsServerStarted)
			{
				KinematicCharacterMotorState reconcileState = CharacterController.GetState();
				reconcileState.CurrentPlatformID = currentPlatform.ID;
				Reconcile(reconcileState);
			}
		}

		/// <summary>
		/// Stores the last created input data for prediction and reconciliation.
		/// </summary>
		private KCCInputReplicateData lastCreatedData;

		/// <summary>
		/// Replicates input data for movement prediction and correction.
		/// Handles prediction logic and platform velocity calculation.
		/// </summary>
		/// <param name="input">Input data to replicate.</param>
		/// <param name="state">Replication state.</param>
		/// <param name="channel">Network channel.</param>
		[Replicate]
		private void Replicate(KCCInputReplicateData input, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
		{
			// Ignore default data
			// FishNet sends default replicate data occasionally
			if (!input.MoveFlags.IsFlagged(KCCMoveFlags.IsActualData))
			{
				return;
			}

			// Handle prediction and reconciliation for non-server/owner
			if (!base.IsServerStarted && !base.IsOwner)
			{
				if (state.IsFuture())
				{
					uint lastCreatedTick = lastCreatedData.GetTick();
					uint thisTick = input.GetTick();
					uint tickDiff = lastCreatedTick - thisTick;
					if (tickDiff <= 1)
					{
						input.MoveFlags = lastCreatedData.MoveFlags;
						// Don't predict jumping, only crouch and sprint
						input.MoveFlags.DisableBit(KCCMoveFlags.Jump);
						input.CameraPosition = lastCreatedData.CameraPosition;
						input.CameraRotation = lastCreatedData.CameraRotation;
						input.MoveAxisForward = lastCreatedData.MoveAxisForward;
						input.MoveAxisRight = lastCreatedData.MoveAxisRight;
					}
				}
				else if (state.ContainsTicked())
				{
					lastCreatedData.Dispose();
					lastCreatedData = input;
				}
			}

			CharacterController.SetInputs(ref input);

			float deltaTime = (float)base.TimeManager.TickDelta;

			// Calculate platform velocity
			Vector3 platformVelocity = Vector3.zero;
			if (currentPlatform != null)
			{
				Vector3 platformPosition = currentPlatform.transform.position;
				platformVelocity = (platformPosition - lastPlatformPosition) / deltaTime;
				lastPlatformPosition = platformPosition;
			}
			Motor.SetPlatformVelocity(platformVelocity);

			Motor.UpdatePhase1(deltaTime);
			Motor.UpdatePhase2(deltaTime);

			Motor.Transform.SetPositionAndRotation(Motor.TransientPosition, Motor.TransientRotation);
		}

		/// <summary>
		/// Reconciles movement state for server-side correction, updating platform reference.
		/// </summary>
		/// <param name="rd">Reconcile data/state.</param>
		/// <param name="channel">Network channel.</param>
		[Reconcile]
		private void Reconcile(KinematicCharacterMotorState rd, Channel channel = Channel.Unreliable)
		{
			CharacterController.ApplyState(rd);

			if (SceneObject.Objects.TryGetValue(rd.CurrentPlatformID, out ISceneObject sceneObject))
			{
				currentPlatform = sceneObject.GameObject.GetComponent<KCCPlatform>();
			}
			else
			{
				currentPlatform = null;
			}
			if (currentPlatform != null)
				lastPlatformPosition = currentPlatform.transform.position;
		}

		/// <summary>
		/// Updates the camera with scroll and look input, using the current tick delta.
		/// </summary>
		/// <param name="scrollInput">Scroll/zoom input.</param>
		/// <param name="lookInputVector">Look input vector.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UpdateCamera(float scrollInput, Vector3 lookInputVector)
		{
			CharacterCamera.UpdateWithInput((float)base.TimeManager.TickDelta, scrollInput, lookInputVector);
		}

		/// <summary>
		/// Sets the orientation method for the character controller (camera or movement based).
		/// </summary>
		/// <param name="method">Orientation method to set.</param>
		[ServerRpc(RunLocally = true)]
		public void SetOrientationMethod(OrientationMethod method)
		{
			CharacterController.OrientationMethod = method;
		}
	}
}