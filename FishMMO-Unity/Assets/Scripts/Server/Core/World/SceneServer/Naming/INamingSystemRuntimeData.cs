using System;
using FishNet.Connection;
using FishMMO.Server.Core.Collections;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for the naming system.
	/// Tracks lookups in flight with their waiting requesters, per-connection request budgets,
	/// and sweep cadence.
	/// </summary>
	public interface INamingSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// In-flight character-name-by-id lookups keyed by character identifier, each with every
		/// connection waiting on it and the form it asked in. Main thread only.
		/// </summary>
		InFlightLookupTable<long, NamingWaiter<NetworkConnection>> CharacterNameByIdInFlight { get; }

		/// <summary>
		/// In-flight guild-name-by-id lookups keyed by guild identifier, each with every connection
		/// waiting on it and the form it asked in. Main thread only.
		/// </summary>
		InFlightLookupTable<long, NamingWaiter<NetworkConnection>> GuildNameByIdInFlight { get; }

		/// <summary>
		/// In-flight character reverse lookups keyed by lowercase character name, each with every
		/// connection waiting on it. Main thread only.
		/// </summary>
		InFlightLookupTable<string, NetworkConnection> CharacterByNameInFlight { get; }

		/// <summary>
		/// Per-connection request budgets, keyed by client ID. An entry idle past the cache TTL is
		/// swept; by then its bucket would have refilled anyway.
		/// </summary>
		LastSeenCacheTracker<int, NamingRequestBucket> ConnectionRequestBuckets { get; }

		/// <summary>
		/// When the next cache sweep is allowed, in <see cref="MonotonicClock"/> seconds.
		/// </summary>
		double NextCacheSweepAt { get; set; }
	}
}
