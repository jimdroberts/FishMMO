using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Chat message data transfer object.
	/// </summary>
	public struct ChatData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly string CharacterName;
		public readonly string AccountName;
		public readonly long WorldServerID;
		public readonly long SceneServerID;
		public readonly byte Channel;
		public readonly string Message;
		public readonly DateTime ServerReceivedTime;
		public readonly DateTime TimeCreated;

		public ChatData(long id, long characterID, long worldServerID, long sceneServerID, byte channel, string message, DateTime serverReceivedTime, DateTime timeCreated)
		{
			ID = id;
			CharacterID = characterID;
			CharacterName = string.Empty;
			AccountName = string.Empty;
			WorldServerID = worldServerID;
			SceneServerID = sceneServerID;
			Channel = channel;
			Message = message;
			ServerReceivedTime = serverReceivedTime;
			TimeCreated = timeCreated;
		}

		public ChatData(
			long id,
			long characterID,
			string characterName,
			string accountName,
			long worldServerID,
			long sceneServerID,
			byte channel,
			string message,
			DateTime serverReceivedTime,
			DateTime timeCreated)
		{
			ID = id;
			CharacterID = characterID;
			CharacterName = characterName ?? string.Empty;
			AccountName = accountName ?? string.Empty;
			WorldServerID = worldServerID;
			SceneServerID = sceneServerID;
			Channel = channel;
			Message = message;
			ServerReceivedTime = serverReceivedTime;
			TimeCreated = timeCreated;
		}
	}
}