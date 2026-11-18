using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Utility.Performance;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
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
	/// <see cref="SpawnerDefinition.MaxSpawnCount"/> caps what any spawner can draw.
	/// </para>
	/// <para>
	/// <b>The pool outlives the scenes.</b> A pooled instance is kept out of every world scene
	/// (<see cref="PersistentPool.Keep"/>): FishNet leaves a despawned object wherever it was, and
	/// a spawned one is moved into its world scene, so every instance that had ever been used died
	/// with that scene when it unloaded, while the reservation still counted it. The next instance
	/// of a dungeon then saw no shortfall, skipped the prewarm and instantiated everything lazily
	/// in play. Now only what was alive at the unload goes with the scene, and
	/// <see cref="Forget"/> takes exactly that out of the count, so the next load makes it again
	/// up front. The rule itself lives in Shared, because the spawner-less despawns — a corpse
	/// with no spawner, ground loot — are Shared code and pool the same way.
	/// </para>
	/// </remarks>
	public static class SpawnerPool
	{
		/// <summary>Log category.</summary>
		private const string LOG = "SpawnerPool";

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
			List<NetworkObject> added = pool.StorePrefabObjects(prefab, shortfall, asServer: true);
			if (added != null)
			{
				/* Instantiated into whichever scene is active, which is not something the pool
				 * should depend on: kept out of the world scenes like every other pooled object. */
				for (int i = 0; i < added.Count; ++i)
				{
					PersistentPool.Keep(networkManager, added[i]);
				}
			}

			reserved[key] = count;
			TotalReserved += shortfall;

			return shortfall;
		}

		/// <summary>
		/// Takes instances that no longer exist out of a prefab's reservation.
		/// </summary>
		/// <remarks>
		/// Called for each object a spawner still had alive when its scene unloaded: those went
		/// with the scene. The next spawner to reserve the prefab then sees the shortfall and
		/// makes them again at its load, instead of the pool finding itself empty during play.
		/// Clamped at zero, since the pool also grows on demand past the reservation and an
		/// instance made that way can be among those lost.
		/// </remarks>
		/// <param name="prefab">The prefab the lost instances were made from.</param>
		/// <param name="count">How many were lost.</param>
		public static void Forget(NetworkObject prefab, int count)
		{
			if (prefab == null || count < 1)
			{
				return;
			}

			(ushort, int) key = (prefab.SpawnableCollectionId, prefab.PrefabId);
			if (!reserved.TryGetValue(key, out int already) || already < 1)
			{
				return;
			}

			int removed = Mathf.Min(already, count);
			reserved[key] = already - removed;
			TotalReserved = Mathf.Max(0, TotalReserved - removed);
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
