using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// World server registration data transfer object.
	/// </summary>
	public struct WorldServerData
	{
		public readonly long ID;
		public readonly string Name;
		public readonly DateTime LastPulse;
		public readonly string Address;
		public readonly ushort Port;
		public readonly int CharacterCount;
		public readonly bool Locked;

		public WorldServerData(long id, string name, DateTime lastPulse, string address, ushort port, int characterCount, bool locked)
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