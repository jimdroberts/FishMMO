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
		/// The container id must be a pure function of (seed, spawn tick) — the same on every peer
		/// regardless of what that peer's ability already holds.
		/// </summary>
		/// <remarks>
		/// The allocator used to linear-probe past any occupied container. Occupancy is
		/// peer-local: an observer that missed a cast, or reclaimed a container the server still
		/// holds, has a different map, so the same cast landed on id N on the server and N+1 on
		/// the observer. <c>AbilityObjectDestroyedBroadcast</c> names an object by container id
		/// alone, so it then addressed a container that peer did not have, silently no-opped, and
		/// left a ghost flying to the end of its lifetime.
		/// </remarks>
		[Test]
		public void ContainerId_ForTheSameSpawn_IsIdenticalAcrossIndependentlyPopulatedAllocators()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ContainerId_ForTheSameSpawn_IsIdenticalAcrossIndependentlyPopulatedAllocators),
					"Two peers whose ability maps hold different numbers of containers must still derive the same id for the same spawn.")
					.GetAwaiter().GetResult();

				const int seed = -424_242;
				PredictionTick spawnTick = new PredictionTick(1_234u);

				AbilityTemplate template = ScriptableObject.CreateInstance<AbilityTemplate>();
				template.name = "ContainerIdProbe";
				try
				{
					// The server: three unrelated casts already in flight.
					Ability serverAbility = new Ability(1L, template);
					serverAbility.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>();
					for (int i = 0; i < 3; i++)
					{
						int occupiedId = AbilityContainerAllocator.ComputeContainerId(seed + 1000 + i, new PredictionTick(500u + (uint)i));
						serverAbility.Objects[occupiedId] = new Dictionary<int, AbilityObject>();
					}

					// An observer that has just come into range: nothing in flight at all.
					Ability observerAbility = new Ability(1L, template);

					bool serverAllocated = AbilityContainerAllocator.TryAllocate(serverAbility, seed, spawnTick,
						out int serverContainerId, out _, out _);
					bool observerAllocated = AbilityContainerAllocator.TryAllocate(observerAbility, seed, spawnTick,
						out int observerContainerId, out _, out _);

					AuthTestTrace.Log("AbilityObjectContainerIdTests", "STEP",
						$"server(3 containers)={serverContainerId} | observer(0 containers)={observerContainerId} | pure={AbilityContainerAllocator.ComputeContainerId(seed, spawnTick)}")
						.GetAwaiter().GetResult();

					LogAssert.IsTrue(serverAllocated && observerAllocated, "Both peers must allocate a fresh container.");
					LogAssert.AreEqual(serverContainerId, observerContainerId,
						"The container id must not depend on how full the peer's ability map happens to be.");
					LogAssert.AreEqual(AbilityContainerAllocator.ComputeContainerId(seed, spawnTick), serverContainerId,
						"The allocated id must be exactly the pure function of seed and spawn tick.");
				}
				finally
				{
					UnityEngine.Object.DestroyImmediate(template);
				}

				AuthTestTrace.Log("AbilityObjectContainerIdTests", "SUCCESS", nameof(ContainerId_ForTheSameSpawn_IsIdenticalAcrossIndependentlyPopulatedAllocators)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AbilityObjectContainerIdTests", "FAILURE", $"{nameof(ContainerId_ForTheSameSpawn_IsIdenticalAcrossIndependentlyPopulatedAllocators)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ContainerId_ForTheSameSpawn_IsIdenticalAcrossIndependentlyPopulatedAllocators)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// The same spawn arriving twice must keep the object that is already simulating.
		/// </summary>
		/// <remarks>
		/// An observer with state forwarding still on receives both the forwarded replicate and
		/// the activation broadcast. The allocator used to destroy the existing object and install
		/// the newcomer, so a copy that had already been fast-forwarded to where the server holds
		/// it was replaced by a fresh one at <c>ElapsedTicks</c> 0 — a projectile visibly jumping
		/// backwards.
		/// </remarks>
		[Test]
		public void Allocate_SameSpawnArrivingTwice_KeepsTheAlreadyFastForwardedObject()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(Allocate_SameSpawnArrivingTwice_KeepsTheAlreadyFastForwardedObject),
					"A duplicate of a spawn already simulating must be refused, not swapped in over a fast-forwarded object.")
					.GetAwaiter().GetResult();

				const int seed = 991;
				PredictionTick spawnTick = new PredictionTick(77u);

				AbilityTemplate template = ScriptableObject.CreateInstance<AbilityTemplate>();
				template.name = "SameSpawnProbe";
				GameObject existingGo = new GameObject("ExistingAbilityObject");
				try
				{
					Ability ability = new Ability(2L, template);

					// The copy that arrived first and was caught up to the server.
					AbilityObject existing = existingGo.AddComponent<AbilityObject>();
					existing.SpawnSeed = seed;
					existing.SpawnTick = spawnTick;
					existing.ElapsedTicks = 45u;

					int containerId = AbilityContainerAllocator.ComputeContainerId(seed, spawnTick);
					ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>()
					{
						{ containerId, new Dictionary<int, AbilityObject>() { { 0, existing } } },
					};

					bool allocated = AbilityContainerAllocator.TryAllocate(ability, seed, spawnTick,
						out int allocatedId, out Dictionary<int, AbilityObject> spawnedObjects, out AbilityObject existingRoot);

					AuthTestTrace.Log("AbilityObjectContainerIdTests", "STEP",
						$"allocated={allocated} id={allocatedId} existingRoot={(existingRoot == null ? "null" : "found")} elapsed={existing.ElapsedTicks}")
						.GetAwaiter().GetResult();

					LogAssert.IsFalse(allocated, "A duplicate of a spawn already present must not be given a container.");
					LogAssert.IsNull(spawnedObjects, "No new container may be created for a duplicate spawn.");
					LogAssert.AreSame(existing, existingRoot,
						"The existing object must be handed back so the caller can fast-forward it instead of starting over.");
					LogAssert.AreEqual(containerId, allocatedId, "The id must still be reported, and it must be the deterministic one.");
					LogAssert.AreEqual(45u, existing.ElapsedTicks,
						"The object that was already caught up must keep its elapsed ticks; a replacement would restart at 0.");
					LogAssert.AreEqual(1, ability.Objects.Count, "The duplicate must not add a second container.");
					LogAssert.AreSame(existing, ability.Objects[containerId][0], "The original object must still be the one in the map.");
				}
				finally
				{
					UnityEngine.Object.DestroyImmediate(existingGo);
					UnityEngine.Object.DestroyImmediate(template);
				}

				AuthTestTrace.Log("AbilityObjectContainerIdTests", "SUCCESS", nameof(Allocate_SameSpawnArrivingTwice_KeepsTheAlreadyFastForwardedObject)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AbilityObjectContainerIdTests", "FAILURE", $"{nameof(Allocate_SameSpawnArrivingTwice_KeepsTheAlreadyFastForwardedObject)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Allocate_SameSpawnArrivingTwice_KeepsTheAlreadyFastForwardedObject)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// A different cast colliding on the same id evicts the stale container rather than
		/// probing to the next slot.
		/// </summary>
		/// <remarks>
		/// Eviction is the replacement for probing: every peer reaches the same decision from the
		/// incoming spawn alone, where probing depended on how full that peer's map was.
		/// </remarks>
		[Test]
		public void Allocate_DifferentCastOnTheSameId_EvictsInsteadOfProbing()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(Allocate_DifferentCastOnTheSameId_EvictsInsteadOfProbing),
					"A collision from a different cast must replace the stale container under the same deterministic id.")
					.GetAwaiter().GetResult();

				const int seed = 12_345;
				PredictionTick spawnTick = new PredictionTick(4_242u);

				AbilityTemplate template = ScriptableObject.CreateInstance<AbilityTemplate>();
				template.name = "CollisionProbe";
				try
				{
					Ability ability = new Ability(3L, template);
					int containerId = AbilityContainerAllocator.ComputeContainerId(seed, spawnTick);

					/* A stale container sitting on the incoming spawn's id. Its objects are gone
					 * (the entry is null), which is the shape a container left behind by a
					 * collected object has. */
					Dictionary<int, AbilityObject> stale = new Dictionary<int, AbilityObject>() { { 0, null } };
					ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>() { { containerId, stale } };

					bool allocated = AbilityContainerAllocator.TryAllocate(ability, seed, spawnTick,
						out int allocatedId, out Dictionary<int, AbilityObject> spawnedObjects, out AbilityObject existingRoot);

					AuthTestTrace.Log("AbilityObjectContainerIdTests", "STEP",
						$"allocated={allocated} id={allocatedId} containers={ability.Objects.Count}")
						.GetAwaiter().GetResult();

					LogAssert.IsTrue(allocated, "The incoming spawn is the live one and must get its container.");
					LogAssert.IsNull(existingRoot, "There was nothing alive to keep.");
					LogAssert.AreEqual(containerId, allocatedId,
						"The id must stay the deterministic one — probing to the next slot is what desynchronised the peers.");
					LogAssert.AreEqual(1, ability.Objects.Count, "The stale container must be replaced, not left alongside a probed one.");
					LogAssert.AreSame(spawnedObjects, ability.Objects[containerId], "The fresh container must be the one installed.");
					LogAssert.AreEqual(0, spawnedObjects.Count, "The fresh container must start empty.");
				}
				finally
				{
					UnityEngine.Object.DestroyImmediate(template);
				}

				AuthTestTrace.Log("AbilityObjectContainerIdTests", "SUCCESS", nameof(Allocate_DifferentCastOnTheSameId_EvictsInsteadOfProbing)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AbilityObjectContainerIdTests", "FAILURE", $"{nameof(Allocate_DifferentCastOnTheSameId_EvictsInsteadOfProbing)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Allocate_DifferentCastOnTheSameId_EvictsInsteadOfProbing)).GetAwaiter().GetResult();
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