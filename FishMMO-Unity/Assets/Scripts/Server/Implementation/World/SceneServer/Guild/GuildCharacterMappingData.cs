using System.Collections.Generic;
using FishMMO.Shared;
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
		public Dictionary<long, Dictionary<long, GuildAddEntry>> GuildMemberTracker { get; private set; }

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
			GuildMemberTracker = new Dictionary<long, Dictionary<long, GuildAddEntry>>();
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