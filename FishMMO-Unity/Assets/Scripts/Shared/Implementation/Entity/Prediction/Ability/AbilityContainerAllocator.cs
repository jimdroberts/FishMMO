using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Deterministic container-ID allocation for spawned ability objects.
	/// Extracted from <see cref="AbilityObject"/> to keep that class focused.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The id is a pure function of (seed, spawn tick).</b> That is the whole contract, and
	/// several things depend on it: <c>AbilityObjectDestroyedBroadcast</c> names an object by
	/// container id alone, and the observer that receives it must arrive at the same id the server
	/// did from the same cast.
	/// </para>
	/// <para>
	/// The previous implementation derived a base id the same way but then LINEAR PROBED past any
	/// container that was already occupied. Occupancy is peer-local — an observer that missed a
	/// cast, or reclaimed a container the server still holds, has a different <c>Objects</c> map —
	/// so the same cast could land on id N on the server and id N+1 on an observer. The destroy
	/// message then addressed a container that peer did not have, silently no-opped, and left a
	/// ghost flying to the end of its lifetime. There is no probing here for that reason: a
	/// collision is resolved by <i>eviction</i>, which every peer performs identically because it
	/// depends only on what the incoming spawn is, not on how full the map happens to be.
	/// </para>
	/// <para>
	/// Two kinds of occupancy are possible, and they are handled differently:
	/// </para>
	/// <list type="bullet">
	/// <item>
	/// <b>The same spawn arrives twice</b> (same seed and spawn tick) — an observer with state
	/// forwarding still on receives both the forwarded replicate and the activation broadcast. The
	/// EXISTING object is kept and the newcomer is discarded. Replacing it was a visible bug: the
	/// existing copy had already been fast-forwarded to where the server holds it, and the
	/// replacement started again at <c>ElapsedTicks</c> 0, so the projectile jumped backwards.
	/// </item>
	/// <item>
	/// <b>A genuinely different cast hashes to the same id.</b> The stale container is destroyed
	/// and replaced, because the incoming spawn is the live one and the id must keep naming what
	/// the server thinks it names.
	/// </item>
	/// </list>
	/// </remarks>
	internal static class AbilityContainerAllocator
	{
		/// <summary>
		/// Prime multiplier used to derive the container ID from seed and spawn tick.
		/// </summary>
		private const int TickMultiplier = 1000003;

		/// <summary>
		/// The container id for a spawn. A pure function of its inputs, with no reference to any
		/// peer's current state — see the type remarks for why that matters.
		/// </summary>
		/// <param name="seed">The spawn seed.</param>
		/// <param name="spawnTick">The replicate tick the object was spawned on.</param>
		internal static int ComputeContainerId(int seed, PredictionTick spawnTick)
		{
			return unchecked(seed ^ ((int)spawnTick.Value * TickMultiplier));
		}

		/// <summary>
		/// Claims the deterministic container for a spawn that is about to be initialised.
		/// </summary>
		/// <param name="ability">The ability whose <c>Objects</c> map hosts the container.</param>
		/// <param name="seed">The spawn seed.</param>
		/// <param name="spawnTick">The prediction tick at which this ability object was spawned.</param>
		/// <param name="containerId">The deterministic container ID, set on every path.</param>
		/// <param name="spawnedObjects">
		/// A newly allocated dictionary, already installed in <c>ability.Objects</c>. Null when the
		/// caller must abandon its spawn.
		/// </param>
		/// <param name="existingRoot">
		/// The live root object of the identical spawn already present, when there is one. The
		/// caller should destroy the object it was about to initialise and use this instead.
		/// </param>
		/// <returns>
		/// True when the caller owns a fresh container and should proceed with initialisation;
		/// false when this exact spawn already exists and must not be duplicated.
		/// </returns>
		public static bool TryAllocate(
			Ability ability,
			int seed,
			PredictionTick spawnTick,
			out int containerId,
			out Dictionary<int, AbilityObject> spawnedObjects,
			out AbilityObject existingRoot)
		{
			containerId = ComputeContainerId(seed, spawnTick);
			spawnedObjects = null;
			existingRoot = null;

			if (ability == null)
			{
				return false;
			}

			if (ability.Objects == null)
			{
				ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>();
			}

			if (ability.Objects.TryGetValue(containerId, out Dictionary<int, AbilityObject> existing))
			{
				if (IsSameSpawn(existing, seed, spawnTick))
				{
					/* Same cast, arriving a second time. Keep what is already simulating: it may
					 * have been fast-forwarded to the server's position, and a replacement would
					 * restart it at ElapsedTicks 0. */
					existingRoot = FindRoot(existing);
					if (existingRoot != null)
					{
						return false;
					}
				}

				/* Either a different cast colliding on this id, or a container whose objects are
				 * all gone. Evict it — the incoming spawn is the live one, and every peer reaches
				 * this same decision from the same inputs. */
				DestroyContainer(existing);
				ability.Objects.Remove(containerId);
			}

			spawnedObjects = new Dictionary<int, AbilityObject>();
			ability.Objects.Add(containerId, spawnedObjects);
			return true;
		}

		/// <summary>
		/// Returns the container's root object (id 0), or the first live object when the root is
		/// already gone but children are still simulating.
		/// </summary>
		private static AbilityObject FindRoot(Dictionary<int, AbilityObject> container)
		{
			if (container == null)
			{
				return null;
			}
			if (container.TryGetValue(0, out AbilityObject root) && root != null)
			{
				return root;
			}
			foreach (AbilityObject obj in container.Values)
			{
				if (obj != null)
				{
					return obj;
				}
			}
			return null;
		}

		/// <summary>
		/// Checks whether an existing container holds the same spawn (same seed and tick).
		/// </summary>
		/// <remarks>
		/// Children copy their root's seed and spawn tick, so any live member answers for the
		/// container. All of them are scanned rather than just the first, so a container whose
		/// root has already expired still reports its identity correctly.
		/// </remarks>
		/// <summary>
		/// True when this exact cast — same seed, same spawn tick — is already running on this peer.
		/// </summary>
		/// <remarks>
		/// Asked before spawning from an observer broadcast, so the caller can tell news from a
		/// duplicate. Spawning a duplicate is harmless (the allocator collapses it and returns what
		/// is already there), but fast-forwarding one is not: the existing object has been
		/// simulating since it was created, and advancing it again puts it ahead of the server's.
		/// </remarks>
		internal static bool IsSpawnAlreadyRunning(Ability ability, int seed, PredictionTick spawnTick)
		{
			if (ability?.Objects == null)
			{
				return false;
			}

			int containerId = ComputeContainerId(seed, spawnTick);
			return ability.Objects.TryGetValue(containerId, out Dictionary<int, AbilityObject> existing) &&
				   IsSameSpawn(existing, seed, spawnTick);
		}

		private static bool IsSameSpawn(Dictionary<int, AbilityObject> container, int seed, PredictionTick spawnTick)
		{
			if (container == null || container.Count == 0) return false;
			foreach (AbilityObject obj in container.Values)
			{
				if (obj == null) continue;
				if (obj.SpawnSeed == seed && obj.SpawnTick.Value == spawnTick.Value)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Destroys all ability objects in a container and removes their ability references.
		/// </summary>
		/// <remarks>
		/// OnDestroy ECA events are suppressed: the container is being <i>evicted</i> because a
		/// different cast claimed its id, not ended by lifetime or collision, so an explosion or
		/// on-death proc firing here would play at a moment nothing happened.
		/// </remarks>
		private static void DestroyContainer(Dictionary<int, AbilityObject> container)
		{
			if (container == null)
			{
				return;
			}
			foreach (AbilityObject obj in container.Values)
			{
				if (obj != null)
				{
					obj.Ability = null;
					obj.DestroyAbilityObjectInternal(dispatchDestroyEvents: false);
				}
			}
		}
	}
}
