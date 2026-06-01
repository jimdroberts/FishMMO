using System.Collections.Generic;
using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regression tests for deterministic ability object container allocation helpers.
	/// </summary>
	[TestFixture]
	public class AbilityObjectContainerIdTests
	{
		private const BindingFlags PrivateStaticFlags = BindingFlags.Static | BindingFlags.NonPublic;
		private const BindingFlags PrivateInstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;

		/// <summary>
		/// Verifies empty containers are treated as occupied collision slots, not same-spawn retries.
		/// </summary>
		[Test]
		public void IsSameSpawnContainer_EmptyContainer_ReturnsFalse()
		{
			Assert.IsFalse(IsSameSpawnContainer(null, 7, new PredictionTick(100u)));
			Assert.IsFalse(IsSameSpawnContainer(new Dictionary<int, AbilityObject>(), 7, new PredictionTick(100u)));
		}

		/// <summary>
		/// Verifies non-empty containers are same-spawn only when every live object matches seed and tick.
		/// </summary>
		[Test]
		public void IsSameSpawnContainer_NonEmptyContainer_RequiresMatchingSeedAndTick()
		{
			GameObject gameObject = new GameObject("AbilityObjectContainerIdTest");
			try
			{
				AbilityObject abilityObject = gameObject.AddComponent<AbilityObject>();
				abilityObject.SpawnSeed = 7;
				abilityObject.SpawnTick = new PredictionTick(100u);
				var container = new Dictionary<int, AbilityObject>
				{
					{ 1, abilityObject },
				};

				Assert.IsTrue(IsSameSpawnContainer(container, 7, new PredictionTick(100u)));
				Assert.IsFalse(IsSameSpawnContainer(container, 8, new PredictionTick(100u)));
				Assert.IsFalse(IsSameSpawnContainer(container, 7, new PredictionTick(101u)));
			}
			finally
			{
				Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Verifies predicted-object rollback remains correct when the uint network tick wraps.
		/// </summary>
		[Test]
		public void IsSpawnTickAfter_AcrossUintWrap_UsesSignedComparison()
		{
			uint reconcileTick = uint.MaxValue - 5u;

			Assert.IsTrue(IsSpawnTickAfter(new PredictionTick(5u), reconcileTick),
				"A spawn tick just after uint wrap is newer than a reconcile tick just before wrap.");
			Assert.IsFalse(IsSpawnTickAfter(new PredictionTick(reconcileTick - 1u), reconcileTick),
				"A spawn tick before the reconcile tick must not be treated as predicted-after-reconcile.");
			Assert.IsFalse(IsSpawnTickAfter(new PredictionTick(reconcileTick), reconcileTick),
				"An object spawned exactly on the reconcile tick is confirmed and must remain.");
		}

		/// <summary>
		/// Child ability objects cloned from a server-side parent must also be marked as
		/// server-side so their collision path dispatches authoritative hit effects.
		/// </summary>
		[Test]
		public void InitializeSpawnedChildObject_CopiesServerFlagFromSource()
		{
			GameObject sourceObject = new GameObject("SourceAbilityObject");
			GameObject childObject = new GameObject("ChildAbilityObject");

			try
			{
				AbilityObject source = sourceObject.AddComponent<AbilityObject>();
				AbilityObject child = childObject.AddComponent<AbilityObject>();
				SetPrivateField(source, "isServer", true);
				SetPrivateField(source, "tickDelta", 1.0f / 30f);
				source.ContainerID = 42;
				source.HitCount = 1;
				source.RemainingLifeTime = 2.0f;
				source.SpawnTick = new PredictionTick(100u);
				source.SpawnSeed = 7;

				var spawnedObjects = new Dictionary<int, AbilityObject>();
				InitializeSpawnedChildObject(child, source, 1, spawnedObjects, source.SpawnSeed);

				Assert.IsTrue(GetPrivateField<bool>(child, "isServer"),
					"Child ability objects must preserve the server-side dispatch flag from their source object.");
				Assert.AreSame(child, spawnedObjects[1],
					"The initialized child must be registered in the shared spawned-object map.");
			}
			finally
			{
				Object.DestroyImmediate(childObject);
				Object.DestroyImmediate(sourceObject);
			}
		}

		private static bool IsSameSpawnContainer(Dictionary<int, AbilityObject> container, int seed, PredictionTick spawnTick)
		{
			return (bool)typeof(AbilityObject)
				.GetMethod("IsSameSpawnContainer", PrivateStaticFlags)
				.Invoke(null, new object[] { container, seed, spawnTick });
		}

		private static bool IsSpawnTickAfter(PredictionTick spawnTick, uint tick)
		{
			return (bool)typeof(Ability)
				.GetMethod("IsSpawnTickAfter", PrivateStaticFlags)
				.Invoke(null, new object[] { spawnTick, tick });
		}

		private static void InitializeSpawnedChildObject(AbilityObject child, AbilityObject source, int childID, Dictionary<int, AbilityObject> spawnedObjects, int seed)
		{
			typeof(AbilityObject)
				.GetMethod("InitializeSpawnedChildObject", PrivateStaticFlags)
				.Invoke(null, new object[] { child, source, childID, spawnedObjects, seed });
		}

		private static void SetPrivateField<T>(AbilityObject instance, string fieldName, T value)
		{
			typeof(AbilityObject)
				.GetField(fieldName, PrivateInstanceFlags)
				.SetValue(instance, value);
		}

		private static T GetPrivateField<T>(AbilityObject instance, string fieldName)
		{
			return (T)typeof(AbilityObject)
				.GetField(fieldName, PrivateInstanceFlags)
				.GetValue(instance);
		}
	}
}