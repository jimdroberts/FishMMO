using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for guild membership tracking and invitation state.
	/// Provides read-only access to guild tracking collections.
	/// </summary>
	public interface IGuildRuntimeData : IRuntimeDataContainer
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

		/// <summary>
		/// Tracks pending guild invitations.
		/// Key: FromCharacterID, Value: ToCharacterID.
		/// </summary>
		Dictionary<long, long> PendingInvitations { get; }

		/// <summary>
		/// Timestamp of the last successful database fetch for guild updates.
		/// </summary>
		DateTime LastFetchTime { get; set; }
	}
}