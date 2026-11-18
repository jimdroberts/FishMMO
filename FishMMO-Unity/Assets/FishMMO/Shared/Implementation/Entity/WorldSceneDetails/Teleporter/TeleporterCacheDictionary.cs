using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable dictionary mapping destination IDs to teleporter cache entries.
	/// Used by TeleporterCache to store all known teleporter destinations.
	/// </summary>
	[Serializable]
	public class TeleporterCacheDictionary : SerializableDictionary<string, TeleporterCacheEntry> { }
}