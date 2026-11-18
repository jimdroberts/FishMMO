using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable dictionary mapping composite keys (SceneName/TeleporterName) to scene teleporter cache entries.
	/// Used by TeleporterCache to store all known scene teleporters.
	/// </summary>
	[Serializable]
	public class SceneTeleporterCacheDictionary : SerializableDictionary<string, SceneTeleporterCacheEntry> { }
}
