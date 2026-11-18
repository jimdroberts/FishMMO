using FishNet.Managing;
using FishNet.Object;
using FishNet.Utility.Performance;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Returns network objects to FishNet's pool somewhere a scene unload cannot destroy them.
	/// </summary>
	/// <remarks>
	/// <para>FishNet leaves a despawned object wherever it was, and a spawn moves a pooled one into
	/// its target scene, so an instance that had ever been used sat in the pool inside its world
	/// scene and died with that scene when it unloaded — while the pool, and the spawner's
	/// reservation, still counted it. The spawner's own despawns were moved out of the world scenes
	/// first (hot-path audit finding L15); a corpse with no spawner, ground loot and a container
	/// placed by script still went back to the pool in place, and every one of them was lost at the
	/// unload.</para>
	///
	/// <para>Every path that returns a once-spawned object to FishNet's pool goes through
	/// <see cref="Despawn"/>, and every path that puts one back some other way (a spawn rolled back
	/// before it happened, a prewarm) calls <see cref="Keep"/>, so they share one rule for where a
	/// pooled object lives: out of every world scene, in <c>DontDestroyOnLoad</c>, inactive until a
	/// spawn moves it into its scene again. The callers:</para>
	/// <list type="bullet">
	/// <item><see cref="Despawn"/>: the spawner (<c>SpawnerRuntime.Despawn</c>); the spawner-less
	/// fallbacks <c>NPC.ReturnToPool</c> and <c>Interactable.Despawn</c>; pets (<c>Pet.Despawn</c>,
	/// <c>PetSystem.DespawnPet</c>); a boss's adds (<c>BossScriptState.DismissAdds</c>); and
	/// characters (<c>CharacterSystem.SaveAndDespawnCharacter</c>,
	/// <c>CharacterSystem.EvictLostCharacter</c>, and <c>CharacterSystem.DespawnLingeringBody</c>
	/// for a combat-logout body).</item>
	/// <item><see cref="Keep"/>: the spawner's prewarm (<c>SpawnerPool.Reserve</c>) and its rolled-back
	/// spawn (<c>SpawnerRuntime.RollBackUnfinishedSpawn</c>); a boss add rolled back
	/// (<c>BossScriptState.RollBackAdd</c>); a pooled pet object with no <c>Pet</c>
	/// (<c>PetSystem.SpawnAndInitializePet</c>); and the two character paths above
	/// (<c>EvictLostCharacter</c>, <c>DespawnLingeringBody</c>) when the object was no longer
	/// spawned.</item>
	/// </list>
	/// </remarks>
	public static class PersistentPool
	{
		/// <summary>
		/// Despawns a spawned object into FishNet's pool and keeps it out of the world scenes.
		/// Server only.
		/// </summary>
		/// <param name="networkManager">The network manager owning the pool.</param>
		/// <param name="instance">The spawned object.</param>
		/// <returns>True when the object was spawned and has been despawned.</returns>
		public static bool Despawn(NetworkManager networkManager, NetworkObject instance)
		{
			if (networkManager == null || instance == null || !instance.IsSpawned)
			{
				return false;
			}

			networkManager.ServerManager.Despawn(instance, DespawnType.Pool);
			Keep(networkManager, instance);
			return true;
		}

		/// <summary>
		/// Keeps a pooled instance out of the world scenes, so no scene's unload can destroy it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Call for an instance that has just gone back into FishNet's pool. It is inactive there,
		/// and a spawn moves it into its target scene again, exactly as it does an instance a
		/// prewarm made.
		/// </para>
		/// <para>
		/// Only for FishNet's own pool, which does not place what it stores; a custom pool keeps its
		/// objects where it chooses. Only in play mode, and only for a root object, which is all
		/// <see cref="Object.DontDestroyOnLoad"/> accepts. Never for a scene object: FishNet does
		/// not pool one at all — it disables it in place, to be re-enabled by its own scene — and
		/// carried out of that scene it would outlive the unload and meet its own copy when the
		/// scene loads again.
		/// </para>
		/// </remarks>
		/// <param name="networkManager">The network manager owning the pool.</param>
		/// <param name="instance">The pooled instance.</param>
		public static void Keep(NetworkManager networkManager, NetworkObject instance)
		{
			if (networkManager == null ||
				instance == null ||
				instance.IsSpawned ||
				instance.IsSceneObject ||
				!Application.isPlaying ||
				!(networkManager.ObjectPool is DefaultObjectPool))
			{
				return;
			}

			GameObject gameObject = instance.gameObject;
			if (gameObject.transform.parent != null)
			{
				return;
			}
			Object.DontDestroyOnLoad(gameObject);
		}
	}
}
