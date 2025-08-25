using FishNet.Connection;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Transporting;
using FishMMO.Logging;
using System;
using System.Runtime.CompilerServices;
using FishMMO.Server.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Wraps FishNet NetworkManager with a clean abstraction for server orchestration.
	/// </summary>
	public class FishNetNetworkWrapper : INetworkManagerWrapper
	{
		private readonly IServerConfiguration config;
		private readonly MonoBehaviour coroutineHost;

		/// <summary>
		/// Gets the network manager wrapper instance.
		/// </summary>
		public NetworkManager NetworkManager { get; private set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="FishNetNetworkWrapper"/> class.
		/// </summary>
		/// <param name="networkManager">The FishNet NetworkManager instance.</param>
		/// <param name="config">The server configuration provider.</param>
		/// <param name="coroutineHost">MonoBehaviour to host coroutines (usually the Server MonoBehaviour).</param>
		public FishNetNetworkWrapper(NetworkManager networkManager, IServerConfiguration config, MonoBehaviour coroutineHost)
		{
			NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
			this.config = config ?? throw new ArgumentNullException(nameof(config));
			this.coroutineHost = coroutineHost;
		}

		/// <summary>
		/// Starts the server, subscribes to connection state, and starts coroutine to await readiness.
		/// </summary>
		public void StartServer()
		{
			if (NetworkManager.ServerManager != null)
			{
				NetworkManager.ServerManager.StartConnection();
				if (coroutineHost != null)
				{
					coroutineHost.StartCoroutine(OnAwaitingConnectionReady());
				}
			}
		}

		/// <summary>
		/// Coroutine that waits for the server connection to be ready before proceeding.
		/// </summary>
		/// <returns>IEnumerator for coroutine.</returns>
		private System.Collections.IEnumerator OnAwaitingConnectionReady()
		{
			// Wait for the connection to the current server to start before we connect the client
			while (!NetworkManager.IsServerStarted)
			{
				yield return new WaitForSeconds(0.5f);
			}
			yield return null;
		}

		/// <summary>
		/// Sets the transport bind address manually.
		/// </summary>
		/// <param name="address">The address to bind the transport to.</param>
		/// <param name="addressType">The type of IP address (IPv4 or IPv6).</param>
		public void SetTransportAddress(string address, IPAddressType addressType)
		{
			NetworkManager.TransportManager.Transport?.SetServerBindAddress(address, addressType);
		}

		/// <summary>
		/// Sets the transport port manually.
		/// </summary>
		/// <param name="port">The port number to use for the transport.</param>
		public void SetTransportPort(ushort port)
		{
			NetworkManager.TransportManager.Transport?.SetPort(port);
		}

		/// <summary>
		/// Sets the maximum number of clients manually.
		/// </summary>
		/// <param name="clients">The maximum number of clients allowed.</param>
		public void SetMaximumClients(int clients)
		{
			NetworkManager.TransportManager.Transport?.SetMaximumClients(clients);
		}

		/// <summary>
		/// Applies transport configuration values from <see cref="IServerConfiguration"/>.
		/// </summary>
		public void ApplyTransportConfiguration()
		{
			var transport = NetworkManager.TransportManager.Transport;
			if (transport == null) return;

			string address = config.GetString("Address", "127.0.0.1");
			ushort port = config.GetUShort("Port", 7777);
			int maxClients = config.GetInt("MaximumClients", 100);

			transport.SetServerBindAddress(address, IPAddressType.IPv4);
			transport.SetPort(port);
			transport.SetMaximumClients(maxClients);
		}

		/// <summary>
		/// Registers a broadcast handler for the given type.
		/// </summary>
		/// <typeparam name="T">The broadcast type.</typeparam>
		/// <param name="handler">The handler to register.</param>
		/// <param name="requireAuthentication">Whether authentication is required for the broadcast.</param>
		public void RegisterBroadcast<T>(
			Action<NetworkConnection, T, Channel> handler,
			bool requireAuthentication = true) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Registered " + typeof(T));
			NetworkManager.ServerManager.RegisterBroadcast(handler, requireAuthentication);
		}

		/// <summary>
		/// Unregisters a broadcast handler for the given type.
		/// </summary>
		/// <typeparam name="T">The broadcast type.</typeparam>
		/// <param name="handler">The handler to unregister.</param>
		/// <param name="requireAuthentication">Whether authentication is required for the broadcast.</param>
		public void UnregisterBroadcast<T>(
			Action<NetworkConnection, T, Channel> handler,
			bool requireAuthentication = true) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Unregistered " + typeof(T));
			NetworkManager.ServerManager.UnregisterBroadcast(handler);
		}

		/// <summary>
		/// Subscribes to server connection state changes.
		/// </summary>
		/// <param name="handler">The handler to invoke on connection state changes.</param>
		public void AttachServerConnectionStateEventHandler(Action<ServerConnectionStateArgs> handler)
		{
			NetworkManager.ServerManager.OnServerConnectionState += handler;
		}

		/// <summary>
		/// Attaches a login authenticator using the provided Npgsql factory.
		/// </summary>
		/// <param name="dbContextFactory">The Npgsql database context factory.</param>
		public void AttachLoginAuthenticator(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			if (NetworkManager.ServerManager.GetAuthenticator() is ServerAuthenticator authenticator)
			{
				authenticator.Server = server;
				authenticator.NpgsqlDbContextFactory = server.CoreServer.NpgsqlDbContextFactory;
			}
		}

		/// <summary>
		/// Broadcasts a message to a network connection.
		/// </summary>
		/// <typeparam name="T">Type of broadcast struct.</typeparam>
		/// <param name="conn">The network connection.</param>
		/// <param name="broadcast">The broadcast message.</param>
		/// <param name="requireAuthentication">Whether authentication is required.</param>
		/// <param name="channel">The channel to use for broadcasting.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Broadcast<T>(NetworkConnection conn, T broadcast, bool requireAuthentication = true, Channel channel = Channel.Reliable) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Sending: " + typeof(T));
			conn.Broadcast(broadcast, requireAuthentication, channel);
		}
	}
}