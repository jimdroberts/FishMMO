using FishNet.Object;

namespace FishMMO.Shared
{
	/// <summary>
	/// Decides which of the two observer synchronisation systems owns a character's state.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A character's state reaches its observers one of two ways, and exactly one of them must be
	/// active at a time:
	/// </para>
	/// <list type="number">
	/// <item>
	/// <b>Forwarded.</b> <c>NetworkObject.EnableStateForwarding</c> is on, so FishNet relays the
	/// owner's replicate input and the server's whole <c>CharacterReconcileData</c> to every
	/// observer, and observers simulate their peers. Exact, and expensive: measured at ~2,610 B/s
	/// per observed peer against ~409 B/s for the alternative, scaling with observers times
	/// entities. Intended for small, precision-critical scenes — an arena or a tournament match.
	/// </item>
	/// <item>
	/// <b>Interpolated.</b> Forwarding is off. The reconcile goes to the owner alone, position
	/// arrives through <c>NetworkTransform</c>, and each controller publishes what observers
	/// actually need through its own change-gated broadcast. This is the open-world mode.
	/// </item>
	/// </list>
	/// <para>
	/// <b>Why this type exists.</b> The two systems overlap completely. Turning forwarding on
	/// without silencing the broadcasts does not merely pay for both — for equipment and buffs it
	/// produces conflicting writes, because the reconcile path and the broadcast path build
	/// different objects for the same slot and build items carrying different identities for the same slot.
	/// Every push site and every reconcile consumer asks this type whose turn it is, so the flag is
	/// a real switch rather than a trap for whoever flips it.
	/// </para>
	/// <para>
	/// <b>The flag is read live, never cached.</b> <c>SetStateForwarding</c> can flip at runtime, and
	/// every FishNet send path reads the property per send, so these helpers do too. A controller
	/// that cached the answer in <c>OnStartNetwork</c> would keep broadcasting into a scene that had
	/// since switched modes.
	/// </para>
	/// </remarks>
	public static class ObserverSyncMode
	{
		/// <summary>
		/// True when this object's observers are fed by discrete broadcasts rather than by the
		/// forwarded reconcile stream.
		/// </summary>
		/// <remarks>
		/// A null or unspawned object answers true. Such an object has no observers to send to, so
		/// the answer is only reached by callers that then find nothing to do — and answering false
		/// would silently disable the broadcast path for a character whose NetworkObject had not
		/// been assigned yet, which is the harder failure to notice.
		/// </remarks>
		public static bool ShouldBroadcastToObservers(NetworkObject networkObject)
		{
			return networkObject == null || !networkObject.EnableStateForwarding;
		}

		/// <summary>
		/// True when this object's observers receive — and must act on — the forwarded reconcile.
		/// </summary>
		/// <remarks>
		/// Used by reconcile consumers that mutate observer-visible state, so a non-owner applies
		/// reconcile data only in the mode where that data is the authority for it. In the
		/// interpolated mode the same fields arrive through a broadcast instead, and letting both
		/// write is what produces duplicate effects and phantom items.
		/// </remarks>
		public static bool ObserversConsumeReconcile(NetworkObject networkObject)
		{
			return networkObject != null && networkObject.EnableStateForwarding;
		}
	}
}
