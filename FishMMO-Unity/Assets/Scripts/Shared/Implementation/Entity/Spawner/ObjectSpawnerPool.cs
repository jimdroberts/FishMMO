using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Utility.Performance;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Pre-allocates the network objects a scene will need, so a map's memory footprint is fixed
	/// at load rather than discovered under load.
	/// </summary>
	/// <remarks>
	/// <para>
	/// FishNet's <see cref="DefaultObjectPool"/> already recycles despawned objects, so nothing was
	/// leaking — but it fills lazily. The first time a spawner needs a prefab it instantiates one,
	/// which means a freshly loaded map pays for every NPC it will ever show as players walk into
	/// each spawner's range: hitching during play, and a heap that only reaches its true size once
	/// the whole map has been visited. Neither is acceptable to plan capacity against.
	/// </para>
	/// <para>
	/// Reserving up front converts that into a known, one-time load cost. It also removes the
	/// pathological case where a spike in concurrent spawns instantiates a batch that then sits in
	/// the pool forever: the reservation <em>is</em> the budget, and
	/// <see cref="ObjectSpawner.MaxSpawnCount"/> caps what any spawner can draw.
	/// </para>
	/// </remarks>
	public static class ObjectSpawnerPool
	{
		/// <summary>Log category.</summary>
		private const string LOG = "ObjectSpawnerPool";

		/// <summary>
		/// Reservations already satisfied, keyed by prefab identity, so several spawners sharing a
		/// prefab reserve the union of their needs rather than the sum.
		/// </summary>
		/// <remarks>
		/// Keyed on (collectionId, prefabId) because a prefab id is only unique within its
		/// spawnable collection.
		/// </remarks>
		private static readonly Dictionary<(ushort CollectionId, int PrefabId), int> reserved =
			new Dictionary<(ushort, int), int>();

		/// <summary>
		/// Total objects reserved across every prefab, for diagnostics.
		/// </summary>
		public static int TotalReserved { get; private set; }

		/// <summary>
		/// Forgets all reservations. Call when tearing a scene server down so a subsequent load
		/// re-reserves against a fresh pool.
		/// </summary>
		public static void Clear()
		{
			reserved.Clear();
			TotalReserved = 0;
		}

		/// <summary>
		/// Ensures the pool holds at least <paramref name="count"/> instances of a prefab.
		/// </summary>
		/// <remarks>
		/// Idempotent per prefab: reserving 5 and then 8 instantiates 5 and then 3, never 13. That
		/// matters because several spawners commonly share one prefab, and summing their maxima
		/// would multiply the reservation for no benefit — a prefab's peak concurrent count is
		/// bounded by the largest single demand plus whatever else is live, and the pool grows on
		/// demand beyond the reservation anyway.
		/// </remarks>
		/// <param name="networkManager">The network manager owning the pool.</param>
		/// <param name="prefab">The prefab to reserve instances of.</param>
		/// <param name="count">How many instances should exist.</param>
		/// <returns>The number of instances newly created.</returns>
		public static int Reserve(NetworkManager networkManager, NetworkObject prefab, int count)
		{
			if (networkManager == null || prefab == null || count < 1)
			{
				return 0;
			}

			DefaultObjectPool pool = networkManager.ObjectPool as DefaultObjectPool;
			if (pool == null)
			{
				// A custom pool implementation may have its own strategy; do not fight it.
				return 0;
			}

			(ushort, int) key = (prefab.SpawnableCollectionId, prefab.PrefabId);
			reserved.TryGetValue(key, out int already);

			int shortfall = count - already;
			if (shortfall < 1)
			{
				return 0;
			}

			// StorePrefabObjects rather than the obsolete CacheObjects wrapper.
			pool.StorePrefabObjects(prefab, shortfall, asServer: true);

			reserved[key] = count;
			TotalReserved += shortfall;

			return shortfall;
		}

		/// <summary>
		/// Logs the reservation total. Called once after a scene's spawners have initialised.
		/// </summary>
		/// <param name="context">Scene or spawner description for the log line.</param>
		public static void LogReservation(string context)
		{
			Log.Debug(LOG, $"{context}: reserved {TotalReserved} pooled network object(s) across {reserved.Count} prefab(s).");
		}
	}
}
