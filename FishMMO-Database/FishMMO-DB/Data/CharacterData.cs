using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character data transfer object.
	/// </summary>
	public struct CharacterData : IVersioned<CharacterData>
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
		public readonly long Version;
		public readonly DateTime TimeCreated;
		public readonly DateTime LastSaved;

		long IVersioned<CharacterData>.Version => Version;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterData"/> struct.
		/// </summary>
		public CharacterData(long id, string name, string nameLowercase, string account, bool selected, long worldServerID, string sceneName, int sceneHandle, string bindScene, float bindX, float bindY, float bindZ, long instanceID, float instanceX, float instanceY, float instanceZ, float instanceRotX, float instanceRotY, float instanceRotZ, float instanceRotW, int raceID, int modelIndex, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW, byte accessLevel, bool online, int flags, long version, DateTime timeCreated, DateTime lastSaved)
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
			Version = version;
			TimeCreated = timeCreated;
			LastSaved = lastSaved;
		}

		public CharacterData WithVersion(long newVersion)
		{
			return new CharacterData(ID, Name, NameLowercase, Account, Selected, WorldServerID, SceneName, SceneHandle, BindScene, BindX, BindY, BindZ, InstanceID, InstanceX, InstanceY, InstanceZ, InstanceRotX, InstanceRotY, InstanceRotZ, InstanceRotW, RaceID, ModelIndex, X, Y, Z, RotX, RotY, RotZ, RotW, AccessLevel, Online, Flags, newVersion, TimeCreated, LastSaved);
		}

		public CharacterData WithFlagsVersionAndTimestamp(int flags, long version, DateTime lastSaved)
		{
			return new CharacterData(ID, Name, NameLowercase, Account, Selected, WorldServerID, SceneName, SceneHandle, BindScene, BindX, BindY, BindZ, InstanceID, InstanceX, InstanceY, InstanceZ, InstanceRotX, InstanceRotY, InstanceRotZ, InstanceRotW, RaceID, ModelIndex, X, Y, Z, RotX, RotY, RotZ, RotW, AccessLevel, Online, flags, version, TimeCreated, lastSaved);
		}
	}
}