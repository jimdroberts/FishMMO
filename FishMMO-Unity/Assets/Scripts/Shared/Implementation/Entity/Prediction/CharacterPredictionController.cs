using System;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Connection;
using FishNet.Managing.Timing;
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
	/// is built once in <c>Awake</c>. Components added after <c>Awake</c> completes
	/// will not participate in the prediction pipeline. All <see cref="IPredictableController"/>
	/// implementations must be attached to the GameObject before the scene loads.
	/// </remarks>
	public class CharacterPredictionController : NetworkBehaviour
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
		public uint CurrentLocalTickSnapshot { get; private set; } = TimeManager.UNSET_TICK;

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
		public uint PendingReplicateTickSnapshot { get; private set; } = TimeManager.UNSET_TICK;

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

		/// <summary>
		/// Cached array of <see cref="IPredictableController"/> components, sorted by <see cref="IPredictableController.Order"/>.
		/// Built once in Awake; dynamic additions after Awake will not participate.
		/// </summary>
		private IPredictableController[] controllers = Array.Empty<IPredictableController>();

		/// <summary>
		/// True when this character's replicate input is produced by a server-side brain
		/// (an <see cref="FishMMO.Shared.Core.IAIController"/>) rather than by a remote client.
		/// </summary>
		/// <remarks>
		/// Ownership alone cannot answer "who writes this character's input?". A monster is
		/// server-owned and has no owning connection, while a pet is owned by the connection of
		/// the player that summoned it — yet both are driven entirely by a server-side
		/// <c>AIController</c>. Gating input on <see cref="NetworkBehaviour.IsOwner"/> therefore
		/// left monsters with nobody producing input at all, and would have let a pet owner's
		/// client produce input for a brain that does not run there.
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
			List<IPredictableController> list = new List<IPredictableController>();
			GetComponents(list);
			List<IPredictableController> sortedList = list
				.OrderBy(c => c.Order)
				.ThenBy(c => c.GetType().FullName)
				.ToList();
			controllers = sortedList.ToArray();

			// An AI brain on the same GameObject means the server writes this character's input.
			serverDrivenInput = GetComponent<FishMMO.Shared.Core.IAIController>() != null;
		}

		/// <summary>
		/// Subscribes to <see cref="TimeManager.OnPreTick"/> and <see cref="TimeManager.OnTick"/> events
		/// and validates that state forwarding is enabled for observer prediction.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

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

			if (base.TimeManager != null)
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
		}

		/// <summary>Removes this character from per-observer streaming.</summary>
		public override void OnStopServer()
		{
			ObserverStreamingRegistry.Unregister(base.NetworkObject);
			base.OnStopServer();
		}

		/// <summary>
		/// Unsubscribes from <see cref="TimeManager"/> tick events and resets all tick snapshots to unset.
		/// </summary>
		public override void OnStopNetwork()
		{
			if (base.TimeManager != null)
			{
				base.TimeManager.OnPreTick -= TimeManager_OnPreTick;
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
			CurrentLocalTickSnapshot = TimeManager.UNSET_TICK;
			CurrentReplicateTickSnapshot = TimeManager.UNSET_TICK;
			PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
			ClearViewOffset();

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
		}

		/// <summary>Resets the latched view offset to "nothing to compensate".</summary>
		private void ClearViewOffset()
		{
			CurrentViewOffsetTicks = 0;
			CurrentViewOffsetFraction = 0;
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

		/// <summary>
		/// Called on each tick. Populates input for the owning client, runs <see cref="Replicate"/>,
		/// and creates reconcile data on the server.
		/// </summary>
		private void TimeManager_OnTick()
		{
			// Snapshot the local authoritative tick (TimeManager.LocalTick) before any controller runs.
			// AbilityObject.OnTick callbacks subscribe to the same TimeManager.OnTick and may fire before
			// or after this method. Using CurrentLocalTickSnapshot avoids one-tick subscription-order drift.
			CurrentLocalTickSnapshot = base.TimeManager != null ? base.TimeManager.LocalTick : TimeManager.UNSET_TICK;
			CurrentReplicateTickSnapshot = TimeManager.UNSET_TICK;
			CharacterReplicateData input = default;
			if (HasInputAuthority)
			{
				for (int i = 0; i < controllers.Length; i++)
				{
					controllers[i].PopulateInput(ref input);
				}
			}
			Replicate(input);
			PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
			CreateReconcile();
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
			PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
			for (int i = 0; i < controllers.Length; i++)
			{
				controllers[i].OnReplicate(ref input, state, channel);
			}
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
				// CharacterReconcileData.Sequence is stamped by FishNet when the reconcile is actually
				// written (ReconcileSequenceStamper), so a tick whose send is skipped does not count.
				Reconcile(data);
			}
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