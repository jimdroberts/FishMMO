using System;

[Serializable]
public class WorldSceneDetails
{
	public CharacterInitialSpawnPositionDictionary initialSpawnPositions = new CharacterInitialSpawnPositionDictionary();
	public RespawnPositionDictionary respawnPositions = new RespawnPositionDictionary();
	public SceneTeleporterDictionary teleporters = new SceneTeleporterDictionary();
	public SceneBoundaryDictionary boundaries = new SceneBoundaryDictionary();
}