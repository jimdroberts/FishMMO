using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server → owner: every portal in one scene that is open for this player, replacing what the
	/// client held for that scene.
	/// </summary>
	/// <remarks>
	/// <para>Sent when the player enters a scene and again whenever the server finishes reading
	/// the records that decide it, so a client never has to ask. Only open portals are listed;
	/// anything absent is closed. Two parallel arrays of primitives rather than a struct array, so
	/// no custom serializer is involved (six bytes a portal).</para>
	/// <para>Nothing here is trusted: travel is re-decided on the server at every use. The client
	/// copy only drives how a portal looks.</para>
	/// </remarks>
	public struct PortalStatesBroadcast : IBroadcast
	{
		/// <summary>The scene the portals stand in (the scene asset's name).</summary>
		public string SceneName;

		/// <summary>The open portals' authored indices.</summary>
		public ushort[] Indices;

		/// <summary>
		/// Seconds each stays open, rounded up; <see cref="PortalActivationRules.NoExpiry"/> for one
		/// that does not close (opened for this character, or permanently for the world).
		/// </summary>
		public int[] RemainingSeconds;
	}

	/// <summary>
	/// Server → client: one portal opened — for the owner alone (a per-character activation) or
	/// for everyone in the scene (a world activation, sent to every player standing in it).
	/// </summary>
	/// <remarks>
	/// Rare (a portal opens once per character, or once per opening for the world), so it names its
	/// scene rather than relying on the receiver's current scene, which can change in flight.
	/// </remarks>
	public struct PortalStateChangedBroadcast : IBroadcast
	{
		public string SceneName;
		public ushort Index;

		/// <summary>See <see cref="PortalStatesBroadcast.RemainingSeconds"/>.</summary>
		public int RemainingSeconds;
	}
}
