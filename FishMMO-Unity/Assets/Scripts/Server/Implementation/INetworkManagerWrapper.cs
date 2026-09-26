using FishNet.Connection;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using FishMMO.Server.Core;
using UnityEngine.SceneManagement;

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
		/// Stops the server.
		/// </summary>
		void StopServer();

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
		void ApplyTransportConfiguration(string addressOverride = null, ushort? portOverride = null);

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
		void UnregisterBroadcast<T>(Action<NetworkConnection, T, Channel> handler) where T : struct, IBroadcast;

		/// <summary>
		/// Subscribes to server connection state changes.
		/// </summary>
		/// <param name="handler">The handler to invoke on connection state changes.</param>
		void RegisterServerConnectionStateEventHandler(Action<ServerConnectionStateArgs> handler);

		/// <summary>
		/// Unsubscribes from server connection state changes.
		/// </summary>
		/// <param name="handler">The handler to remove from connection state changes.</param>
		void UnregisterServerConnectionStateEventHandler(Action<ServerConnectionStateArgs> handler);

		/// <summary>
		/// Attaches a login authenticator using the provided Server.
		/// </summary>
		/// <param name="server">The server instance.</param>
		void AttachLoginAuthenticator(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server);

		/// <summary>
		/// Broadcasts a message to a single network connection.
		/// </summary>
		/// <typeparam name="T">The broadcast type.</typeparam>
		/// <param name="conn">The network connection to send the message to.</param>
		/// <param name="broadcast">The message to broadcast.</param>
		/// <param name="channel">The channel to use for broadcasting (default is Reliable).</param>
		void Broadcast<T>(NetworkConnection conn, T broadcast, bool requireAuthentication = true, Channel channel = Channel.Reliable) where T : struct, IBroadcast;

		/// <summary>
		/// Broadcasts one message to every connection in a set, serialising it once.
		/// </summary>
		/// <remarks>
		/// The single-connection overload serialises per call, so a loop over it pays the
		/// serialisation once per recipient. Use this for any fan-out: a scene's connections, a
		/// match's team, a party's local members. The set is read, never kept; an empty or null
		/// set is a no-op.
		/// </remarks>
		/// <typeparam name="T">The broadcast type.</typeparam>
		/// <param name="connections">The recipients.</param>
		/// <param name="broadcast">The message to broadcast.</param>
		/// <param name="requireAuthentication">Whether each recipient must be authenticated.</param>
		/// <param name="channel">The channel to use for broadcasting (default is Reliable).</param>
		void Broadcast<T>(HashSet<NetworkConnection> connections, T broadcast, bool requireAuthentication = true, Channel channel = Channel.Reliable) where T : struct, IBroadcast;

		/// <summary>
		/// Broadcasts one message to every connection FishNet has in a scene, serialising it once.
		/// </summary>
		/// <remarks>
		/// Reads <c>SceneManager.SceneConnections</c>, which FishNet keeps per loaded scene, so no
		/// caller needs to walk the server's characters asking each for its scene.
		/// </remarks>
		/// <typeparam name="T">The broadcast type.</typeparam>
		/// <param name="scene">The scene whose connections receive the message.</param>
		/// <param name="broadcast">The message to broadcast.</param>
		/// <param name="requireAuthentication">Whether each recipient must be authenticated.</param>
		/// <param name="channel">The channel to use for broadcasting (default is Reliable).</param>
		/// <returns>True if the scene had at least one connection.</returns>
		bool BroadcastToScene<T>(Scene scene, T broadcast, bool requireAuthentication = true, Channel channel = Channel.Reliable) where T : struct, IBroadcast;

		/// <summary>
		/// Gets FishNet's live connection set for a scene. The set belongs to FishNet: read it on
		/// the main thread and never modify or keep it.
		/// </summary>
		/// <param name="scene">The scene.</param>
		/// <param name="connections">The scene's connections, or null.</param>
		/// <returns>True if the scene has at least one connection.</returns>
		bool TryGetSceneConnections(Scene scene, out HashSet<NetworkConnection> connections);
	}
}