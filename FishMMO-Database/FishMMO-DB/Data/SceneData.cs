using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Scene data transfer object.
	/// </summary>
	public struct SceneData
	{
		public readonly long ID;
		public readonly long SceneServerID;
		public readonly long WorldServerID;
		public readonly string SceneName;
		public readonly int SceneHandle;
		public readonly int SceneStatus;
		public readonly int SceneType;
		public readonly long CharacterID;
		public readonly int CharacterCount;
		public readonly DateTime TimeCreated;

		public SceneData(long id, long sceneServerID, long worldServerID, string sceneName, int sceneHandle, int sceneStatus, int sceneType, long characterID, int characterCount, DateTime timeCreated)
		{
			ID = id;
			SceneServerID = sceneServerID;
			WorldServerID = worldServerID;
			SceneName = sceneName;
			SceneHandle = sceneHandle;
			SceneStatus = sceneStatus;
			SceneType = sceneType;
			CharacterID = characterID;
			CharacterCount = characterCount;
			TimeCreated = timeCreated;
		}
	}
}