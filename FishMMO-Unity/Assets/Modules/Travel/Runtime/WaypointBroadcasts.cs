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
	/// <remarks>
	/// <b>The scene is not named.</b> Cross-scene travel is not expressible here, so a refusal is
	/// always about the scene the owner is standing in and the controller substitutes
	/// <c>CurrentSceneName()</c> when it raises the event. The one refusal whose request named a
	/// DIFFERENT scene is <see cref="WaypointTravelRefusalReason.NotInScene"/> — a map that had not
	/// noticed a zone change — and there the scene worth reporting is the one the character is
	/// actually in, which is what the substitution gives. Nothing reads the name on this path
	/// anyway: the map hands its button back and prints the reason.
	/// </remarks>
	public struct WaypointTravelRefusedBroadcast : IBroadcast
	{
		public int WaypointIndex;
		public WaypointTravelRefusalReason Reason;
	}

	/// <summary>
	/// Server → owner: the character has been moved to the waypoint. The position itself
	/// arrives through the reconcile; this exists so the map can close and the arrival sound can
	/// play at the moment it happened rather than when the client notices the jump.
	/// </summary>
	/// <remarks>
	/// <b>The scene is not named.</b> Travel resolves the waypoint out of the character's own
	/// scene (<c>WaypointRegistry.TryGet</c> is keyed by its scene handle), so the arrival is
	/// always in the scene the owner is standing in; the controller substitutes
	/// <c>CurrentSceneName()</c>.
	/// </remarks>
	public struct WaypointTravelledBroadcast : IBroadcast
	{
		public int WaypointIndex;
	}

	/// <summary>
	/// Server → owner: a waypoint was discovered. The owner's controller adds it and the map
	/// starts drawing it.
	/// </summary>
	/// <remarks>
	/// <b>This one really does need the scene name</b>, unlike the three travel messages around
	/// it. A discovery is not always made by standing on the waypoint: <c>GrantWaypointAction</c>
	/// names a scene explicitly so a quest, a purchase or a dialogue can reveal a waypoint in
	/// ANOTHER zone — one that may not even be loaded on this server. Substituting the owner's
	/// current scene would file that grant under the wrong page and leave the client's record
	/// disagreeing with the persisted one.
	/// </remarks>
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
	/// <remarks>
	/// <b>The scene is not named.</b> The message answers an interaction with a waypoint object,
	/// which the character has to be standing next to; the controller substitutes
	/// <c>CurrentSceneName()</c>, which is what the map compares against anyway.
	/// </remarks>
	public struct WaypointOpenMapBroadcast : IBroadcast
	{
		public int WaypointIndex;
	}
}
