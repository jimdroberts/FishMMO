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
		/// Initializes the client socket with the specified transport and MTU.
		/// Must be called before <see cref="StartConnection"/>.
		/// </summary>
		/// <param name="t">The parent transport instance.</param>
		/// <param name="mtu">The maximum transmission unit for datagrams.</param>
		internal void Initialize(Transport t, int mtu)
		{
			base.transport = t;
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
				try { act?.Invoke(); } catch (System.Exception ex) { UnityEngine.Debug.LogWarning($"[WebTransport Client] Drain exception: {ex.Message}"); }
			}
			stopGuard = 0;

			this.port = port;
			this.address = address;

			int slashIndex = address.IndexOf('/');
			serverName = slashIndex >= 0 ? address.Substring(0, slashIndex) : address;

			resetQueues();

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
                incomingEvents.Enqueue(() => transport.HandleClientReceivedDataArgs(
                    new ClientReceivedDataArgs(new ArraySegment<byte>(buf), Channel.Reliable, transport.Index)));
            };
            webglOnDatagram = (_, dataPtr, length) => {
                byte[] buf = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(dataPtr, buf, 0, length);
                incomingEvents.Enqueue(() => transport.HandleClientReceivedDataArgs(
                    new ClientReceivedDataArgs(new ArraySegment<byte>(buf), Channel.Unreliable, transport.Index)));
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
				OnConnect = new NativeCallbacks.ClientConnectDelegate(handleNativeConnect),
				OnDisconnect = new NativeCallbacks.ClientDisconnectDelegate(handleNativeDisconnect),
				OnStreamData = new NativeCallbacks.ClientStreamDataDelegate(handleNativeStreamData),
				OnDatagram = new NativeCallbacks.ClientDatagramDelegate(handleNativeDatagram),
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
				try { act?.Invoke(); } catch (System.Exception ex) { UnityEngine.Debug.LogWarning($"[WebTransport Client] Drain exception: {ex.Message}"); }
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
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void IterateOutgoing()
		{
#if !UNITY_WEBGL || UNITY_EDITOR
			if (clientHandle == null || clientHandle.IsInvalid)
			{
				ClearPacketQueue(outgoing);
				return;
			}
#endif

			dequeueOutgoing();
		}

		/// <summary>
		/// Queues data to be sent to the server.
		/// Channel 0 = reliable (stream), Channel 1 = unreliable (datagram).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
		private void dequeueOutgoing()
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
		private void resetQueues()
		{
			base.ClearPacketQueue(outgoing);
		}

		#region Native Callbacks (invoked from QUIC worker threads)

		/// <summary>
		/// Called by the native library when the client connection is established.
		/// Invoked from a QUIC worker thread — not the Unity main thread.
		/// Queues the connection-state transition for execution on the main thread.
		/// </summary>
		/// <param name="context">User-supplied context pointer.</param>
		private void handleNativeConnect(IntPtr context)
		{
			incomingEvents.Enqueue(() =>
			{
				base.SetConnectionState(LocalConnectionState.Started, false);
			});
		}

		/// <summary>
		/// Called by the native library when the client disconnects.
		/// Invoked from a QUIC worker thread — not the Unity main thread.
		/// Queues the disconnect cleanup for execution on the main thread.
		/// </summary>
		/// <param name="context">User-supplied context pointer.</param>
		/// <param name="errorCode">Zero for clean disconnect; negative for error.</param>
		private void handleNativeDisconnect(IntPtr context, int errorCode)
		{
			incomingEvents.Enqueue(() =>
			{
				if (errorCode != 0)
					UnityEngine.Debug.LogWarning($"[WebTransport Client] Disconnected: {WebTransportNative.ErrorString(errorCode)}");
				StopConnection();
			});
		}

		/// <summary>
		/// Called by the native library when reliable stream data arrives for the client.
		/// Invoked from a QUIC worker thread — not the Unity main thread.
		/// Validates length, copies data to unmanaged memory, then queues processing on the main thread.
		/// </summary>
		/// <param name="context">User-supplied context pointer.</param>
		/// <param name="streamId">The QUIC stream ID.</param>
		/// <param name="dataPtr">Pointer to the received data buffer.</param>
		/// <param name="length">Length of the received data in bytes.</param>
		private void handleNativeStreamData(IntPtr context, ulong streamId, IntPtr dataPtr, int length)
		{
			/* Security: reject invalid or oversized packets before allocating unmanaged memory. */
			if (length <= 0 || length > MaxPacketSize)
			{
				UnityEngine.Debug.LogWarning($"[WebTransport Client] Invalid stream data length {length}. Dropping.");
				return;
			}
			
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
					transport.HandleClientReceivedDataArgs(
						new ClientReceivedDataArgs(segment, Channel.Reliable, transport.Index));
				}
				finally
				{
					System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedCopy);
				}
			});
		}
		/// <summary>
		/// Called by the native library when unreliable datagram data arrives for the client.
		/// Invoked from a QUIC worker thread — not the Unity main thread.
		/// Validates length, copies data to unmanaged memory, then queues processing on the main thread.
		/// </summary>
		/// <param name="context">User-supplied context pointer.</param>
		/// <param name="dataPtr">Pointer to the received datagram buffer.</param>
		/// <param name="length">Length of the received datagram in bytes.</param>
		private void handleNativeDatagram(IntPtr context, IntPtr dataPtr, int length)
		{
			/* Security: reject invalid or oversized datagrams. */
			if (length <= 0 || length > mtu)
			{
				UnityEngine.Debug.LogWarning($"[WebTransport Client] Invalid datagram length {length}. Dropping.");
				return;
			}
			
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
					transport.HandleClientReceivedDataArgs(
						new ClientReceivedDataArgs(segment, Channel.Unreliable, transport.Index));
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