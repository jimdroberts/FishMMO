using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character data transfer object.
	/// </summary>
	public struct CharacterData
	{
		public readonly long ID;
		public readonly string Name;
		public readonly string NameLowercase;
		public readonly string Account;
		public readonly bool Selected;
		public readonly long WorldServerID;
		public readonly string SceneName;
		public readonly int SceneHandle;
		public readonly string BindScene;
		public readonly float BindX;
		public readonly float BindY;
		public readonly float BindZ;
		public readonly long InstanceID;
		public readonly float InstanceX;
		public readonly float InstanceY;
		public readonly float InstanceZ;
		public readonly float InstanceRotX;
		public readonly float InstanceRotY;
		public readonly float InstanceRotZ;
		public readonly float InstanceRotW;
		public readonly int RaceID;
		public readonly int ModelIndex;
		public readonly float X;
		public readonly float Y;
		public readonly float Z;
		public readonly float RotX;
		public readonly float RotY;
		public readonly float RotZ;
		public readonly float RotW;
		public readonly byte AccessLevel;
		public readonly bool Online;
		public readonly int Flags;
		public readonly DateTime TimeCreated;
		public readonly DateTime LastSaved;
		public readonly DateTime TimeDeleted;
		public readonly bool Deleted;

		public CharacterData(long id, string name, string nameLowercase, string account, bool selected, long worldServerID, string sceneName, int sceneHandle, string bindScene, float bindX, float bindY, float bindZ, long instanceID, float instanceX, float instanceY, float instanceZ, float instanceRotX, float instanceRotY, float instanceRotZ, float instanceRotW, int raceID, int modelIndex, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW, byte accessLevel, bool online, int flags, DateTime timeCreated, DateTime lastSaved, DateTime timeDeleted, bool deleted)
		{
			ID = id;
			Name = name;
			NameLowercase = nameLowercase;
			Account = account;
			Selected = selected;
			WorldServerID = worldServerID;
			SceneName = sceneName;
			SceneHandle = sceneHandle;
			BindScene = bindScene;
			BindX = bindX;
			BindY = bindY;
			BindZ = bindZ;
			InstanceID = instanceID;
			InstanceX = instanceX;
			InstanceY = instanceY;
			InstanceZ = instanceZ;
			InstanceRotX = instanceRotX;
			InstanceRotY = instanceRotY;
			InstanceRotZ = instanceRotZ;
			InstanceRotW = instanceRotW;
			RaceID = raceID;
			ModelIndex = modelIndex;
			X = x;
			Y = y;
			Z = z;
			RotX = rotX;
			RotY = rotY;
			RotZ = rotZ;
			RotW = rotW;
			AccessLevel = accessLevel;
			Online = online;
			Flags = flags;
			TimeCreated = timeCreated;
			LastSaved = lastSaved;
			TimeDeleted = timeDeleted;
			Deleted = deleted;
		}
	}
}