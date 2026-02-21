using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for guild member and character tracking.
	/// Provides read-only access to guild membership lookups.
	/// </summary>
	public interface IGuildCharacterMappingData : IRuntimeDataContainer
	{
		/// <summary>
		/// Tracks all guild members for guilds with at least one member logged into this server.
		/// Key: Guild ID, Value: Set of Character IDs.
		/// </summary>
		Dictionary<long, HashSet<long>> GuildMemberTracker { get; }

		/// <summary>
		/// Tracks currently online guild members on this scene server.
		/// Key: Guild ID, Value: Set of Character IDs.
		/// </summary>
		Dictionary<long, HashSet<long>> GuildCharacterTracker { get; }
	}
}