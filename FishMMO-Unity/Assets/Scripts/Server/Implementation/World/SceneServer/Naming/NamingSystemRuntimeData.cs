using System;
using System.Collections.Concurrent;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for naming request in-flight gates and debounce tracking.
	/// </summary>
	public class NamingSystemRuntimeData : RuntimeDataContainer, INamingSystemRuntimeData
	{
		/// <inheritdoc/>
		public ConcurrentDictionary<long, byte> CharacterNameByIdInFlight { get; private set; }

		/// <inheritdoc/>
		public ConcurrentDictionary<long, byte> GuildNameByIdInFlight { get; private set; }

		/// <inheritdoc/>
		public ConcurrentDictionary<string, byte> CharacterByNameInFlight { get; private set; }

		/// <inheritdoc/>
		public LastSeenCacheTracker<int, DateTime> ConnectionRequestTracker { get; private set; }

		/// <inheritdoc/>
		public DateTime NextCacheSweepUtc { get; set; }

		/// <summary>
		/// Initializes all naming runtime trackers.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			CharacterNameByIdInFlight = new ConcurrentDictionary<long, byte>();
			GuildNameByIdInFlight = new ConcurrentDictionary<long, byte>();
			CharacterByNameInFlight = new ConcurrentDictionary<string, byte>();
			ConnectionRequestTracker = new LastSeenCacheTracker<int, DateTime>();
			NextCacheSweepUtc = DateTime.UtcNow;
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
			ConnectionRequestTracker?.Clear();
			NextCacheSweepUtc = DateTime.UtcNow;
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
			ConnectionRequestTracker = null;
		}
	}
}