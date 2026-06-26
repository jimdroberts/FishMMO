using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Deterministic container-ID allocation for spawned ability objects.
	/// Extracted from <see cref="AbilityObject"/> to keep that class focused.
	/// Handles hash collision resolution and duplicate-spawn detection.
	/// </summary>
	internal static class AbilityContainerAllocator
	{
		/// <summary>
		/// Prime multiplier used to derive the base container ID from seed and spawn tick.
		/// </summary>
		private const int TickMultiplier = 1000003;
		/// <summary>
		/// Linear probe step for hash collision resolution.
		/// </summary>
		private const int ProbeStep = 1;
		/// <summary>
		/// Extra probe iterations beyond the current dictionary count to handle slots
		/// freed by in-progress collision resolution.
		/// </summary>
		private const int ProbeSearchSlack = 1;

		/// <summary>
		/// Allocates a deterministic container ID for a spawned ability object.</summary>
		/// <param name="ability">The ability whose Objects dictionary will host the new container.</param>
		/// <param name="seed">The spawn seed, combined with the tick to form the base container ID.</param>
		/// <param name="spawnTick">The prediction tick at which this ability object was spawned.</param>
		/// <param name="containerId">The allocated deterministic container ID.</param>
		/// <param name="spawnedObjects">A newly allocated dictionary stored under containerId in ability.Objects.</param>
		public static void Allocate(
			Ability ability,
			int seed,
			PredictionTick spawnTick,
			out int containerId,
			out Dictionary<int, AbilityObject> spawnedObjects)
		{
			spawnedObjects = new Dictionary<int, AbilityObject>();
			int baseId = unchecked(seed ^ ((int)spawnTick.Value * TickMultiplier));
			containerId = baseId;

			int probeLimit = (ability.Objects?.Count ?? 0) + ProbeSearchSlack;
			for (int probe = 0; probe < probeLimit; probe++)
			{
				if (!ability.Objects.TryGetValue(containerId, out Dictionary<int, AbilityObject> existing))
					return; // Free slot found

				if (IsSameSpawn(existing, seed, spawnTick))
				{
					DestroyContainer(existing);
					ability.Objects.Remove(containerId);
					return;
				}

				if (IsEffectivelyEmpty(existing))
				{
					ability.Objects.Remove(containerId);
					return;
				}

				containerId = unchecked(containerId + ProbeStep);
			}

			throw new InvalidOperationException(
				$"AbilityContainerAllocator: failed to find free container for ability {ability.ID} after {probeLimit} probes.");
		}

		/// <summary>
		/// Checks whether an existing container represents the same spawn (same seed and tick).
		/// Used to detect and replace duplicate spawns.
		/// </summary>
		private static bool IsSameSpawn(Dictionary<int, AbilityObject> container, int seed, PredictionTick spawnTick)
		{
			if (container == null || container.Count == 0) return false;
			foreach (AbilityObject obj in container.Values)
			{
				if (obj == null) continue;
				if (obj.SpawnSeed != seed || obj.SpawnTick.Value != spawnTick.Value) return false;
				return true; // At least one matching object found
			}
			return false;
		}

		/// <summary>
		/// Destroys all ability objects in a container and removes their ability references.
		/// </summary>
		private static void DestroyContainer(Dictionary<int, AbilityObject> container)
		{
			foreach (AbilityObject obj in container.Values)
			{
				if (obj != null)
				{
					obj.Ability = null;
					obj.DestroyAbilityObjectInternal();
				}
			}
		}

		/// <summary>
		/// Returns true when the container is null, empty, or contains only null entries.
		/// </summary>
		private static bool IsEffectivelyEmpty(Dictionary<int, AbilityObject> container)
		{
			if (container == null || container.Count == 0) return true;
			foreach (AbilityObject obj in container.Values)
				if (obj != null) return false;
			return true;
		}
	}
}