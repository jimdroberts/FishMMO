using System;
using System.Collections.Generic;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for guild membership tracking and invitation state.
	/// Manages all guild runtime state separately from GuildSystem logic.
	/// </summary>
	public class GuildRuntimeData : RuntimeDataContainer, IGuildRuntimeData
	{
		/// <summary>
		/// Tracks all guild members for guilds with at least one member logged into this server.
		/// Key: Guild ID, Value: Set of Character IDs.
		/// </summary>
		public Dictionary<long, HashSet<long>> GuildMemberTracker { get; private set; }

		/// <summary>
		/// Tracks currently online guild members on this scene server.
		/// Key: Guild ID, Value: Set of Character IDs.
		/// </summary>
		public Dictionary<long, HashSet<long>> GuildCharacterTracker { get; private set; }

		/// <summary>
		/// Tracks pending guild invitations.
		/// Key: FromCharacterID, Value: ToCharacterID.
		/// </summary>
		public Dictionary<long, long> PendingInvitations { get; private set; }

		/// <summary>
		/// Timestamp of the last successful database fetch for guild updates.
		/// </summary>
		public DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Initializes the guild runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			GuildMemberTracker = new Dictionary<long, HashSet<long>>();
			GuildCharacterTracker = new Dictionary<long, HashSet<long>>();
			PendingInvitations = new Dictionary<long, long>();
			LastFetchTime = DateTime.UtcNow;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all guild tracking data.
		/// </summary>
		public override void Clear()
		{
			GuildMemberTracker?.Clear();
			GuildCharacterTracker?.Clear();
			PendingInvitations?.Clear();
			LastFetchTime = DateTime.UtcNow;
		}

		/// <summary>
		/// Deinitializes the guild runtime data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}