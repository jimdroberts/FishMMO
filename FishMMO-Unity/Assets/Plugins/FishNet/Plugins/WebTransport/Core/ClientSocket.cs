using FishNet.Transporting.WebTransport.Native;
using FishNet.Transporting.WebTransport.WebGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishNet.Transporting.WebTransport.Client
{
    public class ClientSocket : CommonSocket
    {
        #region Private Configuration
        private string _address = string.Empty;
        private ushort _port;
        private int _mtu;
        private string _serverName = string.Empty;
        #endregion

        #region Queues
        private Queue<Packet> _outgoing = new Queue<Packet>();
        #endregion

        private SafeClientHandle _clientHandle;
#if UNITY_WEBGL && !UNITY_EDITOR
        private int _webglIndex = -1;
#endif
        /// <summary>
        /// Atomic guard to ensure StopConnection runs exactly once.
        /// </summary>
        private int _stopGuard = 0;

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

            /* Reset stop guard to allow StopConnection on this new session.
             * Drain any stale incoming events from a previous session first
             * to prevent callbacks from referencing freed resources. */
            while (_incomingEvents.TryDequeue(out _)) { }
            _stopGuard = 0;

            _port = port;
            _address = address;

            int slashIndex = address.IndexOf('/');
            _serverName = slashIndex >= 0 ? address.Substring(0, slashIndex) : address;

            ResetQueues();

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: use browser WebTransport API via JS bridge
            int slashIdx = address.IndexOf('/');
            string host = slashIdx >= 0 ? address.Substring(0, slashIdx) : address;
            string path = slashIdx >= 0 ? address.Substring(slashIdx) : "";
            string url = "https://" + host + ":" + port + path;
            _webglIndex = WebTransportJSLib.WTConnect(url,
                (_) => { _incomingEvents.Enqueue(() => SetConnectionState(LocalConnectionState.Started, false)); },
                (_) => { _incomingEvents.Enqueue(() => { SetConnectionState(LocalConnectionState.Stopped, false); }); },
                (_, dataPtr, length) => {
                    byte[] buf = new byte[length];
                    System.Runtime.InteropServices.Marshal.Copy(dataPtr, buf, 0, length);
                    _incomingEvents.Enqueue(() => Transport.HandleClientReceivedDataArgs(
                        new ClientReceivedDataArgs(new ArraySegment<byte>(buf), Channel.Reliable, Transport.Index)));
                },
                (_, dataPtr, length) => {
                    byte[] buf = new byte[length];
                    System.Runtime.InteropServices.Marshal.Copy(dataPtr, buf, 0, length);
                    _incomingEvents.Enqueue(() => Transport.HandleClientReceivedDataArgs(
                        new ClientReceivedDataArgs(new ArraySegment<byte>(buf), Channel.Unreliable, Transport.Index)));
                },
                (_) => { _incomingEvents.Enqueue(() => SetConnectionState(LocalConnectionState.Stopped, false)); }
            );
            if (_webglIndex < 0)
            {
                base.SetConnectionState(LocalConnectionState.Stopped, false);
                return false;
            }
            return true;
#else
            WebTransportNative.EnsureInitialized();

            _pinnedCallbacks = new NativeCallbacks.ClientCallbacks
            {
                OnConnect = new NativeCallbacks.ClientConnectDelegate(HandleNativeConnect),
                OnDisconnect = new NativeCallbacks.ClientDisconnectDelegate(HandleNativeDisconnect),
                OnStreamData = new NativeCallbacks.ClientStreamDataDelegate(HandleNativeStreamData),
                OnDatagram = new NativeCallbacks.ClientDatagramDelegate(HandleNativeDatagram),
            };

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
#endif
        }

        internal bool StopConnection()
        {
            /* Atomic guard — ensure StopConnection runs exactly once,
             * even if called from both a native callback and user code. */
            if (System.Threading.Interlocked.CompareExchange(ref _stopGuard, 1, 0) != 0)
                return false;

            if (base.GetConnectionState() == LocalConnectionState.Stopped ||
                base.GetConnectionState() == LocalConnectionState.Stopping)
            {
                _stopGuard = 0;
                return false;
            }

            /* Drain stale incoming events before shutdown. */
            while (_incomingEvents.TryDequeue(out _)) { }

            base.SetConnectionState(LocalConnectionState.Stopping, false);

#if UNITY_WEBGL && !UNITY_EDITOR
            if (_webglIndex >= 0)
            {
                WebTransportJSLib.WTDisconnect(_webglIndex);
                _webglIndex = -1;
            }
#else
            if (_clientHandle != null && !_clientHandle.IsInvalid)
            {
                WebTransportNative.wt_client_disconnect(_clientHandle);
                WebTransportNative.wt_client_destroy(_clientHandle);
                _clientHandle = null;
            }
#endif

            base.SetConnectionState(LocalConnectionState.Stopped, false);
            return true;
        }

        /// <summary>
        /// Processes incoming events from the native library.
        /// Must be called each frame from the Unity main thread.
        /// </summary>
        internal void IterateIncoming()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: no poll needed, callbacks fire via JS
#else
            if (_clientHandle == null || _clientHandle.IsInvalid)
                return;
            WebTransportNative.wt_client_poll(_clientHandle, 0);
#endif
            while (_incomingEvents.TryDequeue(out Action act))
            {
                try { act?.Invoke(); } catch (Exception e) { UnityEngine.Debug.LogException(e); }
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

#if UNITY_WEBGL && !UNITY_EDITOR
            if (_webglIndex < 0) return;
#endif

            base.Send(_outgoing, channelId, segment, -1);
        }

        /// <summary>
        /// Dequeues outgoing packets and sends via the native library.
        /// </summary>
        private void DequeueOutgoing()
        {
            if (base.GetConnectionState() != LocalConnectionState.Started)
            {
                ClearPacketQueue(ref _outgoing);
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (_webglIndex < 0) return;
#else
            if (_clientHandle == null || _clientHandle.IsInvalid)
                return;
#endif

            int count = _outgoing.Count;
            for (int i = 0; i < count; i++)
            {
                Packet outgoing = _outgoing.Dequeue();
                bool ok;

#if UNITY_WEBGL && !UNITY_EDITOR
                if (outgoing.Channel == 1)
                    ok = WebTransportJSLib.WTSendDatagram(_webglIndex, outgoing.Data, outgoing.Length);
                else
                    ok = WebTransportJSLib.WTSendStream(_webglIndex, outgoing.Data, outgoing.Length);
                if (!ok)
                    UnityEngine.Debug.LogWarning("[WebTransport Client] Send failed (WebGL)");
#else
                int result;
                if (outgoing.Channel == 1)
                    result = WebTransportNative.wt_client_send_datagram(_clientHandle, outgoing.Data, outgoing.Length);
                else
                    result = WebTransportNative.wt_client_send_stream(_clientHandle, outgoing.Data, outgoing.Length);
                if (result != 0)
                    UnityEngine.Debug.LogWarning($"[WebTransport Client] Send failed with code {result}");
#endif
                outgoing.Dispose();
            }
        }

        /// <summary>
        /// Resets the outgoing packet queue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetQueues()
        {
            base.ClearPacketQueue(_outgoing);
        }

        #region Native Callbacks (invoked from QUIC worker threads)

        private void HandleNativeConnect(IntPtr context)
        {
            _incomingEvents.Enqueue(() =>
            {
                base.SetConnectionState(LocalConnectionState.Started, false);
            });
        }

        private void HandleNativeDisconnect(IntPtr context, int errorCode)
        {
            _incomingEvents.Enqueue(() =>
            {
                if (errorCode != 0)
                    UnityEngine.Debug.LogWarning($"[WebTransport Client] Disconnected with error code {errorCode}");
                StopConnection();
            });
        }

        private void HandleNativeStreamData(IntPtr context, ulong streamId, IntPtr dataPtr, int length)
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

        private void HandleNativeDatagram(IntPtr context, IntPtr dataPtr, int length)
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
