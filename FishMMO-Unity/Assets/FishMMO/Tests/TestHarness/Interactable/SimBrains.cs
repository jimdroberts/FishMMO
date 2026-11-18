using System;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishNet.Managing;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// The server-side NPC brains for the simulation scenes.
	/// </summary>
	/// <remarks>
	/// The shared <see cref="SimServer"/> boots a bare FishNet server with none of the scene
	/// server's systems, so a sim that fights or talks to NPCs runs its own
	/// <see cref="AIBrainHost"/> beside it — the same host the production <c>AISystem</c> wraps —
	/// and prepares each NPC with a per-instance clone of its catalogue archetype, never the
	/// shipped asset.
	/// </remarks>
	public static class SimBrains
	{
		/// <summary>
		/// Starts a brain host for a sim's server, using the project's brain catalogue.
		/// </summary>
		/// <param name="networkManager">The sim's network manager.</param>
		/// <returns>The running host.</returns>
		public static AIBrainHost StartHost(NetworkManager networkManager)
		{
			AIBrainHost host = new AIBrainHost(networkManager, FindCatalogue());
			host.Start();
			return host;
		}

		/// <summary>
		/// The project's brain catalogue. Editor-only lookup; a sim scene only runs in the editor.
		/// </summary>
		public static AIBrainCatalogue FindCatalogue()
		{
#if UNITY_EDITOR
			string[] guids = UnityEditor.AssetDatabase.FindAssets("t:" + nameof(AIBrainCatalogue));
			if (guids.Length > 0)
			{
				return UnityEditor.AssetDatabase.LoadAssetAtPath<AIBrainCatalogue>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
			}
#endif
			Debug.LogError("[SimBrains] No AI brain catalogue found; sim NPCs will have no archetype.");
			return null;
		}

		/// <summary>
		/// A tunable copy of the archetype <paramref name="prefab"/> spawns with.
		/// </summary>
		/// <remarks>
		/// LOD is switched off (null settings = always Active): with zero observers the tier
		/// evaluator would otherwise park every brain Dormant and nothing would ever happen.
		/// </remarks>
		/// <param name="host">The sim's brain host.</param>
		/// <param name="prefab">The NPC prefab.</param>
		/// <param name="tune">Further edits to the copy; may be null.</param>
		/// <returns>The copy, or null when the prefab has no catalogue entry.</returns>
		public static AIArchetypeTemplate CloneArchetype(AIBrainHost host, GameObject prefab, Action<AIArchetypeTemplate> tune = null)
		{
			FishNet.Object.NetworkObject networkObject = prefab != null ? prefab.GetComponent<FishNet.Object.NetworkObject>() : null;
			if (host?.Catalogue == null || networkObject == null ||
				!host.Catalogue.TryGetEntry(networkObject, out AIBrainCatalogue.Entry entry) ||
				entry.Archetype == null)
			{
				return null;
			}

			AIArchetypeTemplate copy = UnityEngine.Object.Instantiate(entry.Archetype);
			copy.name = entry.Archetype.name + " (sim)";
			copy.LodSettings = null;
			tune?.Invoke(copy);
			return copy;
		}
	}
}
