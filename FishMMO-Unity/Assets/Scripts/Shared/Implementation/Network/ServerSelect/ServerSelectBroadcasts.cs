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
		/// Array of available world servers. Each entry is a <see cref="WorldServerDetails"/>
		/// defined in the <c>FishMMO.Shared</c> namespace at
		/// <see cref="WorldServerDetails"/> (defined in the same namespace).
		///
		/// A fixed-size array avoids the concurrent-modification hazard of a
		/// <see cref="System.Collections.Generic.List{T}"/> during FishNet
		/// serialization (which iterates the collection on the network thread).
		/// The array is safe for read-only iteration even when the underlying
		/// data is being replaced on another thread; the server replaces the
		/// entire array rather than mutating entries in place.
		/// </summary>
		public WorldServerDetails[] Servers;
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