using System;
using System.Collections.Generic;
using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

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
		/// Verifies null or empty containers are not treated as same-spawn retries.
		/// </summary>
		[Test]
		public void IsSameSpawnContainer_EmptyContainer_ReturnsFalse()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(IsSameSpawnContainer_EmptyContainer_ReturnsFalse),
					"A null or empty container must not be treated as a same-spawn retry.")
					.GetAwaiter().GetResult();

				bool nullResult = IsSameSpawn(null, 7, new PredictionTick(100u));
				bool emptyResult = IsSameSpawn(new Dictionary<int, AbilityObject>(), 7, new PredictionTick(100u));
				AuthTestTrace.Log("AbilityObjectContainerIdTests", "STEP",
					$"IsSameSpawn(null,seed=7,tick=100)={nullResult} | IsSameSpawn(empty,seed=7,tick=100)={emptyResult}")
					.GetAwaiter().GetResult();

				LogAssert.IsFalse(nullResult, "A null container must not be treated as a same-spawn retry.");
				LogAssert.IsFalse(emptyResult, "An empty container must not be treated as a same-spawn retry.");

				AuthTestTrace.Log("AbilityObjectContainerIdTests", "SUCCESS", nameof(IsSameSpawnContainer_EmptyContainer_ReturnsFalse)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AbilityObjectContainerIdTests", "FAILURE", $"{nameof(IsSameSpawnContainer_EmptyContainer_ReturnsFalse)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(IsSameSpawnContainer_EmptyContainer_ReturnsFalse)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Verifies a populated container is a same-spawn retry only when a live object matches seed and tick.
		/// </summary>
		[Test]
		public void IsSameSpawnContainer_NonEmptyContainer_RequiresMatchingSeedAndTick()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(IsSameSpawnContainer_NonEmptyContainer_RequiresMatchingSeedAndTick),
					"A populated container is a same-spawn retry only when a live object matches the seed and spawn tick.")
					.GetAwaiter().GetResult();

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
					AuthTestTrace.Log("AbilityObjectContainerIdTests", "STEP",
						"Container seeded with one AbilityObject (seed=7, tick=100). Probing match/mismatch combinations.")
						.GetAwaiter().GetResult();

					LogAssert.IsTrue(IsSameSpawn(container, 7, new PredictionTick(100u)),
						"Matching seed and tick must report a same-spawn container.");
					LogAssert.IsFalse(IsSameSpawn(container, 8, new PredictionTick(100u)),
						"A mismatched seed must not report a same-spawn container.");
					LogAssert.IsFalse(IsSameSpawn(container, 7, new PredictionTick(101u)),
						"A mismatched tick must not report a same-spawn container.");
				}
				finally
				{
					UnityEngine.Object.DestroyImmediate(gameObject);
				}

				AuthTestTrace.Log("AbilityObjectContainerIdTests", "SUCCESS", nameof(IsSameSpawnContainer_NonEmptyContainer_RequiresMatchingSeedAndTick)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AbilityObjectContainerIdTests", "FAILURE", $"{nameof(IsSameSpawnContainer_NonEmptyContainer_RequiresMatchingSeedAndTick)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(IsSameSpawnContainer_NonEmptyContainer_RequiresMatchingSeedAndTick)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Verifies predicted-object rollback remains correct when the uint network tick wraps.
		/// </summary>
		[Test]
		public void IsSpawnTickAfter_AcrossUintWrap_UsesSignedComparison()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(IsSpawnTickAfter_AcrossUintWrap_UsesSignedComparison),
					"Predicted-object rollback must use signed tick comparison so it stays correct across a uint wrap.")
					.GetAwaiter().GetResult();

				uint reconcileTick = uint.MaxValue - 5u;
				AuthTestTrace.Log("AbilityObjectContainerIdTests", "STEP",
					$"reconcileTick={reconcileTick} (just before uint wrap). Probing spawn ticks around the wrap boundary.")
					.GetAwaiter().GetResult();

				LogAssert.IsTrue(IsSpawnTickAfter(new PredictionTick(5u), reconcileTick),
					"A spawn tick just after uint wrap is newer than a reconcile tick just before wrap.");
				LogAssert.IsFalse(IsSpawnTickAfter(new PredictionTick(reconcileTick - 1u), reconcileTick),
					"A spawn tick before the reconcile tick must not be treated as predicted-after-reconcile.");
				LogAssert.IsFalse(IsSpawnTickAfter(new PredictionTick(reconcileTick), reconcileTick),
					"An object spawned exactly on the reconcile tick is confirmed and must remain.");

				AuthTestTrace.Log("AbilityObjectContainerIdTests", "SUCCESS", nameof(IsSpawnTickAfter_AcrossUintWrap_UsesSignedComparison)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AbilityObjectContainerIdTests", "FAILURE", $"{nameof(IsSpawnTickAfter_AcrossUintWrap_UsesSignedComparison)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(IsSpawnTickAfter_AcrossUintWrap_UsesSignedComparison)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Child ability objects cloned from a server-side parent must also be marked as
		/// server-side so their collision path dispatches authoritative hit effects.
		/// </summary>
		[Test]
		public void InitializeSpawnedChildObject_CopiesServerFlagFromSource()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(InitializeSpawnedChildObject_CopiesServerFlagFromSource),
					"Child ability objects cloned from a server-side source must inherit the server flag and register in the spawned map.")
					.GetAwaiter().GetResult();

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
					AuthTestTrace.Log("AbilityObjectContainerIdTests", "STEP",
						"Source configured server-side (isServer=true, seed=7, tick=100). Initializing child object id=1.")
						.GetAwaiter().GetResult();

					var spawnedObjects = new Dictionary<int, AbilityObject>();
					InitializeSpawnedChildObject(child, source, 1, spawnedObjects, source.SpawnSeed);
					AuthTestTrace.Log("AbilityObjectContainerIdTests", "STEP",
						$"After init: child.isServer={GetPrivateField<bool>(child, "isServer")} spawnedObjects.ContainsKey(1)={spawnedObjects.ContainsKey(1)}")
						.GetAwaiter().GetResult();

					LogAssert.IsTrue(GetPrivateField<bool>(child, "isServer"),
						"Child ability objects must preserve the server-side dispatch flag from their source object.");
					LogAssert.AreSame(child, spawnedObjects[1],
						"The initialized child must be registered in the shared spawned-object map.");
				}
				finally
				{
					UnityEngine.Object.DestroyImmediate(childObject);
					UnityEngine.Object.DestroyImmediate(sourceObject);
				}

				AuthTestTrace.Log("AbilityObjectContainerIdTests", "SUCCESS", nameof(InitializeSpawnedChildObject_CopiesServerFlagFromSource)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AbilityObjectContainerIdTests", "FAILURE", $"{nameof(InitializeSpawnedChildObject_CopiesServerFlagFromSource)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(InitializeSpawnedChildObject_CopiesServerFlagFromSource)).GetAwaiter().GetResult();
			}
		}

		private static bool IsSameSpawn(Dictionary<int, AbilityObject> container, int seed, PredictionTick spawnTick)
		{
			// Container-allocation helpers were extracted from AbilityObject into
			// AbilityContainerAllocator (and IsSameSpawnContainer was renamed to
			// IsSameSpawn). Reflection targets the current home of the method.
			return (bool)typeof(AbilityContainerAllocator)
				.GetMethod("IsSameSpawn", PrivateStaticFlags)
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