using FishNet.Managing;
using FishNet.Transporting.WebTransport.Native;
using FishNet.Transporting.WebTransport.WebGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishNet.Transporting.WebTransport.Client
{
	public class ClientSocket : CommonSocket, IDisposable
	{
		#region Private Configuration
		private string address = string.Empty;
		private ushort port;
		private string serverName = string.Empty;
		#endregion

		#region Queues
		private ConcurrentQueue<Packet> outgoing = new ConcurrentQueue<Packet>();
		#endregion

		private SafeClientHandle clientHandle;

		/// <summary>
		/// Last connect target for diagnostics (WebGL URL or host:port).
		/// Always available so editor / standalone builds can log without #if.
		/// </summary>
		private string lastConnectTarget = string.Empty;

#if UNITY_WEBGL && !UNITY_EDITOR
		private int webglIndex = -1;

		/// <summary>
		/// Full URL last passed to <see cref="WebTransportJSLib.WTConnect"/>
		/// (e.g. https://loginserver.fishmmo.com:7770).
		/// </summary>
		private string webglConnectUrl
		{
			get => lastConnectTarget;
			set => lastConnectTarget = value ?? string.Empty;
		}

		/// <summary>
		/// Maps JS session index → managed socket. Looked up from static
		/// [AOT.MonoPInvokeCallback] entry points because IL2CPP cannot
		/// marshal instance methods or lambdas to native code.
		/// </summary>
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, ClientSocket> webglSockets =
			new System.Collections.Concurrent.ConcurrentDictionary<int, ClientSocket>();

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
		/// Atomic guard to ensure StopConnection runs exactly once per session.
		/// Reset to 0 at the start of each <see cref="StartConnection"/>.
		/// </summary>
		private int stopGuard = 0;

		/// <summary>
		/// Monotonic session id. Incremented on each <see cref="StartConnection"/>.
		/// Native/JS callbacks capture the id and no-op if the session has been replaced
		/// (prevents stale disconnect/data from a prior Login session killing World connect).
		/// </summary>
		private int sessionGeneration = 0;
		/// <summary>
		/// Atomic guard to ensure Dispose runs exactly once per session.
		/// MUST be reset to 0 in <see cref="StartConnection"/> — otherwise reconnect
		/// skips WTDisconnect and leaves zombie browser WebTransport sessions
		/// (double WTConnect / index=1 with no clean teardown of index=0).
		/// </summary>
		private int disposed = 0;

		/// <summary>Packets accepted into the outgoing queue this session (FishNet→socket).</summary>
		private long wireQueuedCount;
		/// <summary>Successful WTSendStream/Datagram handoffs this session (socket→browser/native).</summary>
		private long wireSentOkCount;
		/// <summary>Failed WTSend handoffs this session.</summary>
		private long wireSentFailCount;
		/// <summary>Bytes handed to WT send this session.</summary>
		private long wireSentBytes;
		/// <summary>Drops because socket state was not Started when SendToServer was called.</summary>
		private long wireDropNotStartedCount;

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
		/// Pinned delegate handles (prevent GC collection of callback delegates).
		/// The struct field roots the managed delegate instances so the GC does
		/// not collect them while the native library holds function pointers.
		/// </summary>
		private NativeCallbacks.ClientCallbacks pinnedCallbacks;
		/// <summary>
		/// Pointer to unmanaged memory containing a Marshal.StructureToPtr copy
		/// of <see cref="pinnedCallbacks"/>. Passed to <c>wt_client_create</c>
		/// instead of <c>ref</c> to avoid GC relocation of the callback table.
		/// Allocated in <see cref="StartConnection"/>; freed in <see cref="StopConnection"/>.
		/// </summary>
		private IntPtr pinnedCallbacksPtr = IntPtr.Zero;

		/// <summary>
		/// Finalizer -- drains the incoming event queue without invoking actions.
		/// The .NET finalizer runs on an arbitrary thread, not the Unity main thread.
		/// The queued actions call FishNet transport callbacks which must execute
		/// on the main thread. In the abandoned-socket case this finalizer is a
		/// last resort; normal cleanup goes through <see cref="StopConnection"/>
		/// which invokes actions on the main thread and frees unmanaged memory.
		/// </summary>
		~ClientSocket()
		{
			while (incomingEvents.TryDequeue(out Action act))
			{
				// Invoke each action so that unmanaged memory held by native
				// callbacks (Marshal.AllocHGlobal) is freed via their finally blocks.
				// The action bodies check connection state and skip FishNet callbacks
				// when the transport is not in Started state.
				// We swallow exceptions — the finalizer thread cannot safely
				// propagate them, and the process is shutting down.
				try { act?.Invoke(); } catch { /* swallow — finalizer */ }
			}
			Dispose();
		}

		/// <summary>
		/// Releases all unmanaged resources (native handles, pinned callback table).
		/// Safe to call multiple times; subsequent calls are no-ops.
		/// Called from <see cref="StopConnection"/> on the main thread and from
		/// the finalizer on the finalizer thread.
		/// </summary>
		public void Dispose()
		{
			if (System.Threading.Interlocked.Exchange(ref this.disposed, 1) != 0)
				return;

#if UNITY_WEBGL && !UNITY_EDITOR
			if (webglIndex >= 0)
			{
				webglSockets.TryRemove(webglIndex, out _);
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

			if (this.pinnedCallbacksPtr != IntPtr.Zero)
			{
				System.Runtime.InteropServices.Marshal.FreeHGlobal(this.pinnedCallbacksPtr);
				this.pinnedCallbacksPtr = IntPtr.Zero;
			}
#endif

			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Comma-separated SHA-256 certificate fingerprints (hex) the browser should
		/// accept in place of a publicly trusted chain. Empty means "require a
		/// publicly trusted certificate", which is the correct production setting.
		/// Only consulted on WebGL — native builds validate against the platform
		/// trust store.
		/// </summary>
		private string serverCertificateHashes = "";

		/// <summary>
		/// Initializes the client socket. Must be called before <see cref="StartConnection"/>.
		/// </summary>
		/// <param name="mtu">
		/// Unused; the datagram send limit is enforced by the transport (GetMTU) and the
		/// receive limit by <see cref="CommonSocket.MaxDatagramReceiveSize"/>.
		/// </param>
		internal void Initialize(Transport t, int mtu)
		{
			base.transport = t;
		}

		/// <summary>
		/// Sets the pinned server certificate hashes used by WebGL builds.
		/// Must be called before <see cref="StartConnection"/>.
		/// </summary>
		internal void SetServerCertificateHashes(string hashes)
		{
			this.serverCertificateHashes = hashes ?? "";
		}

		/// <summary>
		/// Starts the client connection to the specified address.
		/// </summary>
		internal bool StartConnection(string address, ushort port, bool useTls)
		{
			var priorState = base.GetConnectionState();
			if (priorState != LocalConnectionState.Stopped)
			{
				// Prevent double WTConnect while a session is already Starting/Started.
				LogTransportWarning(
					$"[FishWT] StartConnection IGNORED — socket already {priorState} " +
					$"(target was {lastConnectTarget}). One FishNet client / one WT session only.");
				return false;
			}

			// Reset per-session guards. Without this, a previous Stop→Dispose leaves
			// disposed=1 and the next Stop skips WTDisconnect (zombie sessions + double connect).
			System.Threading.Interlocked.Exchange(ref this.disposed, 0);
			stopGuard = 0;
			// Invalidate any native/JS callbacks still in flight from the previous hop.
			System.Threading.Interlocked.Increment(ref sessionGeneration);
			System.Threading.Interlocked.Exchange(ref wireQueuedCount, 0);
			System.Threading.Interlocked.Exchange(ref wireSentOkCount, 0);
			System.Threading.Interlocked.Exchange(ref wireSentFailCount, 0);
			System.Threading.Interlocked.Exchange(ref wireSentBytes, 0);
			System.Threading.Interlocked.Exchange(ref wireDropNotStartedCount, 0);

			base.SetConnectionState(LocalConnectionState.Starting, false);

			// Drain stale incoming events. Actions check sessionGeneration and no-op if
			// they belong to a prior hop; stream/datagram handlers still free unmanaged
			// memory in their finally blocks when invoked.
			while (incomingEvents.TryDequeue(out Action act))
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				try { act?.Invoke(); } catch (System.Exception ex) { LogTransportWarning($"[WebTransport Client] Drain exception: {ex.ToString()}"); }
			}
			System.Threading.Interlocked.Exchange(ref this.incomingEventCount, 0);

			// Assert we are on the Unity main thread.
			if (mainThreadId < 0)
				mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
			System.Diagnostics.Debug.Assert(
				System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadId,
				"[WebTransport Client] StartConnection must be called from the Unity main thread.");

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
			webglConnectUrl = url;

			// Single managed log (jslib also logs). Include stack to find double StartConnection callers.
			UnityEngine.Debug.Log(
				$"[FishWT] WTConnect BEGIN url={url} disposedWasReset=1 " +
				$"stack=\n{UnityEngine.StackTraceUtility.ExtractStackTrace()}");

			webglPendingConnect = this;
			try
			{
				webglIndex = WebTransportJSLib.WTConnect(
					url,
					this.serverCertificateHashes,
					webglStaticOnOpen,
					webglStaticOnClose,
					webglStaticOnStream,
					webglStaticOnDatagram,
					webglStaticOnError);

				if (webglIndex < 0)
				{
					// Immediate failure: WebTransport unsupported, jslib missing, or create threw.
					// Fail fast → Stopped so ClientConnection does not spin the full 20s timeout.
					webglPendingConnect = null;
					LogTransportError(
						$"[FishWT] WTConnect failed immediately for {url} (index=-1). " +
						"Check browser WebTransport support and that WebTransport.jslib is in the WebGL framework.");
					base.SetConnectionState(LocalConnectionState.Stopped, false);
					return false;
				}

				webglSockets[webglIndex] = this;
				webglPendingConnect = null;

				UnityEngine.Debug.Log(
					$"[FishWT] WTConnect accepted index={webglIndex} url={url} (waiting for ready…). " +
					"If you already saw another WTConnect this click, fix double StartConnection.");

				// Configure congestion threshold to avoid silent data loss under
				// game data rates (default 500, up from the previous hardcoded 80).
				// WTSetStreamThreshold is reserved for future stream congestion control.
				// Currently a no-op in the jslib — uncomment when the implementation is ready.
				// WebTransportJSLib.WTSetStreamThreshold(webglIndex, 500);

				return true;
			}
			catch (System.Exception ex)
			{
				webglPendingConnect = null;
				LogTransportError($"[FishWT] WTConnect threw for {url}: {ex.Message}");
				base.SetConnectionState(LocalConnectionState.Stopped, false);
				throw;
			}
#else
			lastConnectTarget = $"{address}:{port}";
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

			// Copy the callback table to unmanaged memory so that the native
			// library's stored pointer survives GC compaction.
			int cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeCallbacks.ClientCallbacks>();
			this.pinnedCallbacksPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(cbSize);
			System.Runtime.InteropServices.Marshal.StructureToPtr(this.pinnedCallbacks, this.pinnedCallbacksPtr, false);

			clientHandle = WebTransportNative.wt_client_create(
				this.pinnedCallbacksPtr,
				IntPtr.Zero);

			if (clientHandle == null || clientHandle.IsInvalid)
			{
				// Free unmanaged callback table on error path.
				if (this.pinnedCallbacksPtr != IntPtr.Zero)
				{
					System.Runtime.InteropServices.Marshal.FreeHGlobal(this.pinnedCallbacksPtr);
					this.pinnedCallbacksPtr = IntPtr.Zero;
				}
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
				// Free unmanaged callback table on error path.
				if (this.pinnedCallbacksPtr != IntPtr.Zero)
				{
					System.Runtime.InteropServices.Marshal.FreeHGlobal(this.pinnedCallbacksPtr);
					this.pinnedCallbacksPtr = IntPtr.Zero;
				}
				base.SetConnectionState(LocalConnectionState.Stopped, false);
				return false;
			}

			return true;
#endif
		}

		internal bool StopConnection()
		{
			// Already fully down — nothing to do.
			if (base.GetConnectionState() == LocalConnectionState.Stopped)
			{
				System.Threading.Interlocked.Exchange(ref stopGuard, 0);
				return false;
			}

			// If a prior stop left us stuck in Stopping (or stopGuard set without
			// reaching Stopped), force-complete to Stopped so World→Scene hops can
			// StartConnection. Editor native QUIC sometimes never delivered Stopped
			// while tick datagrams kept flowing — WebGL path did not hit this.
			bool guardTaken = System.Threading.Interlocked.CompareExchange(ref stopGuard, 1, 0) == 0;
			if (!guardTaken && base.GetConnectionState() != LocalConnectionState.Stopping)
			{
				// Concurrent stop in progress; let the other call finish.
				return false;
			}

			// Drain stale incoming events before shutdown, invoking each so
			// that unmanaged memory is freed.
			while (incomingEvents.TryDequeue(out Action act))
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				try { act?.Invoke(); } catch (System.Exception ex) { LogTransportWarning($"[WebTransport Client] Drain exception: {ex.ToString()}"); }
			}
			System.Threading.Interlocked.Exchange(ref this.incomingEventCount, 0);

			if (base.GetConnectionState() != LocalConnectionState.Stopping &&
				base.GetConnectionState() != LocalConnectionState.Stopped)
			{
				base.SetConnectionState(LocalConnectionState.Stopping, false);
			}

			try
			{
				Dispose();
			}
			catch (System.Exception ex)
			{
				LogTransportError($"[WebTransport Client] Dispose during StopConnection: {ex}");
			}

			// Always land on Stopped so FishNet + ClientConnectionManager can hop.
			if (base.GetConnectionState() != LocalConnectionState.Stopped)
				base.SetConnectionState(LocalConnectionState.Stopped, false);

			// Allow a subsequent ForceStop / StartConnection cycle to re-enter stop.
			// StartConnection also resets stopGuard; clearing here avoids stuck guard
			// if Start is delayed after a forced hop.
			System.Threading.Interlocked.Exchange(ref stopGuard, 0);
			return true;
		}

		/// <summary>
		/// Hard-reset client socket to Stopped regardless of current state.
		/// Used when World→Scene (or any hop) hangs waiting for a clean stop —
		/// especially Unity Editor native QUIC vs WebGL.
		/// </summary>
		internal void ForceStopAndReset()
		{
			LogTransportWarning(
				$"[FishWT] ForceStopAndReset priorState={base.GetConnectionState()} target={lastConnectTarget}");
			// Clear guard so StopConnection can run even if a prior stop stalled.
			System.Threading.Interlocked.Exchange(ref stopGuard, 0);
			StopConnection();
			// Ensure ready for next StartConnection even if Dispose no-op'd.
			System.Threading.Interlocked.Exchange(ref this.disposed, 0);
			System.Threading.Interlocked.Exchange(ref stopGuard, 0);
			if (base.GetConnectionState() != LocalConnectionState.Stopped)
				base.SetConnectionState(LocalConnectionState.Stopped, false);
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
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				try { act?.Invoke(); } catch (Exception e) { LogTransportError(e.ToString()); }
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
			var state = base.GetConnectionState();
			if (state != LocalConnectionState.Started)
			{
				long n = System.Threading.Interlocked.Increment(ref wireDropNotStartedCount);
				if (n <= 8)
				{
					UnityEngine.Debug.LogWarning(
						$"[FishWT] SendToServer DROP state={state} ch={channelId} len={segment.Count} " +
						$"dropNotStarted={n} target={lastConnectTarget} " +
						"(FishNet may have Broadcast above this — wire never saw it)");
				}
				return;
			}

#if UNITY_WEBGL && !UNITY_EDITOR
			if (webglIndex < 0)
			{
				UnityEngine.Debug.LogError(
					$"[FishWT] SendToServer DROP webglIndex<0 len={segment.Count} target={lastConnectTarget}");
				return;
			}
			int sessionIndex = webglIndex;
#else
			int sessionIndex = -1;
#endif

			base.Send(outgoing, channelId, segment, -1);
			long q = System.Threading.Interlocked.Increment(ref wireQueuedCount);
			if (q <= 12 || (q % 50) == 0)
			{
				UnityEngine.Debug.Log(
					$"[FishWT] SendToServer QUEUED #{q} ch={channelId} len={segment.Count} " +
					$"index={sessionIndex} target={lastConnectTarget}");
			}
		}

		/// <summary>
		/// Snapshot of wire counters for handshake diagnostics (queued vs handed to WT).
		/// </summary>
		internal void GetWireStats(out long queued, out long sentOk, out long sentFail, out long sentBytes, out long dropNotStarted)
		{
			queued = System.Threading.Interlocked.Read(ref wireQueuedCount);
			sentOk = System.Threading.Interlocked.Read(ref wireSentOkCount);
			sentFail = System.Threading.Interlocked.Read(ref wireSentFailCount);
			sentBytes = System.Threading.Interlocked.Read(ref wireSentBytes);
			dropNotStarted = System.Threading.Interlocked.Read(ref wireDropNotStartedCount);
		}

		/// <summary>
		/// Dequeues all pending outgoing packets and sends them via the native library
		/// (standalone) or JS bridge (WebGL).
		/// </summary>
		private void DequeueOutgoing()
		{
			if (base.GetConnectionState() != LocalConnectionState.Started)
			{
				int dropped = 0;
				while (this.outgoing.TryDequeue(out Packet dead))
				{
					dropped++;
					dead.Dispose();
				}
				if (dropped > 0)
				{
					UnityEngine.Debug.LogError(
						$"[FishWT] DequeueOutgoing cleared {dropped} packets — state not Started " +
						$"(target={lastConnectTarget}). Broadcast never reached browser WT.");
				}
				return;
			}

#if UNITY_WEBGL && !UNITY_EDITOR
			if (webglIndex < 0)
			{
				UnityEngine.Debug.LogError("[FishWT] DequeueOutgoing webglIndex<0 — cannot send");
				return;
			}
#else
			if (clientHandle == null || clientHandle.IsInvalid)
				return;
#endif

			while (this.outgoing.TryDequeue(out Packet pkt))
			{
				try
				{
#if UNITY_WEBGL && !UNITY_EDITOR
				// Return true = handed to JS; browser Promise may still reject later.
				bool ok;
				if (pkt.Channel == 1)
					ok = WebTransportJSLib.WTSendDatagram(webglIndex, pkt.Data, pkt.Length);
				else
					ok = WebTransportJSLib.WTSendStream(webglIndex, pkt.Data, pkt.Length);

				if (ok)
				{
					long n = System.Threading.Interlocked.Increment(ref wireSentOkCount);
					System.Threading.Interlocked.Add(ref wireSentBytes, pkt.Length);
					if (n <= 12 || (n % 50) == 0)
					{
						UnityEngine.Debug.Log(
							$"[FishWT] WIRE SEND OK #{n} ch={pkt.Channel} len={pkt.Length} " +
							$"index={webglIndex} url={webglConnectUrl} " +
							"(bytes handed to browser WT; LoginServer should see app payload)");
					}
				}
				else
				{
					long n = System.Threading.Interlocked.Increment(ref wireSentFailCount);
					UnityEngine.Debug.LogError(
						$"[FishWT] WIRE SEND FAIL #{n} ch={pkt.Channel} len={pkt.Length} " +
						$"index={webglIndex} url={webglConnectUrl}");
				}
#else
				int result;
				if (pkt.Channel == 1)
					result = WebTransportNative.wt_client_send_datagram(this.clientHandle, pkt.Data, pkt.Length);
				else
					result = WebTransportNative.wt_client_send_stream(this.clientHandle, pkt.Data, pkt.Length);
				if (result == 0)
				{
					long n = System.Threading.Interlocked.Increment(ref wireSentOkCount);
					System.Threading.Interlocked.Add(ref wireSentBytes, pkt.Length);
					if (n <= 12 || (n % 50) == 0)
					{
						UnityEngine.Debug.Log(
							$"[FishWT] WIRE SEND OK #{n} ch={pkt.Channel} len={pkt.Length} native");
					}
				}
				else
				{
					System.Threading.Interlocked.Increment(ref wireSentFailCount);
					transport.NetworkManager?.LogWarning(
						$"[FishWT] WIRE SEND FAIL ch={pkt.Channel} len={pkt.Length}: " +
						$"{WebTransportNative.ErrorString((WebTransportNative.WTError)result)}");
				}
#endif
				}
				finally
				{
					pkt.Dispose();
				}
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
			if (index >= 0 && webglSockets.TryGetValue(index, out socket))
				return true;
			// Race: callback fired before StartConnection registered the index.
			socket = webglPendingConnect;
			return socket != null;
		}

		[AOT.MonoPInvokeCallback(typeof(WTIndexCallback))]
		private static void WebGlOnOpen(int index)
		{
			if (!TryGetWebGlSocket(index, out ClientSocket socket))
				return;
			// Backpressure: drop if event queue is saturated.
			if (System.Threading.Interlocked.Increment(ref socket.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref socket.incomingEventCount);
				socket.LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping open event.");
				return;
			}
			string url = socket.webglConnectUrl;
			socket.incomingEvents.Enqueue(() =>
			{
				UnityEngine.Debug.Log($"[FishWT] open index={index} url={url}");
				socket.SetConnectionState(LocalConnectionState.Started, false);
			});
		}

		[AOT.MonoPInvokeCallback(typeof(WTIndexCallback))]
		private static void WebGlOnClose(int index)
		{
			if (!TryGetWebGlSocket(index, out ClientSocket socket))
				return;
			// Backpressure: drop if event queue is saturated.
			if (System.Threading.Interlocked.Increment(ref socket.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref socket.incomingEventCount);
				socket.LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping close event.");
				return;
			}
			string url = socket.webglConnectUrl;
			socket.incomingEvents.Enqueue(() =>
			{
				socket.LogTransportWarning($"[FishWT] close index={index} url={url}");
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
			{
				Transport transport = socket.transport;
				if (transport == null)
				{
					socket.LogTransportWarning("[WebTransport Client] Dropping reliable stream data — transport not initialized.");
					return;
				}
				transport.HandleClientReceivedDataArgs(
					new ClientReceivedDataArgs(new ArraySegment<byte>(buf), Channel.Reliable, transport.Index));
			});
		}

		[AOT.MonoPInvokeCallback(typeof(WTDataCallback))]
		private static void WebGlOnDatagram(int index, IntPtr dataPtr, int length)
		{
			if (!TryGetWebGlSocket(index, out ClientSocket socket))
				return;
			if (length <= 0 || length > MaxDatagramReceiveSize)
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
			// WebGL is single-threaded, so managed allocations on the JS callback
			// thread are safe (no GC corruption). At high data rates this creates
			// GC pressure; if this becomes a bottleneck, switch to ByteArrayPool.
			byte[] buf = new byte[length];
			System.Runtime.InteropServices.Marshal.Copy(dataPtr, buf, 0, length);
			socket.incomingEvents.Enqueue(() =>
			{
				Transport transport = socket.transport;
				if (transport == null)
				{
					socket.LogTransportWarning("[WebTransport Client] Dropping unreliable datagram — transport not initialized.");
					return;
				}
				transport.HandleClientReceivedDataArgs(
					new ClientReceivedDataArgs(new ArraySegment<byte>(buf), Channel.Unreliable, transport.Index));
			});
		}

		[AOT.MonoPInvokeCallback(typeof(WTIndexCallback))]
		private static void WebGlOnError(int index)
		{
			if (!TryGetWebGlSocket(index, out ClientSocket socket))
				return;
			// Retrieve the real error message from the JS session before it
			// may be removed by the disconnect path. The message was stored
			// by the jslib onError / ready.catch / closed.catch paths.
			string errorDetail = null;
			try
			{
				IntPtr msgPtr = WebTransportJSLib.WTGetLastErrorMessage(index);
				if (msgPtr != IntPtr.Zero)
				{
					errorDetail = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(msgPtr);
					WebTransportJSLib.WASMFree(msgPtr);
				}
			}
			catch { /* best-effort — never let error retrieval prevent cleanup */ }
			// Backpressure: drop if event queue is saturated.
			if (System.Threading.Interlocked.Increment(ref socket.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref socket.incomingEventCount);
				socket.LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping error event.");
				return;
			}
			// Fail fast: mark Stopped immediately so ClientConnectionManager exits
			// its wait loop instead of spinning until ConnectTimeoutSeconds (20s).
			string url = socket.webglConnectUrl;
			string capturedDetail = errorDetail;
			socket.incomingEvents.Enqueue(() =>
			{
				if (!string.IsNullOrEmpty(capturedDetail))
				{
					if (capturedDetail.StartsWith("Ready failed:", StringComparison.Ordinal))
						socket.LogTransportError($"[FishWT] error index={index} url={url} — server rejected connection: {capturedDetail} (check TLS/cert/Origin/server config)");
					else if (capturedDetail.StartsWith("Create failed:", StringComparison.Ordinal))
						socket.LogTransportError($"[FishWT] error index={index} url={url} — create failed: {capturedDetail}");
					else if (capturedDetail.StartsWith("Closed:", StringComparison.Ordinal))
						socket.LogTransportError($"[FishWT] error index={index} url={url} — closed with error: {capturedDetail}");
					else if (capturedDetail == "WebTransport not supported")
						socket.LogTransportError($"[FishWT] error index={index} url={url} — WebTransport API not supported by this browser.");
					else
						socket.LogTransportError($"[FishWT] error index={index} url={url} detail={capturedDetail}");
				}
				else
				{
					socket.LogTransportError(
						$"[FishWT] error index={index} url={url} — WebTransport failed to open " +
						"(TLS/cert/origin/network, or LoginServer down). See browser [FishWT] console lines.");
				}
				// Clean JS session if still registered.
				if (socket.webglIndex >= 0)
				{
					webglSockets.TryRemove(socket.webglIndex, out _);
					try { WebTransportJSLib.WTDisconnect(socket.webglIndex); } catch { /* best effort */ }
					socket.webglIndex = -1;
				}
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
			int gen = sessionGeneration;
			if (System.Threading.Interlocked.Increment(ref this.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping connect event.");
				return;
			}
			incomingEvents.Enqueue(() =>
			{
				if (gen != sessionGeneration)
					return; // stale session (Login hop already replaced by World/Scene)
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
			int gen = sessionGeneration;
			if (System.Threading.Interlocked.Increment(ref this.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping disconnect event.");
				return;
			}
			incomingEvents.Enqueue(() =>
			{
				if (gen != sessionGeneration)
					return; // do not StopConnection on the new hop
				if (errorCode != 0)
					LogTransportWarning("[WebTransport Client] Disconnected: " + WebTransportNative.ErrorString((WebTransportNative.WTError)errorCode));
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

			int gen = sessionGeneration;

			// Copy to unmanaged memory on the callback thread — managed
			// allocations on QUIC threads can corrupt the IL2CPP GC.
			IntPtr unmanagedCopy = System.Runtime.InteropServices.Marshal.AllocHGlobal(length);
			unsafe
			{
				System.Buffer.MemoryCopy((void*)dataPtr, (void*)unmanagedCopy, length, length);
			}

			if (System.Threading.Interlocked.Increment(ref this.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping stream data.");
				System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedCopy);
				return;
			}

			incomingEvents.Enqueue(() =>
			{
				try
				{
					if (gen != sessionGeneration)
						return;
					if (base.GetConnectionState() != LocalConnectionState.Started)
						return;

					byte[] buffer = new byte[length];
					System.Runtime.InteropServices.Marshal.Copy(unmanagedCopy, buffer, 0, length);

					// Channel 0 = reliable (stream)
					ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
					try
					{
						transport.HandleClientReceivedDataArgs(
							new ClientReceivedDataArgs(segment, Channel.Reliable, transport.Index));
					}
					catch (System.Exception ex)
					{
						LogTransportWarning("[WebTransport Client] Receive error: " + ex.ToString());
					}
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
			if (length <= 0 || length > MaxDatagramReceiveSize)
			{
				LogTransportWarning($"[WebTransport Client] Invalid datagram length {length}. Dropping.");
				return;
			}

			int gen = sessionGeneration;

			IntPtr unmanagedCopy = System.Runtime.InteropServices.Marshal.AllocHGlobal(length);
			unsafe
			{
				System.Buffer.MemoryCopy((void*)dataPtr, (void*)unmanagedCopy, length, length);
			}

			if (System.Threading.Interlocked.Increment(ref this.incomingEventCount) > MaxIncomingEvents)
			{
				System.Threading.Interlocked.Decrement(ref this.incomingEventCount);
				LogTransportWarning("[WebTransport Client] Incoming event queue full; dropping datagram.");
				System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedCopy);
				return;
			}

			incomingEvents.Enqueue(() =>
			{
				try
				{
					if (gen != sessionGeneration)
						return;
					if (base.GetConnectionState() != LocalConnectionState.Started)
						return;

					byte[] buffer = new byte[length];
					System.Runtime.InteropServices.Marshal.Copy(unmanagedCopy, buffer, 0, length);

					// Channel 1 = unreliable (datagram)
					ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
					try
					{
						transport.HandleClientReceivedDataArgs(
							new ClientReceivedDataArgs(segment, Channel.Unreliable, transport.Index));
					}
					catch (System.Exception ex)
					{
						LogTransportWarning("[WebTransport Client] Receive error: " + ex.ToString());
					}
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
