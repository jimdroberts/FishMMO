using System.Collections.Generic;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// The guild system's own side of its mapping data: the roster it last delivered to each guild, beside the online
	/// members the core reads (<see cref="IGuildCharacterMappingData"/>).
	/// </summary>
	public interface IGuildRosterMappingData : IGuildCharacterMappingData
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
	}
}
