using System;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Managing.Timing;
using System.Collections.Generic;

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

		private IPredictableController[] controllers = Array.Empty<IPredictableController>();

		private void Awake()
		{
			List<IPredictableController> list = new List<IPredictableController>();
			GetComponents(list);
			list.Sort((a, b) => a.Order.CompareTo(b.Order));
			controllers = list.ToArray();
		}

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

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		public override void OnStopNetwork()
		{
			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
			CurrentLocalTickSnapshot = TimeManager.UNSET_TICK;
			CurrentReplicateTickSnapshot = TimeManager.UNSET_TICK;

			base.OnStopNetwork();
		}

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
			CreateReconcile();
		}

		[Replicate]
		private void Replicate(CharacterReplicateData input, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
		{
			CurrentReplicateTickSnapshot = input.GetTick();
			for (int i = 0; i < controllers.Length; i++)
			{
				controllers[i].OnReplicate(ref input, state, channel);
			}
		}

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