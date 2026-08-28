using System;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
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

		/// <summary>Last <see cref="CharacterReconcileData.Sequence"/> sent by this server object.</summary>
		private byte reconcileSequence;

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

			base.OnStopNetwork();
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
			PendingReplicateTickSnapshot = TimeManager.UNSET_TICK;
			for (int i = 0; i < controllers.Length; i++)
			{
				controllers[i].OnReplicate(ref input, state, channel);
			}
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
				// Chain sequence for the delta reader's loss detection — see CharacterReconcileData.Sequence.
				reconcileSequence = unchecked((byte)(reconcileSequence + 1));
				data.Sequence = reconcileSequence;
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