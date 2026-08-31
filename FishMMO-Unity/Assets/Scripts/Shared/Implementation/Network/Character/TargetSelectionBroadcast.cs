using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Tells the server which character the player's targeting frame currently shows.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Client → server, advisory, and verified on receipt.</b> The server's own
	/// <c>TargetController.Current</c> is cast-scoped — written only when an ability acquisition
	/// resolves, cleared by any miss — so the "current target" pin in
	/// <c>ObserverStreamingRegistry</c> did not exist for a target the player was looking at but
	/// had not yet landed a cast on: in an over-budget crowd, the intended opponent could be
	/// evicted at the exact moment the player engaged. This message closes that gap by telling
	/// the server what the player's frame shows, at most a few times a second, only on change.
	/// </para>
	/// <para>
	/// <b>Keyed on <c>NetworkObject.ObjectId</c>, not the character id.</b> The ObjectId is
	/// already replicated to every peer by FishNet (no payload work), it is what the streaming
	/// pin compares, and targeting is always same-scene so the character id's cross-scene
	/// stability buys nothing here. The server treats the value as a CLAIM: it must resolve to a
	/// live character in the sender's own scene or it is stored as no-target, and the pin that
	/// reads it is additionally bounded by the engagement range ceiling at use — a forged id can
	/// therefore pin nothing the sender could not legitimately be looking at.
	/// </para>
	/// <para>
	/// Nothing gameplay-authoritative reads this. Ability target acquisition stays a server-side
	/// lag-compensated raycast from the replicated aim; this value feeds interest management (and
	/// any future UI mirroring), never combat resolution.
	/// </para>
	/// </remarks>
	public struct TargetSelectionBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the targeted character, or 0 for no target.</summary>
		public int TargetObjectID;
	}
}
