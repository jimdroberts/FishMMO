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
		private string address = string.Empty;
		private ushort port;
		private int mtu;
		private string serverName = string.Empty;
		#endregion

		#region Queues
		private Queue<Packet> outgoing = new Queue<Packet>();
		#endregion

		private SafeClientHandle clientHandle;
#if UNITY_WEBGL && !UNITY_EDITOR
        private int webglIndex = -1;
        /// <summary>
        /// Stored delegate instances for WebGL JS callbacks.
        /// Must be stored as fields to prevent GC collection — the JavaScript
        /// bridge holds references to these delegates and invokes them
        /// asynchronously. If the GC collects them, the browser will crash
        /// with an access violation on the next callback invocation.
        /// </summary>
        private Action<int> webglOnOpen;
        private Action<int> webglOnClose;
        private Action<int, IntPtr, int> webglOnStream;
        private Action<int, IntPtr, int> webglOnDatagram;
        private Action<int> webglOnError;
#endif
		/// <summary>
		/// Atomic guard to ensure StopConnection runs exactly once.
		/// </summary>
		private int stopGuard = 0;

		/// <summary>
		/// Thread-safe queue for events arriving from native callbacks.
		/// Drained on the Unity main thread during IterateIncoming.
		/// </summary>
		private ConcurrentQueue<Action> incomingEvents = new ConcurrentQueue<Action>();

		/// <summary>
		/// Pinned delegate handles (prevent GC collection of callback delegates).
		/// </summary>
		private NativeCallbacks.ClientCallbacks pinnedCallbacks;

		/// <summary>
		/// Initialises this socket for use.
		/// </summary>
		internal void Initialize(Transport t, int mtu)
		{
			base.Transport = t;
			this.mtu = mtu;
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
             * Drain any stale incoming events from a previous session first,
             * INVOKING each action so that unmanaged memory (Marshal.AllocHGlobal)
             * held by native callbacks is properly freed via their finally blocks.
             * Discarding without invoking would leak native heap memory. */
			while (incomingEvents.TryDequeue(out Action act))
			{
				try { act?.Invoke(); } catch { }
			}
			stopGuard = 0;

			this.port = port;
			this.address = address;

			int slashIndex = address.IndexOf('/');
			serverName = slashIndex >= 0 ? address.Substring(0, slashIndex) : address;

			ResetQueues();

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: use browser WebTransport API via JS bridge
            int slashIdx = address.IndexOf('/');
            string host = slashIdx >= 0 ? address.Substring(0, slashIdx) : address;
            string path = slashIdx >= 0 ? address.Substring(slashIdx) : "";
            string url = "https://" + host + ":" + port + path;

            /* Store delegates as instance fields — the JS bridge holds references
             * to these and invokes them asynchronously. If they were inline lambdas
             * the GC could collect them before the JS callbacks fire, causing an
             * access-violation crash in the browser. */
            webglOnOpen = (_) => { incomingEvents.Enqueue(() => SetConnectionState(LocalConnectionState.Started, false)); };
            webglOnClose = (_) => { incomingEvents.Enqueue(() => { UnityEngine.Debug.LogWarning("[WebTransport Client] WebGL connection closed."); SetConnectionState(LocalConnectionState.Stopped, false); }); };
            webglOnStream = (_, dataPtr, length) => {
                byte[] buf = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(dataPtr, buf, 0, length);
                incomingEvents.Enqueue(() => Transport.HandleClientReceivedDataArgs(
                    new ClientReceivedDataArgs(new ArraySegment<byte>(buf), Channel.Reliable, Transport.Index)));
            };
            webglOnDatagram = (_, dataPtr, length) => {
                byte[] buf = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(dataPtr, buf, 0, length);
                incomingEvents.Enqueue(() => Transport.HandleClientReceivedDataArgs(
                    new ClientReceivedDataArgs(new ArraySegment<byte>(buf), Channel.Unreliable, Transport.Index)));
            };
            webglOnError = (_) => { incomingEvents.Enqueue(() => { UnityEngine.Debug.LogError("[WebTransport Client] WebGL connection error."); SetConnectionState(LocalConnectionState.Stopped, false); }); };

            webglIndex = WebTransportJSLib.WTConnect(url,
                webglOnOpen, webglOnClose, webglOnStream, webglOnDatagram, webglOnError);
            if (webglIndex < 0)
            {
                base.SetConnectionState(LocalConnectionState.Stopped, false);
                return false;
            }
            return true;
#else
			WebTransportNative.EnsureInitialized();

			pinnedCallbacks = new NativeCallbacks.ClientCallbacks
			{
				OnConnect = new NativeCallbacks.ClientConnectDelegate(HandleNativeConnect),
				OnDisconnect = new NativeCallbacks.ClientDisconnectDelegate(HandleNativeDisconnect),
				OnStreamData = new NativeCallbacks.ClientStreamDataDelegate(HandleNativeStreamData),
				OnDatagram = new NativeCallbacks.ClientDatagramDelegate(HandleNativeDatagram),
			};

			clientHandle = WebTransportNative.wt_client_create(
				ref pinnedCallbacks,
				IntPtr.Zero);

			if (clientHandle == null || clientHandle.IsInvalid)
			{
				base.SetConnectionState(LocalConnectionState.Stopped, false);
				return false;
			}

			// Start async connection
			int result = WebTransportNative.wt_client_connect(
				clientHandle,
				serverName,
				address,
				port,
				useTls ? 1 : 0);

			if (result != 0)
			{
				WebTransportNative.wt_client_destroy(clientHandle);
				clientHandle = null;
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
			if (System.Threading.Interlocked.CompareExchange(ref stopGuard, 1, 0) != 0)
				return false;

			if (base.GetConnectionState() == LocalConnectionState.Stopped ||
				base.GetConnectionState() == LocalConnectionState.Stopping)
			{
				stopGuard = 0;
				return false;
			}

			/* Drain stale incoming events before shutdown.
             * Invoke (not discard) each action so that unmanaged memory
             * allocated in native callbacks is freed. The actions check
             * connection state before processing — they will skip
             * Transport callbacks since state is about to be Stopping. */
			while (incomingEvents.TryDequeue(out Action act))
			{
				try { act?.Invoke(); } catch { }
			}

			base.SetConnectionState(LocalConnectionState.Stopping, false);

#if UNITY_WEBGL && !UNITY_EDITOR
            if (webglIndex >= 0)
            {
                WebTransportJSLib.WTDisconnect(webglIndex);
                webglIndex = -1;
            }
#else
			if (clientHandle != null && !clientHandle.IsInvalid)
			{
				WebTransportNative.wt_client_disconnect(clientHandle);
				WebTransportNative.wt_client_destroy(clientHandle);
				clientHandle = null;
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
			if (clientHandle == null || clientHandle.IsInvalid)
				return;
			WebTransportNative.wt_client_poll(clientHandle, 0);
#endif
			while (incomingEvents.TryDequeue(out Action act))
			{
				try { act?.Invoke(); } catch (Exception e) { UnityEngine.Debug.LogException(e); }
			}
		}

		/// <summary>
		/// Dequeues and sends outgoing packets.
		/// </summary>
		internal void IterateOutgoing()
		{
			if (clientHandle == null || clientHandle.IsInvalid)
			{
				ClearPacketQueue(outgoing);
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
            if (webglIndex < 0) return;
#endif

			base.Send(outgoing, channelId, segment, -1);
		}

		/// <summary>
		/// Dequeues outgoing packets and sends via the native library.
		/// </summary>
		private void DequeueOutgoing()
		{
			if (base.GetConnectionState() != LocalConnectionState.Started)
			{
				ClearPacketQueue(outgoing);
				return;
			}

#if UNITY_WEBGL && !UNITY_EDITOR
            if (webglIndex < 0) return;
#else
			if (clientHandle == null || clientHandle.IsInvalid)
				return;
#endif

			int count = outgoing.Count;
			for (int i = 0; i < count; i++)
			{
				Packet outgoing = this.outgoing.Dequeue();

#if UNITY_WEBGL && !UNITY_EDITOR
                bool ok;
                if (outgoing.Channel == 1)
                    ok = WebTransportJSLib.WTSendDatagram(webglIndex, outgoing.Data, outgoing.Length);
                else
                    ok = WebTransportJSLib.WTSendStream(webglIndex, outgoing.Data, outgoing.Length);
                if (!ok)
                    UnityEngine.Debug.LogWarning("[WebTransport Client] Send failed (WebGL)");
#else
				int result;
				if (outgoing.Channel == 1)
					result = WebTransportNative.wt_client_send_datagram(clientHandle, outgoing.Data, outgoing.Length);
				else
					result = WebTransportNative.wt_client_send_stream(clientHandle, outgoing.Data, outgoing.Length);
				if (result != 0)
					UnityEngine.Debug.LogWarning($"[WebTransport Client] Send failed: {WebTransportNative.ErrorString(result)}");
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
			base.ClearPacketQueue(outgoing);
		}

		#region Native Callbacks (invoked from QUIC worker threads)

		private void HandleNativeConnect(IntPtr context)
		{
			incomingEvents.Enqueue(() =>
			{
				base.SetConnectionState(LocalConnectionState.Started, false);
			});
		}

		private void HandleNativeDisconnect(IntPtr context, int errorCode)
		{
			incomingEvents.Enqueue(() =>
			{
				if (errorCode != 0)
					UnityEngine.Debug.LogWarning($"[WebTransport Client] Disconnected: {WebTransportNative.ErrorString(errorCode)}");
				StopConnection();
			});
		}

		private void HandleNativeStreamData(IntPtr context, ulong streamId, IntPtr dataPtr, int length)
		{
			/* Copy data to unmanaged memory on the native callback thread.
             * Managed allocations (new byte[]) on non-Unity-main threads can
             * cause GC corruption on some Unity scripting backends. Using
             * Marshal.AllocHGlobal avoids all managed allocations here; the
             * byte[] allocation + Marshal.Copy happens on the main thread
             * inside the queued action. */
			IntPtr unmanagedCopy = System.Runtime.InteropServices.Marshal.AllocHGlobal(length);
			unsafe
			{
				System.Buffer.MemoryCopy((void*)dataPtr, (void*)unmanagedCopy, length, length);
			}

			incomingEvents.Enqueue(() =>
			{
				try
				{
					if (base.GetConnectionState() != LocalConnectionState.Started)
						return;

					byte[] buffer = new byte[length];
					System.Runtime.InteropServices.Marshal.Copy(unmanagedCopy, buffer, 0, length);

					// Channel 0 = reliable (stream)
					ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
					Transport.HandleClientReceivedDataArgs(
						new ClientReceivedDataArgs(segment, Channel.Reliable, Transport.Index));
				}
				finally
				{
					System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedCopy);
				}
			});
		}

		private void HandleNativeDatagram(IntPtr context, IntPtr dataPtr, int length)
		{
			/* Copy data to unmanaged memory on the native callback thread.
             * See HandleNativeStreamData for rationale. */
			IntPtr unmanagedCopy = System.Runtime.InteropServices.Marshal.AllocHGlobal(length);
			unsafe
			{
				System.Buffer.MemoryCopy((void*)dataPtr, (void*)unmanagedCopy, length, length);
			}

			incomingEvents.Enqueue(() =>
			{
				try
				{
					if (base.GetConnectionState() != LocalConnectionState.Started)
						return;

					byte[] buffer = new byte[length];
					System.Runtime.InteropServices.Marshal.Copy(unmanagedCopy, buffer, 0, length);

					// Channel 1 = unreliable (datagram)
					ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
					Transport.HandleClientReceivedDataArgs(
						new ClientReceivedDataArgs(segment, Channel.Unreliable, Transport.Index));
				}
				finally
				{
					System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedCopy);
				}
			});
		}

		#endregion
	}
}