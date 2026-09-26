using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for guild member and character tracking.
	/// Provides read-only access to guild membership lookups.
	/// </summary>
	public interface IGuildCharacterMappingData : IRuntimeDataContainer
	{
		/// <summary>
		/// The roster this server last delivered, for each guild with at least one member logged
		/// into it. Key: Guild ID, Value: each member's row by character ID, in the FULL projection
		/// (officer notes included).
		/// </summary>
		/// <remarks>
		/// The baseline the guild pump diffs a freshly read roster against: departed members are
		/// the keys missing from the new read, and a <c>GuildRosterDeltaBroadcast</c> carries the
		/// rows that differ. One collection for both, so the set of members and the rows they were
		/// sent cannot drift apart.
		/// </remarks>
		Dictionary<long, Dictionary<long, GuildAddEntry>> GuildMemberTracker { get; }

		/// <summary>
		/// Tracks currently online guild members on this scene server.
		/// Key: Guild ID, Value: Set of Character IDs.
		/// </summary>
		Dictionary<long, HashSet<long>> GuildCharacterTracker { get; }
	}
}