using System;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Which brain each NPC prefab runs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The server-side half of an NPC. The prefab is shared with clients and carries only the body,
	/// so the brain it is spawned with — its archetype, and a boss script when it is an encounter —
	/// is looked up here, by prefab, when the server spawns it. A spawner may still override the
	/// archetype for one placement.
	/// </para>
	/// <para>
	/// Lives in a server-only addressable group together with every archetype, state, personality
	/// and boss script it references, so none of that reaches a client build.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "AIBrainCatalogue", menuName = "FishMMO/Server/AI/Brain Catalogue", order = 0)]
	public class AIBrainCatalogue : ScriptableObject
	{
		/// <summary>
		/// One NPC prefab's brain.
		/// </summary>
		[Serializable]
		public class Entry
		{
			/// <summary>
			/// The NPC prefab this entry describes.
			/// </summary>
			[Tooltip("The NPC prefab this brain belongs to.")]
			public NetworkObject Prefab;

			/// <summary>
			/// The brain the prefab spawns with unless a spawner overrides it.
			/// </summary>
			[Tooltip("The brain this NPC spawns with unless a spawner overrides it.")]
			public AIArchetypeTemplate Archetype;

			/// <summary>
			/// Optional phased-encounter script. Kept per prefab, not per archetype, because it
			/// describes one encounter rather than a reusable brain.
			/// </summary>
			[Tooltip("Optional boss script for a phased encounter.")]
			public BossScript BossScript;
		}

		/// <summary>
		/// Every NPC prefab's brain. One entry per prefab.
		/// </summary>
		public List<Entry> Entries = new List<Entry>();

		/// <summary>
		/// Entries by prefab key, built on first lookup.
		/// </summary>
		[NonSerialized]
		private Dictionary<ulong, Entry> byPrefab;

		/// <summary>
		/// Entries whose prefab has no asset path hash yet, which a lookup can never reach.
		/// </summary>
		public int UnreachableEntryCount { get; private set; }

		/// <summary>
		/// Finds the entry for a spawned NPC, or for an NPC prefab asset.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Keyed by FishNet's <see cref="NetworkObject.AssetPathHash"/>, which a pooled instance
		/// carries from the prefab it was instantiated from. FishNet sets it at edit time, never
		/// changes it at runtime, and refuses a prefab collection in which two prefabs share one.
		/// </para>
		/// <para>
		/// Not the prefab id. The id is re-assigned when the spawnable collection initialises, and
		/// the value serialized on a prefab asset can be stale — two shipped NPC prefabs carried the
		/// same one — so a lookup built from it before the collection initialised would hand one
		/// NPC another's brain.
		/// </para>
		/// </remarks>
		/// <param name="networkObject">A spawned NPC or an NPC prefab.</param>
		/// <param name="entry">The entry, when there is one.</param>
		/// <returns>True when the prefab has an entry.</returns>
		public bool TryGetEntry(NetworkObject networkObject, out Entry entry)
		{
			entry = null;
			if (networkObject == null)
			{
				return false;
			}

			if (byPrefab == null)
			{
				Rebuild();
			}
			return byPrefab.TryGetValue(KeyOf(networkObject), out entry);
		}

		/// <summary>
		/// Finds the entry authored for a prefab asset by reference, without relying on its hash.
		/// </summary>
		/// <remarks>
		/// For editor tooling, where a prefab that is not yet in FishNet's spawnable collection has
		/// no asset path hash, or still carries the one of the prefab it was copied from.
		/// </remarks>
		/// <param name="prefab">The prefab asset.</param>
		/// <returns>The entry, or null.</returns>
		public Entry FindByReference(NetworkObject prefab)
		{
			if (prefab == null)
			{
				return null;
			}
			for (int i = 0; i < Entries.Count; ++i)
			{
				Entry candidate = Entries[i];
				if (candidate != null && candidate.Prefab == prefab)
				{
					return candidate;
				}
			}
			return null;
		}

		/// <summary>
		/// Drops the lookup so the next query rebuilds it from <see cref="Entries"/>.
		/// </summary>
		public void Invalidate()
		{
			byPrefab = null;
		}

		/// <summary>
		/// Builds the lookup. A later entry for the same prefab loses to the first, and says so.
		/// </summary>
		private void Rebuild()
		{
			byPrefab = new Dictionary<ulong, Entry>(Entries.Count);
			UnreachableEntryCount = 0;
			for (int i = 0; i < Entries.Count; ++i)
			{
				Entry candidate = Entries[i];
				if (candidate == null || candidate.Prefab == null)
				{
					continue;
				}

				ulong key = KeyOf(candidate.Prefab);
				if (key == 0)
				{
					// Not in a spawnable collection yet; FishNet assigns the hash when it is added.
					UnreachableEntryCount++;
					FishMMO.Logging.Log.Warning("AIBrainCatalogue", $"{candidate.Prefab.name} has no asset path hash; it is not in a spawnable prefab collection, so it cannot be spawned or given a brain.");
					continue;
				}
				if (byPrefab.ContainsKey(key))
				{
					FishMMO.Logging.Log.Warning("AIBrainCatalogue", $"{name} lists {candidate.Prefab.name} more than once; the first entry wins.");
					continue;
				}
				byPrefab.Add(key, candidate);
			}
		}

		/// <summary>
		/// The prefab identity of a network object as one number.
		/// </summary>
		private static ulong KeyOf(NetworkObject networkObject)
		{
			return networkObject.AssetPathHash;
		}

		private void OnValidate()
		{
			Invalidate();
		}
	}
}
