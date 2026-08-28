using FishNet.Managing;
using FishNet.Transporting.WebTransport.Native;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace FishNet.Transporting.WebTransport.Server
{
	/// <summary>
	/// Server-side socket wrapping the native WebTransport C library.
	/// Accepts QUIC connections, manages WebTransport sessions per client,
	/// and provides broadcast + unicast send capability.
	/// </summary>
	public class ServerSocket : CommonSocket, IDisposable
	{
		#region Public
		/// <summary>
		/// Gets the current ConnectionState of a remote client on the server.
		/// </summary>
		internal RemoteConnectionState GetConnectionState(int connectionId)
		{
			this.clientsLock.EnterReadLock();
			try
			{
				return this.clients.Contains(connectionId)
					? RemoteConnectionState.Started
					: RemoteConnectionState.Stopped;
			}
			finally
			{
				this.clientsLock.ExitReadLock();
			}
		}
		#endregion

		#region Private Configuration
		private ushort port;
		private int maximumClients;
		private string certificatePath;
		private string privateKeyPath;
		#endregion

		#region Queues
		/// <summary>
		/// Outbound messages which need to be sent.
		/// </summary>
		private ConcurrentQueue<Packet> outgoing = new ConcurrentQueue<Packet>();
		/// <summary>
		/// Connection IDs to disconnect next iteration.
		/// </summary>
		private HashSet<int> disconnectingNext = new HashSet<int>();
		#endregion

		/// <summary>
		/// Currently connected client IDs.
		/// Maps FishNet's int connection IDs to native ulong connection IDs.
		/// All collection access is synchronized via <see cref="clientsLock"/>.
		/// </summary>
		private HashSet<int> clients = new HashSet<int>();
		private Dictionary<int, ulong> idMapToNative = new Dictionary<int, ulong>();
		private Dictionary<ulong, int> idMapFromNative = new Dictionary<ulong, int>();
		/// <summary>
		/// Address book: connectionId → remote address string.
		/// Synchronized under <see cref="clientsLock"/>.
		/// </summary>
		private Dictionary<int, string> clientAddresses = new Dictionary<int, string>();

		/// <summary>
		/// Reader-writer lock protecting all connection-tracking collections
		/// (<see cref="clients"/>, <see cref="idMapToNative"/>, <see cref="idMapFromNative"/>,
		/// <see cref="clientAddresses"/>, <see cref="nextConnectionId"/>).
		/// Read operations (<c>GetConnectionState</c>, <c>GetConnectionAddress</c>) acquire
		/// the read lock; write operations (connect, disconnect, reset) acquire the write lock.
		/// This prevents crashes from FishNet calling these methods from any thread while
		/// the main thread writes to the collections during <c>IterateIncoming</c>.
		/// </summary>
		private readonly System.Threading.ReaderWriterLockSlim clientsLock =
			new System.Threading.ReaderWriterLockSlim(System.Threading.LockRecursionPolicy.NoRecursion);

		/// <summary>
		/// Monotonic connection ID counter.
		/// </summary>
		private int nextConnectionId = 1;

		/// <summary>
		/// Native server handle from the C library.
		/// </summary>
		private SafeServerHandle serverHandle;

		/// <summary>
		/// Soft limit for queued incoming events to prevent native heap exhaustion
		/// from a flood of incoming packets. The limit is enforced via
		/// <see cref="Interlocked.Increment"/> check-before-enqueue, which has a
		/// TOCTOU window — two concurrent callbacks may both pass the check,
		/// resulting in a transient overage of up to (N-1) extra events where
		/// N is the number of concurrent QUIC worker threads. This is intentional:
		/// a hard bound would require a lock on the hot path; the soft bound with
		/// deferred Decrement correction on drain is lock-free and the overage is
		/// bounded by thread count, not unbounded.
		/// </summary>
		private const int MaxIncomingEvents = 10000;

		/// <summary>
		/// Thread-safe queue for events arriving from native callbacks.
		/// Drained on the Unity main thread during IterateIncoming.
		/// </summary>
		private ConcurrentQueue<Action> incomingEvents = new ConcurrentQueue<Action>();

		/// <summary>
		/// ALPN (Application-Layer Protocol Negotiation) string for QUIC.
		/// Defaults to "h3" for HTTP/3 (WebTransport). Can be overridden
		/// before StartConnection to use a custom ALPN.
		/// </summary>
		private string alpn = "h3";
		/// <summary>
		/// Gets or sets the ALPN (Application-Layer Protocol Negotiation) string.
		/// Setting to null resets to the default "h3".
		/// </summary>
		// NOTE: Setting to null resets the ALPN to "h3" (the default). This is an
		// explicit design choice: the WebTransport spec requires HTTP/3 framing to be
		// negotiated via ALPN, so a null value should never leave TLS unnegotiated --
		// it simply falls back to the standard h3 identifier. The field default is
		// also "h3" (see <see cref="alpn"/>), so this null-coalesce is a safety net
		// for callers that explicitly set to null.
		public string Alpn { get => alpn; set => alpn = value ?? "h3"; }

		/// <summary>
		/// Comma-separated list of allowed Origin header values for browser
		/// WebTransport CORS validation (e.g. "https://play.fishmmo.com").
		/// Empty string or null means allow all origins (development/testing only).
		/// In production, this MUST be set to a specific origin to prevent
		/// cross-site WebTransport connection attempts.
		/// </summary>
		private string allowedOrigins = "";
		/// <summary>
		/// Gets or sets the allowed origins for browser WebTransport CORS
		/// validation. Must be called before <see cref="StartConnection"/>.
		/// Pass an empty string or null to allow all origins (dev/testing only).
		/// </summary>
		public string AllowedOrigins { get => allowedOrigins; set => allowedOrigins = value ?? ""; }

		/// <summary>
		/// Atomic guard to ensure StopConnection runs exactly once,
		/// even if called from both a native callback and user code.
		/// </summary>
		private int stopGuard = 0;
		/// <summary>
		/// Atomic guard to ensure Dispose runs exactly once.
		/// </summary>
		private int disposed = 0;

		/// <summary>
		/// Atomic counter tracking how many items are in <see cref="incomingEvents"/>.
		/// Used with <see cref="System.Threading.Interlocked"/> to enforce a soft
		/// limit against <see cref="MaxIncomingEvents"/>. The increment-then-check
		/// pattern has a TOCTOU window — concurrent QUIC worker threads may both
		/// pass the check, producing a transient overage bounded by thread count,
		/// not unbounded (see <see cref="MaxIncomingEvents"/> for details).
		/// Int64 (long) — effectively overflow-proof; would require ~9 exabytes
		/// of queued events to wrap.
		/// </summary>
		private long incomingEventCount;

		/// <summary>
		/// Pinned delegate handles (prevent GC collection of callback delegates).
		/// The struct field roots the managed delegate instances so the GC does
		/// not collect them while the native library holds function pointers.
		/// </summary>
		private NativeCallbacks.ServerCallbacks pinnedCallbacks;
		/// <summary>
		/// Pointer to unmanaged memory containing a Marshal.StructureToPtr copy
		/// of <see cref="pinnedCallbacks"/>. Passed to <c>wt_server_create</c>
		/// instead of <c>ref</c> to avoid GC relocation of the callback table.
		/// Allocated in <see cref="StartConnection"/>; freed in <see cref="StopConnection"/>.
		/// </summary>
		private IntPtr pinnedCallbacksPtr = IntPtr.Zero;

		/// <summary>
		/// Maps a native context token to the socket that owns it, so the static
		/// [AOT.MonoPInvokeCallback] trampolines below can find their server. IL2CPP
		/// cannot marshal an instance method to native code, so those entry points have
		/// no other route back to the instance.
		/// </summary>
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, ServerSocket> nativeSockets =
			new System.Collections.Concurrent.ConcurrentDictionary<IntPtr, ServerSocket>();

		/// <summary>
		/// Source of the token handed to the native library as its callback context.
		/// A counter rather than a GCHandle so a callback arriving after teardown
		/// resolves to nothing instead of dereferencing freed memory.
		/// </summary>
		private static long nativeContextCounter;

		/// <summary>
		/// This socket's key in <see cref="nativeSockets"/>, or zero when unregistered.
		/// </summary>
		private IntPtr nativeContext = IntPtr.Zero;

		// Rooted for the process lifetime so the GC cannot collect the delegates while
		// the native library holds their function pointers.
		private static readonly NativeCallbacks.ServerConnectDelegate nativeStaticOnConnect = NativeOnConnect;
		private static readonly NativeCallbacks.ServerDisconnectDelegate nativeStaticOnDisconnect = NativeOnDisconnect;
		private static readonly NativeCallbacks.ServerStreamDataDelegate nativeStaticOnStreamData = NativeOnStreamData;
		private static readonly NativeCallbacks.ServerDatagramDelegate nativeStaticOnDatagram = NativeOnDatagram;

		/// <summary>
		/// Finalizer -- drains the incoming event queue without invoking actions.
		/// The .NET finalizer runs on an arbitrary thread, not the Unity main thread.
		/// The queued actions call FishNet transport callbacks which must execute
		/// on the main thread. In the abandoned-socket case this finalizer is a
		/// last resort; normal cleanup goes through <see cref="StopConnection"/>
		/// which invokes actions on the main thread and frees unmanaged memory.
		/// </summary>
		~ServerSocket()
		{
			// Drain the event queue WITHOUT invoking actions -- the finalizer
			// runs on the GC finalizer thread, not the Unity main thread, and
			// the queued actions call FishNet/Unity callbacks which are not
			// thread-safe.
			while (incomingEvents.TryDequeue(out Action act))
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
			}
			System.Threading.Interlocked.Exchange(ref this.incomingEventCount, 0);

			// Free pinned callbacks table allocated via Marshal.AllocHGlobal.
			if (this.pinnedCallbacksPtr != IntPtr.Zero)
			{
				System.Runtime.InteropServices.Marshal.FreeHGlobal(this.pinnedCallbacksPtr);
				this.pinnedCallbacksPtr = IntPtr.Zero;
			}
			ReleaseNativeContext();
			// serverHandle is a SafeHandle; its own finalizer will release it
			// via ReleaseHandle (which has the IsLibraryDeinitialized guard).
		}

		/// <summary>
		/// Releases all managed and unmanaged resources.
		/// Safe to call multiple times; subsequent calls are no-ops.
		/// Drains pending incoming events so that unmanaged memory held by
		/// native callbacks (Marshal.AllocHGlobal) is freed via their finally
		/// blocks. MUST be called from the Unity main thread because the queued
		/// actions invoke FishNet/Unity callbacks.
		/// Called from <see cref="StopConnection"/> on the main thread.
		/// </summary>
		public void Dispose()
		{
			if (System.Threading.Interlocked.Exchange(ref this.disposed, 1) != 0)
				return;

			// Drain pending incoming events, INVOKING each action so that
			// unmanaged memory (Marshal.AllocHGlobal) held by native callbacks
			// is properly freed via their finally blocks.
			while (this.incomingEvents.TryDequeue(out Action act))
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				try { act?.Invoke(); } catch { /* swallow */ }
			}
			System.Threading.Interlocked.Exchange(ref this.incomingEventCount, 0);

			if (this.serverHandle != null && !this.serverHandle.IsInvalid)
			{
				WebTransportNative.wt_server_stop(this.serverHandle);
				WebTransportNative.wt_server_destroy(this.serverHandle);
				this.serverHandle = null;
			}

			if (this.pinnedCallbacksPtr != IntPtr.Zero)
			{
				System.Runtime.InteropServices.Marshal.FreeHGlobal(this.pinnedCallbacksPtr);
				this.pinnedCallbacksPtr = IntPtr.Zero;
			}
			ReleaseNativeContext();

			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Initializes the server socket with the specified transport, MTU, and TLS certificate paths.
		/// Must be called before <see cref="StartConnection"/>.
		/// </summary>
		/// <param name="t">The parent transport instance.</param>
		/// <param name="mtuValue">Unused; the send limit lives on the transport (GetMTU) and the receive limit is MaxDatagramReceiveSize.</param>
		/// <param name="certPath">Path to the TLS certificate PEM file.</param>
		/// <param name="keyPath">Path to the TLS private key PEM file.</param>
		internal void Initialize(Transport t, int mtuValue, string certPath, string keyPath)
		{
			base.transport = t;
			this.certificatePath = certPath ?? "";
			this.privateKeyPath = keyPath ?? "";
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
			this.stopGuard = 0;

			/* Drain any stale incoming events from a previous server session,
             * INVOKING each action so that unmanaged memory (Marshal.AllocHGlobal)
             * held by native callbacks is properly freed via their finally blocks.
             * Discarding without invoking would leak native heap memory. */
			while (this.incomingEvents.TryDequeue(out Action act))
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				try { act?.Invoke(); } catch (System.Exception ex) { transport.NetworkManager?.LogWarning($"[WebTransport Server] Drain exception: {ex.ToString()}"); }
			}
			System.Threading.Interlocked.Exchange(ref this.incomingEventCount, 0);

			if (!WebTransportNative.EnsureInitialized())
			{
				transport.NetworkManager?.LogError("[WebTransport Server] Native library initialization failed. Server cannot start.");
				base.SetConnectionState(LocalConnectionState.Stopped, true);
				return false;
			}

			this.port = port;
			this.maximumClients = maximumClients;
			ResetQueues();

			/* The table must hold the STATIC trampolines, not the instance handlers.
			 * IL2CPP refuses to marshal a delegate over an instance method to native code
			 * and throws where the table is written to unmanaged memory. The server
			 * currently builds under Mono so this never fired here, but it is the same
			 * defect that stopped the IL2CPP client connecting, and it would surface the
			 * moment the server is built with IL2CPP. The context token below carries the
			 * identity that the instance receiver used to supply. */
			this.nativeContext = new IntPtr(System.Threading.Interlocked.Increment(ref nativeContextCounter));
			nativeSockets[this.nativeContext] = this;

			// Pin callback delegates (struct field roots managed delegates).
			this.pinnedCallbacks = new NativeCallbacks.ServerCallbacks
			{
				OnConnect = nativeStaticOnConnect,
				OnDisconnect = nativeStaticOnDisconnect,
				OnStreamData = nativeStaticOnStreamData,
				OnDatagram = nativeStaticOnDatagram,
			};

			// Copy the callback table to unmanaged memory so that the native
			// library's stored pointer survives GC compaction (pin only during
			// P/Invoke is insufficient -- the runtime unpins after return).
			int cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeCallbacks.ServerCallbacks>();
			this.pinnedCallbacksPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(cbSize);
			System.Runtime.InteropServices.Marshal.StructureToPtr(this.pinnedCallbacks, this.pinnedCallbacksPtr, false);

			if (string.IsNullOrWhiteSpace(bindAddress))
			{
				transport.NetworkManager?.LogError(
					"[WebTransport Server] Bind address is null/empty — cannot start.");
				base.SetConnectionState(LocalConnectionState.Stopped, true);
				return false;
			}
			if (port == 0)
			{
				transport.NetworkManager?.LogError(
					"[WebTransport Server] Port is 0 — ApplyTransportConfiguration may not have run. Cannot start.");
				base.SetConnectionState(LocalConnectionState.Stopped, true);
				return false;
			}

			string certForLog = useCustomCertificate ? (this.certificatePath ?? "") : "(self-signed)";
			string keyForLog = useCustomCertificate ? (this.privateKeyPath ?? "") : "(none)";
			transport.NetworkManager?.Log(
				$"[WebTransport Server] Starting bind={bindAddress} port={port} maxClients={maximumClients} cert={certForLog} key={keyForLog} alpn={this.alpn}");

			// Create native server
			this.serverHandle = WebTransportNative.wt_server_create(
				useCustomCertificate ? this.certificatePath : null,
				useCustomCertificate ? this.privateKeyPath : null,
				this.alpn,           // ALPN for HTTP/3
				bindAddress,
				port,
				(uint)maximumClients,
				string.IsNullOrEmpty(this.allowedOrigins) ? null : this.allowedOrigins,
				this.pinnedCallbacksPtr,
				this.nativeContext);
			if (this.serverHandle == null || this.serverHandle.IsInvalid)
			{
				transport.NetworkManager?.LogError(
					$"[WebTransport Server] wt_server_create failed (null/invalid handle). " +
					$"bind={bindAddress} port={port} cert={certForLog} key={keyForLog}");
				// Free unmanaged callback table on error path.
				if (this.pinnedCallbacksPtr != IntPtr.Zero)
				{
					System.Runtime.InteropServices.Marshal.FreeHGlobal(this.pinnedCallbacksPtr);
					this.pinnedCallbacksPtr = IntPtr.Zero;
				}
				ReleaseNativeContext();
				base.SetConnectionState(LocalConnectionState.Stopped, true);
				return false;
			}

			int result = WebTransportNative.wt_server_start(this.serverHandle);
			if (result != 0)
			{
				string errName = WebTransportNative.ErrorString((WebTransportNative.WTError)result);
				transport.NetworkManager?.LogError(
					$"[WebTransport Server] wt_server_start failed: code={result} ({errName}). " +
					$"bind={bindAddress} port={port} cert={certForLog} key={keyForLog}");
				WebTransportNative.wt_server_destroy(this.serverHandle);
				this.serverHandle = null;
				// Free unmanaged callback table on error path.
				if (this.pinnedCallbacksPtr != IntPtr.Zero)
				{
					System.Runtime.InteropServices.Marshal.FreeHGlobal(this.pinnedCallbacksPtr);
					this.pinnedCallbacksPtr = IntPtr.Zero;
				}
				ReleaseNativeContext();
				base.SetConnectionState(LocalConnectionState.Stopped, true);
				return false;
			}

			transport.NetworkManager?.Log(
				$"[WebTransport Server] Started OK bind={bindAddress} port={port}");
			base.SetConnectionState(LocalConnectionState.Started, true);
			return true;
		}

		/// <summary>
		/// Stops the server and disconnects all this.clients.
		/// </summary>
		internal bool StopConnection()
		{
			/* Atomic guard — ensure StopConnection runs exactly once. */
			if (System.Threading.Interlocked.CompareExchange(ref this.stopGuard, 1, 0) != 0)
				return false;

			if (this.serverHandle == null || this.serverHandle.IsInvalid ||
				base.GetConnectionState() == LocalConnectionState.Stopped ||
				base.GetConnectionState() == LocalConnectionState.Stopping)
			{
				this.stopGuard = 0;
				return false;
			}

			/* Drain stale incoming events before shutdown.
             * Invoke (not discard) each action so that unmanaged memory
             * allocated in native callbacks is freed. The actions check
             * connection state before processing — they will skip
             * Transport callbacks since state is about to be Stopping. */
			while (this.incomingEvents.TryDequeue(out Action act))
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				try { act?.Invoke(); } catch (System.Exception ex) { transport.NetworkManager?.LogWarning($"[WebTransport Server] Drain exception: {ex.ToString()}"); }
			}
			System.Threading.Interlocked.Exchange(ref this.incomingEventCount, 0);

			ResetQueues();
			base.SetConnectionState(LocalConnectionState.Stopping, true);

			Dispose();
			base.SetConnectionState(LocalConnectionState.Stopped, true);
			return true;
		}

		/// <summary>
		/// Stops (kicks) a remote client.
		/// </summary>
		internal bool StopConnection(int connectionId, bool immediately)
		{
			if (this.serverHandle == null || this.serverHandle.IsInvalid ||
				base.GetConnectionState() != LocalConnectionState.Started)
				return false;

			if (!immediately)
			{
				this.clientsLock.EnterReadLock();
				try
				{
					if (this.clients.Contains(connectionId))
						this.disconnectingNext.Add(connectionId);
				}
				finally
				{
					this.clientsLock.ExitReadLock();
				}
			}
			else if (this.idMapToNative.TryGetValue(connectionId, out ulong nativeId))
				WebTransportNative.wt_server_disconnect(this.serverHandle, nativeId);

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
		/// <para><b>LIFETIME WARNING:</b> The returned native pointer points directly into
		/// the connection struct's internal storage.  It is valid ONLY while both of the
		/// following hold simultaneously:
		///   (1) the C# <c>clientsLock</c> read lock is held (this call), AND
		///   (2) the native connection remains alive (<c>conn-&gt;in_use == true</c>).
		///
		/// The <c>atomic_load(&amp;conn-&gt;in_use)</c> check inside the native function
		/// only guarantees the pointer is valid at the instant of the check.  A concurrent
		/// native disconnect after that check but before <c>PtrToStringUTF8</c> copies the
		/// data can still produce a dangling-pointer read.
		///
		/// Because we call <c>Marshal.PtrToStringUTF8</c> immediately on the returned pointer
		/// (within the same try block and while the read lock is held), the practical risk
		/// window is a few nanoseconds, but it is NOT zero.  The native-side
		/// <c>wt_server_get_client_addr_impl</c> documents this same caveat.
		/// </para>
		/// </returns>
		internal string GetConnectionAddress(int connectionId)
		{
			SafeServerHandle handle = this.serverHandle;
			if (handle == null || handle.IsInvalid)
				return string.Empty;

			this.clientsLock.EnterReadLock();
			try
			{
				if (this.idMapToNative.TryGetValue(connectionId, out ulong nativeId))
				{
					IntPtr addrPtr = WebTransportNative.wt_server_get_client_address(
						handle, nativeId);
					if (addrPtr != IntPtr.Zero)
						return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(addrPtr) ?? string.Empty;
				}
			}
			finally
			{
				this.clientsLock.ExitReadLock();
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
			if (this.serverHandle == null || this.serverHandle.IsInvalid)
				return;

			WebTransportNative.wt_server_poll(this.serverHandle, 0);

			while (this.incomingEvents.TryDequeue(out Action act))
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				try { act?.Invoke(); } catch (Exception e) { transport.NetworkManager?.LogError(e.ToString()); }
			}
		}

		/// <summary>
		/// Dequeues and sends all pending outgoing packets, then processes
		/// any deferred client disconnects that were queued via
		/// <see cref="StopConnection(int, bool)"/> with <c>immediately=false</c>.
		/// Must be called each frame from the Unity main thread.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void IterateOutgoing()
		{
			if (this.serverHandle == null || this.serverHandle.IsInvalid)
				return;

			DequeueOutgoing();
			DequeueDisconnects();
		}

		/// <summary>
		/// Sends data to a single client or broadcasts to all (-1).
		/// Channel 0 = reliable (stream), Channel 1 = unreliable (datagram).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
		{
			Send(this.outgoing, channelId, segment, connectionId);
		}

		/// <summary>
		/// Returns the configured maximum number of this.clients.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal int GetMaximumClients()
		{
			return this.maximumClients;
		}

		/// <summary>
		/// Sets the configured maximum number of this.clients.
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
				transport.NetworkManager?.LogWarning(
					$"[WebTransport Server] SetMaximumClients({value}) is outside allowed range [1, 100000]. Clamping to {System.Math.Clamp(value, 1, 100000)}.");
				value = System.Math.Clamp(value, 1, 100000);
			}
			this.maximumClients = value;
		}

		#region Private Helpers

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void ResetQueues()
		{
			this.clientsLock.EnterWriteLock();
			try
			{
				this.clients.Clear();
				this.idMapToNative.Clear();
				this.idMapFromNative.Clear();
				this.clientAddresses.Clear();
				this.nextConnectionId = 1;
			}
			finally
			{
				this.clientsLock.ExitWriteLock();
			}
			base.ClearPacketQueue(this.outgoing);
			this.disconnectingNext.Clear();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void DequeueDisconnects()
		{
			/* Process pending disconnects immediately. The HashSet indirection
             * prevents collection-modified-during-enumeration issues that would
             * occur if we disconnected directly during iteration over this.clients. */
			if (this.disconnectingNext.Count > 0)
			{
				foreach (int cid in this.disconnectingNext.ToArray())
					StopConnection(cid, true);
				this.disconnectingNext.Clear();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void DequeueOutgoing()
		{
			if (base.GetConnectionState() != LocalConnectionState.Started ||
				this.serverHandle == null || this.serverHandle.IsInvalid)
			{
				base.ClearPacketQueue(this.outgoing);
				return;
			}

			while (this.outgoing.TryDequeue(out Packet pkt))
			{
				try
				{
					int connectionId = pkt.ConnectionId;

					if (connectionId == -1) // Broadcast
					{
						this.clientsLock.EnterReadLock();
						try
						{
							foreach (int cid in this.clients)
							{
								SendPacketToClient(pkt, cid);
							}
						}
						finally
						{
							this.clientsLock.ExitReadLock();
						}
					}
					else // Unicast
					{
						SendPacketToClient(pkt, connectionId);
					}
				}
				finally
				{
					pkt.Dispose();
				}
			}
		}

		private void SendPacketToClient(Packet packet, int connectionId)
		{
			if (!this.idMapToNative.TryGetValue(connectionId, out ulong nativeId))
				return;

			int result;
			if (packet.Channel == 1) // Unreliable → datagram
			{
				result = WebTransportNative.wt_server_send_datagram(
					this.serverHandle, nativeId, packet.Data, packet.Length);
			}
			else // Reliable → stream
			{
				result = WebTransportNative.wt_server_send_stream(
					this.serverHandle, nativeId, packet.Data, packet.Length);
			}

			if (result != 0)
			{
				transport.NetworkManager?.LogWarning(
					$"[WebTransport Server] Send to {connectionId} failed: {WebTransportNative.ErrorString((WebTransportNative.WTError)result)}");
			}
		}

		#endregion

		#region Native Callbacks (invoked from QUIC worker threads)

		/// <summary>
		/// Resolves the socket a native callback belongs to from its context token.
		/// A miss is normal: the native library may complete a callback that was already
		/// in flight when the socket tore down and removed its token.
		/// </summary>
		private static bool TryGetNativeSocket(IntPtr context, out ServerSocket socket)
		{
			return nativeSockets.TryGetValue(context, out socket) && socket != null;
		}

		/// <summary>
		/// Drops this socket's context token so no further native callback can reach it.
		/// Called only after the native server has been destroyed, so a callback still in
		/// flight during teardown can complete against a live socket.
		/// </summary>
		private void ReleaseNativeContext()
		{
			if (this.nativeContext != IntPtr.Zero)
			{
				nativeSockets.TryRemove(this.nativeContext, out _);
				this.nativeContext = IntPtr.Zero;
			}
		}

		[AOT.MonoPInvokeCallback(typeof(NativeCallbacks.ServerConnectDelegate))]
		private static void NativeOnConnect(IntPtr context, ulong connectionId, IntPtr remoteAddress)
		{
			if (TryGetNativeSocket(context, out ServerSocket socket))
				socket.HandleNativeConnect(context, connectionId, remoteAddress);
		}

		[AOT.MonoPInvokeCallback(typeof(NativeCallbacks.ServerDisconnectDelegate))]
		private static void NativeOnDisconnect(IntPtr context, ulong connectionId, int errorCode)
		{
			if (TryGetNativeSocket(context, out ServerSocket socket))
				socket.HandleNativeDisconnect(context, connectionId, errorCode);
		}

		[AOT.MonoPInvokeCallback(typeof(NativeCallbacks.ServerStreamDataDelegate))]
		private static void NativeOnStreamData(IntPtr context, ulong connectionId, ulong streamId, IntPtr dataPtr, int length)
		{
			if (TryGetNativeSocket(context, out ServerSocket socket))
				socket.HandleNativeStreamData(context, connectionId, streamId, dataPtr, length);
		}

		[AOT.MonoPInvokeCallback(typeof(NativeCallbacks.ServerDatagramDelegate))]
		private static void NativeOnDatagram(IntPtr context, ulong connectionId, IntPtr dataPtr, int length)
		{
			if (TryGetNativeSocket(context, out ServerSocket socket))
				socket.HandleNativeDatagram(context, connectionId, dataPtr, length);
		}

		/// <summary>
		/// Called by the native library when a new client connects.
		/// Invoked from a QUIC worker thread — not the Unity main thread.
		/// Copies the remote address to unmanaged memory for safe marshaling on the main thread.
		/// </summary>
		/// <param name="context">User-supplied context pointer.</param>
		/// <param name="nativeConnectionId">The native connection ID assigned by msquic.</param>
		/// <param name="remoteAddressPtr">Pointer to a null-terminated UTF-8 string of the remote address.</param>
		private void HandleNativeConnect(IntPtr context, ulong nativeConnectionId, IntPtr remoteAddressPtr)
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
					// Cap at MaxAddrLen-1 to guarantee null-terminator space in the
					// fixed-size native buffer (256 bytes). Without this, a 256-byte
					// address with no null would cause MemoryCopy to read past the buffer.
					while (addrLen < MaxAddrLen - 1 && p[addrLen] != 0) addrLen++;
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

			if (System.Threading.Interlocked.Increment(ref this.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				transport.NetworkManager?.LogWarning("[WebTransport Server] Incoming event queue full; dropping connect event.");
				if (unmanagedAddr != IntPtr.Zero)
					System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedAddr);
				return;
			}

			this.incomingEvents.Enqueue(() =>
			{
				try
				{
					string remoteAddr = "unknown";
					if (unmanagedAddr != IntPtr.Zero)
					{
						remoteAddr = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(unmanagedAddr, addrLen) ?? "unknown";
					}

					// Atomic allocation with overflow protection and collision retry.
					int fishNetId;
					for (; ; )
					{
						fishNetId = System.Threading.Interlocked.Increment(ref this.nextConnectionId);
						// Overflow protection: wrap to 1 if the counter overflows past
						// int.MaxValue or wraps to zero/negative.
						if (fishNetId <= 0)
						{
							int wrapped = System.Threading.Interlocked.CompareExchange(
								ref this.nextConnectionId, 2, fishNetId);
							// If CAS succeeded, use 1; if another thread already wrapped,
							// retry with the value that thread set.
							fishNetId = (wrapped == fishNetId) ? 1 : wrapped;
							if (fishNetId <= 0)
								continue;
						}
						break;
					}

					this.clientsLock.EnterWriteLock();
					try
					{
						/* Guard against ID collision if nextConnectionId wraps. */
						if (this.clients.Contains(fishNetId))
						{
							transport.NetworkManager?.LogWarning(
								$"[WebTransport Server] Connection ID {fishNetId} already in use; retrying connect.");
							return;
						}
						this.clients.Add(fishNetId);
						idMapToNative[fishNetId] = nativeConnectionId;
						idMapFromNative[nativeConnectionId] = fishNetId;
						clientAddresses[fishNetId] = remoteAddr;
					}
					finally
					{
						this.clientsLock.ExitWriteLock();
					}

					// Fire transport callback OUTSIDE the write lock to prevent deadlock
					// when subscribers call GetConnectionState/GetConnectionAddress.
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
		private void HandleNativeDisconnect(IntPtr context, ulong nativeConnectionId, int errorCode)
		{
			if (System.Threading.Interlocked.Increment(ref this.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				transport.NetworkManager?.LogWarning("[WebTransport Server] Incoming event queue full; dropping disconnect event.");
				return;
			}

			this.incomingEvents.Enqueue(() =>
			{
				if (errorCode != 0)
					transport.NetworkManager?.LogWarning($"[WebTransport Server] Client {nativeConnectionId} disconnected: {WebTransportNative.ErrorString((WebTransportNative.WTError)errorCode)}");

				if (this.idMapFromNative.TryGetValue(nativeConnectionId, out int fishNetId))
				{
					this.clientsLock.EnterWriteLock();
					try
					{
						this.clients.Remove(fishNetId);
						this.idMapToNative.Remove(fishNetId);
						this.idMapFromNative.Remove(nativeConnectionId);
						this.clientAddresses.Remove(fishNetId);
					}
					finally
					{
						this.clientsLock.ExitWriteLock();
					}

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
		private void HandleNativeStreamData(IntPtr context, ulong nativeConnectionId, ulong streamId, IntPtr dataPtr, int length)
		{
			/* Security: reject invalid or oversized packets before allocating unmanaged memory. */
			if (length <= 0 || length > MaxPacketSize)
			{
				transport.NetworkManager?.LogWarning($"[WebTransport Server] Invalid stream data length {length} from connection {nativeConnectionId}. Dropping.");
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

			if (System.Threading.Interlocked.Increment(ref this.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				transport.NetworkManager?.LogWarning("[WebTransport Server] Incoming event queue full; dropping stream data.");
				System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedCopy);
				return;
			}

			this.incomingEvents.Enqueue(() =>
			{
				try
				{
					if (!this.idMapFromNative.TryGetValue(nativeConnectionId, out int fishNetId))
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
		private void HandleNativeDatagram(IntPtr context, ulong nativeConnectionId, IntPtr dataPtr, int length)
		{
			/* Security: reject invalid or oversized datagrams. Datagrams larger than the MTU
             * should never arrive from a compliant peer, but we validate defensively. */
			if (length <= 0 || length > MaxDatagramReceiveSize)
			{
				transport.NetworkManager?.LogWarning($"[WebTransport Server] Invalid datagram length {length} from connection {nativeConnectionId}. Dropping.");
				return;
			}

			/* Copy data to unmanaged memory on the native callback thread.
             * See HandleNativeStreamData for rationale. */
			IntPtr unmanagedCopy = System.Runtime.InteropServices.Marshal.AllocHGlobal(length);
			unsafe
			{
				System.Buffer.MemoryCopy((void*)dataPtr, (void*)unmanagedCopy, length, length);
			}

			if (System.Threading.Interlocked.Increment(ref this.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				transport.NetworkManager?.LogWarning("[WebTransport Server] Incoming event queue full; dropping datagram data.");
				System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedCopy);
				return;
			}

			this.incomingEvents.Enqueue(() =>
			{
				try
				{
					if (!this.idMapFromNative.TryGetValue(nativeConnectionId, out int fishNetId))
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