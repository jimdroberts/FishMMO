using FishNet.Managing;
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
		/// Maps JS session index → managed socket. Looked up from static
		/// [AOT.MonoPInvokeCallback] entry points because IL2CPP cannot
		/// marshal instance methods or lambdas to native code.
		/// </summary>
		private static readonly Dictionary<int, ClientSocket> webglSockets =
			new Dictionary<int, ClientSocket>();

		/// <summary>
		/// Socket currently calling <see cref="StartConnection"/>, used if a
		/// JS callback races before the index is registered in <see cref="webglSockets"/>.
		/// </summary>
		private static ClientSocket webglPendingConnect;

		// Static delegate instances pinned for the process lifetime so the
		// GC does not collect them while the JS bridge holds references.
		private static readonly WTIndexCallback webglStaticOnOpen     = WebGlOnOpen;
		private static readonly WTIndexCallback webglStaticOnClose    = WebGlOnClose;
		private static readonly WTDataCallback  webglStaticOnStream   = WebGlOnStream;
		private static readonly WTDataCallback  webglStaticOnDatagram = WebGlOnDatagram;
		private static readonly WTIndexCallback webglStaticOnError    = WebGlOnError;
#endif

		/// <summary>
		/// Stored managed thread ID of the Unity main thread.
		/// Set during first initialization and used for thread-affinity assertions.
		/// </summary>
		private static int mainThreadId = -1;

		/// <summary>
		/// Atomic guard to ensure StopConnection runs exactly once.
		/// </summary>
		private int stopGuard = 0;

		/// <summary>
		/// Atomic counter tracking how many items are in <see cref="incomingEvents"/>.
		/// Used with <see cref="System.Threading.Interlocked"/> to prevent a TOCTOU
		/// race between <c>Count</c> check and <c>Enqueue</c> when native callbacks
		/// fire concurrently from QUIC worker threads.
		/// Int64 (long) — effectively overflow-proof; would require ~9 exabytes
		/// of queued events to wrap.
		/// </summary>
		private long incomingEventCount;

		/// <summary>
		/// Maximum number of queued incoming events to prevent native heap exhaustion
		/// from a flood of incoming packets.
		/// </summary>
		private const int MaxIncomingEvents = 10000;

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

			// Drain any stale incoming events from a previous session,
			// INVOKING each action so that unmanaged memory held by native
			// callbacks is properly freed via their finally blocks.
			while (incomingEvents.TryDequeue(out Action act))
			{
				System.Threading.Interlocked.Decrement(ref incomingEventCount);
				try { act?.Invoke(); } catch (System.Exception ex) { LogTransportWarning($"[WebTransport Client] Drain exception: {ex.Message}"); }
			}
			incomingEventCount = 0;

			// Assert we are on the Unity main thread.
			if (mainThreadId < 0)
				mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
			System.Diagnostics.Debug.Assert(
				System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadId,
				"[WebTransport Client] StartConnection must be called from the Unity main thread.");
			stopGuard = 0;

			this.port = port;
			this.address = address;

			int slashIndex = address.IndexOf('/');
			serverName = slashIndex >= 0 ? address.Substring(0, slashIndex) : address;

			ResetQueues();

#if UNITY_WEBGL && !UNITY_EDITOR
			// WebGL: browser WebTransport API via JS bridge.
			// IL2CPP forbids marshaling instance methods / lambdas to JS — only
			// static [AOT.MonoPInvokeCallback] methods may be passed to WTConnect.
			int slashIdx = address.IndexOf('/');
			string host = slashIdx >= 0 ? address.Substring(0, slashIdx) : address;
			string path = slashIdx >= 0 ? address.Substring(slashIdx) : "";
			string url = "https://" + host + ":" + port + path;

			webglPendingConnect = this;
			try
			{
				webglIndex = WebTransportJSLib.WTConnect(
					url,
					webglStaticOnOpen,
					webglStaticOnClose,
					webglStaticOnStream,
					webglStaticOnDatagram,
					webglStaticOnError);

				if (webglIndex < 0)
				{
					webglPendingConnect = null;
					base.SetConnectionState(LocalConnectionState.Stopped, false);
					return false;
				}

				lock (webglSockets)
					webglSockets[webglIndex] = this;
				webglPendingConnect = null;

				// Configure congestion threshold to avoid silent data loss under
				// game data rates (default 500, up from the previous hardcoded 80).
				WebTransportJSLib.WTSetStreamThreshold(webglIndex, 500);

				return true;
			}
			catch
			{
				webglPendingConnect = null;
				throw;
			}
