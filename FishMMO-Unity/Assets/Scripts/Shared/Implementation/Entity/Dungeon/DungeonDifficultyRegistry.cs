using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Which difficulty ruleset applies inside each loaded dungeon scene, on this process.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The bridge between a scene row and the things spawning inside the scene it produced. An NPC
	/// waking up in a dungeon knows the Unity scene it is in and nothing else — not which instance
	/// row asked for it, not which party owns it, and certainly not what difficulty they chose. It
	/// cannot ask the server systems either: those live in the server assembly and this code is
	/// shared with the client.
	/// </para>
	/// <para>
	/// So the scene server, which does know all of that, publishes the answer here as it finishes
	/// loading a scene and withdraws it as the scene unloads. Everything downstream is a dictionary
	/// lookup on a scene handle, which is cheap enough for a spawn path and for a loot roll.
	/// </para>
	/// <para>
	/// <b>Scene handles are process-local, and that is exactly right here.</b> Nothing in this
	/// registry is ever persisted or sent anywhere: it describes scenes loaded in this process, for
	/// the lifetime of those scenes. The cross-process identity of an instance remains its scene
	/// row ID, and this is deliberately not that.
	/// </para>
	/// <para>
	/// Not thread-safe, and not intended to be. Registration happens on the main thread as a scene
	/// finishes loading, and every reader — spawning, loot, death rules — is on the main thread too.
	/// </para>
	/// </remarks>
	public static class DungeonDifficultyRegistry
	{
		/// <summary>Difficulty rules by scene handle, for scenes loaded in this process.</summary>
		private static readonly Dictionary<int, DungeonDifficultyDefinition> difficultyBySceneHandle =
			new Dictionary<int, DungeonDifficultyDefinition>();

		/// <summary>
		/// Publishes the rules that apply inside one loaded scene.
		/// </summary>
		/// <param name="sceneHandle">Scene manager handle of the loaded scene.</param>
		/// <param name="difficulty">The ruleset, or null to publish nothing.</param>
		public static void Register(int sceneHandle, DungeonDifficultyDefinition difficulty)
		{
			if (sceneHandle == 0)
			{
				return;
			}

			if (difficulty == null)
			{
				/* Registering null removes rather than storing a null.
				 *
				 * A scene handle is reused by the engine once its scene has gone, so a stale entry
				 * is not merely useless — it is somebody else's rules, applied to a scene that
				 * never asked for them. Removing on null means the "this dungeon has no special
				 * rules" case actively clears anything left behind at that handle. */
				difficultyBySceneHandle.Remove(sceneHandle);
				return;
			}

			difficultyBySceneHandle[sceneHandle] = difficulty;
		}

		/// <summary>
		/// Withdraws the rules for a scene that is unloading.
		/// </summary>
		/// <remarks>
		/// Must be called for every registered scene. Scene handles are drawn from a per-process
		/// counter and are reused, so an entry that outlived its scene would eventually be read as
		/// the rules for an unrelated one.
		/// </remarks>
		/// <param name="sceneHandle">Scene manager handle of the scene being unloaded.</param>
		public static void Unregister(int sceneHandle)
		{
			difficultyBySceneHandle.Remove(sceneHandle);
		}

		/// <summary>
		/// The rules that apply inside one scene, if any were published for it.
		/// </summary>
		/// <param name="sceneHandle">Scene manager handle to look up.</param>
		/// <param name="difficulty">Receives the ruleset.</param>
		/// <returns>True when the scene has a difficulty; false for the open world and for
		/// dungeons that declare none.</returns>
		public static bool TryGet(int sceneHandle, out DungeonDifficultyDefinition difficulty)
		{
			return difficultyBySceneHandle.TryGetValue(sceneHandle, out difficulty);
		}

		/// <summary>
		/// The loot quantity and currency multipliers for one scene, defaulting to no change.
		/// </summary>
		/// <remarks>
		/// A convenience for the loot paths, which want two floats and do not care whether the
		/// scene had a difficulty at all — the open world simply gets 1 and 1.
		/// </remarks>
		/// <param name="sceneHandle">Scene manager handle to look up.</param>
		/// <param name="lootQuantityMultiplier">Receives the item quantity multiplier.</param>
		/// <param name="currencyMultiplier">Receives the currency multiplier.</param>
		public static void GetLootMultipliers(int sceneHandle, out float lootQuantityMultiplier, out float currencyMultiplier)
		{
			if (TryGet(sceneHandle, out DungeonDifficultyDefinition difficulty) && difficulty != null)
			{
				lootQuantityMultiplier = difficulty.LootQuantityMultiplier;
				currencyMultiplier = difficulty.CurrencyMultiplier;
				return;
			}

			lootQuantityMultiplier = 1.0f;
			currencyMultiplier = 1.0f;
		}

		/// <summary>
		/// Drops every published ruleset. For server shutdown and domain reload.
		/// </summary>
		/// <remarks>
		/// Static state survives entering and leaving play mode in the editor when domain reload is
		/// disabled, and a registry carried across would describe scenes from the previous session.
		/// </remarks>
		public static void Clear()
		{
			difficultyBySceneHandle.Clear();
		}
	}
}
