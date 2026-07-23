using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Scene data transfer object.
	/// </summary>
	public struct SceneData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Scene server hosting this scene.</summary>
		public readonly long SceneServerID;
		/// <summary>World server this scene belongs to.</summary>
		public readonly long WorldServerID;
		/// <summary>Scene name.</summary>
		public readonly string SceneName;
		/// <summary>Scene handle identifier.</summary>
		public readonly int SceneHandle;
		/// <summary>Scene status flag.</summary>
		public readonly int SceneStatus;
		/// <summary>Scene type identifier.</summary>
		public readonly int SceneType;
		/// <summary>Currently occupying character ID.</summary>
		public readonly long CharacterID;
		/// <summary>Number of characters in the scene.</summary>
		public readonly int CharacterCount;
		/// <summary>Timestamp when scene was created.</summary>
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