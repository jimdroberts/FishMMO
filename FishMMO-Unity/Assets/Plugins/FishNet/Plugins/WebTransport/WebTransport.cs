using FishNet.Managing;
using FishNet.Transporting.WebTransport.Native;
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
        private const int _mtu = 1200;

        /// <summary>Server bind address. Set at startup from .cfg file.</summary>
        private string _serverBindAddress = "127.0.0.1";

        /// <summary>Server port. Set at startup from .cfg file.</summary>
        private ushort _port;

        /// <summary>Max concurrent clients. Set at startup from .cfg file.</summary>
        private int _maximumClients = 100;

        /// <summary>Client connect hostname. Set at startup from Constants.GameHost.</summary>
        private string _clientAddress = "game.fishmmo.com";

        /// <summary>TLS certificate PEM path. Set at startup from .cfg file.</summary>
        private string _certificatePath = "";

        /// <summary>TLS private key PEM path. Set at startup from .cfg file.</summary>
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
            if (channelId == 1 && segment.Count > _mtu)
            {
                base.NetworkManager.LogWarning(
                    $"[WebTransport] Datagram of {segment.Count} bytes exceeds MTU of {_mtu}. Dropping.");
                return;
            }
            _client.SendToServer(channelId, segment);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            SanitizeChannel(ref channelId);
            if (channelId == 1 && segment.Count > _mtu)
            {
                base.NetworkManager.LogWarning(
                    $"[WebTransport] Datagram of {segment.Count} bytes exceeds MTU of {_mtu}. Dropping.");
                return;
            }
            _server.SendToClient(channelId, segment, connectionId);
        }
        #endregion

        #region Configuration
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
#if !UNITY_WEBGL || UNITY_EDITOR
            WebTransportNative.Deinitialize();
#endif
        }

        private bool StartServer()
        {
            _server.Initialize(this, _mtu, _certificatePath, _privateKeyPath);
            return _server.StartConnection(_serverBindAddress, _port, _maximumClients, useCustomCertificate: true);
        }

        private bool StopServer()
        {
            return _server.StopConnection();
        }

        private bool StartClient(string address)
        {
            _client.Initialize(this, _mtu);
            return _client.StartConnection(address, _port, useTls: true);
        }

        private bool StopClient()
        {
            return _client.StopConnection();
        }

        /// <summary>
        /// Stops a server connection for the given connection ID.
        /// Despite the "StopClient" name required by the transport interface,
        /// this method delegates to <c>_server.StopConnection</c> because FishNet
        /// passes <c>connectionId</c> overloads here for server-side disconnections.
        /// </summary>
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

    }
}
