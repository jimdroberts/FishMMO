using System;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Serializing;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Unified prediction controller that replaces per-subsystem [Replicate]/[Reconcile].
	/// Discovers all <see cref="IPredictableController"/> components on the same GameObject,
	/// sorts them by <see cref="IPredictableController.Order"/>, and drives them through
	/// a single FishNet Prediction V2 pipeline. This avoids the issues caused by having
	/// multiple predicted NetworkBehaviours on the same NetworkObject.
	/// </summary>
	/// <remarks>
	/// <b>Dynamic components:</b> The sorted <see cref="IPredictableController"/> array
	/// is built in <c>Awake</c> and once more at the object's first network start, after every
	/// Awake on the object, so a module's controller added as a <see cref="CharacterPart"/> by
	/// <c>BaseCharacter.Awake</c> takes part (the Items module's equipment). Components added
	/// later than that do not participate in the prediction pipeline.
	/// </remarks>
	public class CharacterPredictionController : NetworkBehaviour, IServerStepped
	{
		/// <summary>
		/// Snapshot of the current LocalTick (TimeManager.LocalTick) captured at the start of the
		/// <see cref="TimeManager_OnTick"/> pipeline. This is the raw local authoritative tick
		/// observed at the TimeManager level — it is NOT a replicate-domain tick.
		/// Consumers that require a replicate-domain tick must translate this value via the
		/// appropriate controller helpers (e.g., <c>ResolveAuthoritativeTick</c> on target controllers).
		/// Used by external consumers (e.g., <see cref="AbilityObject"/>) to avoid subscription-order drift
		/// between tick callbacks.
		/// </summary>
		public uint CurrentLocalTickSnapshot
		{
			get => IsNapping ? LiveTick : currentLocalTickSnapshot;
			private set => currentLocalTickSnapshot = value;
		}
		private uint currentLocalTickSnapshot = TimeManager.UNSET_TICK;

		/// <summary>
		/// Snapshot of the replicate input tick captured at the start of the current
		/// <see cref="Replicate"/> pass. Controllers that receive callbacks before their
		/// own ordered <c>OnReplicate</c> has run can use this as the current
		/// replicate-domain reference instead of projecting elapsed raw <c>LocalTick</c>
		/// time into prediction state.
		/// </summary>
		public uint CurrentReplicateTickSnapshot { get; private set; } = TimeManager.UNSET_TICK;

		/// <summary>
		/// Best available replicate-domain tick for the upcoming <see cref="Replicate"/>
		/// pass, captured during <see cref="TimeManager.OnPreTick"/> before arbitrary
		/// <see cref="TimeManager.OnTick"/> subscribers can run. This closes the window
		/// where ability objects or region callbacks execute before this controller's
		/// own <see cref="TimeManager_OnTick"/> subscription and would otherwise observe
		/// the previous tick's <see cref="CurrentReplicateTickSnapshot"/>.
		/// </summary>
		/// <remarks>A napping NPC (<see cref="ServerStepSchedule"/>) answers the live tick: it is the tick its next step
		/// would run, exactly what its pre-tick would have published, so a buff landing on it is stamped as it would be on
		/// an awake one.</remarks>
		public uint PendingReplicateTickSnapshot
		{
			get => IsNapping ? LiveTick : pendingReplicateTickSnapshot;
			private set => pendingReplicateTickSnapshot = value;
		}
		private uint pendingReplicateTickSnapshot = TimeManager.UNSET_TICK;

		/// <summary>The server's tick now, or unset without a TimeManager.</summary>
		private uint LiveTick => base.TimeManager != null ? base.TimeManager.LocalTick : TimeManager.UNSET_TICK;

		/// <summary>
		/// The view offset carried by the most recent replicate input — how many ticks behind
		/// server-present the owning client was rendering its peers.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Read by <see cref="LagCompensationTick"/> to decide how far to rewind a hit query. Cached
		/// from the replicate rather than read off a live controller for the usual reason: the value
		/// has to be the one that belongs to the tick being simulated, including during a replay.
		/// Zero for a server-driven character, which compensates nothing.
		/// </para>
		/// <para>
		/// <b>Latched from real input only</b> — see <see cref="CaptureViewOffset"/>. This is not the
		/// value from the most recent <i>invocation</i> of the replicate body, because plenty of those
		/// carry no input at all.
		/// </para>
		/// </remarks>
		public byte CurrentViewOffsetTicks { get; private set; }

		/// <summary>
		/// The sub-tick remainder of <see cref="CurrentViewOffsetTicks"/>, in 1/256ths of a tick.
		/// </summary>
		public byte CurrentViewOffsetFraction { get; private set; }

		/// <summary>The latched input's own tick (the owner's tick domain), which the exact view tick is an offset from.</summary>
		public uint CurrentViewInputTick { get; private set; }

		/// <summary>The latched exact view tick offset (CharacterReplicateData.ViewTickOffset).</summary>
		public ushort CurrentViewTickOffset { get; private set; }

		/// <summary>The latched exact view tick fraction; 0 when the client sent none (CharacterReplicateData.ViewTickFraction).</summary>
		public byte CurrentViewTickFraction { get; private set; }

		/// <summary>Recent exact view claims by input tick (server), so an ability step can rewind to the view of its own input.</summary>
		private readonly (uint InputTick, ushort Offset, byte Fraction)[] viewClaims = new (uint, ushort, byte)[ViewClaimRing];
		private const uint ViewClaimRing = 128;

		/// <summary>The exact view claim the caster sent with input <paramref name="inputTick"/>, when it is still held.</summary>
		public bool TryGetViewClaim(uint inputTick, out ushort offset, out byte fraction)
		{
			(uint InputTick, ushort Offset, byte Fraction) claim = viewClaims[inputTick % ViewClaimRing];
			if (claim.InputTick == inputTick && claim.Fraction != 0)
			{
				offset = claim.Offset;
				fraction = claim.Fraction;
				return true;
			}
			offset = 0;
			fraction = 0;
			return false;
		}

		/// <summary>
		/// Cached array of <see cref="IPredictableController"/> components, sorted by <see cref="IPredictableController.Order"/>.
		/// Built once in Awake; dynamic additions after Awake will not participate.
		/// </summary>
		private IPredictableController[] controllers = Array.Empty<IPredictableController>();

		/// <summary>
		/// True when this character's replicate input is produced by a server-side brain rather
		/// than by a remote client — that is, when the character is an <see cref="NPC"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Ownership alone cannot answer "who writes this character's input?". A monster is
		/// server-owned and has no owning connection, while a pet is owned by the connection of
		/// the player that summoned it — yet both are driven entirely by a server-side brain.
		/// Gating input on <see cref="NetworkBehaviour.IsOwner"/> therefore left monsters with
		/// nobody producing input at all, and would let a pet owner's client produce input for a
		/// brain that does not run there.
		/// </para>
		/// <para>
		/// Decided by the character's TYPE, not by the presence of a brain component. The brain is
		/// attached by the server at spawn and never exists on a client, so a component check
		/// answers "no brain here" on every client — and on the owner of a pet that reads as "the
		/// owner writes this input", which is exactly the hijack this flag exists to prevent.
		/// </para>
		/// </remarks>
		private bool serverDrivenInput;


		/// <summary>
		/// True when this peer is the one responsible for producing this character's replicate
		/// input for the current tick. AI characters answer "the server"; everyone else answers
		/// "the owning client".
		/// </summary>
		public bool HasInputAuthority => serverDrivenInput ? base.IsServerStarted : base.IsOwner;

		/// <summary>
		/// Discovers all <see cref="IPredictableController"/> components on this GameObject,
		/// sorts them by <see cref="IPredictableController.Order"/>, and caches the sorted array.
		/// </summary>
		private void Awake()
		{
			DiscoverControllers();

			// An NPC — monster, townsperson or pet — is driven by the server's brain on every peer.
			serverDrivenInput = GetComponent<NPC>() != null;
			TryGetComponent(out simulatedCharacter);
		}

		/// <summary>True once the controllers were found again after every Awake on the object (see <see cref="OnStartNetwork"/>).</summary>
		private bool controllersSettled;

		/// <summary>Finds and sorts this object's <see cref="IPredictableController"/>s.</summary>
		private void DiscoverControllers()
		{
			List<IPredictableController> list = new List<IPredictableController>();
			GetComponents(list);
			List<IPredictableController> sortedList = list
				.OrderBy(c => c.Order)
				.ThenBy(c => c.GetType().FullName)
				.ToList();
			controllers = sortedList.ToArray();
		}

		/// <summary>
		/// Subscribes to <see cref="TimeManager.OnPreTick"/> and <see cref="TimeManager.OnTick"/> events
		/// and validates that state forwarding is enabled for observer prediction.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			/* Once more, now that every Awake on the object has run: a module's predicted controller is a CharacterPart that
			 * BaseCharacter.Awake adds (the Items module's equipment), and the two Awakes run in no fixed order, so the first
			 * look could miss it. Once per object; a pooled object keeps its components. */
			if (!controllersSettled)
			{
				DiscoverControllers();
				controllersSettled = true;
			}

			/* State forwarding off is the INTENDED configuration for playable characters, not a
			 * misconfiguration. It used to warn here that "predicted observers will desync", which
			 * was true of the old design: observers ran the predicted state machine from the
			 * owner's relayed input, so cutting the relay left them simulating nothing.
			 *
			 * Observers no longer predict their peers. Position arrives via NetworkTransform,
			 * resources via CharacterResourcesBroadcast, buffs via CharacterBuffsBroadcast,
			 * equipment via its own broadcasts, and ability casts via AbilityActivatedBroadcast. The owner
			 * still predicts itself and still receives every reconcile — Server_SendReconcileRpc
			 * writes to the owner in both modes.
			 *
			 * What genuinely breaks is forwarding off with nothing to replicate position, so that
			 * is what is checked now. Without it a character simulates correctly for its owner and
			 * stands still for everyone else, while server-resolved damage keeps landing — a
			 * failure that presents as a content bug rather than a networking one. */
			if (base.NetworkObject != null &&
				!base.NetworkObject.EnableStateForwarding &&
				GetComponent<FishNet.Component.Transforming.NetworkTransform>() == null)
			{
				FishMMO.Logging.Log.Warning(
					"CharacterPredictionController",
					$"'{base.NetworkObject.name}' has state forwarding disabled but no NetworkTransform. " +
					"Observers will receive no position updates for it at all: it will appear frozen " +
					"while still taking and dealing damage. Add a NetworkTransform and assign it to " +
					"the NetworkObject's Prediction settings.");
			}

			/* An NPC builds no reconcile (see SimulateOnServer), so its observers must take its state from the broadcasts:
			 * forwarding off, whatever its prefab says. Both peers decide it from the character's type, so they agree. */
			if (serverDrivenInput && base.NetworkObject != null && base.NetworkObject.EnableStateForwarding)
			{
				base.NetworkObject.SetStateForwarding(false);
			}

			ApplyObserverTransportMode();

			/* An AI character must be ownerless so the server is FishNet's controller for it.
			 * Replicate_Authoritative only accepts server-produced input when the object has no
			 * owner; hand an AI character to a client connection and the server's decisions are
			 * discarded while that client — which does not run the brain — is expected to supply
			 * the input, so the NPC can never act. This is silent at runtime, hence the warning. */
			if (serverDrivenInput && base.IsServerStarted && base.Owner.IsValid)
			{
				FishMMO.Logging.Log.Warning(
					"CharacterPredictionController",
					$"AI character '{gameObject.name}' was spawned with an owning connection " +
					$"(clientId {base.Owner.ClientId}). Server-side AI input will be ignored and the " +
					"character will never act. Spawn AI characters without an owner.");
			}

			/* An NPC on the server steps from the shared schedule, which can let it nap while nobody sees it and it has
			 * nothing to do; everything else keeps its own subscriptions. */
			if (serverDrivenInput && base.IsServerStarted)
			{
				stepSchedule = ServerStepSchedule.For(base.TimeManager);
			}
			if (stepSchedule != null)
			{
				idleStepStreak = 0;
				napPhase = base.NetworkObject != null ? base.NetworkObject.ObjectId : 0;
				stepSchedule.Add(this);
			}
			else if (base.TimeManager != null)
			{
				base.TimeManager.OnPreTick += TimeManager_OnPreTick;
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		/// <summary>
		/// Registers this character for per-observer streaming (density-scaled range and the
		/// full-rate observer cap). See <see cref="ObserverStreamingRegistry"/>.
		/// </summary>
		public override void OnStartServer()
		{
			base.OnStartServer();
			if (TryGetComponent(out ICharacter character))
			{
				ObserverStreamingRegistry.Register(base.NetworkObject, character);
			}
			if (stepSchedule != null && base.NetworkObject != null && !observersHooked)
			{
				base.NetworkObject.OnObserversActive += NetworkObject_OnObserversActive;
				observersHooked = true;
			}
		}

		/// <summary>Removes this character from per-observer streaming.</summary>
		public override void OnStopServer()
		{
			ObserverStreamingRegistry.Unregister(base.NetworkObject);
			if (observersHooked && base.NetworkObject != null)
			{
				base.NetworkObject.OnObserversActive -= NetworkObject_OnObserversActive;
			}
			observersHooked = false;
			base.OnStopServer();
		}

		/// <summary>
		/// Unsubscribes from <see cref="TimeManager"/> tick events and resets all tick snapshots to unset.
		/// </summary>
		public override void OnStopNetwork()
		{
			if (stepSchedule != null)
			{
				stepSchedule.Remove(this);
				stepSchedule = null;
			}
			else if (base.TimeManager != null)
			{
				base.TimeManager.OnPreTick -= TimeManager_OnPreTick;
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
			CurrentLocalTickSnapshot = TimeManager.UNSET_TICK;
			CurrentReplicateTickSnapshot = TimeManager.UNSET_TICK;
			PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
			ClearViewOffset();
			ClearQuietReconcileState();

			base.OnStopNetwork();
		}

		/// <summary>
		/// Drops the latched view offset when this object changes hands.
		/// </summary>
		/// <remarks>
		/// The offset describes ONE connection's latency, and <see cref="CaptureViewOffset"/> holds the
		/// last measured value across input gaps on purpose. That makes it stale rather than merely
		/// absent when the connection behind it changes, so the two places the connection can change —
		/// a new owner, and a pooled object's despawn — clear it rather than letting the next owner
		/// inherit the previous one's latency until its first input arrives.
		/// </remarks>
		/// <param name="prevOwner">The connection that owned this object before.</param>
		public override void OnOwnershipServer(NetworkConnection prevOwner)
		{
			base.OnOwnershipServer(prevOwner);
			ClearViewOffset();
			ClearQuietReconcileState();
		}

		/// <summary>A new owner (or a pooled reuse) starts with every reconcile sent until it is known to be idle.</summary>
		private void ClearQuietReconcileState()
		{
			inertInputStreak = 0;
			identicalReconcileSends = 0;
			lastReconcileLength = -1;
			lastReconcileSentTick = TimeManager.UNSET_TICK;
			hasLastQuietData = false;
		}

		/// <summary>Resets the latched view offset to "nothing to compensate".</summary>
		private void ClearViewOffset()
		{
			CurrentViewOffsetTicks = 0;
			CurrentViewOffsetFraction = 0;
			CurrentViewInputTick = 0;
			CurrentViewTickOffset = 0;
			CurrentViewTickFraction = 0;
			Array.Clear(viewClaims, 0, viewClaims.Length);
		}

		/// <summary>
		/// Called before each tick. Captures the current local tick and computes the pending
		/// replicate-domain tick for the upcoming <see cref="Replicate"/> pass.
		/// </summary>
		private void TimeManager_OnPreTick()
		{
			if (base.TimeManager == null)
			{
				CurrentLocalTickSnapshot = TimeManager.UNSET_TICK;
				CurrentReplicateTickSnapshot = TimeManager.UNSET_TICK;
				PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
				return;
			}

			uint previousReplicateTick = CurrentReplicateTickSnapshot;
			CurrentLocalTickSnapshot = base.TimeManager.LocalTick;
			CurrentReplicateTickSnapshot = TimeManager.UNSET_TICK;
			PendingReplicateTickSnapshot = ResolvePendingReplicateTick(previousReplicateTick);
		}

		/// <summary>
		/// Resolves the best available replicate-domain tick for the upcoming replicate pass.
		/// For the owning client, returns <see cref="TimeManager.LocalTick"/>.
		/// For non-owners, uses the owner's replicate tick or extrapolates from the previous tick.
		/// </summary>
		/// <param name="previousReplicateTick">The replicate tick from the previous pass.</param>
		/// <returns>The resolved replicate tick, or <see cref="TimeManager.UNSET_TICK"/> if unavailable.</returns>
		private uint ResolvePendingReplicateTick(uint previousReplicateTick)
		{
			if (base.TimeManager == null)
			{
				return TimeManager.UNSET_TICK;
			}

			// Server-driven AI characters resolve against the server's own tick even when a
			// client owns the NetworkObject, because the server is what produces their input.
			if (base.IsController || (serverDrivenInput && base.IsServerStarted))
			{
				return base.TimeManager.LocalTick;
			}

			if (base.Owner.IsValid && !base.Owner.ReplicateTick.IsUnset)
			{
				uint ownerReplicateTick = base.Owner.ReplicateTick.Value(base.TimeManager);
				if (ownerReplicateTick != TimeManager.UNSET_TICK)
				{
					return ownerReplicateTick;
				}
			}

			if (previousReplicateTick != TimeManager.UNSET_TICK)
			{
				return unchecked(previousReplicateTick + 1u);
			}

			return TimeManager.UNSET_TICK;
		}

		/// <summary>
		/// Stops the NetworkTransform duplicating position when prediction already carries it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A forwarded object's observers simulate it: they receive the owner's replicate input and
		/// the server's reconcile, and run the same motor. The NetworkTransform on top of that is
		/// the same position a second time, on its own channel, and the two writers also fight over
		/// the transform on the observing client.
		/// </para>
		/// <para>
		/// <b>Only when prediction genuinely moves this character.</b> That means a
		/// <see cref="KCCPlayer"/>, which is the only thing that fills
		/// <c>CharacterReconcileData.MotorState</c>. NPCs run the same prediction pipeline for their
		/// abilities, buffs and attributes but are moved by a NavMeshAgent, so their MotorState is
		/// default every tick and the NetworkTransform is the only thing that moves them anywhere.
		/// Silencing it for an NPC because forwarding happened to be on would freeze it for every
		/// observer while it carried on fighting.
		/// </para>
		/// <para>
		/// Position and rotation are switched rather than the component disabled, because
		/// <c>NetworkTransform.enabled</c> does not stop it: it sends from a TimeManager
		/// subscription taken in OnStartNetwork and receives through an ObserversRpc, and neither
		/// consults <c>enabled</c>. Scale is left alone — prediction does not carry it.
		/// </para>
		/// <para>
		/// Server side only. The server decides what it sends, and a client flipping its own copy
		/// would change nothing except what that client itself transmits.
		/// </para>
		/// <para>
		/// Public because the mode can change at runtime: whatever flips
		/// <c>NetworkObject.SetStateForwarding</c> — an arena or tournament handing out precision
		/// for the duration of a match — must call this afterwards, or the character keeps the
		/// transport it started with.
		/// </para>
		/// </remarks>
		public void ApplyObserverTransportMode()
		{
			if (!base.IsServerStarted || base.NetworkObject == null)
			{
				return;
			}

			FishNet.Component.Transforming.NetworkTransform networkTransform =
				GetComponent<FishNet.Component.Transforming.NetworkTransform>();
			if (networkTransform == null)
			{
				return;
			}

			bool predictionMovesThisCharacter = GetComponent<KCCPlayer>() != null;
			bool transformIsRedundant = IsTransformRedundant(
				predictionMovesThisCharacter, base.NetworkObject.EnableStateForwarding);

			networkTransform.SetSynchronizePosition(!transformIsRedundant);
			networkTransform.SetSynchronizeRotation(!transformIsRedundant);
		}

		/// <summary>
		/// The whole rule <see cref="ApplyObserverTransportMode"/> applies, as a pure function.
		/// </summary>
		/// <remarks>
		/// Named and separated so the truth table can be asserted directly rather than inferred
		/// from the two <c>SetSynchronize*</c> calls it drives. Both inputs matter and neither alone
		/// is sufficient: forwarding on makes the reconcile carry position, but only for a character
		/// prediction actually moves — an NPC's <c>MotorState</c> is default every tick because a
		/// NavMeshAgent moves it, so silencing its transform would freeze it for every observer
		/// while it carried on fighting.
		/// </remarks>
		/// <param name="predictionMovesThisCharacter">True when a <see cref="KCCPlayer"/> is present.</param>
		/// <param name="stateForwardingEnabled">The object's live <c>EnableStateForwarding</c>.</param>
		/// <returns>True when the NetworkTransform would be sending position a second time.</returns>
		internal static bool IsTransformRedundant(bool predictionMovesThisCharacter, bool stateForwardingEnabled)
		{
			return predictionMovesThisCharacter && stateForwardingEnabled;
		}

		/// <summary>How this peer runs this character each tick (see <see cref="ResolveTickMode"/>).</summary>
		public enum TickMode
		{
			/// <summary>FishNet prediction: the owner predicts itself, the server runs the owner's input and reconciles.</summary>
			Predicted,
			/// <summary>The server alone simulates it: an NPC. No replicate, no reconcile.</summary>
			ServerSimulated,
			/// <summary>Nothing to simulate here: a character this client does not own, shown by NetworkTransform and broadcasts.</summary>
			Observed,
		}

		/// <summary>
		/// Who simulates a character on this peer.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Prediction is the owning client's feature. An NPC has no owner, and nobody predicts it: its brain and every
		/// controller run on the server only, so the server steps it directly, without FishNet's replicate (input queue,
		/// history) or reconcile. Observers see it as they see another player: position through the NetworkTransform,
		/// state through the controllers' observer broadcasts (<see cref="IPredictableController.OnServerTick"/>).
		/// </para>
		/// <para>
		/// A client runs nothing for a character it does not own while state forwarding is off: FishNet's
		/// non-authoritative replicate would return at once. With forwarding on (a match that hands observers the owner's
		/// input), the observed character is predicted again.
		/// </para>
		/// </remarks>
		internal static TickMode ResolveTickMode(bool isServerStarted, bool serverDrivenInput, bool isOwner, bool stateForwarding)
		{
			if (serverDrivenInput)
			{
				return isServerStarted ? TickMode.ServerSimulated : TickMode.Observed;
			}
			if (isServerStarted || isOwner || stateForwarding)
			{
				return TickMode.Predicted;
			}
			return TickMode.Observed;
		}

		/// <summary>The controllers' once-a-tick server work, after the character's step.</summary>
		private void RunServerTickHooks()
		{
			for (int i = 0; i < controllers.Length; i++)
			{
				try
				{
					controllers[i].OnServerTick();
				}
				catch (Exception ex)
				{
					ReportReplicateFailure(controllers[i], ex);
				}
			}
		}

		/// <summary>
		/// Called on each tick. Runs this character the way <see cref="ResolveTickMode"/> says: predicted, simulated by
		/// the server alone, or not at all.
		/// </summary>
		private void TimeManager_OnTick()
		{
			// Snapshot the local authoritative tick (TimeManager.LocalTick) before any controller runs.
			// AbilityObject.OnTick callbacks subscribe to the same TimeManager.OnTick and may fire before
			// or after this method. Using CurrentLocalTickSnapshot avoids one-tick subscription-order drift.
			CurrentLocalTickSnapshot = base.TimeManager != null ? base.TimeManager.LocalTick : TimeManager.UNSET_TICK;
			CurrentReplicateTickSnapshot = TimeManager.UNSET_TICK;

			switch (ResolveTickMode(base.IsServerStarted, serverDrivenInput, base.IsOwner,
				base.NetworkObject != null && base.NetworkObject.EnableStateForwarding))
			{
				case TickMode.ServerSimulated:
					SimulateOnServer();
					return;
				case TickMode.Observed:
					// What the empty replicate left behind: no replicate tick, and the pending one spent.
					PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
					return;
			}

			CharacterReplicateData input = default;
			if (HasInputAuthority)
			{
				for (int i = 0; i < controllers.Length; i++)
				{
					controllers[i].PopulateInput(ref input);
				}
				KeepIdleViewClaim(ref input);
			}
			Replicate(input);
			PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
			CreateReconcile();
		}

		/// <summary>
		/// Whether an NPC's step can be skipped this tick: nobody observes it AND it is idle. Idle means its brain asked
		/// for nothing (inert input), no cast of its is running or in flight, and it is out of combat. An NPC fighting
		/// out of every player's sight (another NPC, a pet's target) keeps stepping, so the fight resolves.
		/// </summary>
		/// <remarks>Only an inert input is ever dropped: the brain's queued request is drained by PopulateInput, so
		/// skipping a tick that carried one would lose it.</remarks>
		internal static bool CanSkipServerStep(bool observed, bool inertInput, bool castActive, bool inCombat)
		{
			return !observed && inertInput && !castActive && !inCombat;
		}

		// ───── Server step schedule (NPCs on the server) ─────────────────────

		/// <summary>The schedule this NPC steps from on the server; null for every other character and peer.</summary>
		private ServerStepSchedule stepSchedule;

		/// <summary>Whether <see cref="NetworkObject_OnObserversActive"/> is subscribed.</summary>
		private bool observersHooked;

		/// <summary>Steps skipped in a row as idle and unseen (<see cref="CanSkipServerStep"/>).</summary>
		private int idleStepStreak;

		/// <summary>This NPC's nap bucket phase: its object id, so a scene's NPCs spread over every tick.</summary>
		private int napPhase;

		/// <summary>Skipped steps in a row before an idle, unseen NPC naps out of the tick: one second at 30 Hz.</summary>
		internal const int NapAfterIdleSteps = 30;

		/// <summary>Whether an NPC that has skipped <paramref name="idleStreak"/> steps in a row should nap.</summary>
		internal static bool ShouldNap(int idleStreak) => idleStreak >= NapAfterIdleSteps;

		/// <summary>True while this NPC sleeps out of the server's tick (<see cref="ServerStepSchedule"/>).</summary>
		public bool IsNapping => stepSchedule != null && ((IServerStepped)this).ScheduleSlot >= 0;

		int IServerStepped.ScheduleSlot { get; set; } = ServerStepSchedule.Unscheduled;
		int IServerStepped.AwakeIndex { get; set; }
		int IServerStepped.NapPhase => napPhase;

		void IServerStepped.ServerPreTick()
		{
			TimeManager_OnPreTick();
		}

		bool IServerStepped.ServerStep()
		{
			// The ServerSimulated branch of TimeManager_OnTick, without re-deciding a mode an NPC on the server always has.
			CurrentLocalTickSnapshot = base.TimeManager != null ? base.TimeManager.LocalTick : TimeManager.UNSET_TICK;
			CurrentReplicateTickSnapshot = TimeManager.UNSET_TICK;
			SimulateOnServer();
			return !ShouldNap(idleStepStreak);
		}

		void IServerStepped.OnNapped()
		{
			// What the skipped step left: no replicate tick. The two others answer the live tick while it naps.
			CurrentReplicateTickSnapshot = TimeManager.UNSET_TICK;
		}

		void IServerStepped.OnWoken()
		{
			// What its pre-tick would have published, for the rest of this tick (woken mid-tick, it steps this tick or next).
			uint now = LiveTick;
			CurrentLocalTickSnapshot = now;
			CurrentReplicateTickSnapshot = TimeManager.UNSET_TICK;
			PendingReplicateTickSnapshot = now;
		}

		/// <summary>
		/// Server: an NPC napping out of the tick steps again from now on. Call it when something gives the NPC work its
		/// step must do: its brain queued a cast, it entered combat. Does nothing for an awake NPC or any other character.
		/// </summary>
		/// <remarks>The nap's own once-a-second look catches anything that does not call this, a second late at worst.</remarks>
		public void WakeServerStep()
		{
			if (stepSchedule != null && IsNapping)
			{
				idleStepStreak = 0;
				stepSchedule.Wake(this);
			}
		}

		/// <summary>Takes this NPC out of the server step schedule for good (until its next spawn): nothing steps it.</summary>
		/// <remarks>For the combat sim's probe, which drives a fighter's ticks by hand.</remarks>
		private void LeaveStepSchedule()
		{
			if (stepSchedule != null)
			{
				stepSchedule.Remove(this);
				stepSchedule = null;
			}
		}

		/// <summary>An NPC a player can now see steps every tick again.</summary>
		private void NetworkObject_OnObserversActive(NetworkObject networkObject)
		{
			if (networkObject != null && networkObject.Observers.Count > 0)
			{
				WakeServerStep();
			}
		}

		/// <summary>The character this controller steps, for the idle test (<see cref="CanSkipServerStep"/>).</summary>
		private ICharacter simulatedCharacter;

		/// <summary>NPC steps skipped because nobody observed the NPC, since the process started (for reports).</summary>
		public static long UnobservedStepsSkipped;

		/// <summary>The replicate state an NPC's step runs with: a fresh tick of real (server-made) input, never a replay.</summary>
		internal const ReplicateState ServerStepState = ReplicateState.Ticked | ReplicateState.Created;

		/// <summary>
		/// One tick of an NPC, on the server: its brain's input, then every controller's step, then their observer pushes.
		/// </summary>
		/// <remarks>
		/// <para>
		/// What FishNet's authoritative replicate did for an ownerless object, minus what only prediction needs: the input
		/// queue and history, and the reconcile nobody receives. The input tick is the server's own tick, as it always was
		/// for an ownerless object (its replicate domain IS the server tick).
		/// </para>
		/// <para>
		/// <b>Nobody observing it and idle, nothing to do</b> (<see cref="CanSkipServerStep"/>). An idle NPC no player can see
		/// (a scene waiting to unload, the far side of a map) skips the step; one fighting out of sight keeps it. What the
		/// skipped step would have advanced is tick-scheduled and
		/// catches up when it is next seen: a buff runs every tick it missed, regeneration fires its missed pulses one per
		/// tick, a cooldown or cast compares ticks. Lag-compensation history records on its own (post-tick) as before.
		/// </para>
		/// </remarks>
		private void SimulateOnServer()
		{
			if (!base.IsSpawned || base.TimeManager == null)
			{
				PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
				return;
			}
			uint tick = base.TimeManager.LocalTick;
			CharacterReplicateData input = default;
			for (int i = 0; i < controllers.Length; i++)
			{
				controllers[i].PopulateInput(ref input);
			}
			input.SetTick(tick);

			/* The cheap facts first. An observed NPC, or one whose brain asked for something, steps without the
			 * cast, live-object and combat questions being asked at all. */
			bool observed = base.NetworkObject.Observers.Count > 0;
			if (!observed && IsInertInput(input) &&
				CanSkipServerStep(observed, true, CastInProgress || HasLiveAbilityObjects,
					simulatedCharacter != null && simulatedCharacter.IsFlagged(CharacterFlags.IsInCombat)))
			{
				UnobservedStepsSkipped++;
				++idleStepStreak;
				PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
				return;
			}

			idleStepStreak = 0;
			CurrentReplicateTickSnapshot = tick;
			PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
			for (int i = 0; i < controllers.Length; i++)
			{
				// One controller's failure stays its own, as in the replicate.
				try
				{
					controllers[i].OnReplicate(ref input, ServerStepState, Channel.Unreliable);
				}
				catch (Exception ex)
				{
					ReportReplicateFailure(controllers[i], ex);
				}
			}
			RunServerTickHooks();
		}

		/// <summary>
		/// Replicate method: runs all <see cref="IPredictableController.OnReplicate"/> calls in order.
		/// Updates <see cref="CurrentReplicateTickSnapshot"/> from the input tick.
		/// </summary>
		/// <param name="input">The unified replicate input data.</param>
		/// <param name="state">The current replicate state.</param>
		/// <param name="channel">The network channel.</param>
		[Replicate]
		private void Replicate(CharacterReplicateData input, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
		{
			CurrentReplicateTickSnapshot = input.GetTick();
			CaptureViewOffset(input, state);
			if (base.IsServerStarted)
			{
				// Counted on real input only: a tick that ran default data (an empty queue) is never "known idle".
				inertInputStreak = state.ContainsCreated() && IsInertInput(input) ? inertInputStreak + 1 : 0;
			}
			PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
			for (int i = 0; i < controllers.Length; i++)
			{
				/* One controller's failure stays its own. This runs inside TimeManager.OnTick, a plain multicast event: an
				 * exception escaping here skipped every subscriber after this character for that tick — every other
				 * character's prediction tick. On 11-16 a pet with no ability spawner threw on each cast (1,438 times), and
				 * while it fought, the wolves' attacks never resolved, so no hit landed on the player or the pet. */
				try
				{
					controllers[i].OnReplicate(ref input, state, channel);
				}
				catch (Exception ex)
				{
					ReportReplicateFailure(controllers[i], ex);
				}
			}
		}

		/// <summary>Tick of the last replicate failure logged, so a failure repeating every tick logs once a second.</summary>
		/// <remarks>Counted in ticks, not read from the wall clock: nothing in the prediction path reads one
		/// (ObserverSynchronizationProofTests), and a second of ticks is the same second.</remarks>
		private uint lastReplicateFailureLogTick;

		/// <summary>Whether a replicate failure has been logged yet.</summary>
		private bool replicateFailureLogged;

		/// <summary>Number of replicate failures since the last one logged.</summary>
		private int suppressedReplicateFailures;

		private void ReportReplicateFailure(IPredictableController failed, Exception ex)
		{
			uint now = base.TimeManager != null ? base.TimeManager.LocalTick : 0u;
			uint perSecond = base.TimeManager != null ? (uint)base.TimeManager.TickRate : 1u;
			if (replicateFailureLogged && now - lastReplicateFailureLogTick < perSecond)
			{
				++suppressedReplicateFailures;
				return;
			}
			replicateFailureLogged = true;
			lastReplicateFailureLogTick = now;
			string repeats = suppressedReplicateFailures > 0 ? $" ({suppressedReplicateFailures} more since the last report)" : string.Empty;
			suppressedReplicateFailures = 0;
			FishMMO.Logging.Log.Error("CharacterPredictionController",
				$"{gameObject.name}: {failed?.GetType().Name} threw in its replicate{repeats}; the other controllers and characters carry on.", ex);
		}

		/// <summary>
		/// Latches the client-measured view offset, but only from a replicate that carried real input.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>An empty input queue does not skip the tick — it runs the body with a default struct.</b>
		/// FishNet's <c>Replicate_NonAuthoritative</c> calls <c>ReplicateDefaultData()</c> whenever the
		/// server has nothing queued for this connection, which builds a default-initialised
		/// <see cref="CharacterReplicateData"/> and invokes the replicate body with it. Assigning the
		/// view offset unconditionally therefore wrote a ZERO on every such tick, and
		/// <see cref="LagCompensationTick.TryResolve"/> declines on a zero offset — so that tick's hits
		/// silently resolved against live positions.
		/// </para>
		/// <para>
		/// The queue holds <c>StateInterpolation</c> entries in steady state (2 on every shipped
		/// scene), so it empties on any loss or jitter spike costing more than two ticks, and again
		/// during the refill after one. That matters most for a projectile: its sweep reads this value
		/// on every tick of its flight, not the value from the tick it was cast on, so a mid-flight
		/// hiccup un-compensated part of a single shot.
		/// </para>
		/// <para>
		/// <b><c>Created</c> is the engine's own discriminator</b>, not one invented here. Real queued
		/// input runs as <c>Ticked | Created</c> on the server and on the owner alike, and a replay
		/// re-supplies the tick's actual input with <c>Created</c> still set; only
		/// <c>ReplicateDefaultData</c> omits it. Latching on it means a gap holds the last measured
		/// offset — which is the right answer, because the client's latency did not change just
		/// because a packet was late.
		/// </para>
		/// <para>
		/// Kept as its own method so the rule can be tested without going through the codegen-rewritten
		/// <c>[Replicate]</c> body.
		/// </para>
		/// </remarks>
		/// <param name="input">The replicate input for this invocation.</param>
		/// <param name="state">The replicate state FishNet invoked the body with.</param>
		internal void CaptureViewOffset(CharacterReplicateData input, ReplicateState state)
		{
			if (!state.ContainsCreated())
			{
				return;
			}

			CurrentViewOffsetTicks = input.ViewOffsetTicks;
			CurrentViewOffsetFraction = input.ViewOffsetFraction;
			CurrentViewInputTick = input.GetTick();
			CurrentViewTickOffset = input.ViewTickOffset;
			CurrentViewTickFraction = input.ViewTickFraction;
			viewClaims[CurrentViewInputTick % ViewClaimRing] = (CurrentViewInputTick, input.ViewTickOffset, input.ViewTickFraction);
		}

		/// <summary>
		/// Creates reconcile data by running all <see cref="IPredictableController.OnCreateReconcile"/>
		/// calls and dispatches via <see cref="Reconcile"/>.
		/// Gated on <see cref="NetworkBehaviour.IsServerStarted"/> and <see cref="NetworkBehaviour.IsSpawned"/>
		/// to prevent NRE during network startup.
		/// </summary>
		public override void CreateReconcile()
		{
			// Also gate on IsSpawned. CreateReconcile can be invoked by FishNet
			// after OnStartNetwork begins but before the NetworkObject is fully spawned;
			// dispatching reconcile in that window NREs through subsystem controllers
			// whose internal state isn't fully wired yet.
			if (base.IsServerStarted && base.IsSpawned)
			{
				CharacterReconcileData data = default;
				for (int i = 0; i < controllers.Length; i++)
				{
					controllers[i].OnCreateReconcile(ref data);
				}
				// Every tick, sent or not: the controllers' once-a-tick server work (observer pushes).
				RunServerTickHooks();
				uint tick = base.TimeManager.LocalTick;
				if (!ShouldSendReconcile(data.UnpackFlags, tick,
						base.NetworkManager.TransportManager.GetSendInterval(base.Owner)))
				{
					return;
				}
				/* Only a reconcile with an owner goes anywhere (state forwarding is off), so only those are judged and counted:
				 * an NPC's is written for FishNet's bookkeeping and dropped unsent, and judging it would serialise it every
				 * tick for nothing. */
				if (base.Owner.IsValid)
				{
					if (IsQuietReconcile(ref data, tick))
					{
						QuietReconcilesSkipped++;
						return;
					}
					ReconcilesSent++;
				}
				// FishNet decides full vs delta and names the delta's baseline when the reconcile is
				// actually written, so a tick whose send is skipped does not disturb the delta baseline.
				Reconcile(data);
			}
		}

		#region Quiet reconciles
		/// <summary>True to skip reconciles that repeat a settled state to an idle owner (see <see cref="ShouldSkipQuietReconcile"/>).</summary>
		public static bool QuietReconciles = true;
		/// <summary>Reconciles skipped as quiet since the process started (for the simulate report).</summary>
		public static long QuietReconcilesSkipped;
		/// <summary>Reconciles written since the process started (for the simulate report).</summary>
		public static long ReconcilesSent;

		/// <summary>Ticks of real, inert input the server must have run before a reconcile may be skipped.</summary>
		internal const int QuietInertTicks = 6;
		/// <summary>Identical reconciles sent in a row before the next identical one may be skipped: one lost reconcile never leaves the owner without the settled state.</summary>
		internal const int QuietConfirmations = 3;
		/// <summary>Even a quiet owner gets a reconcile at least this often, in ticks (1 s at 30 Hz).</summary>
		internal const uint QuietKeepaliveTicks = 30;

		/// <summary>The state groups <see cref="QuietMissCounts"/> counts. Declared first: static fields initialise in order.</summary>
		public static readonly string[] QuietMissNames =
			{ "position", "rotation", "velocity", "motor other", "resources", "ability", "cooldowns", "buffs", "equipment", "attributes", "exposure", "rng" };
		/// <summary>What changed in an idle owner's reconcile that therefore could not be skipped (index = <see cref="QuietMissNames"/>).</summary>
		public static readonly long[] QuietMissCounts = new long[QuietMissNames.Length];

		/// <summary>The reconcile last judged for an idle owner, to say what changed in the next one.</summary>
		private CharacterReconcileData lastQuietData;
		private bool hasLastQuietData;

		private static void NoteQuietMiss(in CharacterReconcileData now, in CharacterReconcileData before)
		{
			KinematicCharacterController.KinematicCharacterMotorState a = now.MotorState, b = before.MotorState;
			bool position = !a.Position.Equals(b.Position);
			bool rotation = !a.Rotation.Equals(b.Rotation);
			bool velocity = !a.BaseVelocity.Equals(b.BaseVelocity) || !a.AttachedRigidbodyVelocity.Equals(b.AttachedRigidbodyVelocity);
			bool motorOther = a.MustUnground != b.MustUnground || a.MustUngroundTime != b.MustUngroundTime ||
				a.TimeSinceLastAbleToJump != b.TimeSinceLastAbleToJump || a.TimeSinceJumpRequested != b.TimeSinceJumpRequested ||
				a.IsCrouching != b.IsCrouching || a.JumpRequested != b.JumpRequested || a.IsSwimming != b.IsSwimming ||
				a.IsFlying != b.IsFlying || a.MotionTicks != b.MotionTicks || a.MotionSpeed != b.MotionSpeed || a.MotionYaw != b.MotionYaw || a.ClimbPhase != b.ClimbPhase || a.ClimbTicks != b.ClimbTicks || a.ClimbOutTicks != b.ClimbOutTicks ||
				a.Climbable != b.Climbable || a.CurrentPlatformID != b.CurrentPlatformID ||
				a.LastMovementIterationFoundAnyGround != b.LastMovementIterationFoundAnyGround ||
				a.GroundingStatus.FoundAnyGround != b.GroundingStatus.FoundAnyGround ||
				a.GroundingStatus.IsStableOnGround != b.GroundingStatus.IsStableOnGround ||
				!a.GroundingStatus.GroundNormal.Equals(b.GroundingStatus.GroundNormal);
			Count(0, position);
			Count(1, rotation);
			Count(2, velocity);
			Count(3, motorOther);
			Count(4, !now.ResourceState.Equals(before.ResourceState));
			Count(5, now.AbilityID != before.AbilityID || now.RemainingTicks != before.RemainingTicks || now.ChargedHoldTicks != before.ChargedHoldTicks ||
				now.Seed != before.Seed || now.PackedFlagsAndSlot != before.PackedFlagsAndSlot);
			Count(6, !SameEntries(now.Cooldowns, before.Cooldowns));
			Count(7, !SameEntries(now.Buffs, before.Buffs));
			Count(8, !SameEntries(now.Equipment, before.Equipment));
			Count(9, !SameEntries(now.Attributes, before.Attributes));
			Count(10, !SameEntries(now.Exposure, before.Exposure));
			Count(11, now.RngS0 != before.RngS0 || now.RngS1 != before.RngS1 || now.RngS2 != before.RngS2 || now.RngS3 != before.RngS3);

			static void Count(int group, bool changed)
			{
				if (changed)
				{
					QuietMissCounts[group]++;
				}
			}
		}

		private static bool SameEntries<T>(T[] a, T[] b)
		{
			if (ReferenceEquals(a, b))
			{
				return true;
			}
			int lengthA = a?.Length ?? 0, lengthB = b?.Length ?? 0;
			if (lengthA != lengthB)
			{
				return false;
			}
			EqualityComparer<T> comparer = EqualityComparer<T>.Default;
			for (int i = 0; i < lengthA; ++i)
			{
				if (!comparer.Equals(a[i], b[i]))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Consecutive server ticks that ran real input with no movement, action or request in it.</summary>
		private int inertInputStreak;
		/// <summary>Identical reconciles sent in a row (by their full serialization).</summary>
		private int identicalReconcileSends;
		/// <summary>Server tick of the last reconcile written.</summary>
		private uint lastReconcileSentTick = TimeManager.UNSET_TICK;
		/// <summary>Full serialization of the last reconcile written while the owner was idle; length -1 when none.</summary>
		private byte[] lastReconcileBytes = new byte[256];
		private int lastReconcileLength = -1;

		/// <summary>
		/// Opts the character input into FishNet's held idle input (FISHMMO EDIT, ReplicateIdle): an owner standing still
		/// stops sending the same input every tick, and the server repeats it meanwhile. Registered on both sides before any
		/// replicate runs; the rule is the same on both, which is what makes a held tick identical to the owner's own.
		/// </summary>
		[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
		internal static void RegisterHeldInput()
		{
			ReplicateIdle<CharacterReplicateData>.CanHold = data => IsInertInput(data);
			ReplicateIdle<CharacterReplicateData>.SameInput = (a, b) => SameHeldInput(a, b);
			ReplicateIdle<CharacterReplicateData>.CanHoldNow = behaviour => !(behaviour is CharacterPredictionController controller && controller.CastInProgress);
		}

		/// <summary>The view claim of the last input this owner built, for <see cref="KeepIdleViewClaim"/>.</summary>
		private ushort lastViewTickOffset;
		private byte lastViewTickFraction;
		private byte lastViewOffsetTicks;
		private byte lastViewOffsetFraction;
		private bool hasLastViewClaim;

		/// <summary>
		/// Owner: while nothing can use the view claim, the input keeps the last one, so an idle input repeats.
		/// </summary>
		/// <remarks>
		/// The claim (the tick this client was drawing) carries the frame's lateness past the tick boundary, which changes
		/// from frame to frame at any frame rate that does not divide the tick rate (75, 144, 165, uncapped), and the timing's
		/// small clock adjustments move it too. An idle player's input then changed every few ticks, so it was sent every
		/// tick and the server never saw the two repeats it holds on. The claim is only read to rewind other characters for
		/// this character's hits: with no cast in progress and nothing of its in flight there is nothing to rewind for, and
		/// the input that starts a cast carries a fresh claim.
		/// </remarks>
		private void KeepIdleViewClaim(ref CharacterReplicateData input)
		{
			bool claimUnused = IsInertInput(input) && !CastInProgress && !HasLiveAbilityObjects;
			if (claimUnused && hasLastViewClaim)
			{
				input.ViewTickOffset = lastViewTickOffset;
				input.ViewTickFraction = lastViewTickFraction;
				input.ViewOffsetTicks = lastViewOffsetTicks;
				input.ViewOffsetFraction = lastViewOffsetFraction;
				return;
			}
			lastViewTickOffset = input.ViewTickOffset;
			lastViewTickFraction = input.ViewTickFraction;
			lastViewOffsetTicks = input.ViewOffsetTicks;
			lastViewOffsetFraction = input.ViewOffsetFraction;
			hasLastViewClaim = true;
		}

		/// <summary>True while this character has an ability object of its own in the world (a projectile in flight).</summary>
		private bool HasLiveAbilityObjects
		{
			get
			{
				if (!castSourceResolved)
				{
					castSourceResolved = true;
					castSource = GetComponent<IAbilityController>();
				}
				return castSource != null && castSource.HasLiveObjects;
			}
		}

		/// <summary>
		/// True while this character's ability controller has a cast in progress: its input is then never held, on either
		/// side (<see cref="ReplicateIdle{T}.CanHoldNow"/>).
		/// </summary>
		/// <remarks>
		/// An input sent during a cast reads as inert (no keys, nothing queued), but repeating it is not harmless: every
		/// tick that runs counts the cast. Read from the STATE each peer already has, not sent: the owner asks its own
		/// predicted controller and keeps sending every tick of a cast; the server asks its own and, when a tick's input
		/// has not arrived, runs a blank tick, which the cast waits out (AbilityController.FreezesOnBlankTick). Where the
		/// two disagree for a moment it is safe in both directions: an owner that believes it is still casting only sends
		/// more, and a server still on a cast the owner has finished gets that cast's last inputs before the owner goes
		/// quiet.
		/// </remarks>
		internal bool CastInProgress
		{
			get
			{
				if (!castSourceResolved)
				{
					castSourceResolved = true;
					castSource = GetComponent<IAbilityController>();
				}
				return castSource != null && castSource.IsActivating;
			}
		}

		private IAbilityController castSource;
		private bool castSourceResolved;

		/// <summary>
		/// True when two inputs are identical apart from their tick: every axis, flag, request, view claim and the exact aim.
		/// </summary>
		/// <remarks>
		/// Exact, not approximate: the server runs the held input in place of the owner's, so a repeat has to be the very
		/// input the owner ran. A camera turned by the smallest step changes the aim, and that input is sent.
		/// </remarks>
		internal static bool SameHeldInput(in CharacterReplicateData a, in CharacterReplicateData b)
		{
			return a.MoveAxisForward.Equals(b.MoveAxisForward) && a.MoveAxisRight.Equals(b.MoveAxisRight) &&
				a.MoveFlags == b.MoveFlags &&
				a.ViewOffsetTicks == b.ViewOffsetTicks && a.ViewOffsetFraction == b.ViewOffsetFraction &&
				a.ViewTickOffset == b.ViewTickOffset && a.ViewTickFraction == b.ViewTickFraction &&
				a.AimDirection.x.Equals(b.AimDirection.x) && a.AimDirection.y.Equals(b.AimDirection.y) && a.AimDirection.z.Equals(b.AimDirection.z) &&
				a.HeadingOffset == b.HeadingOffset && a.ConvergenceDistance == b.ConvergenceDistance &&
				a.AimOriginRight == b.AimOriginRight && a.AimOriginUp == b.AimOriginUp &&
				a.ActivationFlags == b.ActivationFlags && a.QueuedAbilityID == b.QueuedAbilityID &&
				a.EquipmentRequest == b.EquipmentRequest && a.EquipmentIndex == b.EquipmentIndex &&
				a.HitClaimVictim == b.HitClaimVictim && a.HitClaimSpawnAge == b.HitClaimSpawnAge && a.HitClaimObject == b.HitClaimObject;
		}

		/// <summary>
		/// An input that moves nothing and asks for nothing: no axes, no move flag, no activation flag, nothing queued, no
		/// equipment request. The aim may be anything: turning the camera turns the character, and the state then differs.
		/// </summary>
		internal static bool IsInertInput(in CharacterReplicateData input)
		{
			const int actualData = 1 << (int)KCCMoveFlags.IsActualData;
			const int actualAbilityData = 1 << (int)AbilityActivationFlags.IsActualData;
			return input.MoveAxisForward == 0f && input.MoveAxisRight == 0f &&
				(input.MoveFlags & ~actualData) == 0 &&
				(input.ActivationFlags & ~actualAbilityData) == 0 &&
				input.QueuedAbilityID == 0 && input.EquipmentRequest == 0 &&
				// One-shot requests: an input carrying one asks for something, and must never be run again as a repeat.
				input.ConvergenceDistance == 0 && input.HitClaimVictim == 0;
		}

		/// <summary>
		/// Whether a reconcile may be skipped because it would tell an idle owner nothing new.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A standing player costs a reconcile on every flush tick, each one repeating the last. All of these must hold
		/// to skip one:
		/// </para>
		/// <list type="bullet">
		/// <item>No one-shot report (Denied, NoSpawn) rides it.</item>
		/// <item>The server has run <see cref="QuietInertTicks"/> ticks of the owner's real input, all of it inert, so the owner
		/// predicted the same nothing; a key pressed against a wall is input, and keeps every reconcile flowing.</item>
		/// <item>Its full serialization is byte for byte the last one sent, and that one was sent
		/// <see cref="QuietConfirmations"/> times in a row, so even a lost packet left the owner with the settled state.</item>
		/// <item>The last one sent is less than <see cref="QuietKeepaliveTicks"/> old.</item>
		/// </list>
		/// <para>
		/// Anything that changes the state (regeneration, a buff ticking down, damage, a turn of the camera) makes the bytes
		/// differ and goes out at once, exactly as before.
		/// </para>
		/// </remarks>
		internal static bool ShouldSkipQuietReconcile(bool oneShotReport, int inertStreak, int identicalSends, bool sameAsLastSent, uint ticksSinceLastSent)
		{
			return !oneShotReport &&
				inertStreak >= QuietInertTicks &&
				identicalSends >= QuietConfirmations &&
				sameAsLastSent &&
				ticksSinceLastSent < QuietKeepaliveTicks;
		}

		/// <summary>
		/// Decides <see cref="ShouldSkipQuietReconcile"/> for this tick's reconcile, and records it as sent when it is not skipped.
		/// </summary>
		private bool IsQuietReconcile(ref CharacterReconcileData data, uint tick)
		{
			int flags = data.UnpackFlags;
			bool oneShot = flags.IsFlagged(AbilityActivationFlags.Denied) || flags.IsFlagged(AbilityActivationFlags.NoSpawn);
			if (!QuietReconciles || oneShot || inertInputStreak == 0)
			{
				identicalReconcileSends = 0;
				lastReconcileLength = -1;
				lastReconcileSentTick = tick;
				hasLastQuietData = false;
				return false;
			}

			PooledWriter writer = WriterPool.Retrieve();
			writer.WriteCharacterReconcileData(data);
			ArraySegment<byte> bytes = writer.GetArraySegment();
			bool same = lastReconcileLength == bytes.Count &&
				bytes.AsSpan().SequenceEqual(new ReadOnlySpan<byte>(lastReconcileBytes, 0, lastReconcileLength));
			uint sinceSent = lastReconcileSentTick == TimeManager.UNSET_TICK ? uint.MaxValue : tick - lastReconcileSentTick;
			if (ShouldSkipQuietReconcile(false, inertInputStreak, identicalReconcileSends, same, sinceSent))
			{
				WriterPool.Store(writer);
				return true;
			}
			// An idle owner's reconcile that has to go because the state moved: note what moved (simulate report).
			if (!same && inertInputStreak >= QuietInertTicks && hasLastQuietData)
			{
				NoteQuietMiss(in data, in lastQuietData);
			}
			lastQuietData = data;
			hasLastQuietData = true;

			identicalReconcileSends = same ? identicalReconcileSends + 1 : 1;
			if (lastReconcileBytes.Length < bytes.Count)
			{
				lastReconcileBytes = new byte[Math.Max(bytes.Count, lastReconcileBytes.Length * 2)];
			}
			System.Buffer.BlockCopy(bytes.Array, bytes.Offset, lastReconcileBytes, 0, bytes.Count);
			lastReconcileLength = bytes.Count;
			lastReconcileSentTick = tick;
			WriterPool.Store(writer);
			return false;
		}
		#endregion

		/// <summary>
		/// Whether the server sends this tick's reconcile: on every tick it flushes outgoing data, and on any tick whose
		/// reconcile carries a one-shot report.
		/// </summary>
		/// <remarks>
		/// <para>
		/// With a send interval of 2 two reconciles left in each packet and the owner used only the newer: the older was
		/// 36 bytes of nothing, a third of the owner's own traffic. The one sent is for the tick the packet leaves on, so the
		/// owner corrects from the same state as before, no later. With an interval of 1 every tick is sent, as before.
		/// </para>
		/// <para>
		/// <b>One-shot reports are never skipped.</b> A reconcile can carry an event that belongs to its own tick and is
		/// consumed when it is built: an activation the server denied (Denied) and a predicted spawn the server did not make
		/// (NoSpawn, which only means anything on the tick it describes). Dropping that tick's reconcile would drop the
		/// report, and the owner would keep a ghost projectile or a cast the server refused.
		/// </para>
		/// </remarks>
		internal static bool ShouldSendReconcile(int abilityFlags, uint tick, byte sendInterval)
		{
			if (sendInterval <= 1 || tick % sendInterval == 0)
			{
				return true;
			}
			return abilityFlags.IsFlagged(AbilityActivationFlags.Denied) || abilityFlags.IsFlagged(AbilityActivationFlags.NoSpawn);
		}

		/// <summary>
		/// Reconcile method: runs all <see cref="IPredictableController.OnReconcile"/> calls in order
		/// to restore state from the server's authoritative reconcile data.
		/// </summary>
		/// <param name="rd">The reconcile data from the server.</param>
		/// <param name="channel">The network channel.</param>
		[Reconcile]
		private void Reconcile(CharacterReconcileData rd, Channel channel = Channel.Unreliable)
		{
			for (int i = 0; i < controllers.Length; i++)
			{
				controllers[i].OnReconcile(rd, channel);
			}
		}
	}
}