#else
			if (!WebTransportNative.EnsureInitialized())
			{
				base.SetConnectionState(LocalConnectionState.Stopped, false);
				return false;
			}

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
			// Atomic guard — ensure StopConnection runs exactly once.
			if (System.Threading.Interlocked.CompareExchange(ref stopGuard, 1, 0) != 0)
				return false;

			if (base.GetConnectionState() == LocalConnectionState.Stopped ||
				base.GetConnectionState() == LocalConnectionState.Stopping)
			{
				stopGuard = 0;
				return false;
			}

			// Drain stale incoming events before shutdown, invoking each so
			// that unmanaged memory is freed.
			while (incomingEvents.TryDequeue(out Action act))
			{
				System.Threading.Interlocked.Decrement(ref incomingEventCount);
				try { act?.Invoke(); } catch (System.Exception ex) { LogTransportWarning($"[WebTransport Client] Drain exception: {ex.Message}"); }
			}
			incomingEventCount = 0;

			base.SetConnectionState(LocalConnectionState.Stopping, false);

#if UNITY_WEBGL && !UNITY_EDITOR
			if (webglIndex >= 0)
			{
				lock (webglSockets)
					webglSockets.Remove(webglIndex);
				if (ReferenceEquals(webglPendingConnect, this))
					webglPendingConnect = null;
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
#if !UNITY_WEBGL || UNITY_EDITOR
			if (clientHandle == null || clientHandle.IsInvalid)
				return;
			WebTransportNative.wt_client_poll(clientHandle, 0);
#endif
			while (incomingEvents.TryDequeue(out Action act))
			{
				System.Threading.Interlocked.Decrement(ref incomingEventCount);
				try { act?.Invoke(); } catch (Exception e) { LogTransportError(e.Message); }
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
			DequeueOutgoing();
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
		/// Dequeues all pending outgoing packets and sends them via the native library
		/// (standalone) or JS bridge (WebGL).  The connection state is checked before
		/// each send, and the queue is drained if the connection is not in the Started state.
		/// Each packet is disposed after sending, returning its buffer to <see cref="ByteArrayPool"/>.
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
				Packet pkt = this.outgoing.Dequeue();

#if UNITY_WEBGL && !UNITY_EDITOR
				bool ok;
				if (pkt.Channel == 1)
					ok = WebTransportJSLib.WTSendDatagram(webglIndex, pkt.Data, pkt.Length);
				else
					ok = WebTransportJSLib.WTSendStream(webglIndex, pkt.Data, pkt.Length);
				if (!ok)
					LogTransportWarning("[WebTransport Client] Send failed (WebGL)");
#else
				int result;
				if (pkt.Channel == 1)
					result = WebTransportNative.wt_client_send_datagram(this.clientHandle, pkt.Data, pkt.Length);
				else
					result = WebTransportNative.wt_client_send_stream(this.clientHandle, pkt.Data, pkt.Length);
				if (result != 0)
					transport.NetworkManager?.LogWarning($"[WebTransport Client] Send failed: {WebTransportNative.ErrorString(result)}");
#endif
				pkt.Dispose();
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

#if UNITY_WEBGL && !UNITY_EDITOR
		#region WebGL static callbacks (IL2CPP / jslib)

		/// <summary>
		/// Resolves the socket for a given JS session index.
		/// Handles the race where a callback fires before StartConnection
		/// finishes registering the index in <see cref="webglSockets"/>.
		/// </summary>
		private static bool TryGetWebGlSocket(int index, out ClientSocket socket)
		{
			lock (webglSockets)
			{
				if (index >= 0 && webglSockets.TryGetValue(index, out socket))
					return true;
			}
			// Race: callback fired before StartConnection registered the index.
			socket = webglPendingConnect;
			return socket != null;
		}

		[AOT.MonoPInvokeCallback(typeof(WTIndexCallback))]
		private static void WebGlOnOpen(int index)
		{
			if (!TryGetWebGlSocket(index, out ClientSocket socket))
				return;
			socket.incomingEvents.Enqueue(() =>
				socket.SetConnectionState(LocalConnectionState.Started, false));
		}

		[AOT.MonoPInvokeCallback(typeof(WTIndexCallback))]
		private static void WebGlOnClose(int index)
		{
			if (!TryGetWebGlSocket(index, out ClientSocket socket))
				return;
			socket.incomingEvents.Enqueue(() =>
			{
				socket.LogTransportWarning("[WebTransport Client] WebGL connection closed.");
				socket.SetConnectionState(LocalConnectionState.Stopped, false);
			});
		}

		[AOT.MonoPInvokeCallback(typeof(WTDataCallback))]
		private static void WebGlOnStream(int index, IntPtr dataPtr, int length)
		{
			if (!TryGetWebGlSocket(index, out ClientSocket socket))
				return;
			// Security: reject invalid or oversized packets.
			if (length <= 0 || length > MaxPacketSize)
			{
				socket.LogTransportWarning($"[WebTransport Client] Invalid stream data length {length}. Dropping.");
				return;
			}
			// Backpressure: drop if event queue is saturated.
			if (System.Threading.Interlocked.Increment(ref socket.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref socket.incomingEventCount);
				socket.LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping stream data.");
				return;
			}
			byte[] buf = new byte[length];
			System.Runtime.InteropServices.Marshal.Copy(dataPtr, buf, 0, length);
			socket.incomingEvents.Enqueue(() =>
				socket.transport.HandleClientReceivedDataArgs(
					new ClientReceivedDataArgs(new ArraySegment<byte>(buf), Channel.Reliable, socket.transport.Index)));
		}

		[AOT.MonoPInvokeCallback(typeof(WTDataCallback))]
		private static void WebGlOnDatagram(int index, IntPtr dataPtr, int length)
		{
			if (!TryGetWebGlSocket(index, out ClientSocket socket))
				return;
			if (length <= 0 || length > socket.mtu)
			{
				socket.LogTransportWarning($"[WebTransport Client] Invalid datagram length {length}. Dropping.");
				return;
			}
			if (System.Threading.Interlocked.Increment(ref socket.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref socket.incomingEventCount);
				socket.LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping datagram.");
				return;
			}
			byte[] buf = new byte[length];
			System.Runtime.InteropServices.Marshal.Copy(dataPtr, buf, 0, length);
			socket.incomingEvents.Enqueue(() =>
				socket.transport.HandleClientReceivedDataArgs(
					new ClientReceivedDataArgs(new ArraySegment<byte>(buf), Channel.Unreliable, socket.transport.Index)));
		}

		[AOT.MonoPInvokeCallback(typeof(WTIndexCallback))]
		private static void WebGlOnError(int index)
		{
			if (!TryGetWebGlSocket(index, out ClientSocket socket))
				return;
			socket.incomingEvents.Enqueue(() =>
			{
				socket.LogTransportError("[WebTransport Client] WebGL connection error.");
				socket.SetConnectionState(LocalConnectionState.Stopped, false);
			});
		}

		#endregion
#endif

		#region Native Callbacks (invoked from QUIC worker threads)

		/// <summary>
		/// Called by the native library when the client connection is established.
		/// Invoked from a QUIC worker thread — not the Unity main thread.
		/// Queues the connection-state transition for execution on the main thread.
		/// </summary>
		private void HandleNativeConnect(IntPtr context)
		{
			if (System.Threading.Interlocked.Increment(ref incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref incomingEventCount);
				LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping connect event.");
				return;
			}
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
		private void HandleNativeDisconnect(IntPtr context, int errorCode)
		{
			if (System.Threading.Interlocked.Increment(ref incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref incomingEventCount);
				LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping disconnect event.");
				return;
			}
			incomingEvents.Enqueue(() =>
			{
				if (errorCode != 0)
					LogTransportWarning("[WebTransport Client] Disconnected: " + WebTransportNative.ErrorString(errorCode));
				StopConnection();
			});
		}

		/// <summary>
		/// Called by the native library when reliable stream data arrives for the client.
		/// Invoked from a QUIC worker thread — not the Unity main thread.
		/// Copies data to unmanaged memory to avoid managed allocations on the callback thread.
		/// </summary>
		private void HandleNativeStreamData(IntPtr context, ulong streamId, IntPtr dataPtr, int length)
		{
			if (length <= 0 || length > MaxPacketSize)
			{
				LogTransportWarning($"[WebTransport Client] Invalid stream data length {length}. Dropping.");
				return;
			}

			// Copy to unmanaged memory on the callback thread — managed
			// allocations on QUIC threads can corrupt the IL2CPP GC.
			IntPtr unmanagedCopy = System.Runtime.InteropServices.Marshal.AllocHGlobal(length);
			unsafe
			{
				System.Buffer.MemoryCopy((void*)dataPtr, (void*)unmanagedCopy, length, length);
			}

			if (System.Threading.Interlocked.Increment(ref incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref incomingEventCount);
				LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping stream data.");
				System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedCopy);
				return;
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
		/// </summary>
		private void HandleNativeDatagram(IntPtr context, IntPtr dataPtr, int length)
		{
			if (length <= 0 || length > this.mtu)
			{
				LogTransportWarning($"[WebTransport Client] Invalid datagram length {length}. Dropping.");
				return;
			}

			IntPtr unmanagedCopy = System.Runtime.InteropServices.Marshal.AllocHGlobal(length);
			unsafe
			{
				System.Buffer.MemoryCopy((void*)dataPtr, (void*)unmanagedCopy, length, length);
			}

			if (System.Threading.Interlocked.Increment(ref incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref incomingEventCount);
				LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping datagram.");
				System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedCopy);
				return;
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
