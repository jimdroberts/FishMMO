using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A character with no ability spawner assigned. "a lesser fire elemental" shipped that way and every cast threw in
	/// AbilityObject.SimulatedSpawnerPose (1,438 times on the live scene server, 11-16); the controller now finds one.
	/// </summary>
	[TestFixture]
	public class AbilitySpawnerFallbackTests
	{
		private GameObject host;

		[TearDown]
		public void TearDown()
		{
			if (host != null) Object.DestroyImmediate(host);
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		private static void Resolve(AbilityController controller)
		{
			MethodInfo resolve = typeof(AbilityController).GetMethod("ResolveAbilitySpawner", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(resolve, "AbilityController resolves a missing spawner");
			resolve.Invoke(controller, null);
		}

		[Test]
		public void AMissingSpawner_IsTheChildNamedAbilitySpawnPoint()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			host = new GameObject("SpawnerFallback");
			host.SetActive(false);
			Transform point = new GameObject("AbilitySpawnPoint").transform;
			point.SetParent(host.transform, false);
			AbilityController controller = host.AddComponent<AbilityController>();
			controller.AbilitySpawner = null;

			Resolve(controller);

			LogAssert.IsTrue(controller.AbilitySpawner == point, "the authored spawn point is found by name");
		}

		[Test]
		public void WithNoSpawnPointAtAll_TheCharacterItselfIsUsed()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			host = new GameObject("SpawnerFallbackBare");
			host.SetActive(false);
			AbilityController controller = host.AddComponent<AbilityController>();
			controller.AbilitySpawner = null;

			Resolve(controller);

			LogAssert.IsTrue(controller.AbilitySpawner == host.transform, "never null: a cast must not throw");
		}

		[Test]
		public void EveryCharacterPrefabAssignsItsSpawner()
		{
			foreach (string path in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Shared/Entity" }))
			{
				string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(path);
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
				AbilityController abilities = prefab != null ? prefab.GetComponent<AbilityController>() : null;
				if (abilities != null)
				{
					LogAssert.IsTrue(abilities.AbilitySpawner != null, $"{assetPath} has an AbilityController with no AbilitySpawner assigned");
				}
			}
		}
	}
}
