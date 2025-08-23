using FishNet.Connection;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Interface for network-related operations, decoupling the Server class
	/// from the concrete FishNet implementation.
	/// </summary>
	public interface INetworkManagerWrapper
	{
		/// <summary>
		/// Gets the underlying FishNet NetworkManager instance.
		/// </summary>
		NetworkManager NetworkManager { get; }

		/// <summary>
		/// Starts the server.
		/// </summary>
		void StartServer();

		/// <summary>
		/// Sets the transport bind address manually.
		/// </summary>
		/// <param name="address">The address to bind the transport to.</param>
		/// <param name="addressType">The type of IP address (IPv4 or IPv6).</param>
		void SetTransportAddress(string address, IPAddressType addressType);

		/// <summary>
		/// Sets the transport port manually.
		/// </summary>
		/// <param name="port">The port number to use for the transport.</param>
		void SetTransportPort(ushort port);

		/// <summary>
		/// Sets the maximum number of clients manually.
		/// </summary>
		/// <param name="clients">The maximum number of clients allowed.</param>
		void SetMaximumClients(int clients);

		/// <summary>
		/// Applies transport configuration values from <see cref="IServerConfiguration"/>.
		/// </summary>
		void ApplyTransportConfiguration();

		/// <summary>
		/// Registers a broadcast handler for the given type.
		/// </summary>
		/// <typeparam name="T">The broadcast type.</typeparam>
		/// <param name="handler">The handler to register.</param>
		/// <param name="requireAuthentication">Whether authentication is required for the broadcast.</param>
		void RegisterBroadcast<T>(Action<NetworkConnection, T, Channel> handler, bool requireAuthentication = true) where T : struct, IBroadcast;

		/// <summary>
		/// Unregisters a broadcast handler for the given type.
		/// </summary>
		/// <typeparam name="T">The broadcast type.</typeparam>
		/// <param name="handler">The handler to unregister.</param>
		/// <param name="requireAuthentication">Whether authentication is required for the broadcast.</param>
		void UnregisterBroadcast<T>(Action<NetworkConnection, T, Channel> handler, bool requireAuthentication = true) where T : struct, IBroadcast;

		/// <summary>
		/// Subscribes to server connection state changes.
		/// </summary>
		/// <param name="handler">The handler to invoke on connection state changes.</param>
		void AttachServerConnectionStateEventHandler(Action<ServerConnectionStateArgs> handler);

		/// <summary>
		/// Attaches a login authenticator using the provided Server.
		/// </summary>
		/// <param name="server">The server instance.</param>
		void AttachLoginAuthenticator(IServer<INetworkManagerWrapper, NetworkConnection, ServerBehaviour> server);

		/// <summary>
		/// Broadcasts a message to a single network connection.
		/// </summary>
		/// <typeparam name="T">The broadcast type.</typeparam>
		/// <param name="conn">The network connection to send the message to.</param>
		/// <param name="broadcast">The message to broadcast.</param>
		/// <param name="channel">The channel to use for broadcasting (default is Reliable).</param>
		void Broadcast<T>(NetworkConnection conn, T broadcast, bool requireAuthentication = true, Channel channel = Channel.Reliable) where T : struct, IBroadcast;
	}
}