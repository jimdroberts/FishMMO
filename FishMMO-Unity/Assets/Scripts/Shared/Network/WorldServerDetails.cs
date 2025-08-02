using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class containing details about a world server, including name, address, status, and player count.
	/// </summary>
	[Serializable]
	public class WorldServerDetails
	{
		/// <summary>Name of the world server.</summary>
		public string Name;
		/// <summary>Timestamp of the last server heartbeat or status update.</summary>
		public DateTime LastPulse;
		/// <summary>IP address or hostname of the server.</summary>
		public string Address;
		/// <summary>Port number for the server.</summary>
		public ushort Port;
		/// <summary>Number of characters currently on the server.</summary>
		public int CharacterCount;
		/// <summary>Indicates whether the server is locked (not accepting new connections).</summary>
		public bool Locked;
	}
}