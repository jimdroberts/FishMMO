namespace FishMMO.Client
{
	/// <summary>
	/// How the local player stands towards the thing a map marker is attached to.
	/// </summary>
	/// <remarks>
	/// Ordered from most to least trusted, which is also the order the marker filter walks: the
	/// first rule that matches wins, so a guild member who is also in the party is drawn as a
	/// party member rather than twice.
	/// </remarks>
	public enum MapRelationship : byte
	{
		/// <summary>The local player's own character.</summary>
		Self = 0,
		/// <summary>A member of the local player's party.</summary>
		Party,
		/// <summary>A member of the local player's guild, not also in the party.</summary>
		Guild,
		/// <summary>A player character the faction matrix rates as an ally.</summary>
		FriendlyPlayer,
		/// <summary>A player character with no faction standing either way.</summary>
		NeutralPlayer,
		/// <summary>A player character the faction matrix rates as an enemy.</summary>
		HostilePlayer,
		/// <summary>Anything that is not a player character.</summary>
		NonPlayer,
	}
}
