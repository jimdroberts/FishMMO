using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Authority gate for region actions that mutate gameplay state (buffs, attributes).
	/// The server is authoritative for gameplay; clients only observe the result through the
	/// normal replication/reconcile path, so these actions must never run on a client peer.
	/// </summary>
	public static class RegionActionGate
	{
		/// <summary>
		/// Pure decision: may a gameplay-mutating region action execute?
		/// </summary>
		/// <param name="hasInitiator">True when the event carries a live initiator with a network object.</param>
		/// <param name="isServerStarted">True when this peer is running the server.</param>
		/// <param name="isReconciling">True while the prediction system is replaying (never mutate then).</param>
		public static bool Decide(bool hasInitiator, bool isServerStarted, bool isReconciling)
		{
			return hasInitiator && isServerStarted && !isReconciling;
		}

		/// <summary>
		/// Evaluates <see cref="Decide"/> against a real initiator and event.
		/// </summary>
		public static bool ShouldExecuteGameplay(ICharacter initiator, EventData eventData)
		{
			bool hasInitiator = initiator != null && initiator.NetworkObject != null;
			bool isServer = hasInitiator && initiator.NetworkObject.IsServerStarted;
			bool reconciling = eventData != null &&
				eventData.TryGet(out RegionEventData regionData) &&
				regionData.IsReconciling;
			return Decide(hasInitiator, isServer, reconciling);
		}
	}
}
