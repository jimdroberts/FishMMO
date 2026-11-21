using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Editor access to the server's <see cref="AIBrainCatalogue"/>: which archetype and boss script
	/// each NPC prefab spawns with.
	/// </summary>
	/// <remarks>
	/// NPC prefabs no longer carry any AI, so every tool that used to read or write the prefab's
	/// controller goes through here instead. The catalogue is found by type, so it can be moved;
	/// <see cref="DefaultPath"/> is only where a missing one is created.
	/// </remarks>
	public static class AIBrainCatalogueEditorUtility
	{
		/// <summary>
		/// Where a catalogue is created when the project has none.
		/// </summary>
		public const string DefaultPath = "Assets/Prefabs/Server/SceneServer/AIBrainCatalogue.asset";

		/// <summary>
		/// The project's brain catalogue.
		/// </summary>
		/// <param name="create">Create one at <see cref="DefaultPath"/> when none exists.</param>
		/// <returns>The catalogue, or null.</returns>
		public static AIBrainCatalogue Find(bool create = false)
		{
			string[] guids = AssetDatabase.FindAssets("t:" + nameof(AIBrainCatalogue));
			for (int i = 0; i < guids.Length; ++i)
			{
				AIBrainCatalogue found = AssetDatabase.LoadAssetAtPath<AIBrainCatalogue>(AssetDatabase.GUIDToAssetPath(guids[i]));
				if (found != null)
				{
					if (guids.Length > 1)
					{
						Debug.LogWarning($"[AIBrainCatalogue] {guids.Length} brain catalogues exist; using '{AssetDatabase.GetAssetPath(found)}'. The scene server uses the one its AISystem names.");
					}
					return found;
				}
			}

			if (!create)
			{
				return null;
			}

			AIBrainCatalogue catalogue = ScriptableObject.CreateInstance<AIBrainCatalogue>();
			AssetDatabase.CreateAsset(catalogue, DefaultPath);
			ServerAddressables.Register(DefaultPath);
			AssetDatabase.SaveAssets();
			return catalogue;
		}

		/// <summary>
		/// The catalogue entry for an NPC prefab, or null.
		/// </summary>
		public static AIBrainCatalogue.Entry Get(GameObject prefab)
		{
			NetworkObject networkObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
			AIBrainCatalogue catalogue = networkObject != null ? Find() : null;
			return catalogue != null ? catalogue.FindByReference(networkObject) : null;
		}

		/// <summary>
		/// The archetype an NPC prefab spawns with, or null.
		/// </summary>
		public static AIArchetypeTemplate GetArchetype(GameObject prefab)
		{
			return Get(prefab)?.Archetype;
		}

		/// <summary>
		/// The boss script an NPC prefab spawns with, or null.
		/// </summary>
		public static BossScript GetBossScript(GameObject prefab)
		{
			return Get(prefab)?.BossScript;
		}

		/// <summary>
		/// Sets the archetype an NPC prefab spawns with, creating its entry (and the catalogue) when
		/// needed.
		/// </summary>
		/// <param name="prefab">The NPC prefab asset.</param>
		/// <param name="archetype">The archetype.</param>
		public static void SetArchetype(GameObject prefab, AIArchetypeTemplate archetype)
		{
			AIBrainCatalogue.Entry entry = GetOrCreate(prefab, out AIBrainCatalogue catalogue);
			if (entry == null || entry.Archetype == archetype)
			{
				return;
			}
			Undo.RecordObject(catalogue, "Set NPC Archetype");
			entry.Archetype = archetype;
			Save(catalogue);
		}

		/// <summary>
		/// Sets the boss script an NPC prefab spawns with, creating its entry when needed.
		/// </summary>
		/// <param name="prefab">The NPC prefab asset.</param>
		/// <param name="bossScript">The boss script, or null for none.</param>
		public static void SetBossScript(GameObject prefab, BossScript bossScript)
		{
			AIBrainCatalogue.Entry entry = GetOrCreate(prefab, out AIBrainCatalogue catalogue);
			if (entry == null || entry.BossScript == bossScript)
			{
				return;
			}
			Undo.RecordObject(catalogue, "Set NPC Boss Script");
			entry.BossScript = bossScript;
			Save(catalogue);
		}

		/// <summary>
		/// Removes the entries whose prefab no longer exists.
		/// </summary>
		/// <returns>The number of entries removed.</returns>
		public static int PruneMissing()
		{
			AIBrainCatalogue catalogue = Find();
			if (catalogue == null)
			{
				return 0;
			}
			int removed = catalogue.Entries.RemoveAll(e => e == null || e.Prefab == null);
			if (removed > 0)
			{
				Save(catalogue);
			}
			return removed;
		}

		private static AIBrainCatalogue.Entry GetOrCreate(GameObject prefab, out AIBrainCatalogue catalogue)
		{
			catalogue = null;
			NetworkObject networkObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
			if (networkObject == null || prefab.GetComponent<NPC>() == null)
			{
				return null;
			}

			catalogue = Find(create: true);
			AIBrainCatalogue.Entry entry = catalogue.FindByReference(networkObject);
			if (entry == null)
			{
				Undo.RecordObject(catalogue, "Add NPC Brain");
				entry = new AIBrainCatalogue.Entry { Prefab = networkObject };
				catalogue.Entries.Add(entry);
				catalogue.Entries.Sort((a, b) => string.CompareOrdinal(a?.Prefab != null ? a.Prefab.name : string.Empty, b?.Prefab != null ? b.Prefab.name : string.Empty));
				Save(catalogue);
			}
			return entry;
		}

		private static void Save(AIBrainCatalogue catalogue)
		{
			catalogue.Invalidate();
			EditorUtility.SetDirty(catalogue);
			AssetDatabase.SaveAssetIfDirty(catalogue);
		}
	}
}
