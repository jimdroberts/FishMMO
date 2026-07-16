using FishNet.Transporting.WebTransport.Native;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishNet.Transporting.WebTransport.Client
{
    /// <summary>
    /// Client-side socket wrapping the native WebTransport C library.
    /// Manages a single QUIC connection to the server with one WebTransport session.
    /// </summary>
    public class ClientSocket : CommonSocket
    {
        #region Private Configuration
        private string _address = string.Empty;
        private ushort _port;
        private int _mtu;
        /// <summary>
        /// Raw SNI hostname extracted from the address (before any '/' path separator).
        /// </summary>
        private string _serverName = string.Empty;
        #endregion

        #region Queues
        /// <summary>
        /// Outbound messages to be sent during IterateOutgoing.
        /// </summary>
        private Queue<Packet> _outgoing = new Queue<Packet>();
        #endregion

        /// <summary>
        /// Native client handle from the C library.
        /// </summary>
        private SafeClientHandle _clientHandle;

        /// <summary>
        /// Thread-safe queue for events arriving from native callbacks.
        /// Drained on the Unity main thread during IterateIncoming.
        /// </summary>
        private ConcurrentQueue<Action> _incomingEvents = new ConcurrentQueue<Action>();

        /// <summary>
        /// Pinned delegate handles (prevent GC collection of callback delegates).
        /// </summary>
        private NativeCallbacks.ClientCallbacks _pinnedCallbacks;

        /// <summary>
        /// Initialises this socket for use.
        /// </summary>
        internal void Initialize(Transport t, int mtu)
        {
            base.Transport = t;
            _mtu = mtu;
        }

        /// <summary>
        /// Starts the client connection to the specified address.
        /// </summary>
        internal bool StartConnection(string address, ushort port, bool useTls)
        {
            if (base.GetConnectionState() != LocalConnectionState.Stopped)
                return false;

            base.SetConnectionState(LocalConnectionState.Starting, false);

            _port = port;
            _address = address;

            // Extract SNI hostname (everything before the first '/')
            int slashIndex = address.IndexOf('/');
            _serverName = slashIndex >= 0 ? address.Substring(0, slashIndex) : address;

            ResetQueues();

            // Pin callback delegates
            _pinnedCallbacks = new NativeCallbacks.ClientCallbacks
            {
                OnConnect = new NativeCallbacks.ClientConnectDelegate(HandleNativeConnect),
                OnDisconnect = new NativeCallbacks.ClientDisconnectDelegate(HandleNativeDisconnect),
                OnStreamData = new NativeCallbacks.ClientStreamDataDelegate(HandleNativeStreamData),
                OnDatagram = new NativeCallbacks.ClientDatagramDelegate(HandleNativeDatagram),
            };

            // Create native client
            _clientHandle = WebTransportNative.wt_client_create(
                ref _pinnedCallbacks,
                IntPtr.Zero);

            if (_clientHandle == null || _clientHandle.IsInvalid)
            {
                base.SetConnectionState(LocalConnectionState.Stopped, false);
                return false;
            }

            // Start async connection
            int result = WebTransportNative.wt_client_connect(
                _clientHandle,
                _serverName,
                _address,
                _port,
                useTls ? 1 : 0);

            if (result != 0)
            {
                WebTransportNative.wt_client_destroy(_clientHandle);
                _clientHandle = null;
                base.SetConnectionState(LocalConnectionState.Stopped, false);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Stops the local client socket.
        /// </summary>
        internal bool StopConnection()
        {
            if (base.GetConnectionState() == LocalConnectionState.Stopped ||
                base.GetConnectionState() == LocalConnectionState.Stopping)
                return false;

            base.SetConnectionState(LocalConnectionState.Stopping, false);

            if (_clientHandle != null && !_clientHandle.IsInvalid)
            {
                WebTransportNative.wt_client_disconnect(_clientHandle);
                WebTransportNative.wt_client_destroy(_clientHandle);
                _clientHandle = null;
            }

            base.SetConnectionState(LocalConnectionState.Stopped, false);
            return true;
        }

        /// <summary>
        /// Processes incoming events from the native library.
        /// Must be called each frame from the Unity main thread.
        /// </summary>
        internal void IterateIncoming()
        {
            if (_clientHandle == null || _clientHandle.IsInvalid)
                return;

            // Poll the native library (non-blocking)
            WebTransportNative.wt_client_poll(_clientHandle, 0);

            // Drain thread-safe event queue onto the main thread
            while (_incomingEvents.TryDequeue(out Action act))
            {
                act?.Invoke();
            }
        }

        /// <summary>
        /// Dequeues and sends outgoing packets.
        /// </summary>
        internal void IterateOutgoing()
        {
            if (_clientHandle == null || _clientHandle.IsInvalid)
            {
                ClearPacketQueue(ref _outgoing);
                return;
            }

            DequeueOutgoing();
        }

        /// <summary>
        /// Queues data to be sent to the server.
        /// Channel 0 = reliable (stream), Channel 1 = unreliable (datagram).
        /// </summary>
        internal void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            if (base.GetConnectionState() != LocalConnectionState.Started)
                return;

            base.Send(ref _outgoing, channelId, segment, -1);
        }

        /// <summary>
        /// Dequeues outgoing packets and sends via the native library.
        /// </summary>
        private void DequeueOutgoing()
        {
            if (_clientHandle == null || _clientHandle.IsInvalid)
                return;

            int count = _outgoing.Count;
            for (int i = 0; i < count; i++)
            {
                Packet outgoing = _outgoing.Dequeue();

                int result;
                if (outgoing.Channel == 1) // Unreliable → datagram
                {
                    result = WebTransportNative.wt_client_send_datagram(
                        _clientHandle, outgoing.Data, outgoing.Length);
                }
                else // Reliable → stream
                {
                    result = WebTransportNative.wt_client_send_stream(
                        _clientHandle, outgoing.Data, outgoing.Length);
                }

                if (result != 0)
                {
                    // Send failed — connection likely lost
                    UnityEngine.Debug.LogWarning($"[WebTransport Client] Send failed with code {result}");
                }
                outgoing.Dispose();
            }
        }

        /// <summary>
        /// Resets the outgoing packet queue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetQueues()
        {
            base.ClearPacketQueue(ref _outgoing);
        }

        #region Native Callbacks (invoked from QUIC worker threads)

        private void HandleNativeConnect()
        {
            _incomingEvents.Enqueue(() =>
            {
                base.SetConnectionState(LocalConnectionState.Started, false);
            });
        }

        private void HandleNativeDisconnect(int errorCode)
        {
            _incomingEvents.Enqueue(() =>
            {
                StopConnection();
            });
        }

        private void HandleNativeStreamData(ulong streamId, IntPtr dataPtr, int length)
        {
            // Copy data from native memory before queueing
            byte[] buffer = new byte[length];
            System.Runtime.InteropServices.Marshal.Copy(dataPtr, buffer, 0, length);

            _incomingEvents.Enqueue(() =>
            {
                if (base.GetConnectionState() != LocalConnectionState.Started)
                    return;

                // Channel 0 = reliable (stream)
                ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
                Transport.HandleClientReceivedDataArgs(
                    new ClientReceivedDataArgs(segment, Channel.Reliable, Transport.Index));
            });
        }

        private void HandleNativeDatagram(IntPtr dataPtr, int length)
        {
            // Copy data from native memory before queueing
            byte[] buffer = new byte[length];
            System.Runtime.InteropServices.Marshal.Copy(dataPtr, buffer, 0, length);

            _incomingEvents.Enqueue(() =>
            {
                if (base.GetConnectionState() != LocalConnectionState.Started)
                    return;

                // Channel 1 = unreliable (datagram)
                ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
                Transport.HandleClientReceivedDataArgs(
                    new ClientReceivedDataArgs(segment, Channel.Unreliable, Transport.Index));
            });
        }

        #endregion
    }
}
