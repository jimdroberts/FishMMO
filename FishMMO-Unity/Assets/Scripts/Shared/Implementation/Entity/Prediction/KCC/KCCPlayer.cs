using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
using System;
using KinematicCharacterController;
using System.Runtime.CompilerServices;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Movement subsystem for KCC-based prediction. Implements <see cref="IPredictableController"/>
	/// so that <see cref="CharacterPredictionController"/> drives replication and reconciliation
	/// through a single unified pipeline.
	/// </summary>
	public class KCCPlayer : NetworkBehaviour, IPredictableController
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
		/// Returns <see cref="KCCInputReplicateData"/> which is converted to the
		/// unified <see cref="CharacterReplicateData"/> by <see cref="PopulateInput"/>.
		/// </summary>
		public Func<KCCInputReplicateData> OnHandleCharacterInput;

		/// <summary>
		/// The current platform the player is standing on (for moving platforms).
		/// </summary>
		private KCCPlatform currentPlatform;

		/// <summary>
		/// The last known position of the current platform.
		/// Included in reconcile data to ensure platform velocity is computed
		/// correctly during replay. Without this, replay computes stale deltas.
		/// </summary>
		private Vector3 lastPlatformPosition;

		/// <summary>
		/// Platform ID received from reconcile when the actual <see cref="KCCPlatform"/>
		/// is not yet resolvable from <see cref="SceneObject.Objects"/>.
		/// This happens during scene-transfer or spawn ordering races where character
		/// reconcile can arrive before the client has registered the platform scene object.
		/// </summary>
		private long pendingPlatformID;

		/// <summary>
		/// Stores the last received input data for observer future-state prediction.
		/// </summary>
		private KCCInputReplicateData lastCreatedData;

		/// <summary>
		/// True once we have received at least one Ticked replicate on an observer.
		/// Prevents using the default-initialized lastCreatedData (tick 0) which would
		/// produce incorrect tickDiff values for the first several observer ticks.
		/// </summary>
		private bool hasLastCreatedData;

		/// <summary>
		/// Maximum tick difference for observer movement prediction.
		/// At high RTT (e.g., 200ms at 30 tick/s = 6+ ticks of buffered data),
		/// a window of 1 causes movement to visually stutter on observers.
		/// Widen to match expected RTT in ticks.
		/// </summary>
		public uint ObserverPredictionWindowTicks = 1;

		/// <inheritdoc/>
		public int Order => 0;

		/// <summary>
		/// Sets the current platform and snapshots its position for velocity calculation.
		/// </summary>
		/// <param name="platform">The platform to set, or null to clear.</param>
		public void SetPlatform(KCCPlatform platform)
		{
			currentPlatform = platform;
			pendingPlatformID = platform != null ? platform.ID : 0;
			if (currentPlatform != null)
			{
				lastPlatformPosition = currentPlatform.transform.position;
			}
		}

		/// <summary>
		/// Initializes motor, controller, and rigidbody settings.
		/// </summary>
		private void Awake()
		{
			Motor = GetComponent<KinematicCharacterMotor>();

			CharacterController = GetComponent<KCCController>();
			CharacterController.Motor = Motor;
			Motor.CharacterController = CharacterController;

			Rigidbody rb = GetComponent<Rigidbody>();
			if (rb != null)
			{
				rb.isKinematic = true;
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

		/// <inheritdoc/>
		public void PopulateInput(ref CharacterReplicateData input)
		{
			if (OnHandleCharacterInput == null)
			{
				return;
			}
			KCCInputReplicateData kccInput = OnHandleCharacterInput();
			input.MoveAxisForward = kccInput.MoveAxisForward;
			input.MoveAxisRight = kccInput.MoveAxisRight;
			input.MoveFlags = kccInput.MoveFlags;
			input.CameraPosition = kccInput.CameraPosition;
			input.CameraRotation = kccInput.CameraRotation;
		}

		/// <inheritdoc/>
		public void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel)
		{
			TryResolvePendingPlatform();

			KCCInputReplicateData kccInput = new KCCInputReplicateData(
				input.MoveAxisForward, input.MoveAxisRight, input.MoveFlags,
				input.CameraPosition, input.CameraRotation);
			kccInput.SetTick(input.GetTick());

			// Observer prediction must run BEFORE the IsActualData gate.
			// Future ticks have default data (no IsActualData), so the early
			// return below would prevent observer prediction from ever executing.
			if (!base.IsServerStarted && !base.IsOwner)
			{
				if (state.IsFuture())
				{
					if (hasLastCreatedData)
					{
						uint thisTick = kccInput.GetTick();
						uint lastCreatedTick = lastCreatedData.GetTick();

						if (lastCreatedTick <= thisTick)
						{
							uint tickDiff = thisTick - lastCreatedTick;
							if (tickDiff <= ObserverPredictionWindowTicks)
							{
								kccInput.MoveFlags = lastCreatedData.MoveFlags;
								KCCMoveFlagsHelper.ClearOneShotFlags(ref kccInput.MoveFlags);
								kccInput.MoveFlags.EnableBit(KCCMoveFlags.IsActualData);
								kccInput.CameraPosition = lastCreatedData.CameraPosition;
								kccInput.CameraRotation = lastCreatedData.CameraRotation;
								kccInput.MoveAxisForward = lastCreatedData.MoveAxisForward;
								kccInput.MoveAxisRight = lastCreatedData.MoveAxisRight;
							}
						}
					}
				}
				else if (state.ContainsTicked() && kccInput.MoveFlags.IsFlagged(KCCMoveFlags.IsActualData))
				{
					lastCreatedData.Dispose();
					lastCreatedData = kccInput;
					hasLastCreatedData = true;
				}
			}

			if (!kccInput.MoveFlags.IsFlagged(KCCMoveFlags.IsActualData))
			{
				return;
			}

			CharacterController.SetInputs(ref kccInput);

			float deltaTime = (float)base.TimeManager.TickDelta;

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

		/// <inheritdoc/>
		public void OnCreateReconcile(ref CharacterReconcileData reconcileData)
		{
			KinematicCharacterMotorState motorState = CharacterController.GetState();
			motorState.CurrentPlatformID = currentPlatform != null ? currentPlatform.ID : 0;
			motorState.LastPlatformPosition = lastPlatformPosition;
			reconcileData.MotorState = motorState;
		}

		/// <inheritdoc/>
		public void OnReconcile(CharacterReconcileData rd, Channel channel)
		{
			CharacterController.ApplyState(rd.MotorState);

			pendingPlatformID = rd.MotorState.CurrentPlatformID;
			currentPlatform = ResolvePlatform(pendingPlatformID);
			if (currentPlatform != null)
			{
				pendingPlatformID = 0;
			}

			lastPlatformPosition = rd.MotorState.LastPlatformPosition;
		}

		/// <summary>
		/// Resets observer prediction state on network reset (e.g., reconnect or scene transfer).
		/// </summary>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			lastCreatedData.Dispose();
			hasLastCreatedData = false;
			currentPlatform = null;
			pendingPlatformID = 0;
			lastPlatformPosition = Vector3.zero;
		}

		/// <summary>
		/// Resolves any pending platform reference after a reconcile or scene-object registration race.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void TryResolvePendingPlatform()
		{
			if (currentPlatform != null || pendingPlatformID == 0)
			{
				return;
			}

			currentPlatform = ResolvePlatform(pendingPlatformID);
			if (currentPlatform != null)
			{
				pendingPlatformID = 0;
			}
		}

		/// <summary>
		/// Resolves a <see cref="KCCPlatform"/> from a scene-object ID.
		/// </summary>
		/// <param name="platformID">The platform scene-object ID.</param>
		/// <returns>The resolved platform, or null if unavailable.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static KCCPlatform ResolvePlatform(long platformID)
		{
			if (platformID == 0)
			{
				return null;
			}

			if (SceneObject.Objects.TryGetValue(platformID, out ISceneObject sceneObject) &&
				sceneObject.GameObject != null)
			{
				return sceneObject.GameObject.GetComponent<KCCPlatform>();
			}

			return null;
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
		/// Sets the orientation method for the character controller.
		/// </summary>
		/// <param name="method">Orientation method to set.</param>
		[ServerRpc(RunLocally = true, RequireOwnership = true)]
		public void SetOrientationMethod(OrientationMethod method)
		{
			CharacterController.OrientationMethod = method;
		}
	}
}