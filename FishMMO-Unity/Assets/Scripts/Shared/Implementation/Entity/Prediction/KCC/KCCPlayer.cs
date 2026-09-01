using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
using System;
using KinematicCharacterController;
using System.Runtime.CompilerServices;
using FishMMO.Logging;
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
		/// How many ticks past the last real input an observing client may keep re-simulating that
		/// input before it stops carrying it forward.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Currently inert.</b> The block that reads this only runs for a non-owner, non-server
		/// peer, and a client is never handed another character's replicate input while state
		/// forwarding is off — <c>Replicate_NonAuthoritative</c> returns before invoking the
		/// replicate body, and forwarding is 0 on all 39 networked objects in this project.
		/// Observers see other players through <c>NetworkTransform</c> instead. So changing this
		/// value has no effect today; it becomes live only if forwarding is enabled.
		/// </para>
		/// <para>
		/// The value would then want to be near the client's buffered-input depth — about 6 ticks at
		/// 200ms and tick rate 30 — because a window of 1 lets an observed character stall for a tick
		/// whenever an input is late. It is a plain public field serialized on all nine character
		/// prefabs, so raising it means editing those prefabs, not this default.
		/// </para>
		/// </remarks>
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

			/* Re-assert the motor's physics scene PER SPAWN, not per pooled instance.
			 *
			 * Awake captures it once from whatever scene the instance sat in at instantiation,
			 * and Awake runs once per POOLED instance — but world scenes are loaded with
			 * LocalPhysicsMode.Physics3D (SceneServerSystem's connection load, which the client
			 * mirrors), so each world scene has its own physics world. A pooled character reused
			 * across a scene transfer therefore kept querying the PREVIOUS scene's physics — a
			 * scene that may since have unloaded — and a motor whose queries return nothing
			 * collides with nothing: the client copy falls through the ground and through moving
			 * platforms while the server, which re-assigns per spawn in its loading path
			 * (CharacterSystem.Loading), holds it up — an unresolvable rubber-band that presents
			 * as "falling through the world/platform" after a scene change. By OnStartClient the
			 * object is in its destination scene, so this is the client-side mirror of the
			 * server's per-spawn assignment. */
			if (Motor != null)
			{
				Motor.SetPhysicsScene(Motor.gameObject.scene.GetPhysicsScene());
			}

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
		/// <summary>
		/// The fallback aim direction, already through the wire quantiser.
		/// </summary>
		/// <remarks>
		/// Static and computed once: <c>Quantize</c> is a round trip through the packer, and this
		/// is a constant. See <see cref="PopulateInput"/> for why the raw fallback is not used.
		/// </remarks>
		private static readonly Vector3 QuantizedFallbackAim =
			AimDirectionCompression.QuantizedFallbackDirection;

		/// <summary>
		/// How far behind the server this client's rendered view of its peers will be BY THE TIME THE
		/// SERVER RUNS THIS INPUT: the full round trip plus the interpolation buffer it deliberately
		/// holds, split into whole ticks and a 1/256 tick remainder.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The full round trip, not half of it.</b> The offset is subtracted from the server's tick
		/// at the moment the query runs, and that instant is not the instant this input was produced —
		/// the input still has to cross the network. Both halves of the trip are therefore in play and
		/// they are different halves:
		/// </para>
		/// <list type="number">
		/// <item>The state this client is looking at left the server one way trip ago, and it is
		/// rendered <see cref="LagCompensationTick.SpectatorInterpolationTicks"/> behind even that.</item>
		/// <item>The input built from that view takes another one way trip to reach the server.</item>
		/// </list>
		/// <para>
		/// Half the round trip covers only the first, so a shot resolved against it landed half a
		/// round trip ahead of where the shooter aimed — at 200&#160;ms and 6&#160;m/s, most of a
		/// character's width, and the error grew with ping. There is a third term, the server's own
		/// replicate queue, which this client cannot see; <see cref="LagCompensationTick.TryResolve"/>
		/// adds it from <c>PredictionManager.StateInterpolation</c> on arrival.
		/// </para>
		/// <para>
		/// Computed here because this is the only peer that knows the latency terms. See
		/// <see cref="CharacterReplicateData.ViewOffsetTicks"/> for why the server cannot derive it,
		/// and <see cref="LagCompensationTick"/> for the cap applied to it on arrival.
		/// </para>
		/// </remarks>
		private void ResolveViewOffset(out byte wholeTicks, out byte fraction)
		{
			wholeTicks = (byte)LagCompensationTick.SpectatorInterpolationTicks;
			fraction = 0;

			FishNet.Managing.Timing.TimeManager timeManager = base.TimeManager;
			if (timeManager == null)
			{
				return;
			}

			/* The arithmetic itself lives beside the server half it has to cancel against — see
			 * LagCompensationTick.ResolveViewOffset. This method supplies the two measurements only
			 * the owning client can take and adds nothing of its own, which is what lets a test
			 * compose both halves of the loop and assert they cancel exactly. */
			LagCompensationTick.ResolveViewOffset(
				timeManager.RoundTripTime, timeManager.TickDelta, out wholeTicks, out fraction);
		}


		public void PopulateInput(ref CharacterReplicateData input)
		{
			/* Both early exits below leave a default-initialised input on the wire, whose aim is
			 * Vector3.zero. Consumers substitute AimDirectionCompression.FallbackDirection for a
			 * zero aim, but they do it AFTER the wire round trip on the remote peers and BEFORE it
			 * on this one, so the owner would simulate from the raw fallback while everyone else
			 * simulated from its encoded form — a small divergence, in the one case where nothing
			 * else is going right anyway. Writing the quantised fallback explicitly means every
			 * peer starts from the identical vector. */
			input.AimDirection = QuantizedFallbackAim;

			if (OnHandleCharacterInput == null)
			{
				return;
			}
#if !UNITY_SERVER
			// TryBindOwnerCamera only runs from OnStartClient/OnOwnershipClient and resolves
			// the camera through Camera.main, a tag lookup that races scene load and camera
			// activation. If both attempts missed, CharacterCamera stays null forever and the
			// input handler below dereferences it every tick (NullReferenceException), leaving
			// movement dead with no visible error. Retry on the tick that actually needs it so
			// a late-activating camera still binds.
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
			/* Quantised here, not on read — same rule as the aim below. The owner predicts from
			 * this struct, so it has to commit to the axis value the wire can carry or it
			 * simulates a slightly different movement magnitude from everyone else. */
			input.MoveAxisForward = MoveAxisCompression.Quantize(kccInput.MoveAxisForward);
			input.MoveAxisRight = MoveAxisCompression.Quantize(kccInput.MoveAxisRight);
			input.MoveFlags = kccInput.MoveFlags;
			ResolveViewOffset(out input.ViewOffsetTicks, out input.ViewOffsetFraction);
			/* The aim ORIGIN is deliberately not carried. It used to travel as CameraPosition,
			 * taken from this client verbatim and never checked against the character, which let a
			 * modified client choose the point the server raycast for victims from. Every peer now
			 * derives it from the motor instead -- see CharacterAimOrigin. Only the DIRECTION is
			 * still the client's to choose, and that is bounded to a unit vector by its encoding. */
			/* Quantise HERE, not on read. The owner predicts from this same struct, so storing the
			 * raw camera forward would have the owner simulate a direction the wire cannot carry
			 * while the server and every observer simulate the decoded one — the ability system is
			 * deterministic, so that divergence showed up as a slightly different shot on every
			 * cast. See AimDirectionCompression. */
			input.AimDirection = AimDirectionCompression.Quantize(kccInput.CameraRotation * Vector3.forward);
		}

		/// <inheritdoc/>
		public void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel)
		{
			TryResolvePendingPlatform();

			/* Movement gate. IsInCombat is intentionally excluded — players move freely during
			 * combat; only teleportation is blocked (see CharacterSystem.Connection.cs).
			 *
			 * Split into a PREDICTED half and a SERVER-ONLY half, and the split is the whole point.
			 * The gate used to be entirely server-side, so a stunned owner carried on predicting
			 * movement while the server refused it, and the reconcile snapped the character back on
			 * every tick for the stun's whole duration. What the player saw was rubber-banding
			 * rather than a stun — the correction was real, but the feedback was wrong. */
			if (CharacterController != null &&
				CharacterController.Character != null)
			{
				IPlayerCharacter character = CharacterController.Character;

				/* Predicted on BOTH peers, because both can reach the same answer on the same tick.
				 *
				 * IsFrozen, IsStunned and IsMesmerized are set by StateBuffTemplate and
				 * CompositeBuffTemplate, which run wherever BuffController.SimulatesBuffEffects is
				 * true — the server AND the owner. So the owner already knows it is stunned; it
				 * simply was not asked. (Those two flags used to be set and read nowhere at all,
				 * which is why CharacterIncapacitation exists: one definition, shared by all three
				 * gates that need it.)
				 *
				 * Death is tested on the replicated HEALTH VALUE, never on CharacterFlags.IsDead.
				 * Flags ride the spawn payload and are never re-synced, so a client's copy is stale
				 * from its first death onward and gating on it would freeze the owner permanently
				 * after one death. Resource state is reconciled every tick, so both sides evaluate
				 * this identically — the same substitution AbilityController.CanStartActivation
				 * makes for the same reason. */
				if (CharacterIncapacitation.IsIncapacitated(character) ||
					IsHealthDepleted(character))
				{
					return;
				}

				/* Server-only, because these are server bookkeeping the client cannot evaluate.
				 * IsTeleporting is set and cleared entirely server-side, and IsLoaded is a flag from
				 * the spawn payload that a client holds as "true" for its whole session — testing
				 * either on the owner would gate movement on a value that never changes there. */
				if (base.IsServerStarted &&
					(character.IsTeleporting ||
					 !character.IsFlagged(CharacterFlags.IsLoaded)))
				{
					return;
				}
			}

			KCCInputReplicateData kccInput = new KCCInputReplicateData(
				input.MoveAxisForward, input.MoveAxisRight, input.MoveFlags,
				CharacterAimOrigin.Resolve(Motor, transform),
				AimDirectionCompression.ToRotation(input.AimDirection));
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
								/* CameraPosition is NOT carried forward: it is derived from the
								 * motor for the current tick above, and copying the previous
								 * tick's origin would aim from where the character used to be. */
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
				/* Use the platform's deterministically-cached per-tick velocity rather than
				 * differencing its transform locally. FishNet does not guarantee a deterministic
				 * tick order across NetworkObjects, so reading the platform's transform directly
				 * could observe an updated or pre-update position depending on whether the platform
				 * stepped first.
				 *
				 * Ask for the velocity of the tick being simulated, not the platform's present one.
				 * The platform never replays — it has no owner, so no reconcile reaches a client and
				 * nothing rolls it back — so during a reconcile that replays k ticks every replayed
				 * tick would otherwise inherit the same frozen present-tick value where the server
				 * used each tick's own, bending the replayed path at every direction reversal. The
				 * ring covers 64 ticks; beyond that, or on the live tick, the present value is the
				 * right answer anyway. */
				if (!currentPlatform.TryGetVelocityForTick(input.GetTick(), out platformVelocity))
				{
					platformVelocity = currentPlatform.LastCompletedTickVelocity;
				}
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

		/// <summary>
		/// True when this character has a health resource and it has been reduced to zero.
		/// </summary>
		/// <remarks>
		/// The shared definition of "dead" for prediction-path code: derived purely from replicated
		/// resource state, so the owner and the server reach the same answer on the same tick.
		/// <para>
		/// A character with no health resource configured is NOT treated as dead — it has no health
		/// to lose, and freezing it would be a movement bug for anything that simply cannot be
		/// attacked. Same rule as <c>CharacterAttributeController.IsHealthDepleted</c>.
		/// </para>
		/// </remarks>
		private static bool IsHealthDepleted(IPlayerCharacter character)
		{
			return character.TryGet(out ICharacterDamageController damageController) &&
				damageController.ResourceInstance != null &&
				damageController.ResourceInstance.CurrentValue <= 0.0f;
		}

		/// <inheritdoc/>
		public void OnCreateReconcile(ref CharacterReconcileData reconcileData)
		{
			KinematicCharacterMotorState motorState = CharacterController.GetState();
			motorState.CurrentPlatformID = currentPlatform != null ? currentPlatform.ID : 0;
			reconcileData.MotorState = motorState;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para>
		/// The owner always reconciles — this is the correction that repairs its own predicted
		/// movement.
		/// </para>
		/// <para>
		/// A non-owner only reconciles a forwarded object, because that is the only mode in which
		/// the reconcile is what positions this character on an observing client. With forwarding
		/// off, position arrives through <c>NetworkTransform</c> and applying a motor state on top
		/// would give the transform two writers — the same failure
		/// <see cref="CharacterPredictionController.ApplyObserverTransportMode"/> exists to prevent
		/// in the other direction. See <see cref="ObserverSyncMode"/>.
		/// </para>
		/// </remarks>
		public void OnReconcile(CharacterReconcileData rd, Channel channel)
		{
			if (!base.IsOwner && !ObserverSyncMode.ObserversConsumeReconcile(base.NetworkObject))
			{
				return;
			}

			CharacterController.ApplyState(rd.MotorState);

			pendingPlatformID = rd.MotorState.CurrentPlatformID;
			currentPlatform = ResolvePlatform(pendingPlatformID);
			if (currentPlatform != null)
			{
				pendingPlatformID = 0;
			}
			else if (pendingPlatformID != 0)
			{
				/* Diagnostic for the falling-through-platforms class. The server says this rider
				 * is standing on platform <id>, and this peer cannot find it in the scene-object
				 * registry — so every replayed and future tick simulates with ZERO platform
				 * velocity while the server simulates with the real one, and the rider walks off
				 * the moving surface (or the reconcile drags them through it). Throttled by the
				 * pending latch: this logs once per reconcile burst, and TryResolvePendingPlatform
				 * quietly retries until registration catches up. */
				Log.Debug("KCCPlayer",
					$"Reconcile says riding platform {pendingPlatformID} but it is not registered on this peer; simulating without platform velocity until it resolves.");
			}
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