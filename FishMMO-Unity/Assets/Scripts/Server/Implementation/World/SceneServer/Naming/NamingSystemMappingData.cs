using System;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Mapping data container for naming caches.
	/// Provides TTL-friendly O(1) touch caches for direct and reverse lookups.
	/// </summary>
	public class NamingSystemMappingData : RuntimeDataContainer, INamingSystemMappingData
	{
		/// <inheritdoc/>
		public LastSeenCacheTracker<long, string> CharacterNameByIdCache { get; private set; }

		/// <inheritdoc/>
		public LastSeenCacheTracker<long, string> GuildNameByIdCache { get; private set; }

		/// <inheritdoc/>
		public LastSeenCacheTracker<string, long> CharacterIdByNameCache { get; private set; }

		/// <inheritdoc/>
		public LastSeenCacheTracker<string, string> CharacterNameByNameCache { get; private set; }

		/// <inheritdoc/>
		public LastSeenCacheTracker<string, byte> CharacterMissingByNameCache { get; private set; }

		/// <inheritdoc/>
		public void SweepAllCaches(DateTime nowUtc, TimeSpan ttl, int maxScan, int maxRemove)
		{
			CharacterNameByIdCache?.SweepExpired(nowUtc, ttl, maxScan, maxRemove);
			GuildNameByIdCache?.SweepExpired(nowUtc, ttl, maxScan, maxRemove);
			CharacterIdByNameCache?.SweepExpired(nowUtc, ttl, maxScan, maxRemove);
			CharacterNameByNameCache?.SweepExpired(nowUtc, ttl, maxScan, maxRemove);
			CharacterMissingByNameCache?.SweepExpired(nowUtc, ttl, maxScan, maxRemove);
		}

		/// <summary>
		/// Initializes all naming caches.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			CharacterNameByIdCache = new LastSeenCacheTracker<long, string>();
			GuildNameByIdCache = new LastSeenCacheTracker<long, string>();
			CharacterIdByNameCache = new LastSeenCacheTracker<string, long>(System.StringComparer.Ordinal);
			CharacterNameByNameCache = new LastSeenCacheTracker<string, string>(System.StringComparer.Ordinal);
			CharacterMissingByNameCache = new LastSeenCacheTracker<string, byte>(System.StringComparer.Ordinal);
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all naming caches.
		/// </summary>
		public override void Clear()
		{
			CharacterNameByIdCache?.Clear();
			GuildNameByIdCache?.Clear();
			CharacterIdByNameCache?.Clear();
			CharacterNameByNameCache?.Clear();
			CharacterMissingByNameCache?.Clear();
		}

		/// <summary>
		/// Deinitializes naming caches and releases references.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
			CharacterNameByIdCache = null;
			GuildNameByIdCache = null;
			CharacterIdByNameCache = null;
			CharacterNameByNameCache = null;
			CharacterMissingByNameCache = null;
		}
	}
}