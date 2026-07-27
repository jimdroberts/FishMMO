using System;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Managing.Timing;
using System.Collections.Generic;
using System.Linq;

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
		}

		/// <summary>
		/// Subscribes to <see cref="TimeManager.OnPreTick"/> and <see cref="TimeManager.OnTick"/> events
		/// and validates that state forwarding is enabled for observer prediction.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			// State forwarding is REQUIRED for non-owner observers to receive reconciles and stay in sync.
			// Without it, only the owning client + server run the predicted state machine; observers will desync.
			if (base.NetworkObject != null && !base.NetworkObject.EnableStateForwarding)
			{
				FishMMO.Logging.Log.Warning(
					"CharacterPredictionController",
					$"State forwarding is disabled on NetworkObject '{base.NetworkObject.name}'. Predicted observers will desync. " +
					"Enable 'State Forwarding' on the NetworkObject's Prediction settings.");
			}

			// One-line spawn contract dump (client + server). Compare PrefabId / NB count /
			// ComponentIndex of this controller after dual rebuild if PacketId spam returns.
			if (base.NetworkObject != null)
			{
				int nbCount = base.NetworkObject.NetworkBehaviours != null
					? base.NetworkObject.NetworkBehaviours.Count
					: -1;
				FishMMO.Logging.Log.Info(
					"CharacterPredictionController",
					$"Network contract asServer={base.IsServerStarted} name={base.NetworkObject.name} " +
					$"PrefabId={base.NetworkObject.PrefabId} ObjectId={base.ObjectId} " +
					$"ComponentIndex={base.ComponentIndex} NetworkBehaviours={nbCount}");
			}

			if (base.TimeManager != null)
			{
				base.TimeManager.OnPreTick += TimeManager_OnPreTick;
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
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

			if (base.IsController)
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
			if (base.IsOwner)
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