using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class containing a list of server ports for server selection.
	/// </summary>
	[Serializable]
	public class ServerAddresses
	{
		/// <summary>List of available server addresses (internal use).</summary>
		public List<ServerAddress> Addresses;
		/// <summary>List of available server ports (client use).</summary>
		public List<ushort> Ports;
	}

	/// <summary>
	/// Internal server bind address. For client-facing communication,
	/// the address is always Constants.Configuration.GameHost — use
	/// Port directly or ServerAddresses.Ports.
	/// </summary>
	[Serializable]
	public struct ServerAddress
	{
		/// <summary>IP address or hostname the server binds to.</summary>
		public string Address;
		/// <summary>Port number for the server.</summary>
		public ushort Port;
	}
}