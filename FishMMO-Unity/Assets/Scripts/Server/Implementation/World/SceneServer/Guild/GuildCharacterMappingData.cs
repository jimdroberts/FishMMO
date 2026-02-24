using System.Collections.Generic;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for guild member and character tracking.
	/// Manages guild membership lookups separately from GuildSystem logic.
	/// </summary>
	public class GuildCharacterMappingData : RuntimeDataContainer, IGuildCharacterMappingData
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
		/// Initializes the guild character mapping data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			GuildMemberTracker = new Dictionary<long, HashSet<long>>();
			GuildCharacterTracker = new Dictionary<long, HashSet<long>>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all guild character mapping data.
		/// </summary>
		public override void Clear()
		{
			GuildMemberTracker?.Clear();
			GuildCharacterTracker?.Clear();
		}

		/// <summary>
		/// Deinitializes the guild character mapping data container.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}