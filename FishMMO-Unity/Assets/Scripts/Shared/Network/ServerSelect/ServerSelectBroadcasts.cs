using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for requesting the list of available servers.
	/// No additional data required.
	/// </summary>
	public struct RequestServerListBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for sending the list of available servers to the client.
	/// Contains a list of world server details.
	/// </summary>
	public struct ServerListBroadcast : IBroadcast
	{
		/// <summary>List of available world servers.</summary>
		public List<WorldServerDetails> Servers;
	}
	/// <summary>
	/// Broadcast for connecting to a world scene server.
	/// Contains the server address and port.
	/// </summary>
	public struct WorldSceneConnectBroadcast : IBroadcast
	{
		/// <summary>IP address or hostname of the world scene server.</summary>
		public string Address;
		/// <summary>Port number for the world scene server.</summary>
		public ushort Port;
	}
}