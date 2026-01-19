using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Patch server registration data transfer object.
	/// </summary>
	public struct PatchServerData
	{
		public readonly long ID;
		public readonly string Address;
		public readonly ushort Port;
		public readonly DateTime LastPulse;

		public PatchServerData(long id, string address, ushort port, DateTime lastPulse)
		{
			ID = id;
			Address = address;
			Port = port;
			LastPulse = lastPulse;
		}
	}
}