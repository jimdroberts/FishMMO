using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[Serializable]
	public class WorldSceneDetails
	{
		public int MaxClients;
		public Sprite SceneTransitionImage;
		public CharacterInitialSpawnPositionDictionary InitialSpawnPositions = new CharacterInitialSpawnPositionDictionary();
		public CharacterRespawnPositionDictionary RespawnPositions = new CharacterRespawnPositionDictionary();
		public SceneTeleporterDictionary Teleporters = new SceneTeleporterDictionary();
		public SceneBoundaryDictionary Boundaries = new SceneBoundaryDictionary();
	}
}