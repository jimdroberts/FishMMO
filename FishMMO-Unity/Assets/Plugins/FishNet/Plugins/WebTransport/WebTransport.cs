using FishNet.Managing;
using FishNet.Transporting.WebTransport.Native;
using FishNet.Managing.Transporting;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishNet.Transporting.WebTransport
{
	/// <summary>
	/// WebTransport (QUIC/HTTP3) transport for FishMMO.
	///
	/// <para>Platform matrix:</para>
	/// <list type="table">
	///   <listheader><term>Platform</term><description>Backend</description></listheader>
	///   <item><term>Windows/Linux (standalone)</term><description>Native C library via P/Invoke (fishmmo_webtransport / msquic)</description></item>
	///   <item><term>macOS (standalone)</term><description>Native C library via P/Invoke — requires manual build of libfishmmo_webtransport.dylib (see FishMMO-WebTransport/README.md); no pre-built binary shipped</description></item>
	///   <item><term>WebGL (browser build)</term><description>Browser WebTransport API via JavaScript interop (WebTransport.jslib)</description></item>
	///   <item><term>Unity Editor (any host OS)</term><description>Native C library loaded from the current platform's plugin — QUIC testing without a full build</description></item>
	/// </list>
	/// </summary>
	/// <remarks>
	/// Architecture: WebTransport is designed to work with or without NGINX.
	/// WebTransport runs over HTTP/3 (QUIC), which requires TLS 1.3 natively.
	///
	/// QUIC requires TLS 1.3 — there is no plain-QUIC mode.
	/// Certificate and private key paths are configured in server .cfg files
	/// and applied via <c>SetCertificatePath</c> / <c>SetPrivateKeyPath</c>
	/// at startup by <c>FishNetNetworkWrapper.ConfigureWebTransport</c>.
	///
	/// Channel mapping (native, no suffix byte needed):
	///   - Channel 0 (Reliable)   → WebTransport bidirectional streams
	///   - Channel 1 (Unreliable) → QUIC DATAGRAM frames
	/// </remarks>
	[DisallowMultipleComponent]
	public class WebTransport : Transport
	{
		#region Configuration
		/// <summary>QUIC minimum MTU (RFC 9000 §14).</summary>
		private const int MTU = 1200;

		/// <summary>Server bind address. Set at startup from .cfg file.</summary>
		private string serverBindAddress = "127.0.0.1";

		/// <summary>Server port. Set at startup from .cfg file.</summary>
		private ushort port;

		/// <summary>Max concurrent clients. Set at startup from .cfg file.</summary>
		/// <remarks>
		/// Note: This value is forwarded to <see cref="Server.ServerSocket.SetMaximumClients"/> on start.
		/// Both this field and the equivalent field in ServerSocket store the same limit.
		/// <c>GetMaximumClients()</c> reads from ServerSocket, not this field.
		/// </remarks>
		private int maximumClients = 100;

		/// <summary>Client connect hostname. Set at startup from Constants.GameHost.</summary>
		private string clientAddress = "game.fishmmo.com";

		/// <summary>TLS certificate PEM path. Set at startup from .cfg file.</summary>
		private string certificatePath = "";

		/// <summary>TLS private key PEM path. Set at startup from .cfg file.</summary>
		private string privateKeyPath = "";

		/// <summary>
		/// Server-side idle timeout in seconds. If no data is received within this window,
		/// the server considers the client disconnected.
		/// </summary>
		[SerializeField]
		private float serverTimeout = 120f;

		/// <summary>
		/// Client-side idle timeout in seconds. If no data is received within this window,
		/// the client considers the server disconnected.
		/// </summary>
		[SerializeField]
		private float clientTimeout = 20f;
		#endregion

		#region Private
		/// <summary>
		/// Server socket and handler.
		/// </summary>
		private Server.ServerSocket serverSocket = new Server.ServerSocket();
		/// <summary>
		/// Client socket and handler.
		/// </summary>
		private Client.ClientSocket clientSocket = new Client.ClientSocket();
		#endregion


		#region Initialization and Unity
		protected void OnDestroy()
		{
			Shutdown();
		}
		#endregion

		#region ConnectionStates
		public override string GetConnectionAddress(int connectionId)
		{
			return this.serverSocket.GetConnectionAddress(connectionId);
		}

		public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
		public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
		public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;

		public override LocalConnectionState GetConnectionState(bool server)
		{
			return server ? this.serverSocket.GetConnectionState() : this.clientSocket.GetConnectionState();
		}

		public override RemoteConnectionState GetConnectionState(int connectionId)
		{
			return this.serverSocket.GetConnectionState(connectionId);
		}

		public override void HandleClientConnectionState(ClientConnectionStateArgs connectionStateArgs)
		{
			OnClientConnectionState?.Invoke(connectionStateArgs);
		}

		public override void HandleServerConnectionState(ServerConnectionStateArgs connectionStateArgs)
		{
			OnServerConnectionState?.Invoke(connectionStateArgs);
		}

		public override void HandleRemoteConnectionState(RemoteConnectionStateArgs connectionStateArgs)
		{
			OnRemoteConnectionState?.Invoke(connectionStateArgs);
		}
		#endregion

		#region Iterating
		public override void IterateIncoming(bool server)
		{
			if (server)
				this.serverSocket.IterateIncoming();
			else
				this.clientSocket.IterateIncoming();
		}

		public override void IterateOutgoing(bool server)
		{
			if (server)
				this.serverSocket.IterateOutgoing();
			else
				this.clientSocket.IterateOutgoing();
		}
		#endregion

		#region ReceivedData
		public override event Action<ClientReceivedDataArgs> OnClientReceivedData;
		public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs)
		{
			OnClientReceivedData?.Invoke(receivedDataArgs);
		}

		public override event Action<ServerReceivedDataArgs> OnServerReceivedData;
		public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs)
		{
			OnServerReceivedData?.Invoke(receivedDataArgs);
		}
		#endregion

		#region Sending
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public override void SendToServer(byte channelId, ArraySegment<byte> segment)
		{
			SanitizeChannel(ref channelId);
			if (channelId == 1 && segment.Count > MTU)
			{
				base.NetworkManager.LogWarning(
					$"[WebTransport] Datagram of {segment.Count} bytes exceeds MTU of {MTU}. Dropping.");
				return;
			}
			this.clientSocket.SendToServer(channelId, segment);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
		{
			SanitizeChannel(ref channelId);
			if (channelId == 1 && segment.Count > MTU)
			{
				base.NetworkManager.LogWarning(
					$"[WebTransport] Datagram of {segment.Count} bytes exceeds MTU of {MTU}. Dropping.");
				return;
			}
			this.serverSocket.SendToClient(channelId, segment, connectionId);
		}
		#endregion

		#region Configuration
		/// <summary>
		/// Sets the TLS certificate path.
		/// </summary>
		public void SetCertificatePath(string path)
		{
			certificatePath = path;
		}

		/// <summary>
		/// Sets the TLS private key path.
		/// </summary>
		public void SetPrivateKeyPath(string path)
		{
			privateKeyPath = path;
		}

		/// <summary>
		/// Sets the ALPN (Application-Layer Protocol Negotiation) string for QUIC.
		/// Defaults to "h3" for HTTP/3 (WebTransport). Some proxy deployments
		/// may require "wq" (WebTransport over QUIC without HTTP/3 framing).
		/// Only takes effect before the server is started.
		/// </summary>
		public void SetAlpn(string alpn)
		{
			this.serverSocket.Alpn = alpn;
		}

		/// <summary>
		/// How long in seconds until either the server or client socket must go without data before timeout.
		/// Values are configurable in the Unity inspector via <see cref="serverTimeout"/> and <see cref="clientTimeout"/>.
		/// </summary>
		public override float GetTimeout(bool asServer)
		{
			return asServer ? serverTimeout : clientTimeout;
		}

		/// <summary>
		/// Returns the maximum number of clients allowed to connect to the server.
		/// The value is clamped to the range [1, 100000] to prevent resource exhaustion.
		/// </summary>
		public override int GetMaximumClients()
		{
			return this.serverSocket.GetMaximumClients();
		}

		/// <summary>
		/// Sets the maximum number of clients allowed to connect to the server.
		/// Only takes effect when the server is not currently running.
		/// The value is clamped to the range [1, 100000] to prevent resource exhaustion.
		/// </summary>
		/// <param name="value">The maximum number of clients (clamped to [1, 100000]).</param>
		public override void SetMaximumClients(int value)
		{
			/* Security: clamp to valid range [1, 100000] to prevent
			 * resource exhaustion from unbounded input. */
			if (value < 1 || value > 100000)
			{
				base.NetworkManager.LogWarning(
					$"SetMaximumClients({value}) is outside allowed range [1, 100000]. Clamping to {Mathf.Clamp(value, 1, 100000)}.");
				value = Mathf.Clamp(value, 1, 100000);
			}

			if (this.serverSocket.GetConnectionState() != LocalConnectionState.Stopped)
				base.NetworkManager.LogWarning($"Cannot set maximum clients when server is running.");
			else
			{
				this.maximumClients = value;
				this.serverSocket.SetMaximumClients(value);
			}
		}

		/// <summary>
		/// Sets which address the client will connect to.
		/// </summary>
		public override void SetClientAddress(string address)
		{
			this.clientAddress = address;
		}

		/// <summary>
		/// Gets which address the client will connect to.
		/// </summary>
		public override string GetClientAddress()
		{
			return clientAddress;
		}

		/// <summary>
		/// Sets which address the server will bind to.
		/// </summary>
		public override void SetServerBindAddress(string address, IPAddressType addressType)
		{
			this.serverBindAddress = address;
		}

		/// <summary>
		/// Gets which address the server will bind to.
		/// </summary>
		public override string GetServerBindAddress(IPAddressType addressType)
		{
			return serverBindAddress;
		}

		/// <summary>
		/// Sets which port to use.
		/// </summary>
		public override void SetPort(ushort port)
		{
			this.port = port;
		}

		/// <summary>
		/// Gets which port to use.
		/// </summary>
		public override ushort GetPort()
		{
			return port;
		}
		#endregion

		#region Start and Stop
		public override bool StartConnection(bool server)
		{
			if (server)
				return startServer();
			else
				return startClient(clientAddress);
		}

		public override bool StopConnection(bool server)
		{
			if (server)
				return stopServer();
			else
				return stopClient();
		}

		public override bool StopConnection(int connectionId, bool immediately)
		{
			return StopRemoteConnection(connectionId, immediately);
		}

		public override void Shutdown()
		{
			StopConnection(false);
			StopConnection(true);
#if !UNITY_WEBGL || UNITY_EDITOR
			WebTransportNative.Deinitialize();
#endif
		}

		private bool startServer()
		{
			#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
				base.NetworkManager.LogError("[WebTransport] macOS standalone is not supported. Use Unity Editor on macOS or build from Windows/Linux. See FishMMO-WebTransport/README.md for manual build instructions.");
				return false;
			#endif
			this.serverSocket.Initialize(this, MTU, certificatePath, privateKeyPath);
			// ALPN (Application-Layer Protocol Negotiation) is hardcoded to "h3" for HTTP/3 (WebTransport) in
			// ServerSocket.DefaultAlpn. Override via ServerSocket.Alpn before calling StartConnection if needed.
			return this.serverSocket.StartConnection(serverBindAddress, port, maximumClients, useCustomCertificate: true);
		}

		private bool stopServer()
		{
			return this.serverSocket.StopConnection();
		}

		private bool startClient(string address)
		{
			#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
				base.NetworkManager.LogError("[WebTransport] macOS standalone is not supported. Use Unity Editor on macOS or build from Windows/Linux. See FishMMO-WebTransport/README.md for manual build instructions.");
				return false;
			#endif
			this.clientSocket.Initialize(this, MTU);
			return this.clientSocket.StartConnection(address, port, useTls: true);
		}

		private bool stopClient()
		{
			return this.clientSocket.StopConnection();
		}

		/// <summary>
		/// Stops a server connection for the given connection ID.
		/// Delegates to <c>this.serverSocket.StopConnection</c> because the transport
		/// passes <c>connectionId</c> overloads here for server-side disconnections.
		/// </summary>
		private bool StopRemoteConnection(int connectionId, bool immediately)
		{
			return this.serverSocket.StopConnection(connectionId, immediately);
		}
		#endregion

		#region Channels
		/// <summary>
		/// If channelId is invalid then channelId becomes forced to reliable.
		/// WebTransport uses stream=reliable (0), datagram=unreliable (1).
		/// </summary>
		private void SanitizeChannel(ref byte channelId)
		{
			if (channelId >= TransportManager.CHANNEL_COUNT)
			{
				base.NetworkManager.LogWarning($"Channel of {channelId} is out of range. Defaulting to reliable.");
				channelId = 0;
			}
		}

		/// <summary>
		/// Gets the MTU for a channel.
		/// </summary>
		public override int GetMTU(byte channel)
		{
			return MTU;
		}
		#endregion

	}
}