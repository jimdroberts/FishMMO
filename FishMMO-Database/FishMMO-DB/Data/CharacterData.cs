using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character data transfer object.
	/// </summary>
	public struct CharacterData : IVersioned<CharacterData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Character display name.</summary>
		public readonly string Name;
		/// <summary>Lowercase copy of name for lookups.</summary>
		public readonly string NameLowercase;
		/// <summary>Account name that owns this character.</summary>
		public readonly string Account;
		/// <summary>Whether this is the active character.</summary>
		public readonly bool Selected;
		/// <summary>World server ID this character belongs to.</summary>
		public readonly long WorldServerID;
		/// <summary>Current scene name.</summary>
		public readonly string SceneName;
		/// <summary>Current scene handle.</summary>
		public readonly int SceneHandle;
		/// <summary>Bind point scene name.</summary>
		public readonly string BindScene;
		/// <summary>Bind point position X.</summary>
		public readonly float BindX;
		/// <summary>Bind point position Y.</summary>
		public readonly float BindY;
		/// <summary>Bind point position Z.</summary>
		public readonly float BindZ;
		/// <summary>Instance ID (0 if over-world).</summary>
		public readonly long InstanceID;
		/// <summary>Instance spawn position X.</summary>
		public readonly float InstanceX;
		/// <summary>Instance spawn position Y.</summary>
		public readonly float InstanceY;
		/// <summary>Instance spawn position Z.</summary>
		public readonly float InstanceZ;
		/// <summary>Instance spawn rotation X.</summary>
		public readonly float InstanceRotX;
		/// <summary>Instance spawn rotation Y.</summary>
		public readonly float InstanceRotY;
		/// <summary>Instance spawn rotation Z.</summary>
		public readonly float InstanceRotZ;
		/// <summary>Instance spawn rotation W.</summary>
		public readonly float InstanceRotW;
		/// <summary>Character race template ID.</summary>
		public readonly int RaceID;
		/// <summary>Character model index.</summary>
		public readonly int ModelIndex;
		/// <summary>World position X.</summary>
		public readonly float X;
		/// <summary>World position Y.</summary>
		public readonly float Y;
		/// <summary>World position Z.</summary>
		public readonly float Z;
		/// <summary>World rotation X.</summary>
		public readonly float RotX;
		/// <summary>World rotation Y.</summary>
		public readonly float RotY;
		/// <summary>World rotation Z.</summary>
		public readonly float RotZ;
		/// <summary>World rotation W.</summary>
		public readonly float RotW;
		/// <summary>Access level for permissions.</summary>
		public readonly byte AccessLevel;
		/// <summary>Whether the character is currently online.</summary>
		public readonly bool Online;
		/// <summary>Character state flags.</summary>
		public readonly int Flags;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Timestamp when character was created.</summary>
		public readonly DateTime TimeCreated;
		/// <summary>Timestamp of last save.</summary>
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

		public CharacterData WithWorldServerIdVersionAndTimestamp(long worldServerID, long version, DateTime lastSaved)
		{
			return new CharacterData(ID, Name, NameLowercase, Account, Selected, worldServerID, SceneName, SceneHandle, BindScene, BindX, BindY, BindZ, InstanceID, InstanceX, InstanceY, InstanceZ, InstanceRotX, InstanceRotY, InstanceRotZ, InstanceRotW, RaceID, ModelIndex, X, Y, Z, RotX, RotY, RotZ, RotW, AccessLevel, Online, Flags, version, TimeCreated, lastSaved);
		}
	}
}