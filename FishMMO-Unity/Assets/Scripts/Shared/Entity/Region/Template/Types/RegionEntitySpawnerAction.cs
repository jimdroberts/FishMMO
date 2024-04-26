namespace FishMMO.Shared
{
	public class RegionEntitySpawnerAction : RegionAction
	{
		//public Entity[] spawnables;
		public int minSpawns = 0;
		public int maxSpawns = 1;

		public override void Invoke(IPlayerCharacter character, Region region)
		{
			// spawn things
		}
	}
}