using FishNet.Managing;
using FishNet.Managing.Transporting;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishNet.Transporting.WebTransport
{
    /// <remarks>
    /// Architecture: WebTransport is designed to work with or without NGINX.
    /// WebTransport runs over HTTP/3 (QUIC), which requires TLS 1.3 natively.
    ///
    /// Deployment modes:
    ///   - Behind NGINX: NGINX terminates QUIC on UDP/443, proxies to backend.
    ///     Set <c>_useTls = false</c> when NGINX handles TLS.
    ///   - Direct: Game server runs QUIC directly. Set <c>_useTls = true</c>
    ///     and provide certificate paths.
    ///
    /// Channel mapping (native, no suffix byte needed):
    ///   - Channel 0 (Reliable)   → WebTransport bidirectional streams
    ///   - Channel 1 (Unreliable) → QUIC DATAGRAM frames
    /// </remarks>
    [DisallowMultipleComponent]
    public class WebTransport : Transport
    {
        #region Serialized
        /// <summary>
        /// True to use TLS (QUIC always needs TLS, but NGINX may handle it).
        /// When behind NGINX that terminates QUIC, set to false.
        /// For direct server, set to true and provide cert paths.
        /// </summary>
        [Tooltip("True to enable TLS in the native QUIC stack. Set false when NGINX terminates QUIC.")]
        [SerializeField]
        private bool _useTls = true;

        /// <summary>
        /// Maximum transmission unit for unreliable datagrams.
        /// </summary>
        [Tooltip("Maximum transmission unit for the unreliable (datagram) channel.")]
        [Range(MINIMUM_MTU, MAXIMUM_MTU)]
        [SerializeField]
        private int _mtu = 1200;

        /// <summary>
        /// Address the server will bind to.
        /// </summary>
        [Tooltip("Server bind address. Use 'localhost' for co-located proxy, '0.0.0.0' for direct access.")]
        [SerializeField]
        private string _serverBindAddress = "localhost";

        /// <summary>
        /// Port to use for QUIC/WebTransport.
        /// </summary>
        [Tooltip("Port to use for WebTransport QUIC connections.")]
        [SerializeField]
        private ushort _port = 7770;

        /// <summary>
        /// Maximum number of players which may be connected at once.
        /// </summary>
        [Tooltip("Maximum number of concurrent clients.")]
        [Range(1, 4096)]
        [SerializeField]
        private int _maximumClients = 2000;

        /// <summary>
        /// Address the client will connect to.
        /// </summary>
        [Tooltip("Address the client will connect to.")]
        [SerializeField]
        private string _clientAddress = "localhost";

        /// <summary>
        /// Path to TLS certificate (PEM format). Used when _useTls is true.
        /// </summary>
        [Tooltip("Path to TLS certificate file (PEM). Only used when Use TLS is enabled.")]
        [SerializeField]
        private string _certificatePath = "";

        /// <summary>
        /// Path to TLS private key (PEM format). Used when _useTls is true.
        /// </summary>
        [Tooltip("Path to TLS private key file (PEM). Only used when Use TLS is enabled.")]
        [SerializeField]
        private string _privateKeyPath = "";
        #endregion

        #region Private
        /// <summary>
        /// Server socket and handler.
        /// </summary>
        private Server.ServerSocket _server = new Server.ServerSocket();
        /// <summary>
        /// Client socket and handler.
        /// </summary>
        private Client.ClientSocket _client = new Client.ClientSocket();
        #endregion

        #region Const
        private const int MINIMUM_MTU = 576;
        private const int MAXIMUM_MTU = 65527; // QUIC max datagram payload
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
            return _server.GetConnectionAddress(connectionId);
        }

        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;

        public override LocalConnectionState GetConnectionState(bool server)
        {
            return server ? _server.GetConnectionState() : _client.GetConnectionState();
        }

        public override RemoteConnectionState GetConnectionState(int connectionId)
        {
            return _server.GetConnectionState(connectionId);
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
                _server.IterateIncoming();
            else
                _client.IterateIncoming();
        }

        public override void IterateOutgoing(bool server)
        {
            if (server)
                _server.IterateOutgoing();
            else
                _client.IterateOutgoing();
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
            _client.SendToServer(channelId, segment);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            SanitizeChannel(ref channelId);
            _server.SendToClient(channelId, segment, connectionId);
        }
        #endregion

        #region Configuration
        /// <summary>
        /// Sets whether TLS is enabled for QUIC connections.
        /// </summary>
        public void SetUseTLS(bool useTls)
        {
            _useTls = useTls;
        }

        /// <summary>
        /// Sets the TLS certificate path.
        /// </summary>
        public void SetCertificatePath(string path)
        {
            _certificatePath = path;
        }

        /// <summary>
        /// Sets the TLS private key path.
        /// </summary>
        public void SetPrivateKeyPath(string path)
        {
            _privateKeyPath = path;
        }

        /// <summary>
        /// How long in seconds until either the server or client socket must go without data before timeout.
        /// </summary>
        public override float GetTimeout(bool asServer)
        {
            return asServer ? 120f : 20f;
        }

        /// <summary>
        /// Returns the maximum number of clients allowed to connect to the server.
        /// </summary>
        public override int GetMaximumClients()
        {
            return _server.GetMaximumClients();
        }

        /// <summary>
        /// Sets maximum number of clients allowed to connect to the server.
        /// </summary>
        public override void SetMaximumClients(int value)
        {
            if (_server.GetConnectionState() != LocalConnectionState.Stopped)
                base.NetworkManager.LogWarning($"Cannot set maximum clients when server is running.");
            else
                _maximumClients = value;
        }

        /// <summary>
        /// Sets which address the client will connect to.
        /// </summary>
        public override void SetClientAddress(string address)
        {
            _clientAddress = address;
        }

        /// <summary>
        /// Gets which address the client will connect to.
        /// </summary>
        public override string GetClientAddress()
        {
            return _clientAddress;
        }

        /// <summary>
        /// Sets which address the server will bind to.
        /// </summary>
        public override void SetServerBindAddress(string address, IPAddressType addressType)
        {
            _serverBindAddress = address;
        }

        /// <summary>
        /// Gets which address the server will bind to.
        /// </summary>
        public override string GetServerBindAddress(IPAddressType addressType)
        {
            return _serverBindAddress;
        }

        /// <summary>
        /// Sets which port to use.
        /// </summary>
        public override void SetPort(ushort port)
        {
            _port = port;
        }

        /// <summary>
        /// Gets which port to use.
        /// </summary>
        public override ushort GetPort()
        {
            return _port;
        }
        #endregion

        #region Start and Stop
        public override bool StartConnection(bool server)
        {
            if (server)
                return StartServer();
            else
                return StartClient(_clientAddress);
        }

        public override bool StopConnection(bool server)
        {
            if (server)
                return StopServer();
            else
                return StopClient();
        }

        public override bool StopConnection(int connectionId, bool immediately)
        {
            return StopClient(connectionId, immediately);
        }

        public override void Shutdown()
        {
            StopConnection(false);
            StopConnection(true);
        }

        private bool StartServer()
        {
            _server.Initialize(this, _mtu, _certificatePath, _privateKeyPath);
            return _server.StartConnection(_serverBindAddress, _port, _maximumClients, _useTls);
        }

        private bool StopServer()
        {
            return _server.StopConnection();
        }

        private bool StartClient(string address)
        {
            _client.Initialize(this, _mtu);
            return _client.StartConnection(address, _port, _useTls);
        }

        private bool StopClient()
        {
            return _client.StopConnection();
        }

        private bool StopClient(int connectionId, bool immediately)
        {
            return _server.StopConnection(connectionId, immediately);
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
            return _mtu;
        }
        #endregion

        #region Editor
#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_mtu < MINIMUM_MTU)
                _mtu = MINIMUM_MTU;
            else if (_mtu > MAXIMUM_MTU)
                _mtu = MAXIMUM_MTU;
        }
#endif
        #endregion
    }
}
