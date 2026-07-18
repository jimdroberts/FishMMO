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
		/// <summary>
		/// List of available world servers.
		///
		/// WARNING: FishNet serializes this list by iterating over it.
		/// Any modifications (add/remove/clear) during serialization cause
		/// undefined behavior (e.g. corrupt packets or crashes). The server
		/// MUST copy the list before sending whenever it might be mutated
		/// concurrently by another thread.
		/// </summary>
		public List<WorldServerDetails> Servers;
	}
	/// <summary>
	/// Broadcast for connecting to a world scene server.
	/// Contains only the port; address is always Constants.Configuration.GameHost.
	/// </summary>
	public struct WorldSceneConnectBroadcast : IBroadcast
	{
		/// <summary>Port number for the world scene server.</summary>
		public ushort Port;
	}
}