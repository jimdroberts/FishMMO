using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Scene server registration data transfer object.
	/// </summary>
	public struct SceneServerData
	{
		public readonly long ID;
		public readonly string Name;
		public readonly DateTime LastPulse;
		public readonly string Address;
		public readonly int Port;
		public readonly int CharacterCount;
		public readonly bool Locked;

		public SceneServerData(long id, string name, DateTime lastPulse, string address, int port, int characterCount, bool locked)
		{
			ID = id;
			Name = name;
			LastPulse = lastPulse;
			Address = address;
			Port = port;
			CharacterCount = characterCount;
			Locked = locked;
		}
	}
}