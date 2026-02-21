using FishMMO.Server.Core.Collections;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Mapping/cache state for the naming system.
	/// Stores bounded TTL caches for direct and reverse lookups.
	/// </summary>
	public interface INamingSystemMappingData : IRuntimeDataContainer
	{
		/// <summary>
		/// Cache of character names keyed by character identifier.
		/// </summary>
		LastSeenCacheTracker<long, string> CharacterNameByIdCache { get; }

		/// <summary>
		/// Cache of guild names keyed by guild identifier.
		/// </summary>
		LastSeenCacheTracker<long, string> GuildNameByIdCache { get; }

		/// <summary>
		/// Reverse cache of character identifiers keyed by lowercase character name.
		/// </summary>
		LastSeenCacheTracker<string, long> CharacterIdByNameCache { get; }

		/// <summary>
		/// Reverse cache of canonical character names keyed by lowercase character name.
		/// </summary>
		LastSeenCacheTracker<string, string> CharacterNameByNameCache { get; }

		/// <summary>
		/// Negative reverse-lookup cache keyed by lowercase character name.
		/// </summary>
		LastSeenCacheTracker<string, byte> CharacterMissingByNameCache { get; }
	}
}