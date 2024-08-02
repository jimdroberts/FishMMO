using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Region Enity Spawner Action", menuName = "Region/Region Entity Spawner", order = 1)]
	public class RegionEntitySpawnerAction : RegionAction
	{
		//public Entity[] spawnables;
		public int minSpawns = 0;
		public int maxSpawns = 1;

		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
			// spawn things
		}
	}
}