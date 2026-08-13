using FishNet.Connection;
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
		public int Order => 80;

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

			// Initialize the motor's PhysicsScene from the GameObject's scene so that
			// collision queries (CapsuleCast, OverlapCapsule, Raycast) work on both
			// client and server. The server overrides this during character loading
			// (CharacterSystem.Loading.cs:820) with the scene-specific physics scene.
			// Without this, the client motor's collision queries silently return
			// nothing, causing constant position reconciles every tick.
			Motor.SetPhysicsScene(Motor.gameObject.scene.GetPhysicsScene());

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
			TryBindOwnerCamera();
		}

		/// <summary>
		/// Called when the local client gains ownership. Binds the main camera
		/// for the case where ownership arrives after OnStartClient.
		/// </summary>
		public override void OnOwnershipClient(NetworkConnection prevOwner)
		{
			base.OnOwnershipClient(prevOwner);
			TryBindOwnerCamera();
		}

		/// <summary>
		/// Binds the main camera to this character's follow point. Safe to call
		/// repeatedly — only runs once (when CharacterCamera is still null and we own the object).
		/// </summary>
		private void TryBindOwnerCamera()
		{
			if (!base.IsOwner || CharacterCamera != null) return;

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
#endif

		/// <inheritdoc/>
		public void PopulateInput(ref CharacterReplicateData input)
		{
			if (OnHandleCharacterInput == null)
			{
				return;
			}
#if !UNITY_SERVER
			// TryBindOwnerCamera only fires once, from OnStartClient/OnOwnershipClient,
			// and relies on Camera.main — a scene tag lookup that can race with scene
			// load/camera activation. If it missed that one attempt, CharacterCamera is
			// left permanently null and every tick from here on would throw a
			// NullReferenceException reading CharacterCamera.Transform. Retry here, on
			// the tick that actually needs it, so a late-activating camera still binds.
			if (CharacterCamera == null)
			{
				TryBindOwnerCamera();
				if (CharacterCamera == null)
				{
					return;
				}
			}
#endif
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

			// Server-authoritative movement gate: reject movement input from characters
			// that are dead, frozen, teleporting, or unloaded. IsInCombat is intentionally
			// excluded — players can move freely during combat. Only teleportation is
			// blocked while in combat (see CharacterSystem.Connection.cs).
			if (base.IsServerStarted &&
				CharacterController != null &&
				CharacterController.Character != null)
			{
				IPlayerCharacter character = CharacterController.Character;
				if (character.IsFlagged(CharacterFlags.IsDead) ||
					character.IsTeleporting ||
					character.IsFlagged(CharacterFlags.IsFrozen) ||
					!character.IsFlagged(CharacterFlags.IsLoaded))
				{
					return;
				}
			}

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
				// Use the platform's deterministically-cached per-tick velocity rather than
				// computing (currentPosition - lastPlatformPosition)/dt locally. FishNet does
				// not guarantee a deterministic tick order across NetworkObjects, so reading
				// the platform's transform directly could observe an updated or pre-update
				// position depending on whether the platform's [Replicate] ran first. The
				// cached value is the velocity from the platform's most recently completed
				// tick and is identical on server and client.
				platformVelocity = currentPlatform.LastCompletedTickVelocity;
				lastPlatformPosition = currentPlatform.transform.position;
			}
			Motor.SetPlatformVelocity(platformVelocity);

			// Stamina consumed by sprint/jump inside the motor update (KCCController.UpdateVelocity)
			// must still re-simulate during reconcile replay to keep the predicted stamina value
			// correct, but the OnAttributeUpdated notifications it raises must NOT fire during
			// replay (UI flicker / duplicate ECA). This mirrors the suppression that BuffController
			// and CharacterAttributeController already apply around their own replay-time mutations.
			// KCC runs first (Order 80), before the attribute controller enters its own replay
			// suppression scope; the depth counter inside the controller handles any nesting safely.
			ICharacterAttributeController attributeController = null;
			bool suppressAttributeNotifications = state.ContainsReplayed() &&
				CharacterController.Character != null &&
				CharacterController.Character.TryGet(out attributeController);
			if (suppressAttributeNotifications)
			{
				attributeController.BeginNotificationSuppression();
			}
			try
			{
				Motor.UpdatePhase1(deltaTime);
				Motor.UpdatePhase2(deltaTime);
			}
			finally
			{
				if (suppressAttributeNotifications && attributeController != null)
				{
					attributeController.EndNotificationSuppression();
				}
			}

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
			// Server-side enum validation, never trust client-supplied enum
			// values. A malicious or buggy client could send out-of-range integers that
			// silently cast to undefined enum members and corrupt the KCC controller.
			if (!System.Enum.IsDefined(typeof(OrientationMethod), method))
			{
				return;
			}
			CharacterController.OrientationMethod = method;
		}
	}
}