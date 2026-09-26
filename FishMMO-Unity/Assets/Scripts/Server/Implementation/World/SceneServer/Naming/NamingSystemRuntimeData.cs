using System;
using FishNet.Connection;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Core.Collections;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for naming lookups in flight and per-connection request budgets.
	/// </summary>
	public class NamingSystemRuntimeData : RuntimeDataContainer, INamingSystemRuntimeData
	{
		/// <summary>
		/// Most keys of one kind in flight at once. A request beyond it is not answered, as a
		/// request beyond its connection's budget is not; the client asks again.
		/// </summary>
		public const int MaxInFlightLookups = 5000;

		/// <summary>
		/// Most connections waiting on one key. Far above any honest overlap: it bounds only what
		/// one key can be made to cost.
		/// </summary>
		public const int MaxWaitersPerLookup = 256;

		/// <inheritdoc/>
		public InFlightLookupTable<long, NamingWaiter<NetworkConnection>> CharacterNameByIdInFlight { get; private set; }

		/// <inheritdoc/>
		public InFlightLookupTable<long, NamingWaiter<NetworkConnection>> GuildNameByIdInFlight { get; private set; }

		/// <inheritdoc/>
		public InFlightLookupTable<string, NetworkConnection> CharacterByNameInFlight { get; private set; }

		/// <inheritdoc/>
		public LastSeenCacheTracker<int, NamingRequestBucket> ConnectionRequestBuckets { get; private set; }

		/// <inheritdoc/>
		public double NextCacheSweepAt { get; set; }

		/// <summary>
		/// Initializes all naming runtime trackers.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			CharacterNameByIdInFlight = new InFlightLookupTable<long, NamingWaiter<NetworkConnection>>(MaxInFlightLookups, MaxWaitersPerLookup);
			GuildNameByIdInFlight = new InFlightLookupTable<long, NamingWaiter<NetworkConnection>>(MaxInFlightLookups, MaxWaitersPerLookup);
			CharacterByNameInFlight = new InFlightLookupTable<string, NetworkConnection>(MaxInFlightLookups, MaxWaitersPerLookup, StringComparer.Ordinal);
			ConnectionRequestBuckets = new LastSeenCacheTracker<int, NamingRequestBucket>();
			NextCacheSweepAt = MonotonicClock.NowSeconds;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all runtime trackers.
		/// </summary>
		public override void Clear()
		{
			CharacterNameByIdInFlight?.Clear();
			GuildNameByIdInFlight?.Clear();
			CharacterByNameInFlight?.Clear();
			ConnectionRequestBuckets?.Clear();
			NextCacheSweepAt = MonotonicClock.NowSeconds;
		}

		/// <summary>
		/// Deinitializes runtime trackers and releases references.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
			CharacterNameByIdInFlight = null;
			GuildNameByIdInFlight = null;
			CharacterByNameInFlight = null;
			ConnectionRequestBuckets = null;
		}
	}
}
