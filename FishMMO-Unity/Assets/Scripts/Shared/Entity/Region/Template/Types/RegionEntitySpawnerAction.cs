using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Region action that spawns entities within a region when invoked. The number of entities spawned is determined by minSpawns and maxSpawns.
	/// </summary>
	[CreateAssetMenu(fileName = "New Region Enity Spawner Action", menuName = "FishMMO/Region/Region Entity Spawner", order = 1)]
	public class RegionEntitySpawnerAction : RegionAction
	{
		/// <summary>
		/// Array of entities that can be spawned in the region.
		/// </summary>
		// public Entity[] spawnables;

		/// <summary>
		/// The minimum number of entities to spawn when this action is triggered.
		/// </summary>
		public int minSpawns = 0;

		/// <summary>
		/// The maximum number of entities to spawn when this action is triggered.
		/// </summary>
		public int maxSpawns = 1;

		/// <summary>
		/// Invokes the region action, spawning entities in the region according to minSpawns and maxSpawns. Actual spawning logic is not yet implemented.
		/// </summary>
		/// <param name="character">The player character triggering the action.</param>
		/// <param name="region">The region in which entities will be spawned.</param>
		/// <param name="isReconciling">Indicates if the action is part of a reconciliation process.</param>
		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
			// Spawn entities in the region based on minSpawns and maxSpawns.
			// Actual spawning logic is currently not implemented.
		}
	}
}