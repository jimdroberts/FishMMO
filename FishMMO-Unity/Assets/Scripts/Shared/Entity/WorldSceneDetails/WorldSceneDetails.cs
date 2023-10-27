using System;

namespace FishMMO.Shared
{
	[Serializable]
	public class WorldSceneDetails
	{
		public CharacterInitialSpawnPositionDictionary InitialSpawnPositions = new CharacterInitialSpawnPositionDictionary();
		public RespawnPositionDictionary RespawnPositions = new RespawnPositionDictionary();
		public SceneTeleporterDictionary Teleporters = new SceneTeleporterDictionary();
		public SceneBoundaryDictionary Boundaries = new SceneBoundaryDictionary();
	}
}