using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character data transfer object.
	/// </summary>
	public struct CharacterData
	{
		public long ID { get; set; }
		public string Name { get; set; }
		public string NameLowercase { get; set; }
		public string Account { get; set; }
		public bool Selected { get; set; }
		public long WorldServerID { get; set; }
		public string SceneName { get; set; }
		public int SceneHandle { get; set; }
		public string BindScene { get; set; }
		public float BindX { get; set; }
		public float BindY { get; set; }
		public float BindZ { get; set; }
		public long InstanceID { get; set; }
		public float InstanceX { get; set; }
		public float InstanceY { get; set; }
		public float InstanceZ { get; set; }
		public float InstanceRotX { get; set; }
		public float InstanceRotY { get; set; }
		public float InstanceRotZ { get; set; }
		public float InstanceRotW { get; set; }
		public int RaceID { get; set; }
		public int ModelIndex { get; set; }
		public float X { get; set; }
		public float Y { get; set; }
		public float Z { get; set; }
		public float RotX { get; set; }
		public float RotY { get; set; }
		public float RotZ { get; set; }
		public float RotW { get; set; }
		public byte AccessLevel { get; set; }
		public bool Online { get; set; }
		public int Flags { get; set; }
		public DateTime TimeCreated { get; set; }
		public DateTime LastSaved { get; set; }
		public DateTime TimeDeleted { get; set; }
		public bool Deleted { get; set; }
	}
}