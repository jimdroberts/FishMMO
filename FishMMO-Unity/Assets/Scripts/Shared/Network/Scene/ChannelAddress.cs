using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable struct representing a network channel address, including IP, port, and scene handle.
	/// Used for network communication and scene management.
	/// </summary>
	[Serializable]
	public struct ChannelAddress
	{
		/// <summary>IP address or hostname of the channel.</summary>
		public string Address;
		/// <summary>Port number for the channel.</summary>
		public ushort Port;
		/// <summary>Handle or identifier for the associated scene.</summary>
		public int SceneHandle;
	}
}