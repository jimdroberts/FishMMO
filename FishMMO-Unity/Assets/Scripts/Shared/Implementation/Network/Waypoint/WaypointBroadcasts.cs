using FishNet.Broadcast;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Client → server: fast travel to a discovered waypoint in the scene the character stands in.
	/// </summary>
	/// <remarks>
	/// Carries the scene name the client's map was showing, not just the index, so a request
	/// from a map that has not yet noticed a zone change is refused as
	/// <see cref="WaypointTravelRefusalReason.NotInScene"/> rather than resolving index N in
	/// whatever scene the character happens to be in now. Cross-scene travel is deliberately not
	/// a thing this message can ask for; that is the world-map system's job later.
	/// </remarks>
	public struct WaypointTravelRequestBroadcast : IBroadcast
	{
		/// <summary>The scene the waypoint stands in — must be the character's current scene.</summary>
		public string SceneName;

		/// <summary>The waypoint's authored index within that scene.</summary>
		public int WaypointIndex;
	}

	/// <summary>
	/// Server → owner: the fast-travel request was declined, and why.
	/// </summary>
	public struct WaypointTravelRefusedBroadcast : IBroadcast
	{
		public string SceneName;
		public int WaypointIndex;
		public WaypointTravelRefusalReason Reason;
	}

	/// <summary>
	/// Server → owner: the character has been moved to the waypoint. The position itself
	/// arrives through the reconcile; this exists so the map can close and the arrival sound can
	/// play at the moment it happened rather than when the client notices the jump.
	/// </summary>
	public struct WaypointTravelledBroadcast : IBroadcast
	{
		public string SceneName;
		public int WaypointIndex;
	}

	/// <summary>
	/// Server → owner: a waypoint was discovered. The owner's controller adds it and the map
	/// starts drawing it.
	/// </summary>
	public struct WaypointUnlockedBroadcast : IBroadcast
	{
		public string SceneName;
		public int WaypointIndex;
	}

	/// <summary>
	/// Server → owner: open the world map on this waypoint. Sent when the character interacts
	/// with a waypoint they had already discovered — the waypoint object doubles as the way to
	/// reach the travel map, as it does in the games this borrows from.
	/// </summary>
	public struct WaypointOpenMapBroadcast : IBroadcast
	{
		public string SceneName;
		public int WaypointIndex;
	}
}
