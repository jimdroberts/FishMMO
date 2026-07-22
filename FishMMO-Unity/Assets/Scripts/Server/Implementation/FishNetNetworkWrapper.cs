using FishNet.Connection;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.WebTransport;
using FishNet.Transporting.Multipass;
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
	public class FishNetNetworkWrapper : INetworkManagerWrapper, IDisposable
	{
		private readonly IServerConfiguration config;
		private readonly MonoBehaviour coroutineHost;
		private Coroutine awaitingConnectionCoroutine;

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
					awaitingConnectionCoroutine = coroutineHost.StartCoroutine(OnAwaitingConnectionReady());
				}
			}
		}

		/// <summary>
		/// Stops the server.
		/// </summary>
		public void StopServer()
		{
			if (NetworkManager.ServerManager != null)
			{
				if (awaitingConnectionCoroutine != null && coroutineHost != null)
				{
					coroutineHost.StopCoroutine(awaitingConnectionCoroutine);
					awaitingConnectionCoroutine = null;
				}

				NetworkManager.ServerManager.StopConnection(true);
			}
		}

		public void Dispose()
		{
			if (awaitingConnectionCoroutine != null && coroutineHost != null)
			{
				coroutineHost.StopCoroutine(awaitingConnectionCoroutine);
				awaitingConnectionCoroutine = null;
			}
		}

		/// <summary>
		/// Coroutine that waits for the server connection to be ready before proceeding.
		/// Waits until <see cref="NetworkManager.IsServerStarted"/> is true, then completes.
		/// </summary>
		/// <returns>IEnumerator for coroutine.</returns>
		private System.Collections.IEnumerator OnAwaitingConnectionReady()
		{
			float deadline = Time.realtimeSinceStartup + 30f;
			while (!NetworkManager.IsServerStarted && Time.realtimeSinceStartup < deadline)
				yield return null;
			if (!NetworkManager.IsServerStarted)
				Log.Warning("FishNetNetworkWrapper", "Server failed to start within 30 seconds.");
			awaitingConnectionCoroutine = null;
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
		/// WebTransport (QUIC/HTTP3) for all platforms. Each game server terminates its
		/// own TLS — NGINX forwards raw UDP at Layer 4.
		///
		/// <para>Certificate path defaults by platform (overridable via config):</para>
		/// <list type="table">
		///   <listheader><term>Platform</term><description>Default cert path</description></listheader>
		///   <item><term>Linux</term><description><c>/etc/fishmmo/certs/fullchain.pem</c></description></item>
		///   <item><term>Windows</term><description><c>C:\ProgramData\FishMMO\certs\fullchain.pem</c></description></item>
		///   <item><term>macOS</term><description><c>/usr/local/share/fishmmo/certs/fullchain.pem</c></description></item>
		///   <item><term>Other</term><description><c>certs/fullchain.pem</c> (relative to working directory)</description></item>
		/// </list>
		/// When <paramref name="addressOverride"/> or <paramref name="portOverride"/>
		/// are supplied they take precedence over the .cfg file values, allowing the
		/// Server component's Inspector overrides to control the actual transport bind
		/// address and port.
		/// </summary>
		public void ApplyTransportConfiguration(string addressOverride = null, ushort? portOverride = null)
		{
			var transport = NetworkManager.TransportManager.Transport;
			if (transport == null) return;

			// Inspector/component overrides take precedence over .cfg file values.
			string address = !string.IsNullOrWhiteSpace(addressOverride) ? addressOverride : config.GetString("Address", "127.0.0.1");
			ushort port = portOverride.HasValue && portOverride.Value > 0 ? portOverride.Value : config.GetUShort("Port", 7777);
			int maxClients = config.GetInt("MaximumClients", 100);

			// IPv6 dual-stack support: when enabled, binds both IPv4 and IPv6 on the same port.
			bool enableIPv6 = string.Equals(config.GetString("EnableIPv6", "false"), "true", StringComparison.OrdinalIgnoreCase);
			string ipv6Address = config.GetString("IPv6Address", "::1");

			// WebTransport: each game server terminates QUIC/TLS with PEM certificates.
			// Certificate paths come from the server .cfg file — configurable per platform,
			// per deployment (Linux, Windows, macOS) and per certificate source
			// (Let's Encrypt, Cloudflare, Porkbun, etc.).
			//
			// When using Multipass (the normal case), the top-level TransportManager.Transport
			// IS Multipass — a connection multiplexer that does NOT forward SetServerBindAddress /
			// SetPort / SetMaximumClients to children.  We must configure each child WebTransport
			// directly with address, port, maxClients, AND TLS certificates.
			if (NetworkManager.TransportManager.GetTransport<Multipass>() is Multipass mp)
			{
				if (mp.Transports == null || mp.Transports.Count == 0)
				{
					Log.Warning("FishNetNetworkWrapper", "Multipass has no child transports.");
					return;
				}
				bool configured = false;
				foreach (var t in mp.Transports)
				{
					if (t is WebTransport wt)
					{
						wt.SetServerBindAddress(address, IPAddressType.IPv4);
						if (enableIPv6)
						{
							wt.SetServerBindAddress(ipv6Address, IPAddressType.IPv6);
						}
						wt.SetPort(port);
						wt.SetMaximumClients(maxClients);
						if (!ConfigureWebTransport(wt))
							Log.Warning("FishNetNetworkWrapper", "WebTransport configuration failed for Multipass child — TLS certificates not loaded.");
						configured = true;
					}
				}
				if (!configured)
					Log.Warning("FishNetNetworkWrapper", "No WebTransport in Multipass — transport not configured.");
			}
			else if (transport is WebTransport wt)
			{
				// No Multipass — configure the transport directly.
				transport.SetServerBindAddress(address, IPAddressType.IPv4);
				if (enableIPv6)
				{
					transport.SetServerBindAddress(ipv6Address, IPAddressType.IPv6);
				}
				transport.SetPort(port);
				transport.SetMaximumClients(maxClients);
				if (!ConfigureWebTransport(wt))
					Log.Warning("FishNetNetworkWrapper", "WebTransport configuration failed -- TLS certificates not loaded.");
			}
		}

		private bool ConfigureWebTransport(WebTransport wt)
		{
			// Certificate paths from .cfg file — fully configurable per deployment.
			// Falls back to platform defaults if not specified in config.
			// Environment variables FISHMMO_CERT_PATH and FISHMMO_KEY_PATH take
			// precedence over config, allowing Docker and CI deployments to inject
			// paths without modifying configuration files.
			string envCertPath = System.Environment.GetEnvironmentVariable("FISHMMO_CERT_PATH");
			string envKeyPath = System.Environment.GetEnvironmentVariable("FISHMMO_KEY_PATH");

			string certPath;
			string keyPath;
			if (!string.IsNullOrWhiteSpace(envCertPath) && !string.IsNullOrWhiteSpace(envKeyPath))
			{
				certPath = envCertPath;
				keyPath = envKeyPath;

				if (!System.IO.File.Exists(certPath))
				{
					Log.Error("FishNetNetworkWrapper", $"Certificate file not found (from env): {certPath}");
					return false;
				}
				if (!System.IO.File.Exists(keyPath))
				{
					Log.Error("FishNetNetworkWrapper", $"Private key file not found (from env): {keyPath}");
					return false;
				}

				wt.SetCertificatePath(certPath);
				wt.SetPrivateKeyPath(keyPath);

				Log.Debug("FishNetNetworkWrapper",
					$"WebTransport configured: cert={certPath}, key={keyPath}");
				return true;
			}

			// No env vars set — fall back to config / platform defaults.
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
			string defaultCertPath = "/etc/fishmmo/certs/fullchain.pem";
			string defaultKeyPath = "/etc/fishmmo/certs/privkey.pem";
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			string defaultCertPath = "C:\\ProgramData\\FishMMO\\certs\\fullchain.pem";
			string defaultKeyPath  = "C:\\ProgramData\\FishMMO\\certs\\privkey.pem";
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
			string defaultCertPath = "/usr/local/share/fishmmo/certs/fullchain.pem";
			string defaultKeyPath  = "/usr/local/share/fishmmo/certs/privkey.pem";
#else
			string defaultCertPath = "certs/fullchain.pem";
			string defaultKeyPath  = "certs/privkey.pem";
#endif
			certPath = config.GetString("CertificatePath", defaultCertPath);
			keyPath = config.GetString("PrivateKeyPath", defaultKeyPath);

			// Validate certificate files exist before passing paths to native.
			if (!System.IO.File.Exists(certPath))
			{
				Log.Error("FishNetNetworkWrapper", $"Certificate file not found: {certPath}. WebTransport will be skipped.");
				return false;
			}
			if (!System.IO.File.Exists(keyPath))
			{
				Log.Error("FishNetNetworkWrapper", $"Private key file not found: {keyPath}. WebTransport will be skipped.");
				return false;
			}

			wt.SetCertificatePath(certPath);
			wt.SetPrivateKeyPath(keyPath);

			Log.Debug("FishNetNetworkWrapper",
				$"WebTransport configured: cert={certPath}, key={keyPath}");
			return true;
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
		public void UnregisterBroadcast<T>(
			Action<NetworkConnection, T, Channel> handler) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Unregistered " + typeof(T));
			NetworkManager.ServerManager.UnregisterBroadcast(handler);
		}

		/// <summary>
		/// Subscribes to server connection state changes.
		/// </summary>
		/// <param name="handler">The handler to invoke on connection state changes.</param>
		public void RegisterServerConnectionStateEventHandler(Action<ServerConnectionStateArgs> handler)
		{
			NetworkManager.ServerManager.OnServerConnectionState += handler;
		}

		/// <summary>
		/// Unsubscribes from server connection state changes.
		/// </summary>
		/// <param name="handler">The handler to unsubscribe from connection state changes.</param>
		public void UnregisterServerConnectionStateEventHandler(Action<ServerConnectionStateArgs> handler)
		{
			NetworkManager.ServerManager.OnServerConnectionState -= handler;
		}

		/// <summary>
		/// Attaches a login authenticator, assigns server references, and initializes async workers.
		/// </summary>
		/// <param name="server">The server instance providing infrastructure access.</param>
		public void AttachLoginAuthenticator(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			if (NetworkManager.ServerManager.GetAuthenticator() is IServerAuthenticator authenticator)
			{
				authenticator.Server = server;
				authenticator.InitializeWorkers();
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
			conn.Broadcast(broadcast, requireAuthentication, channel);
		}
	}
}
