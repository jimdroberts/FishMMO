using System;
using System.Collections.Concurrent;
using FishMMO.Server.Core.Collections;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for the naming system.
	/// Tracks in-flight request gates, per-connection debounce, and sweep cadence.
	/// </summary>
	public interface INamingSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// In-flight character-name-by-id requests keyed by character identifier.
		/// </summary>
		ConcurrentDictionary<long, byte> CharacterNameByIdInFlight { get; }

		/// <summary>
		/// In-flight guild-name-by-id requests keyed by guild identifier.
		/// </summary>
		ConcurrentDictionary<long, byte> GuildNameByIdInFlight { get; }

		/// <summary>
		/// In-flight character reverse-lookup requests keyed by lowercase character name.
		/// </summary>
		ConcurrentDictionary<string, byte> CharacterByNameInFlight { get; }

		/// <summary>
		/// Per-connection request timestamp tracker used for request debouncing.
		/// </summary>
		LastSeenCacheTracker<int, DateTime> ConnectionRequestTracker { get; }

		/// <summary>
		/// Next UTC timestamp when cache sweep is allowed.
		/// </summary>
		DateTime NextCacheSweepUtc { get; set; }
	}
}