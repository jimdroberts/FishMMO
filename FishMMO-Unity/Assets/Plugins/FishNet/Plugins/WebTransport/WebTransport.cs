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
		/// <summary>
		/// Maximum application payload for one unreliable datagram.
		/// </summary>
		/// <remarks>
		/// QUIC guarantees a 1200-byte path (RFC 9000 §14), but that is the
		/// size of the whole UDP datagram, not of the payload we may put in
		/// it. Deduct the 1-RTT packet header, the DATAGRAM frame header, and
		/// the AEAD tag, then the HTTP/3 Datagram Quarter Stream ID that a
		/// browser session adds (RFC 9297 §2.1). Reporting the full 1200 here
		/// produced datagrams the transport could not actually send.
		/// </remarks>
		private const int DatagramMTU = 1150;

		/// <summary>
		/// Maximum application payload for one reliable message.
		/// </summary>
		/// <remarks>
		/// <para>The reliable channel runs over a QUIC stream, which has no MTU:
		/// QUIC segments and reassembles transparently and this transport
		/// length-delimits each message, so any value up to 64 KB is carried
		/// correctly. The limit here is not a protocol constraint.</para>
		/// <para>It is a memory one. FishNet allocates a <c>PacketBundle</c> per
		/// channel <i>per connection</i>, and each bundle holds buffers sized to
		/// this value — so raising it multiplies server memory by the client
		/// count. At 4000 clients every extra kilobyte here costs about 8 MB.
		/// The default stays near the datagram size for that reason; messages
		/// above it are split by FishNet and reassembled correctly, which costs
		/// a few header bytes and nothing else.</para>
		/// <para>Raise it with <see cref="SetReliableMTU"/> on deployments with
		/// few connections and large reliable payloads.</para>
		/// </remarks>
		private const int DefaultStreamMTU = 1200;

		/// <summary>
		/// Hard ceiling for <see cref="SetReliableMTU"/>. Matches the receive-side
		/// cap on both backends (WT_MAX_FRAMED_MESSAGE in the native library,
		/// _MAX_MESSAGE in the WebGL bridge) with room for the length prefix.
		/// </summary>
		private const int MaxStreamMTU = 65000;

		/// <summary>Current reliable-channel MTU. See <see cref="DefaultStreamMTU"/>.</summary>
		private int streamMTU = DefaultStreamMTU;

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
		/// Comma-separated SHA-256 fingerprints of the <i>remote</i> server's
		/// certificate that a WebGL client will accept in place of a publicly
		/// trusted chain.
		/// </summary>
		/// <remarks>
		/// <para><b>This is a client setting, not a server one.</b> The name follows
		/// the W3C option it feeds (<c>serverCertificateHashes</c>), which means
		/// "hashes of the server's certificate" — supplied by whoever is
		/// connecting. A server never reads this field; it presents its own
		/// certificate via <see cref="SetCertificatePath"/> and
		/// <see cref="SetPrivateKeyPath"/> exactly as before.</para>
		/// <para>It also only matters on WebGL. Native clients validate against the
		/// platform trust store and ignore it.</para>
		/// <para>Leave empty in production: browsers then require a normally trusted
		/// certificate, which is what you want. Set it only for development,
		/// where the browser would otherwise refuse a self-signed certificate
		/// and a WebGL build could not reach a local server at all. The pinned
		/// certificate must be ECDSA P-256 and valid for at most 14 days —
		/// a browser rule, not one this transport imposes.</para>
		/// </remarks>
		private string serverCertificateHashes = "";

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
		private void OnDestroy()
		{
			// Always tear down sockets. In the Editor, do NOT call wt_deinit() here —
			// Play Mode stop destroys this component while TimeManager may still tick once,
			// and Deinitialize() races msquic (native/mono crash after Stop Play while connected).
			try
			{
				ForceStopClient();
			}
			catch { /* best effort during teardown */ }
			try
			{
				StopConnection(false);
			}
			catch { /* best effort */ }
			try
			{
				StopConnection(true);
			}
			catch { /* best effort */ }

#if UNITY_EDITOR
			// Leave native library loaded for the next Play session (EnsureInitialized is idempotent).
#else
#if !UNITY_WEBGL
			try
			{
				WebTransportNative.Deinitialize();
			}
			catch { /* best effort */ }
#endif
#endif
		}
		#endregion

		#region ConnectionStates
		/// <summary>
		/// Returns the remote address string for a connected client.
		/// Delegates to <see cref="Server.ServerSocket.GetConnectionAddress"/>.
		/// </summary>
		/// <param name="connectionId">The FishNet connection ID.</param>
		/// <returns>The remote address as a string, or <see cref="string.Empty"/> if not found.</returns>
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
			if (channelId == 1 && segment.Count > DatagramMTU)
			{
				base.NetworkManager.LogWarning(
					$"[WebTransport] Datagram of {segment.Count} bytes exceeds the {DatagramMTU} byte limit. Dropping.");
				return;
			}
			this.clientSocket.SendToServer(channelId, segment);
		}

		/// <summary>
		/// Force the client socket to Stopped so a new <see cref="StartConnection"/> can run.
		/// Call when FishNet StopConnection hangs (observed in Unity Editor World->Scene hops).
		/// </summary>
		public void ForceStopClient()
		{
			this.clientSocket?.ForceStopAndReset();
		}

		/// <summary>
		/// Wire-send counters for diagnostics (handshake / create-account).
		/// </summary>
		public void GetClientWireStats(out long queued, out long sentOk, out long sentFail, out long sentBytes, out long dropNotStarted)
		{
			if (clientSocket != null)
				clientSocket.GetWireStats(out queued, out sentOk, out sentFail, out sentBytes, out dropNotStarted);
			else
				queued = sentOk = sentFail = sentBytes = dropNotStarted = 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
		{
			SanitizeChannel(ref channelId);
			if (channelId == 1 && segment.Count > DatagramMTU)
			{
				base.NetworkManager.LogWarning(
					$"[WebTransport] Datagram of {segment.Count} bytes exceeds the {DatagramMTU} byte limit. Dropping.");
				return;
			}
			this.serverSocket.SendToClient(channelId, segment, connectionId);
		}
		#endregion

		#region Configuration
		/// <summary>
		/// Sets the reliable-channel MTU. Must be called before the server or client
		/// starts, because FishNet reads it once when a connection is initialized.
		/// </summary>
		/// <param name="value">
		/// Bytes, clamped to 1200..65000. Larger values reduce message splitting but
		/// cost roughly <c>2 × value</c> of buffer per connection — see
		/// <see cref="DefaultStreamMTU"/> before raising it on a server with many
		/// clients.
		/// </param>
		public void SetReliableMTU(int value)
		{
			if (value < DefaultStreamMTU)
				value = DefaultStreamMTU;
			else if (value > MaxStreamMTU)
				value = MaxStreamMTU;

			this.streamMTU = value;
		}

		/// <summary>
		/// Pins the server certificates a WebGL client will accept, for development
		/// against a server whose certificate is not publicly trusted.
		/// </summary>
		/// <param name="hashes">
		/// Comma-separated SHA-256 fingerprints in hex (colons optional), or null to
		/// clear the pinning and require a publicly trusted certificate.
		/// </param>
		/// <returns><c>true</c> if every supplied fingerprint was well formed.</returns>
		/// <remarks>
		/// Has no effect outside WebGL builds: native clients validate against the
		/// platform trust store. Get a fingerprint with
		/// <c>openssl x509 -in cert.pem -noout -fingerprint -sha256</c>.
		/// </remarks>
		public bool SetServerCertificateHashes(string hashes)
		{
			if (string.IsNullOrWhiteSpace(hashes))
			{
				serverCertificateHashes = "";
				return true;
			}

			bool allValid = true;
			string[] parts = hashes.Split(',');
			for (int i = 0; i < parts.Length; i++)
			{
				string hex = parts[i].Trim().Replace(":", "");
				if (hex.Length == 0)
					continue;
				if (hex.Length != 64)
				{
					base.NetworkManager?.LogError(
						$"[WebTransport] Certificate hash \"{parts[i].Trim()}\" is {hex.Length} hex characters; a SHA-256 fingerprint is 64.");
					allValid = false;
					continue;
				}
				for (int c = 0; c < hex.Length; c++)
				{
					char ch = hex[c];
					bool isHex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
					if (!isHex)
					{
						base.NetworkManager?.LogError(
							$"[WebTransport] Certificate hash \"{parts[i].Trim()}\" contains a non-hex character '{ch}'.");
						allValid = false;
						break;
					}
				}
			}

			serverCertificateHashes = hashes;
			return allValid;
		}

		/// <summary>
		/// Sets the TLS certificate path. Validates the file exists on disk.
		/// </summary>
		/// <param name="path">Path to the PEM certificate file.</param>
		/// <returns><c>true</c> if the path is valid and the file exists; <c>false</c> otherwise.</returns>
		public bool SetCertificatePath(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				base.NetworkManager?.LogError("[WebTransport] Certificate path is null or empty.");
				return false;
			}
			if (!System.IO.File.Exists(path))
			{
				base.NetworkManager?.LogError($"[WebTransport] Certificate file not found: {path}");
				return false;
			}
			certificatePath = path;
			return true;
		}

		/// <summary>
		/// Sets the TLS private key path. Validates the file exists on disk.
		/// </summary>
		/// <param name="path">Path to the PEM private key file.</param>
		/// <returns><c>true</c> if the path is valid and the file exists; <c>false</c> otherwise.</returns>
		public bool SetPrivateKeyPath(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				base.NetworkManager?.LogError("[WebTransport] Private key path is null or empty.");
				return false;
			}
			if (!System.IO.File.Exists(path))
			{
				base.NetworkManager?.LogError($"[WebTransport] Private key file not found: {path}");
				return false;
			}
			privateKeyPath = path;
			return true;
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
		/// Sets the allowed origins for browser WebTransport CORS validation.
		/// Comma-separated list of Origin header values (e.g. "https://play.fishmmo.com").
		/// Empty or null means allow all origins (development/testing only).
		/// In production, set to a specific origin to prevent cross-site connections.
		/// Only takes effect before the server is started.
		/// </summary>
		public void SetAllowedOrigins(string origins)
		{
			this.serverSocket.AllowedOrigins = origins;
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
			// Range validation is duplicated in ServerSocket.SetMaximumClients.
			// Both layers validate defensively: WebTransport validates on the
			// public API surface (callable from Inspector/editor code), while
			// ServerSocket validates at the transport layer (callable from
			// FishNet internals). Keep both — they protect different entry points.
			if (value < 1 || value > 100000)
			{
				base.NetworkManager.LogWarning(
					$"SetMaximumClients({value}) is outside allowed range [1, 100000]. Clamping to {System.Math.Clamp(value, 1, 100000)}.");
				value = System.Math.Clamp(value, 1, 100000);
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
			if (string.IsNullOrWhiteSpace(address))
			{
				base.NetworkManager?.LogError("[WebTransport] Client address cannot be null or empty.");
				return;
			}
			this.clientAddress = address;
		}

		/// <summary>
		/// Gets which address the client will connect to.
		/// </summary>
		public override string GetClientAddress()
		{
			return this.clientAddress;
		}

		/// <summary>
		/// Sets which address the server will bind to.
		/// </summary>
		/// <remarks>
		/// The <paramref name="addressType"/> parameter is accepted for FishNet API compatibility
		/// but does not currently affect binding — the native WebTransport stack is IPv4-only.
		/// Dual-stack / IPv6 support is handled at a higher level by FishNetNetworkWrapper,
		/// which calls this method twice (once per IPAddressType) for each Multipass child
		/// transport, giving the illusion of dual-stack. When the native library gains IPv6
		/// support, this method should store both addresses and bind accordingly.
		/// </remarks>
		public override void SetServerBindAddress(string address, IPAddressType addressType)
		{
			if (string.IsNullOrWhiteSpace(address))
			{
				base.NetworkManager?.LogError("[WebTransport] Server bind address cannot be null or empty.");
				return;
			}
			this.serverBindAddress = address;
		}

		/// <summary>
		/// Gets which address the server will bind to.
		/// </summary>
		public override string GetServerBindAddress(IPAddressType addressType)
		{
			return this.serverBindAddress;
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
			return this.port;
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
			try { ForceStopClient(); } catch { /* best effort */ }
			try { StopConnection(false); } catch { /* best effort */ }
			try { StopConnection(true); } catch { /* best effort */ }
			// Only full process quit should deinit the native library. Editor Play Mode
			// stop uses OnDestroy without deinit (see OnDestroy).
#if !UNITY_WEBGL && !UNITY_EDITOR
			try { WebTransportNative.Deinitialize(); } catch { /* best effort */ }
#endif
		}

		private bool startServer()
		{
#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR
			if (!WebTransportNative.EnsureInitialized())
			{
				base.NetworkManager.LogError("[WebTransport] Native library not available on this platform. See FishMMO-WebTransport/README.md for build instructions.");
				return false;
			}
#endif
			this.serverSocket.Initialize(this, DatagramMTU, certificatePath, privateKeyPath);
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
#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR
			if (!WebTransportNative.EnsureInitialized())
			{
				base.NetworkManager.LogError("[WebTransport] Native library not available on this platform. See FishMMO-WebTransport/README.md for build instructions.");
				return false;
			}
#endif
			this.clientSocket.Initialize(this, DatagramMTU);
			this.clientSocket.SetServerCertificateHashes(this.serverCertificateHashes);
			return this.clientSocket.StartConnection(address, port, useTls: true);
		}

		private bool stopClient()
		{
			// Prefer normal stop; if it no-ops while still not Stopped, force reset.
			bool ok = this.clientSocket.StopConnection();
			if (this.clientSocket.GetConnectionState() != LocalConnectionState.Stopped)
			{
				this.clientSocket.ForceStopAndReset();
				ok = this.clientSocket.GetConnectionState() == LocalConnectionState.Stopped;
			}
			return ok;
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
		/// Gets the maximum payload FishNet may hand to a channel.
		/// </summary>
		/// <param name="channel">0 = reliable (stream), 1 = unreliable (datagram).</param>
		public override int GetMTU(byte channel)
		{
			return channel == 1 ? DatagramMTU : this.streamMTU;
		}
		#endregion
	}
}