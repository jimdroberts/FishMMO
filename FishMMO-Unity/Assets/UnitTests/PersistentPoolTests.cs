using System;
using System.IO;
using FishMMO.Shared;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins for where a pooled network object lives: out of every world scene, whether a spawner
	/// despawned it or nothing did.
	/// </summary>
	/// <remarks>
	/// FishNet leaves a despawned object in the scene it was in, so a pooled instance left there
	/// died with the scene at its unload while the pool still counted it. The spawner's despawns
	/// were moved out first; a corpse with no spawner and ground loot still pooled in place. These
	/// hold every path to the one rule, <see cref="PersistentPool"/>.
	/// </remarks>
	[TestFixture]
	public class PersistentPoolTests
	{
		private const string Scripts = "Assets/Scripts";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		[Test]
		public void NothingToDespawn_IsRefusedQuietly()
		{
			Assert.IsFalse(PersistentPool.Despawn(null, null));
			Assert.DoesNotThrow(() => PersistentPool.Keep(null, null));
		}

		[Test]
		public void ACorpseWithNoSpawner_PoolsOutOfTheWorldScene()
		{
			string body = MethodBody(ReadSource($"{Scripts}/Shared/Implementation/Entity/NPC/NPC.cs"),
				"public void ReturnToPool()", "private void NotifyCorpseExpired()");

			LogAssert.IsTrue(body.Contains("PersistentPool.Despawn(NetworkManager, NetworkObject);"),
				"NPC.ReturnToPool's fallback must pool through PersistentPool");
			LogAssert.IsFalse(body.Contains("ServerManager.Despawn("),
				"a direct pooled despawn leaves the corpse in its world scene, to die at the unload");
		}

		[Test]
		public void GroundLootWithNoSpawner_PoolsOutOfTheWorldScene()
		{
			string body = MethodBody(ReadSource($"{Scripts}/Shared/Implementation/Entity/Interactable/Interactable.cs"),
				"public void Despawn()", "public override void ResetState(bool asServer)");

			LogAssert.IsTrue(body.Contains("PersistentPool.Despawn(NetworkManager, NetworkObject);"),
				"Interactable.Despawn's fallback must pool through PersistentPool");
			LogAssert.IsFalse(body.Contains("ServerManager.Despawn("),
				"a direct pooled despawn leaves the drop in its world scene, to die at the unload");
		}

		[Test]
		public void TheSpawner_PoolsTheSameWay()
		{
			string runtime = ReadSource($"{Scripts}/Server/Implementation/World/SceneServer/Spawner/SpawnerRuntime.cs");
			LogAssert.IsTrue(runtime.Contains("PersistentPool.Despawn(NetworkManager, spawnable.NetworkObject);"),
				"the spawner's despawn must use the same rule as the spawner-less ones");
			LogAssert.IsTrue(runtime.Contains("PersistentPool.Keep(NetworkManager, nob);"),
				"an interrupted spawn's rollback must keep its instance out of the world scene");

			string pool = ReadSource($"{Scripts}/Server/Implementation/World/SceneServer/Spawner/SpawnerPool.cs");
			LogAssert.IsTrue(pool.Contains("PersistentPool.Keep(networkManager, added[i]);"),
				"prewarmed instances must live where every other pooled instance does");
			LogAssert.IsFalse(pool.Contains("public static void Keep("),
				"one rule, in one place: SpawnerPool must not grow its own copy back");
		}

		/// <summary>
		/// A pet has no spawner and was pooled in place by every path that sends it away, so a pet
		/// that had been out in a dungeon died with the instance when it unloaded.
		/// </summary>
		[Test]
		public void Pets_PoolOutOfTheWorldScene()
		{
			string pet = MethodBody(ReadSource($"{Scripts}/Shared/Implementation/Entity/NPC/Pet/Pet.cs"),
				"public override void Despawn()", "ReturnToPool();");
			LogAssert.IsTrue(pet.Contains("PersistentPool.Despawn(NetworkManager, NetworkObject);"),
				"Pet.Despawn (a pet's own death) must pool through PersistentPool");
			LogAssert.IsFalse(pet.Contains("ServerManager.Despawn("),
				"a direct pooled despawn leaves the pet in its world scene, to die at the unload");

			string system = ReadSource($"{Scripts}/Server/Implementation/World/SceneServer/Pet/PetSystem.cs");
			LogAssert.IsFalse(system.Contains("ServerManager.Despawn("),
				"every PetSystem despawn (dismissal, re-summon, owner despawn) goes through DespawnPet");
			string helper = MethodBody(system, "private void DespawnPet(NetworkObject petObject)", "/// <summary>");
			LogAssert.IsTrue(helper.Contains("PersistentPool.Despawn(Server.NetworkWrapper.NetworkManager, petObject);"),
				"and DespawnPet pools through the one rule");
			LogAssert.IsTrue(system.Contains("PersistentPool.Keep(Server.NetworkWrapper.NetworkManager, nob);"),
				"a pooled object that turned out not to be a pet goes back out of the world scenes too");
		}

		/// <summary>
		/// A player character is pooled in place the same way, and an instance scene unloads when
		/// its last occupant leaves — every dungeon exit threw a pooled character away and the next
		/// login paid for a fresh instantiate of the whole prefab.
		/// </summary>
		[Test]
		public void PlayerCharacters_PoolOutOfTheWorldScene()
		{
			string saving = ReadSource($"{Scripts}/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs");
			string combatLogout = ReadSource($"{Scripts}/Server/Implementation/World/SceneServer/Character/CharacterSystem.CombatLogout.cs");

			string despawn = MethodBody(saving, "private void SaveAndDespawnCharacter(", "private CharacterData BuildCharacterData(");
			LogAssert.IsTrue(despawn.Contains("PersistentPool.Despawn(Server.NetworkWrapper.NetworkManager, character.NetworkObject);"),
				"a logout, a transfer and a linger's end pool the character through PersistentPool");

			string evict = MethodBody(saving, "private void EvictLostCharacter(", "#endregion");
			LogAssert.IsTrue(evict.Contains("PersistentPool.Despawn(Server.NetworkWrapper.NetworkManager, character.NetworkObject)"),
				"so does the eviction of a character whose claim was lost");

			string body = MethodBody(combatLogout, "private void DespawnLingeringBody(", "/// <summary>");
			LogAssert.IsTrue(body.Contains("PersistentPool.Despawn(Server.NetworkWrapper.NetworkManager, character.NetworkObject)"),
				"and the despawn of a lingering body");

			LogAssert.IsFalse(saving.Contains("ServerManager.Despawn(") || combatLogout.Contains("ServerManager.Despawn("),
				"no character despawn goes around the rule");
		}

		/// <summary>
		/// FishNet never pools a scene object; it disables it in place for its scene to re-enable.
		/// Carried into DontDestroyOnLoad it would outlive the unload and meet its own copy on the
		/// next load.
		/// </summary>
		[Test]
		public void ASceneObject_IsNeverCarriedOutOfItsScene()
		{
			string body = MethodBody(ReadSource($"{Scripts}/Shared/Implementation/Tools/PersistentPool.cs"),
				"public static void Keep(", "Object.DontDestroyOnLoad(gameObject);");

			LogAssert.IsTrue(body.Contains("instance.IsSceneObject ||"),
				"Keep must refuse a scene object");
		}
	}
}
