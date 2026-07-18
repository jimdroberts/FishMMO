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
			RemoteConnectionState state = clients.Contains(connectionId)
				? RemoteConnectionState.Started
				: RemoteConnectionState.Stopped;
			return state;
		}
		#endregion

		#region Private Configuration
		private ushort port;
		private int maximumClients;
		private int mtu;
		private string certificatePath;
		private string privateKeyPath;
		#endregion

		#region Queues
		/// <summary>
		/// Outbound messages which need to be sent.
		/// </summary>
		private Queue<Packet> outgoing = new Queue<Packet>();
		/// <summary>
		/// Connection IDs to disconnect next iteration.
		/// </summary>
		private HashSet<int> disconnectingNext = new HashSet<int>();
		#endregion

		/// <summary>
		/// Currently connected client IDs.
		/// Maps FishNet's int connection IDs to native ulong connection IDs.
		/// </summary>
		private HashSet<int> clients = new HashSet<int>();
		private Dictionary<int, ulong> idMapToNative = new Dictionary<int, ulong>();
		private Dictionary<ulong, int> idMapFromNative = new Dictionary<ulong, int>();

		/// <summary>
		/// Monotonic connection ID counter.
		/// </summary>
		private int nextConnectionId = 1;

		/// <summary>
		/// Native server handle from the C library.
		/// </summary>
		private SafeServerHandle serverHandle;

		/// <summary>
		/// Thread-safe queue for events arriving from native callbacks.
		/// Drained on the Unity main thread during IterateIncoming.
		/// </summary>
		private ConcurrentQueue<Action> incomingEvents = new ConcurrentQueue<Action>();

		/// <summary>
		/// Atomic guard to ensure StopConnection runs exactly once,
		/// even if called from both a native callback and user code.
		/// </summary>
		private int stopGuard = 0;

		/// <summary>
		/// Pinned delegate handles (prevent GC collection of callback delegates).
		/// </summary>
		private NativeCallbacks.ServerCallbacks pinnedCallbacks;

		/// <summary>
		/// Address book: connectionId → remote address string.
		/// </summary>
		private Dictionary<int, string> clientAddresses = new Dictionary<int, string>();

		/// <summary>
		/// Initializes the server socket with the specified transport, MTU, and TLS certificate paths.
		/// Must be called before <see cref="StartConnection"/>.
		/// </summary>
		/// <param name="t">The parent transport instance.</param>
		/// <param name="mtuValue">The maximum transmission unit for datagrams.</param>
		/// <param name="certPath">Path to the TLS certificate PEM file.</param>
		/// <param name="keyPath">Path to the TLS private key PEM file.</param>
		internal void Initialize(Transport t, int mtuValue, string certPath, string keyPath)
		{
			base.transport = t;
			this.mtu = mtuValue;
			certificatePath = certPath ?? "";
			privateKeyPath = keyPath ?? "";
		}

		/// <summary>
		/// Starts the server — creates native listener and begins accepting connections.
		/// QUIC ALWAYS requires TLS 1.3 — there is no unencrypted mode.
		/// When <paramref name="useCustomCertificate"/> is true, the certificate and key
		/// paths from the .cfg file are used (production). When false, a self-signed
		/// development certificate is generated automatically (dev/testing only).
		/// </summary>
		internal bool StartConnection(string bindAddress, ushort port, int maximumClients, bool useCustomCertificate)
		{
			if (base.GetConnectionState() != LocalConnectionState.Stopped)
				return false;

			base.SetConnectionState(LocalConnectionState.Starting, true);

			/* Reset stop guard for server restart support. */
			stopGuard = 0;

			/* Drain any stale incoming events from a previous server session,
             * INVOKING each action so that unmanaged memory (Marshal.AllocHGlobal)
             * held by native callbacks is properly freed via their finally blocks.
             * Discarding without invoking would leak native heap memory. */
			while (incomingEvents.TryDequeue(out Action act))
			{
				try { act?.Invoke(); } catch (System.Exception ex) { UnityEngine.Debug.LogWarning($"[WebTransport Server] Drain exception: {ex.Message}"); }
			}

			WebTransportNative.EnsureInitialized();

			this.port = port;
			this.maximumClients = maximumClients;
			resetQueues();

			// Pin callback delegates
			pinnedCallbacks = new NativeCallbacks.ServerCallbacks
			{
				OnConnect = new NativeCallbacks.ServerConnectDelegate(handleNativeConnect),
				OnDisconnect = new NativeCallbacks.ServerDisconnectDelegate(handleNativeDisconnect),
				OnStreamData = new NativeCallbacks.ServerStreamDataDelegate(handleNativeStreamData),
				OnDatagram = new NativeCallbacks.ServerDatagramDelegate(handleNativeDatagram),
			};

			// Create native server
			serverHandle = WebTransportNative.wt_server_create(
				useCustomCertificate ? certificatePath : null,
				useCustomCertificate ? privateKeyPath : null,
				"h3",           // ALPN for HTTP/3
				bindAddress,
				port,
				(uint)maximumClients,
				ref pinnedCallbacks,
				IntPtr.Zero);

			if (serverHandle == null || serverHandle.IsInvalid)
			{
				base.SetConnectionState(LocalConnectionState.Stopped, true);
				return false;
			}

			int result = WebTransportNative.wt_server_start(serverHandle);
			if (result != 0)
			{
				WebTransportNative.wt_server_destroy(serverHandle);
				serverHandle = null;
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
			/* Atomic guard — ensure StopConnection runs exactly once. */
			if (System.Threading.Interlocked.CompareExchange(ref stopGuard, 1, 0) != 0)
				return false;

			if (serverHandle == null || serverHandle.IsInvalid ||
				base.GetConnectionState() == LocalConnectionState.Stopped ||
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
				try { act?.Invoke(); } catch (System.Exception ex) { UnityEngine.Debug.LogWarning($"[WebTransport Server] Drain exception: {ex.Message}"); }
			}

			resetQueues();
			base.SetConnectionState(LocalConnectionState.Stopping, true);

			WebTransportNative.wt_server_stop(serverHandle);
			WebTransportNative.wt_server_destroy(serverHandle);
			serverHandle = null;

			base.SetConnectionState(LocalConnectionState.Stopped, true);
			return true;
		}

		/// <summary>
		/// Stops (kicks) a remote client.
		/// </summary>
		internal bool StopConnection(int connectionId, bool immediately)
		{
			if (serverHandle == null || serverHandle.IsInvalid ||
				base.GetConnectionState() != LocalConnectionState.Started)
				return false;

			if (!immediately)
				disconnectingNext.Add(connectionId);
			else if (idMapToNative.TryGetValue(connectionId, out ulong nativeId))
				WebTransportNative.wt_server_disconnect(serverHandle, nativeId);

			return true;
		}

		/// <summary>
		/// Gets the remote address string for a connected client.
		/// </summary>
		/// <returns>
		/// The remote address as a string, or <see cref="string.Empty"/> if the connection is not found.
		///
		/// <para><b>Memory model:</b> The native function <c>wt_server_get_client_address</c> returns
		/// a pointer to internal static storage within the server's connection struct
		/// (<c>conn-&gt;remote_addr</c>, a fixed-size char array embedded in <c>wt_server_conn_t</c>).
		/// This pointer is NOT per-call allocated memory — it lives as long as the connection
		/// is active in the native server's connection array. No free is required and no
		/// corresponding free function exists in the C API.</para>
		///
		/// <para><b>Thread safety:</b> The returned pointer is atomically guarded by
		/// <c>atomic_load(&amp;conn-&gt;in_use)</c> in the native code. The C# side marshals
		/// (copies) the string immediately via <see cref="System.Runtime.InteropServices.Marshal.PtrToStringAnsi"/>,
		/// so there is no dangling-pointer window on this side.</para>
		/// </returns>
		internal string GetConnectionAddress(int connectionId)
		{
			if (serverHandle == null || serverHandle.IsInvalid)
				return string.Empty;

			if (idMapToNative.TryGetValue(connectionId, out ulong nativeId))
			{
				IntPtr addrPtr = WebTransportNative.wt_server_get_client_address(
					serverHandle, nativeId);
				if (addrPtr != IntPtr.Zero)
					return System.Runtime.InteropServices.Marshal.PtrToStringAnsi(addrPtr) ?? string.Empty;
			}

			return string.Empty;
		}

		/// <summary>
		/// Processes incoming events from the native library.
		/// Must be called each frame.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void IterateIncoming()
		{
			if (serverHandle == null || serverHandle.IsInvalid)
				return;

			WebTransportNative.wt_server_poll(serverHandle, 0);

			while (incomingEvents.TryDequeue(out Action act))
			{
				try { act?.Invoke(); } catch (Exception e) { UnityEngine.Debug.LogException(e); }
			}
		}

		/// <summary>
		/// Dequeues outgoing packets and processes pending disconnects.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void IterateOutgoing()
		{
			if (serverHandle == null || serverHandle.IsInvalid)
				return;

			dequeueOutgoing();
			dequeueDisconnects();
		}

		/// <summary>
		/// Sends data to a single client or broadcasts to all (-1).
		/// Channel 0 = reliable (stream), Channel 1 = unreliable (datagram).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
		{
			Send(outgoing, channelId, segment, connectionId);
		}

		/// <summary>
		/// Returns the configured maximum number of clients.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal int GetMaximumClients()
		{
			return maximumClients;
		}

		/// <summary>
		/// Sets the configured maximum number of clients.
		/// Only takes effect if the server is not currently running.
		/// </summary>
		internal void SetMaximumClients(int value)
		{
			if (GetConnectionState() != LocalConnectionState.Stopped)
				return;
			/* Security: clamp to valid range [1, 100000] to prevent
			 * resource exhaustion from unbounded input. */
			if (value < 1 || value > 100000)
			{
				UnityEngine.Debug.LogWarning(
					$"[WebTransport Server] SetMaximumClients({value}) is outside allowed range [1, 100000]. Clamping to {System.Math.Clamp(value, 1, 100000)}.");
				value = System.Math.Clamp(value, 1, 100000);
			}
			maximumClients = value;
		}

		#region Private Helpers

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void resetQueues()
		{
			clients.Clear();
			idMapToNative.Clear();
			idMapFromNative.Clear();
			clientAddresses.Clear();
			nextConnectionId = 1;
			base.ClearPacketQueue(outgoing);
			disconnectingNext.Clear();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void dequeueDisconnects()
		{
			/* Process pending disconnects immediately. The HashSet indirection
             * prevents collection-modified-during-enumeration issues that would
             * occur if we disconnected directly during iteration over clients. */
			if (disconnectingNext.Count > 0)
			{
				foreach (int cid in disconnectingNext)
					StopConnection(cid, true);
				disconnectingNext.Clear();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void dequeueOutgoing()
		{
			if (base.GetConnectionState() != LocalConnectionState.Started ||
				serverHandle == null || serverHandle.IsInvalid)
			{
				base.ClearPacketQueue(outgoing);
				return;
			}

			int count = outgoing.Count;
			for (int i = 0; i < count; i++)
			{
				Packet outgoing = this.outgoing.Dequeue();
				int connectionId = outgoing.ConnectionId;

				if (connectionId == -1) // Broadcast
				{
					foreach (int cid in clients)
					{
						sendPacketToClient(outgoing, cid);
					}
				}
				else // Unicast
				{
					sendPacketToClient(outgoing, connectionId);
				}

				outgoing.Dispose();
			}
		}

		private void sendPacketToClient(Packet packet, int connectionId)
		{
			if (!idMapToNative.TryGetValue(connectionId, out ulong nativeId))
				return;

			int result;
			if (packet.Channel == 1) // Unreliable → datagram
			{
				result = WebTransportNative.wt_server_send_datagram(
					serverHandle, nativeId, packet.Data, packet.Length);
			}
			else // Reliable → stream
			{
				result = WebTransportNative.wt_server_send_stream(
					serverHandle, nativeId, packet.Data, packet.Length);
			}

			if (result != 0)
			{
				UnityEngine.Debug.LogWarning(
					$"[WebTransport Server] Send to {connectionId} failed: {WebTransportNative.ErrorString(result)}");
			}
		}

		#endregion

		#region Native Callbacks (invoked from QUIC worker threads)

		/// <summary>
		/// Called by the native library when a new client connects.
		/// Invoked from a QUIC worker thread — not the Unity main thread.
		/// Copies the remote address to unmanaged memory for safe marshaling on the main thread.
		/// </summary>
		/// <param name="context">User-supplied context pointer.</param>
		/// <param name="nativeConnectionId">The native connection ID assigned by msquic.</param>
		/// <param name="remoteAddressPtr">Pointer to a null-terminated UTF-8 string of the remote address.</param>
		private void handleNativeConnect(IntPtr context, ulong nativeConnectionId, IntPtr remoteAddressPtr)
		{
			/* Copy the remote address string to unmanaged memory on the native
             * callback thread. Managed allocations (new string) on non-Unity-main
             * threads can cause GC corruption on some Unity scripting backends
             * (particularly IL2CPP). We copy the raw bytes with AllocHGlobal here,
             * then marshal to a managed string on the main thread inside the queued
             * action — the same pattern used by HandleNativeStreamData. */
			int addrLen = 0;
			const int MaxAddrLen = 256;
			IntPtr unmanagedAddr = IntPtr.Zero;
			if (remoteAddressPtr != IntPtr.Zero)
			{
				// Find the null terminator length on the callback thread.
				unsafe
				{
					byte* p = (byte*)remoteAddressPtr;
					while (addrLen < MaxAddrLen && p[addrLen] != 0) addrLen++;
				}
				if (addrLen > 0)
				{
					unmanagedAddr = System.Runtime.InteropServices.Marshal.AllocHGlobal(addrLen + 1);
					unsafe
					{
						System.Buffer.MemoryCopy((void*)remoteAddressPtr, (void*)unmanagedAddr, addrLen + 1, addrLen + 1);
					}
				}
			}

			incomingEvents.Enqueue(() =>
			{
				try
				{
					string remoteAddr = "unknown";
					if (unmanagedAddr != IntPtr.Zero)
					{
						remoteAddr = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(unmanagedAddr, addrLen) ?? "unknown";
					}

					int fishNetId = nextConnectionId++;
					clients.Add(fishNetId);
					idMapToNative[fishNetId] = nativeConnectionId;
					idMapFromNative[nativeConnectionId] = fishNetId;
					clientAddresses[fishNetId] = remoteAddr;

					transport.HandleRemoteConnectionState(
						new RemoteConnectionStateArgs(RemoteConnectionState.Started, fishNetId, transport.Index));
				}
				finally
				{
					if (unmanagedAddr != IntPtr.Zero)
						System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedAddr);
				}
			});
		}

		/// <summary>
		/// Called by the native library when a client disconnects.
		/// Invoked from a QUIC worker thread — not the Unity main thread.
		/// Queues the cleanup action for execution on the main thread.
		/// </summary>
		/// <param name="context">User-supplied context pointer.</param>
		/// <param name="nativeConnectionId">The native connection ID that disconnected.</param>
		/// <param name="errorCode">Zero for clean disconnect; negative for error.</param>
		private void handleNativeDisconnect(IntPtr context, ulong nativeConnectionId, int errorCode)
		{
			incomingEvents.Enqueue(() =>
			{
				if (errorCode != 0)
					UnityEngine.Debug.LogWarning($"[WebTransport Server] Client {nativeConnectionId} disconnected: {WebTransportNative.ErrorString(errorCode)}");

				if (idMapFromNative.TryGetValue(nativeConnectionId, out int fishNetId))
				{
					clients.Remove(fishNetId);
					idMapToNative.Remove(fishNetId);
					idMapFromNative.Remove(nativeConnectionId);
					clientAddresses.Remove(fishNetId);

					transport.HandleRemoteConnectionState(
						new RemoteConnectionStateArgs(RemoteConnectionState.Stopped, fishNetId, transport.Index));
				}
			});
		}

		/// <summary>
		/// Called by the native library when reliable stream data arrives.
		/// Invoked from a QUIC worker thread — not the Unity main thread.
		/// Validates length, copies data to unmanaged memory, then queues processing on the main thread.
		/// </summary>
		/// <param name="context">User-supplied context pointer.</param>
		/// <param name="nativeConnectionId">The native connection ID that sent the data.</param>
		/// <param name="streamId">The QUIC stream ID.</param>
		/// <param name="dataPtr">Pointer to the received data buffer.</param>
		/// <param name="length">Length of the received data in bytes.</param>
		private void handleNativeStreamData(IntPtr context, ulong nativeConnectionId, ulong streamId, IntPtr dataPtr, int length)
		{
			/* Security: reject invalid or oversized packets before allocating unmanaged memory. */
			if (length <= 0 || length > MaxPacketSize)
			{
				UnityEngine.Debug.LogWarning($"[WebTransport Server] Invalid stream data length {length} from connection {nativeConnectionId}. Dropping.");
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
					if (!idMapFromNative.TryGetValue(nativeConnectionId, out int fishNetId))
						return;

					byte[] buffer = new byte[length];
					System.Runtime.InteropServices.Marshal.Copy(unmanagedCopy, buffer, 0, length);

					// Channel 0 = reliable (stream)
					ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
					transport.HandleServerReceivedDataArgs(
						new ServerReceivedDataArgs(segment, Channel.Reliable, fishNetId, transport.Index));
				}
				finally
				{
					System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedCopy);
				}
			});
		}

		/// <summary>
		/// Called by the native library when unreliable datagram data arrives.
		/// Invoked from a QUIC worker thread — not the Unity main thread.
		/// Validates length, copies data to unmanaged memory, then queues processing on the main thread.
		/// </summary>
		/// <param name="context">User-supplied context pointer.</param>
		/// <param name="nativeConnectionId">The native connection ID that sent the datagram.</param>
		/// <param name="dataPtr">Pointer to the received datagram buffer.</param>
		/// <param name="length">Length of the received datagram in bytes.</param>
		private void handleNativeDatagram(IntPtr context, ulong nativeConnectionId, IntPtr dataPtr, int length)
		{
			/* Security: reject invalid or oversized datagrams. Datagrams larger than the MTU
             * should never arrive from a compliant peer, but we validate defensively. */
			if (length <= 0 || length > mtu)
			{
				UnityEngine.Debug.LogWarning($"[WebTransport Server] Invalid datagram length {length} from connection {nativeConnectionId}. Dropping.");
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
					if (!idMapFromNative.TryGetValue(nativeConnectionId, out int fishNetId))
						return;

					byte[] buffer = new byte[length];
					System.Runtime.InteropServices.Marshal.Copy(unmanagedCopy, buffer, 0, length);

					// Channel 1 = unreliable (datagram)
					ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
					transport.HandleServerReceivedDataArgs(
						new ServerReceivedDataArgs(segment, Channel.Unreliable, fishNetId, transport.Index));
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