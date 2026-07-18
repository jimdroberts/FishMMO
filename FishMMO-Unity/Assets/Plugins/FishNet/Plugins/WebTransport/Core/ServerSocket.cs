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
		/// Initialises this socket for use.
		/// </summary>
		internal void Initialize(Transport t, int mtu, string certPath, string keyPath)
		{
			base.Transport = t;
			this.mtu = mtu;
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
				try { act?.Invoke(); } catch { }
			}

			WebTransportNative.EnsureInitialized();

			this.port = port;
			this.maximumClients = maximumClients;
			ResetQueues();

			// Pin callback delegates
			pinnedCallbacks = new NativeCallbacks.ServerCallbacks
			{
				OnConnect = new NativeCallbacks.ServerConnectDelegate(HandleNativeConnect),
				OnDisconnect = new NativeCallbacks.ServerDisconnectDelegate(HandleNativeDisconnect),
				OnStreamData = new NativeCallbacks.ServerStreamDataDelegate(HandleNativeStreamData),
				OnDatagram = new NativeCallbacks.ServerDatagramDelegate(HandleNativeDatagram),
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
				try { act?.Invoke(); } catch { }
			}

			ResetQueues();
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
		internal void IterateOutgoing()
		{
			if (serverHandle == null || serverHandle.IsInvalid)
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
			Send(outgoing, channelId, segment, connectionId);
		}

		/// <summary>
		/// Returns the configured maximum number of clients.
		/// </summary>
		internal int GetMaximumClients()
		{
			return maximumClients;
		}

		#region Private Helpers

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void ResetQueues()
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
		private void DequeueDisconnects()
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
		private void DequeueOutgoing()
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

		private void HandleNativeConnect(IntPtr context, ulong nativeConnectionId, IntPtr remoteAddressPtr)
		{
			/* Copy the remote address string to unmanaged memory on the native
             * callback thread. Managed allocations (new string) on non-Unity-main
             * threads can cause GC corruption on some Unity scripting backends
             * (particularly IL2CPP). We copy the raw bytes with AllocHGlobal here,
             * then marshal to a managed string on the main thread inside the queued
             * action — the same pattern used by HandleNativeStreamData. */
			int addrLen = 0;
			IntPtr unmanagedAddr = IntPtr.Zero;
			if (remoteAddressPtr != IntPtr.Zero)
			{
				// Find the null terminator length on the callback thread.
				unsafe
				{
					byte* p = (byte*)remoteAddressPtr;
					while (p[addrLen] != 0) addrLen++;
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

					Transport.HandleRemoteConnectionState(
						new RemoteConnectionStateArgs(RemoteConnectionState.Started, fishNetId, Transport.Index));
				}
				finally
				{
					if (unmanagedAddr != IntPtr.Zero)
						System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedAddr);
				}
			});
		}

		private void HandleNativeDisconnect(IntPtr context, ulong nativeConnectionId, int errorCode)
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

					Transport.HandleRemoteConnectionState(
						new RemoteConnectionStateArgs(RemoteConnectionState.Stopped, fishNetId, Transport.Index));
				}
			});
		}

		private void HandleNativeStreamData(IntPtr context, ulong nativeConnectionId, ulong streamId, IntPtr dataPtr, int length)
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
					if (!idMapFromNative.TryGetValue(nativeConnectionId, out int fishNetId))
						return;

					byte[] buffer = new byte[length];
					System.Runtime.InteropServices.Marshal.Copy(unmanagedCopy, buffer, 0, length);

					// Channel 0 = reliable (stream)
					ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
					Transport.HandleServerReceivedDataArgs(
						new ServerReceivedDataArgs(segment, Channel.Reliable, fishNetId, Transport.Index));
				}
				finally
				{
					System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedCopy);
				}
			});
		}

		private void HandleNativeDatagram(IntPtr context, ulong nativeConnectionId, IntPtr dataPtr, int length)
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
					if (!idMapFromNative.TryGetValue(nativeConnectionId, out int fishNetId))
						return;

					byte[] buffer = new byte[length];
					System.Runtime.InteropServices.Marshal.Copy(unmanagedCopy, buffer, 0, length);

					// Channel 1 = unreliable (datagram)
					ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
					Transport.HandleServerReceivedDataArgs(
						new ServerReceivedDataArgs(segment, Channel.Unreliable, fishNetId, Transport.Index));
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