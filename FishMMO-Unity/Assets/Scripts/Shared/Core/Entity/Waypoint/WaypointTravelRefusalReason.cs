namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Why a fast-travel request was declined. Sent to the owner so the map can say so rather
	/// than appear to ignore the click.
	/// </summary>
	/// <remarks>
	/// Ordered from "the player cannot do anything right now" to "this particular waypoint is not
	/// available", which is also the order the server checks them in. See
	/// <c>WaypointTravel.Decide</c> for the truth table.
	/// </remarks>
	public enum WaypointTravelRefusalReason : byte
	{
		/// <summary>Not refused.</summary>
		None = 0,

		/// <summary>The character is dead, incapacitated, mid-transfer or not yet loaded.</summary>
		CannotAct = 1,

		/// <summary>The character is in combat. Fast travel is never a combat escape.</summary>
		InCombat = 2,

		/// <summary>The waypoint named by the request does not exist in the scene the character is in.</summary>
		UnknownWaypoint = 3,

		/// <summary>
		/// The request named a scene other than the one the character is standing in. Waypoints
		/// are usable only from inside their own scene until the world-map system lands.
		/// </summary>
		NotInScene = 4,

		/// <summary>The character has not discovered the waypoint.</summary>
		Locked = 5,

		/// <summary>The waypoint's designer-authored travel conditions were not met.</summary>
		ConditionsNotMet = 6,

		/// <summary>The character travelled too recently.</summary>
		TooSoon = 7,

		/// <summary>The peer asked to travel is not the authority. Only ever seen in a misconfigured process.</summary>
		NotAuthoritative = 8,
	}
}
