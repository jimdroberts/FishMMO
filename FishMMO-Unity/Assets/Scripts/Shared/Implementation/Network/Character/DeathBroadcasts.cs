using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Sent server to client when a player dies. Triggers the death dialog UI.
	/// </summary>
	public struct DeathBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Sent server to client when another player offers a resurrect.
	/// The client adds an "Accept Resurrect" button to the death dialog.
	/// </summary>
	public struct ResurrectOfferBroadcast : IBroadcast
	{
		/// <summary>Character ID of the player offering the resurrect.</summary>
		public long ResurrectorID;
	}

	/// <summary>
	/// Sent client to server when the dead player accepts a resurrect.
	/// </summary>
	public struct ResurrectAcceptBroadcast : IBroadcast
	{
		/// <summary>Character ID of the resurrector (must match the offer).</summary>
		public long ResurrectorID;
	}

	/// <summary>
	/// Sent client to server when the dead player chooses to respawn at their bind point.
	/// </summary>
	public struct RespawnAtBindPointBroadcast : IBroadcast
	{
	}
}
