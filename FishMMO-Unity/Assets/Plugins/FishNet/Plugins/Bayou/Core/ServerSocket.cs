using JamesFrowen.SimpleWeb;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishNet.Transporting.Bayou.Server
{
    public class ServerSocket : CommonSocket
    {

        #region Public.
        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId to get ConnectionState for.</param>
        internal RemoteConnectionState GetConnectionState(int connectionId)
        {
            RemoteConnectionState state = _clients.Contains(connectionId) ? RemoteConnectionState.Started : RemoteConnectionState.Stopped;
            return state;
        }
        #endregion

        #region Private.
        #region Configuration.
        /// <summary>
        /// Port used by server.
        /// </summary>
        private ushort _port;
        /// <summary>
        /// Maximum number of allowed clients.
        /// </summary>
        private int _maximumClients;
        /// <summary>
        /// MTU sizes for each channel.
        /// </summary>
        private int _mtu;
        #endregion
        #region Queues.
        /// <summary>
        /// Outbound messages which need to be handled.
        /// </summary>
        private Queue<Packet> _outgoing = new Queue<Packet>();
        /// <summary>
        /// Ids to disconnect next iteration. This ensures data goes through to disconnecting remote connections. This may be removed in a later release.
        /// </summary>
        private List<int> _disconnectingNext = new List<int>();
        /// <summary>
        /// Ids to disconnect immediately.
        /// </summary>
        private List<int> _disconnectingNow = new List<int>();
        /// <summary>
        /// ConnectionEvents which need to be handled.
        /// </summary>
        private Queue<RemoteConnectionEvent> _remoteConnectionEvents = new Queue<RemoteConnectionEvent>();
        #endregion
        /// <summary>
        /// Currently connected clients.
        /// </summary>
        private List<int> _clients = new List<int>();
        /// <summary>
        /// Server socket manager.
        /// </summary>
        private SimpleWebServer _server;
        /// <summary>
        /// SslConfiguration to use.
        /// </summary>
        private SslConfiguration _sslConfiguration;
        #endregion

        ~ServerSocket()
        {
            StopConnection();
        }

        /// <summary>
        /// Initializes this for use.
        /// </summary>
        /// <param name="t"></param>
        internal void Initialize(Transport t, int unreliableMTU, SslConfiguration config)
        {
            _sslConfiguration = config;
            base.Transport = t;
            _mtu = unreliableMTU;
        }

        /// <summary>
        /// Threaded operation to process server actions.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Socket()
        {
            TcpConfig tcpConfig = new TcpConfig(false, 5000, 20000);
            SslConfig config;
            if (!_sslConfiguration.Enabled)
                config = new SslConfig();
            else
                config = new SslConfig(_sslConfiguration.Enabled, _sslConfiguration.CertificatePath, _sslConfiguration.CertificatePassword,
                    _sslConfiguration.SslProtocol);
            _server = new SimpleWebServer(5000, tcpConfig, _mtu, 5000, config);

            _server.onConnect += _server_onConnect;
            _server.onDisconnect += _server_onDisconnect;
            _server.onData += _server_onData;
            _server.onError += _server_onError;

            base.SetConnectionState(LocalConnectionState.Starting, true);
            _server.Start(_port);
            base.SetConnectionState(LocalConnectionState.Started, true);
        }

        /// <summary>
        /// Called when a client connection errors.
        /// </summary>
        private void _server_onError(int clientId, Exception arg2)
        {
            StopConnection(clientId, true);
        }

        /// <summary>
        /// Called when receiving data.
        /// </summary>
        private void _server_onData(int clientId, ArraySegment<byte> data)
        {
            if (_server == null || !_server.Active)
                return;

            Channel channel;
            ArraySegment<byte> segment = base.RemoveChannel(data, out channel);

            ServerReceivedDataArgs dataArgs = new ServerReceivedDataArgs(segment, channel, clientId, base.Transport.Index);
            base.Transport.HandleServerReceivedDataArgs(dataArgs);
        }

        /// <summary>
        /// Called when a client connects.
        /// </summary>
        private void _server_onConnect(int clientId)
        {
            if (_server == null || !_server.Active)
                return;

            if (_clients.Count >= _maximumClients)
                _server.KickClient(clientId);
            else
                _remoteConnectionEvents.Enqueue(new RemoteConnectionEvent(true, clientId));
        }

        /// <summary>
        /// Called when a client disconnects.
        /// </summary>
        private void _server_onDisconnect(int clientId)
        {
            StopConnection(clientId, true);
        }

        /// <summary>
        /// Gets the address of a remote connection Id.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns>Returns string.empty if Id is not found.</returns>
        internal string GetConnectionAddress(int connectionId)
        {
            if (_server == null || !_server.Active)
                return string.Empty;

            return _server.GetClientAddress(connectionId);
        }

        /// <summary>
        /// Starts the server.
        /// </summary>
        internal bool StartConnection(ushort port, int maximumClients)
        {
            if (base.GetConnectionState() != LocalConnectionState.Stopped)
                return false;

            base.SetConnectionState(LocalConnectionState.Starting, true);

            //Assign properties.
            _port = port;
            _maximumClients = maximumClients;
            ResetQueues();
            Socket();
            return true;
        }

        /// <summary>
        /// Stops the local socket.
        /// </summary>
        internal bool StopConnection()
        {
            if (_server == null || base.GetConnectionState() == LocalConnectionState.Stopped || base.GetConnectionState() == LocalConnectionState.Stopping)
                return false;

            ResetQueues();
            base.SetConnectionState(LocalConnectionState.Stopping, true);
            _server.Stop();
            base.SetConnectionState(LocalConnectionState.Stopped, true);

            return true;
        }

        /// <summary>
        /// Stops a remote client disconnecting the client from the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId of the client to disconnect.</param>
        internal bool StopConnection(int connectionId, bool immediately)
        {
            if (_server == null || base.GetConnectionState() != LocalConnectionState.Started)
                return false;

            //Don't disconnect immediately, wait until next command iteration.
            if (!immediately)
            {
                _disconnectingNext.Add(connectionId);
            }
            //Disconnect immediately.
            else
            {
                _server.KickClient(connectionId);
                _clients.Remove(connectionId);
                base.Transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Stopped, connectionId, base.Transport.Index));
            }

            return true;
        }

        /// <summary>
        /// Resets queues.
        /// </summary>
        private void ResetQueues()
        {
            _clients.Clear();
            base.ClearPacketQueue(ref _outgoing);
            _disconnectingNext.Clear();
            _disconnectingNow.Clear();
            _remoteConnectionEvents.Clear();
        }

        /// <summary>
        /// Dequeues and processes commands.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DequeueDisconnects()
        {
            int count;

            count = _disconnectingNow.Count;
            //If there are disconnect nows.
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                    StopConnection(_disconnectingNow[i], true);

                _disconnectingNow.Clear();
            }

            count = _disconnectingNext.Count;
            //If there are disconnect next.
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                    _disconnectingNow.Add(_disconnectingNext[i]);

                _disconnectingNext.Clear();
            }
        }

        /// <summary>
        /// Dequeues and processes outgoing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DequeueOutgoing()
        {
            if (base.GetConnectionState() != LocalConnectionState.Started || _server == null)
            {
                //Not started, clear outgoing.
                base.ClearPacketQueue(ref _outgoing);
            }
            else
            {
                int count = _outgoing.Count;
                for (int i = 0; i < count; i++)
                {
                    Packet outgoing = _outgoing.Dequeue();
                    int connectionId = outgoing.ConnectionId;
                    AddChannel(ref outgoing);
                    ArraySegment<byte> segment = outgoing.GetArraySegment();

                    //Send to all clients.
                    if (connectionId == -1)
                        _server.SendAll(_clients, segment);
                    //Send to one client.
                    else
                        _server.SendOne(connectionId, segment);

                    outgoing.Dispose();
                }
            }
        }

        /// <summary>
        /// Allows for Outgoing queue to be iterated.
        /// </summary>
        internal void IterateOutgoing()
        {
            if (_server == null)
                return;

            DequeueOutgoing();
            DequeueDisconnects();
        }

        /// <summary>
        /// Iterates the Incoming queue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void IterateIncoming()
        {
            if (_server == null)
                return;

            //Handle connection and disconnection events.
            while (_remoteConnectionEvents.Count > 0)
            {
                RemoteConnectionEvent connectionEvent = _remoteConnectionEvents.Dequeue();
                if (connectionEvent.Connected)
                    _clients.Add(connectionEvent.ConnectionId);
                RemoteConnectionState state = (connectionEvent.Connected) ? RemoteConnectionState.Started : RemoteConnectionState.Stopped;
                base.Transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(state, connectionEvent.ConnectionId, base.Transport.Index));
            }

            //Read data from clients.
            _server.ProcessMessageQueue();
        }

        /// <summary>
        /// Sends a packet to a single, or all clients.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            Send(ref _outgoing, channelId, segment, connectionId);
        }

        /// <summary>
        /// Returns the maximum number of clients allowed to connect to the server. If the transport does not support this method the value -1 is returned.
        /// </summary>
        /// <returns></returns>
        internal int GetMaximumClients()
        {
            return _maximumClients;
        }
    }
}
