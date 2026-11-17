using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Which guild members are logged into this scene server (guild chat reaches them). The Social module's guild
	/// system keeps it; see <c>IGuildRosterMappingData</c> for the roster cache it keeps beside it.
	/// </summary>
	public interface IGuildCharacterMappingData : IRuntimeDataContainer
	{
		/// <summary>
		/// Tracks currently online guild members on this scene server.
		/// Key: Guild ID, Value: Set of Character IDs.
		/// </summary>
		Dictionary<long, HashSet<long>> GuildCharacterTracker { get; }
	}
}