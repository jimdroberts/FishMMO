using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Group finder queue row data transfer object.
	/// </summary>
	public struct GroupFinderQueueData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>World server the character belongs to.</summary>
		public readonly long WorldServerID;
		/// <summary>The waiting character.</summary>
		public readonly long CharacterID;
		/// <summary>Instance kind: the shared <c>SceneType</c> value (Group = dungeon, PvP = arena).</summary>
		public readonly int SceneType;
		/// <summary>Pre-made group queued with, or 0.</summary>
		public readonly long GroupID;
		/// <summary>Dungeon or arena scene the character wants to play.</summary>
		public readonly string SceneName;
		/// <summary>Difficulty index into the dungeon's own list.</summary>
		public readonly int Difficulty;
		/// <summary>Row status. See <see cref="Enums.GroupFinderQueueStatus"/>.</summary>
		public readonly int Status;
		/// <summary>Party the character was matched into, or 0 while waiting.</summary>
		public readonly long PartyID;
		/// <summary>Instance the character was matched into, or 0 while waiting.</summary>
		public readonly long InstanceID;
		/// <summary>When the character joined the queue (UTC).</summary>
		public readonly DateTime TimeCreated;
		/// <summary>Last heartbeat from the character's scene server (UTC).</summary>
		public readonly DateTime LastPulse;
		/// <summary>When the row was matched (UTC), or null while waiting.</summary>
		public readonly DateTime? TimeMatched;

		public GroupFinderQueueData(long id, long worldServerID, long characterID, int sceneType, long groupID, string sceneName, int difficulty, int status, long partyID, long instanceID, DateTime timeCreated, DateTime lastPulse, DateTime? timeMatched)
		{
			ID = id;
			WorldServerID = worldServerID;
			CharacterID = characterID;
			SceneType = sceneType;
			GroupID = groupID;
			SceneName = sceneName;
			Difficulty = difficulty;
			Status = status;
			PartyID = partyID;
			InstanceID = instanceID;
			TimeCreated = timeCreated;
			LastPulse = lastPulse;
			TimeMatched = timeMatched;
		}
	}
}
