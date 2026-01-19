using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Login server registration data transfer object.
	/// </summary>
	public struct LoginServerData
	{
		public readonly long ID;
		public readonly string Name;
		public readonly DateTime LastPulse;
		public readonly string Address;
		public readonly ushort Port;

		public LoginServerData(long id, string name, DateTime lastPulse, string address, ushort port)
		{
			ID = id;
			Name = name;
			LastPulse = lastPulse;
			Address = address;
			Port = port;
		}
	}
}