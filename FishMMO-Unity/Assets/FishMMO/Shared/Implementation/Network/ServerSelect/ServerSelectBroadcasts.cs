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

	/// <summary>
	/// Client → server: request a connection token for the server this client is about to
	/// connect to next (Login → World, World → Scene).
	/// </summary>
	/// <remarks>
	/// Every game server is reached through the same L4 UDP proxy and therefore sees
	/// 127.0.0.1, so each connection needs a token carrying the client's real IP. Only a
	/// party that already knows that IP can issue one, which is why the client asks the
	/// server it is currently authenticated to rather than going back to IPFetch.
	/// Answered with <see cref="ConnectionTokenBroadcast"/>. Authenticated clients only.
	/// </remarks>
	public struct RequestConnectionTokenBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Server → client: a freshly minted connection token, or an empty token when the
	/// server could not mint one. Answers <see cref="RequestConnectionTokenBroadcast"/>.
	/// </summary>
	public struct ConnectionTokenBroadcast : IBroadcast
	{
		/// <summary>
		/// One-time connection token to send in the next ClientHandshake. Empty when
		/// minting failed, in which case the next hop rejects the handshake.
		/// </summary>
		public string ConnectionToken;
	}
}