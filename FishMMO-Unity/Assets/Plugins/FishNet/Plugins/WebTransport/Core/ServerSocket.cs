using FishNet.Transporting.WebTransport.Native;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishNet.Transporting.WebTransport.Server
{
    /// <summary>
    /// Server-side socket wrapping the native WebTransport C library.
    /// Accepts QUIC connections, manages WebTransport sessions per client,
    /// and provides broadcast + unicast send capability.
    /// </summary>
    public class ServerSocket : CommonSocket
    {
        #region Public
        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        internal RemoteConnectionState GetConnectionState(int connectionId)
        {
            RemoteConnectionState state = _clients.Contains(connectionId)
                ? RemoteConnectionState.Started
                : RemoteConnectionState.Stopped;
            return state;
        }
        #endregion

        #region Private Configuration
        private ushort _port;
        private int _maximumClients;
        private int _mtu;
        private string _certificatePath;
        private string _privateKeyPath;
        #endregion

        #region Queues
        /// <summary>
        /// Outbound messages which need to be sent.
        /// </summary>
        private Queue<Packet> _outgoing = new Queue<Packet>();
        /// <summary>
        /// Connection IDs to disconnect next iteration.
        /// </summary>
        private List<int> _disconnectingNext = new List<int>();
        /// <summary>
        /// Connection IDs to disconnect immediately.
        /// </summary>
        private List<int> _disconnectingNow = new List<int>();
        #endregion

        /// <summary>
        /// Currently connected client IDs.
        /// Maps FishNet's int connection IDs to native ulong connection IDs.
        /// </summary>
        private HashSet<int> _clients = new HashSet<int>();
        private Dictionary<int, ulong> _idMapToNative = new Dictionary<int, ulong>();
        private Dictionary<ulong, int> _idMapFromNative = new Dictionary<ulong, int>();

        /// <summary>
        /// Monotonic connection ID counter.
        /// </summary>
        private int _nextConnectionId = 1;

        /// <summary>
        /// Native server handle from the C library.
        /// </summary>
        private SafeServerHandle _serverHandle;

        /// <summary>
        /// Thread-safe queue for events arriving from native callbacks.
        /// Drained on the Unity main thread during IterateIncoming.
        /// </summary>
        private ConcurrentQueue<Action> _incomingEvents = new ConcurrentQueue<Action>();

        /// <summary>
        /// Pinned delegate handles (prevent GC collection of callback delegates).
        /// </summary>
        private NativeCallbacks.ServerCallbacks _pinnedCallbacks;

        /// <summary>
        /// Address book: connectionId → remote address string.
        /// </summary>
        private Dictionary<int, string> _clientAddresses = new Dictionary<int, string>();

        /// <summary>
        /// Initialises this socket for use.
        /// </summary>
        internal void Initialize(Transport t, int mtu, string certPath, string keyPath)
        {
            base.Transport = t;
            _mtu = mtu;
            _certificatePath = certPath ?? "";
            _privateKeyPath = keyPath ?? "";
        }

        /// <summary>
        /// Starts the server — creates native listener and begins accepting connections.
        /// </summary>
        internal bool StartConnection(string bindAddress, ushort port, int maximumClients, bool useTls)
        {
            if (base.GetConnectionState() != LocalConnectionState.Stopped)
                return false;

            base.SetConnectionState(LocalConnectionState.Starting, true);

            WebTransportNative.EnsureInitialized();

            _port = port;
            _maximumClients = maximumClients;
            ResetQueues();

            // Pin callback delegates
            _pinnedCallbacks = new NativeCallbacks.ServerCallbacks
            {
                OnConnect = new NativeCallbacks.ServerConnectDelegate(HandleNativeConnect),
                OnDisconnect = new NativeCallbacks.ServerDisconnectDelegate(HandleNativeDisconnect),
                OnStreamData = new NativeCallbacks.ServerStreamDataDelegate(HandleNativeStreamData),
                OnDatagram = new NativeCallbacks.ServerDatagramDelegate(HandleNativeDatagram),
            };

            // Create native server
            _serverHandle = WebTransportNative.wt_server_create(
                useTls ? _certificatePath : null,
                useTls ? _privateKeyPath : null,
                "h3",           // ALPN for HTTP/3
                bindAddress,
                port,
                (uint)maximumClients,
                ref _pinnedCallbacks,
                IntPtr.Zero);

            if (_serverHandle == null || _serverHandle.IsInvalid)
            {
                base.SetConnectionState(LocalConnectionState.Stopped, true);
                return false;
            }

            int result = WebTransportNative.wt_server_start(_serverHandle);
            if (result != 0)
            {
                WebTransportNative.wt_server_destroy(_serverHandle);
                _serverHandle = null;
                base.SetConnectionState(LocalConnectionState.Stopped, true);
                return false;
            }

            base.SetConnectionState(LocalConnectionState.Started, true);
            return true;
        }

        /// <summary>
        /// Stops the server and disconnects all clients.
        /// </summary>
        internal bool StopConnection()
        {
            if (_serverHandle == null || _serverHandle.IsInvalid ||
                base.GetConnectionState() == LocalConnectionState.Stopped ||
                base.GetConnectionState() == LocalConnectionState.Stopping)
                return false;

            ResetQueues();
            base.SetConnectionState(LocalConnectionState.Stopping, true);

            WebTransportNative.wt_server_stop(_serverHandle);
            WebTransportNative.wt_server_destroy(_serverHandle);
            _serverHandle = null;

            base.SetConnectionState(LocalConnectionState.Stopped, true);
            return true;
        }

        /// <summary>
        /// Stops (kicks) a remote client.
        /// </summary>
        internal bool StopConnection(int connectionId, bool immediately)
        {
            if (_serverHandle == null || _serverHandle.IsInvalid ||
                base.GetConnectionState() != LocalConnectionState.Started)
                return false;

            if (!immediately)
                _disconnectingNext.Add(connectionId);
            else if (_idMapToNative.TryGetValue(connectionId, out ulong nativeId))
                WebTransportNative.wt_server_disconnect(_serverHandle, nativeId);

            return true;
        }

        /// <summary>
        /// Gets the remote address string for a connected client.
        /// </summary>
        internal string GetConnectionAddress(int connectionId)
        {
            if (_serverHandle == null || _serverHandle.IsInvalid)
                return string.Empty;

            if (_idMapToNative.TryGetValue(connectionId, out ulong nativeId))
            {
                IntPtr addrPtr = WebTransportNative.wt_server_get_client_address(
                    _serverHandle, nativeId);
                if (addrPtr != IntPtr.Zero)
                    return System.Runtime.InteropServices.Marshal.PtrToStringAnsi(addrPtr) ?? string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// Processes incoming events from the native library.
        /// Must be called each frame.
        /// </summary>
        internal void IterateIncoming()
        {
            if (_serverHandle == null || _serverHandle.IsInvalid)
                return;

            WebTransportNative.wt_server_poll(_serverHandle, 0);

            while (_incomingEvents.TryDequeue(out Action act))
            {
                act?.Invoke();
            }
        }

        /// <summary>
        /// Dequeues outgoing packets and processes pending disconnects.
        /// </summary>
        internal void IterateOutgoing()
        {
            if (_serverHandle == null || _serverHandle.IsInvalid)
                return;

            DequeueOutgoing();
            DequeueDisconnects();
        }

        /// <summary>
        /// Sends data to a single client or broadcasts to all (-1).
        /// Channel 0 = reliable (stream), Channel 1 = unreliable (datagram).
        /// </summary>
        internal void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            Send(ref _outgoing, channelId, segment, connectionId);
        }

        /// <summary>
        /// Returns the configured maximum number of clients.
        /// </summary>
        internal int GetMaximumClients()
        {
            return _maximumClients;
        }

        #region Private Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetQueues()
        {
            _clients.Clear();
            _idMapToNative.Clear();
            _idMapFromNative.Clear();
            _clientAddresses.Clear();
            _nextConnectionId = 1;
            base.ClearPacketQueue(ref _outgoing);
            _disconnectingNext.Clear();
            _disconnectingNow.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DequeueDisconnects()
        {
            int count = _disconnectingNow.Count;
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                    StopConnection(_disconnectingNow[i], true);
                _disconnectingNow.Clear();
            }

            count = _disconnectingNext.Count;
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                    _disconnectingNow.Add(_disconnectingNext[i]);
                _disconnectingNext.Clear();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DequeueOutgoing()
        {
            if (base.GetConnectionState() != LocalConnectionState.Started ||
                _serverHandle == null || _serverHandle.IsInvalid)
            {
                base.ClearPacketQueue(ref _outgoing);
                return;
            }

            int count = _outgoing.Count;
            for (int i = 0; i < count; i++)
            {
                Packet outgoing = _outgoing.Dequeue();
                int connectionId = outgoing.ConnectionId;

                if (connectionId == -1) // Broadcast
                {
                    foreach (int cid in _clients)
                    {
                        SendPacketToClient(outgoing, cid);
                    }
                }
                else // Unicast
                {
                    SendPacketToClient(outgoing, connectionId);
                }

                outgoing.Dispose();
            }
        }

        private void SendPacketToClient(Packet packet, int connectionId)
        {
            if (!_idMapToNative.TryGetValue(connectionId, out ulong nativeId))
                return;

            int result;
            if (packet.Channel == 1) // Unreliable → datagram
            {
                result = WebTransportNative.wt_server_send_datagram(
                    _serverHandle, nativeId, packet.Data, packet.Length);
            }
            else // Reliable → stream
            {
                result = WebTransportNative.wt_server_send_stream(
                    _serverHandle, nativeId, packet.Data, packet.Length);
            }

            if (result != 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[WebTransport Server] Send to {connectionId} failed with code {result}");
            }
        }

        #endregion

        #region Native Callbacks (invoked from QUIC worker threads)

        private void HandleNativeConnect(ulong nativeConnectionId, IntPtr remoteAddressPtr)
        {
            string remoteAddr = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(remoteAddressPtr) ?? "unknown";

            _incomingEvents.Enqueue(() =>
            {
                int fishNetId = _nextConnectionId++;
                _clients.Add(fishNetId);
                _idMapToNative[fishNetId] = nativeConnectionId;
                _idMapFromNative[nativeConnectionId] = fishNetId;
                _clientAddresses[fishNetId] = remoteAddr;

                Transport.HandleRemoteConnectionState(
                    new RemoteConnectionStateArgs(RemoteConnectionState.Started, fishNetId, Transport.Index));
            });
        }

        private void HandleNativeDisconnect(ulong nativeConnectionId, int errorCode)
        {
            _incomingEvents.Enqueue(() =>
            {
                if (_idMapFromNative.TryGetValue(nativeConnectionId, out int fishNetId))
                {
                    _clients.Remove(fishNetId);
                    _idMapToNative.Remove(fishNetId);
                    _idMapFromNative.Remove(nativeConnectionId);
                    _clientAddresses.Remove(fishNetId);

                    Transport.HandleRemoteConnectionState(
                        new RemoteConnectionStateArgs(RemoteConnectionState.Stopped, fishNetId, Transport.Index));
                }
            });
        }

        private void HandleNativeStreamData(ulong nativeConnectionId, ulong streamId, IntPtr dataPtr, int length)
        {
            byte[] buffer = new byte[length];
            System.Runtime.InteropServices.Marshal.Copy(dataPtr, buffer, 0, length);

            _incomingEvents.Enqueue(() =>
            {
                if (!_idMapFromNative.TryGetValue(nativeConnectionId, out int fishNetId))
                    return;

                // Channel 0 = reliable (stream)
                ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
                Transport.HandleServerReceivedDataArgs(
                    new ServerReceivedDataArgs(segment, Channel.Reliable, fishNetId, Transport.Index));
            });
        }

        private void HandleNativeDatagram(ulong nativeConnectionId, IntPtr dataPtr, int length)
        {
            byte[] buffer = new byte[length];
            System.Runtime.InteropServices.Marshal.Copy(dataPtr, buffer, 0, length);

            _incomingEvents.Enqueue(() =>
            {
                if (!_idMapFromNative.TryGetValue(nativeConnectionId, out int fishNetId))
                    return;

                // Channel 1 = unreliable (datagram)
                ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
                Transport.HandleServerReceivedDataArgs(
                    new ServerReceivedDataArgs(segment, Channel.Unreliable, fishNetId, Transport.Index));
            });
        }

        #endregion
    }
}