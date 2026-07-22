using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FishNet.Transporting.WebTransport.Native
{
	// ── SafeHandles (always available for the type system) ───

	public class SafeServerHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		public SafeServerHandle() : base(true) { }

		/// <summary>
		/// Releases the native server handle. This method MAY be invoked on the
		/// GC finalizer thread (not just Dispose). The <see cref="WebTransportNative.IsLibraryDeinitialized"/>
		/// guard prevents calling into the native library after <c>wt_deinit()</c>
		/// has torn down msquic state — a race that would otherwise produce
		/// undefined behaviour (access violation / double-free).
		/// </summary>
		protected override bool ReleaseHandle()
		{
#if !UNITY_WEBGL || UNITY_EDITOR
			if (!IsInvalid && !WebTransportNative.IsLibraryDeinitialized)
				WebTransportNative.wt_server_destroy_impl(handle);
#endif
			return true;
		}
	}

	public class SafeClientHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		public SafeClientHandle() : base(true) { }

		/// <summary>
		/// Releases the native client handle. This method MAY be invoked on the
		/// GC finalizer thread (not just Dispose). The <see cref="WebTransportNative.IsLibraryDeinitialized"/>
		/// guard prevents calling into the native library after <c>wt_deinit()</c>
		/// has torn down msquic state — a race that would otherwise produce
		/// undefined behaviour (access violation / double-free).
		/// </summary>
		protected override bool ReleaseHandle()
		{
#if !UNITY_WEBGL || UNITY_EDITOR
			if (!IsInvalid && !WebTransportNative.IsLibraryDeinitialized)
				WebTransportNative.wt_client_destroy_impl(handle);
#endif
			return true;
		}
	}

	// ── Callback delegates (always available) ─────────────────
	// IMPORTANT: Every delegate's first parameter is IntPtr context,
	// matching the C function pointer signature void (*fn)(void* ctx, ...).
	// Without this, all arguments are shifted by one position.

	public static class NativeCallbacks
	{
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ServerConnectDelegate(
			IntPtr context, ulong connectionId, IntPtr remoteAddress);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ServerDisconnectDelegate(
			IntPtr context, ulong connectionId, int errorCode);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ServerStreamDataDelegate(
			IntPtr context, ulong connectionId, ulong streamId, IntPtr data, int length);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ServerDatagramDelegate(
			IntPtr context, ulong connectionId, IntPtr data, int length);

		[StructLayout(LayoutKind.Sequential)]
		public struct ServerCallbacks
		{
			public ServerConnectDelegate OnConnect;
			public ServerDisconnectDelegate OnDisconnect;
			public ServerStreamDataDelegate OnStreamData;
			public ServerDatagramDelegate OnDatagram;
		}

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ClientConnectDelegate(
			IntPtr context);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ClientDisconnectDelegate(
			IntPtr context, int errorCode);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ClientStreamDataDelegate(
			IntPtr context, ulong streamId, IntPtr data, int length);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ClientDatagramDelegate(
			IntPtr context, IntPtr data, int length);

		[StructLayout(LayoutKind.Sequential)]
		public struct ClientCallbacks
		{
			public ClientConnectDelegate OnConnect;
			public ClientDisconnectDelegate OnDisconnect;
			public ClientStreamDataDelegate OnStreamData;
			public ClientDatagramDelegate OnDatagram;
		}
	}

	// ── P/Invoke surface ─────────────────────────────────────

	public static class WebTransportNative
	{
		// ── Error code constants (match webtransport_api.h) ──────
		public const int WT_OK = 0;
		public const int WT_ERR_UNKNOWN = -1;
		public const int WT_ERR_INVALID_STATE = -2;
		public const int WT_ERR_CONNECT_FAILED = -3;
		public const int WT_ERR_TLS_FAILED = -4;
		public const int WT_ERR_SEND_FAILED = -5;
		public const int WT_ERR_BUFFER_FULL = -6;
		public const int WT_ERR_NOT_FOUND = -7;

		/// <summary>
		/// Returns a human-readable error string for a WebTransport error code.
		/// Uses the native wt_error_string() function when available; falls back
		/// to the code constant name for well-known codes, or the raw integer.
		/// </summary>
		public static string ErrorString(int errorCode)
		{
#if !UNITY_WEBGL || UNITY_EDITOR
			try
			{
				IntPtr ptr = wt_error_string(errorCode);
				if (ptr != IntPtr.Zero)
				{
					string native = Marshal.PtrToStringUTF8(ptr);
					if (!string.IsNullOrEmpty(native))
						return $"{native} (code {errorCode})";
				}
			}
			catch (Exception ex)
			{
				UnityEngine.Debug.LogWarning($"[WebTransport] ErrorString exception: {ex.Message}");
			}
#endif
			return errorCode switch
			{
				WT_OK => "OK",
				WT_ERR_UNKNOWN => "ERR_UNKNOWN",
				WT_ERR_INVALID_STATE => "ERR_INVALID_STATE",
				WT_ERR_CONNECT_FAILED => "ERR_CONNECT_FAILED",
				WT_ERR_TLS_FAILED => "ERR_TLS_FAILED",
				WT_ERR_SEND_FAILED => "ERR_SEND_FAILED",
				WT_ERR_BUFFER_FULL => "ERR_BUFFER_FULL",
				WT_ERR_NOT_FOUND => "ERR_NOT_FOUND",
				_ => $"Unknown ({errorCode})"
			};
		}

		/// <summary>
		/// Thread-safe init guard. 0 = not initialized, 1 = initializing/in progress.
		/// Prevents double-init races when called from multiple threads.
		/// </summary>
		private static int initGuard = 0;
		private static int deinitGuard = 0;
		private static volatile bool initialized = false;
		/// <summary>
		/// Set to true immediately before <c>wt_deinit()</c> is called.
		/// Read by <see cref="SafeServerHandle.ReleaseHandle"/> and
		/// <see cref="SafeClientHandle.ReleaseHandle"/> (which may run on the GC
		/// finalizer thread) to suppress native destroy calls after the library
		/// has been torn down.
		/// </summary>
		internal static volatile bool IsLibraryDeinitialized = false;

		public static bool IsInitialized => initialized;

		/// <summary>Ensure wt_init() is called exactly once before any native operations.</summary>
		/// <remarks>
		/// <b>Thread safety:</b> This method MUST be called from the Unity main thread only.
		/// It uses an Interlocked.CompareExchange guard as a secondary safety net, but
		/// concurrent calls from multiple threads are not a supported scenario.
		/// Both <c>EnsureInitialized</c> and <c>Deinitialize</c> are called from the main
		/// thread during controlled startup/shutdown sequences (see ServerSocket.StartConnection,
		/// WebTransport.Shutdown). Calling them concurrently would violate the expected
		/// lifecycle and may produce undefined behavior in the native library.
		/// </remarks>
			/// <returns><c>true</c> if the native library was successfully initialized (or was already initialized);
			/// <c>false</c> if initialization failed (caller should gracefully degrade / retry later).</returns>
		public static bool EnsureInitialized()
		{
			if (initialized) return true;

			/* Only one thread proceeds past this guard. */
			if (System.Threading.Interlocked.CompareExchange(ref initGuard, 1, 0) != 0)
			{
				/* Brief yield (50ms max). This method is documented as
				 * main-thread-only; the fallback exists only as a secondary
				 * safety net. 25 iterations x 2ms = 50ms, avoiding a visible
				 * frame hitch at startup. */
				for (int i = 0; i < 25 && !initialized; i++)
					System.Threading.Thread.Sleep(2);
				/* Timed out — caller will get an error from the native operation; they can retry next frame. */
				return initialized;
			}
			/* Double-check — another thread may have completed init while we waited for the guard. */
			if (initialized) { initGuard = 0; return true; }

#if !UNITY_WEBGL || UNITY_EDITOR
			int result = wt_init();
			if (result != 0)
			{
				UnityEngine.Debug.LogError($"[WebTransport] wt_init() failed: {ErrorString(result)}");
				initGuard = 0;
				return false;
			}
#endif
			initialized = true;
			IsLibraryDeinitialized = false; // reset for re-init after Deinitialize()
			initGuard = 0; // reset so Deinitialize()+EnsureInitialized() works for restarts
			return true;
		}

		/// <summary>Call wt_deinit() and reset state. Safe to call even if not initialized.</summary>
		/// <remarks>
		/// <b>Thread safety:</b> This method MUST be called from the Unity main thread only.
		/// See <see cref="EnsureInitialized"/> for details. The deinitGuard using
		/// Interlocked.CompareExchange is a secondary safety net; concurrent calls are
		/// not a supported scenario.
		/// </remarks>
		public static void Deinitialize()
		{
			if (System.Threading.Interlocked.CompareExchange(ref deinitGuard, 1, 0) != 0) return;
			if (!initialized) { deinitGuard = 0; return; }
#if !UNITY_WEBGL || UNITY_EDITOR
			// Set the deinitialized flag BEFORE calling wt_deinit() so that any
			// SafeHandle finalizer that fires concurrently will see the flag and
			// skip its P/Invoke — preventing use-after-free on msquic state.
			//
			// TOCTOU note: There is a narrow time-of-check-to-time-of-use window between
			// IsLibraryDeinitialized = true and wt_deinit() completing. If a SafeHandle
			// finalizer fires during wt_deinit(), it will see the flag as true and skip
			// its native destroy call — which is safe. The flag must be set BEFORE
			// wt_deinit() because after deinit completes, the msquic state is gone and
			// any P/Invoke from a finalizer would be a use-after-free. Setting the flag
			// first guarantees that every finalizer observes the shutdown regardless
			// of when it runs.
			IsLibraryDeinitialized = true;
			wt_deinit();
#endif
			initialized = false;
			deinitGuard = 0; // reset for subsequent cycles
		}

#if !UNITY_WEBGL || UNITY_EDITOR
		// Native binary availability (as of 2026-07):
		//   linux_x86_64:  ✅ libfishmmo_webtransport.so
		//   windows_x86_64: ❌ fishmmo_webtransport.dll (build with build_windows.ps1)
		//   mac_x86_64:     ❌ libfishmmo_webtransport.dylib (build with build_macos.sh)
		private const string LIB = "fishmmo_webtransport";

		// ── P/Invoke marshaling ───────────────────────────────
		// LPUTF8Str marshals .NET strings as UTF-8 to the native
		// library, which uses const char* (UTF-8) on all platforms.
		// Supported in Unity 2021.2+.
		// On Linux/macOS LPStr already maps to UTF-8; on Windows
		// LPStr would map to the system ANSI code page, corrupting
		// non-ASCII cert paths, ALPNs, and addresses.

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl, EntryPoint = "wt_server_create")]
		public static extern SafeServerHandle wt_server_create(
			[MarshalAs(UnmanagedType.LPUTF8Str)] string certificatePath,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string privateKeyPath,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string alpn,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string bindAddress,
			ushort port, uint maxClients,
			ref NativeCallbacks.ServerCallbacks callbacks,
			IntPtr context);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl, EntryPoint = "wt_server_destroy")]
		internal static extern void wt_server_destroy_impl(IntPtr server);

		public static void wt_server_destroy(SafeServerHandle server)
		{
			if (server != null && !server.IsInvalid)
			{
				wt_server_destroy_impl(server.DangerousGetHandle());
				server.SetHandleAsInvalid();
			}
		}

		/// <summary>
		/// Starts the WebTransport server, beginning to accept client connections.
		/// </summary>
		/// <param name="server">The server handle created by <see cref="wt_server_create"/>.</param>
		/// <returns>0 on success, or a negative error code on failure.</returns>
		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_server_start(SafeServerHandle server);

		/// <summary>
		/// Stops the WebTransport server, closing all active connections.
		/// </summary>
		/// <param name="server">The server handle to stop.</param>
		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern void wt_server_stop(SafeServerHandle server);

		/// <summary>
		/// Polls the server for pending events (new connections, disconnections, received data).
		/// Must be called regularly from the main thread.
		/// </summary>
		/// <param name="server">The server handle to poll.</param>
		/// <param name="timeoutUs">Poll timeout in microseconds. 0 means no wait (non-blocking).</param>
		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern void wt_server_poll(SafeServerHandle server, int timeoutUs);

		/// <summary>
		/// Sends reliable data to a connected client over a QUIC stream.
		/// </summary>
		/// <param name="server">The server handle.</param>
		/// <param name="connectionId">The native connection ID of the target client.</param>
		/// <param name="data">The byte array of data to send.</param>
		/// <param name="length">The number of bytes to send.</param>
		/// <returns>0 on success, or a negative error code on failure.</returns>
		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_server_send_stream(SafeServerHandle server, ulong connectionId, byte[] data, int length);

		/// <summary>
		/// Sends unreliable data to a connected client over a QUIC DATAGRAM frame.
		/// </summary>
		/// <param name="server">The server handle.</param>
		/// <param name="connectionId">The native connection ID of the target client.</param>
		/// <param name="data">The byte array of data to send.</param>
		/// <param name="length">The number of bytes to send.</param>
		/// <returns>0 on success, or a negative error code on failure.</returns>
		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_server_send_datagram(SafeServerHandle server, ulong connectionId, byte[] data, int length);

		/// <summary>
		/// Disconnects a specific client from the server.
		/// </summary>
		/// <param name="server">The server handle.</param>
		/// <param name="connectionId">The native connection ID of the client to disconnect.</param>
		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern void wt_server_disconnect(SafeServerHandle server, ulong connectionId);

		/// <summary>
		/// Gets the remote address string for a connected client.
		/// The returned pointer references internal storage within the server's connection struct.
		/// The string must be marshaled immediately (no free is required).
		/// </summary>
		/// <param name="server">The server handle.</param>
		/// <param name="connectionId">The native connection ID of the client.</param>
		/// <returns>A pointer to a null-terminated UTF-8 string, or <see cref="IntPtr.Zero"/> if not found.</returns>
		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr wt_server_get_client_address(SafeServerHandle server, ulong connectionId);

		/// <summary>
		/// Gets the current number of connected clients.
		/// </summary>
		/// <param name="server">The server handle.</param>
		/// <returns>The number of connected clients.</returns>
		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_server_get_client_count(SafeServerHandle server);

		/// <summary>
		/// Gets the configured maximum number of clients for this server.
		/// </summary>
		/// <param name="server">The server handle.</param>
		/// <returns>The maximum number of clients.</returns>
		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_server_get_max_clients(SafeServerHandle server);

		/// <summary>
		/// Gets the current server state.
		/// </summary>
		/// <param name="server">The server handle.</param>
		/// <returns>An integer representing the server state (0 = stopped, 1 = started, etc.).</returns>
		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_server_get_state(SafeServerHandle server);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl, EntryPoint = "wt_client_create")]
		public static extern SafeClientHandle wt_client_create(
			ref NativeCallbacks.ClientCallbacks callbacks,
			IntPtr context);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl, EntryPoint = "wt_client_destroy")]
		internal static extern void wt_client_destroy_impl(IntPtr client);

		public static void wt_client_destroy(SafeClientHandle client)
		{
			if (client != null && !client.IsInvalid)
			{
				wt_client_destroy_impl(client.DangerousGetHandle());
				client.SetHandleAsInvalid();
			}
		}

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_client_connect(SafeClientHandle client,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string serverName,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string address,
			ushort port, int useTls);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern void wt_client_disconnect(SafeClientHandle client);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern void wt_client_poll(SafeClientHandle client, int timeoutUs);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_client_send_stream(SafeClientHandle client, byte[] data, int length);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_client_send_datagram(SafeClientHandle client, byte[] data, int length);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_client_is_connected(SafeClientHandle client);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_client_get_mtu(SafeClientHandle client);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_init();

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern void wt_deinit();

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr wt_error_string(int errorCode);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr wt_version();

#else  // UNITY_WEBGL && !UNITY_EDITOR — stub implementations

		public static SafeServerHandle wt_server_create(
			string certificatePath, string privateKeyPath,
			string alpn, string bindAddress, ushort port,
			uint maxClients,
			ref NativeCallbacks.ServerCallbacks callbacks,
			IntPtr context) => new SafeServerHandle();

		internal static void wt_server_destroy_impl(IntPtr server) { }
		public static void wt_server_destroy(SafeServerHandle server) { }
		public static int wt_server_start(SafeServerHandle server) => -1;
		public static void wt_server_stop(SafeServerHandle server) { }
		public static void wt_server_poll(SafeServerHandle server, int timeoutUs) { }
		public static int wt_server_send_stream(SafeServerHandle s, ulong c, byte[] d, int l) => -1;
		public static int wt_server_send_datagram(SafeServerHandle s, ulong c, byte[] d, int l) => -1;
		public static void wt_server_disconnect(SafeServerHandle s, ulong c) { }
		public static IntPtr wt_server_get_client_address(SafeServerHandle s, ulong c) => IntPtr.Zero;
		public static int wt_server_get_client_count(SafeServerHandle s) => 0;
		public static int wt_server_get_max_clients(SafeServerHandle s) => 0;
		public static int wt_server_get_state(SafeServerHandle s) => 0;

		public static SafeClientHandle wt_client_create(
			ref NativeCallbacks.ClientCallbacks cb, IntPtr ctx) => new SafeClientHandle();
		internal static void wt_client_destroy_impl(IntPtr client) { }
		public static void wt_client_destroy(SafeClientHandle client) { }
		public static int wt_client_connect(SafeClientHandle c, string sn, string addr, ushort p, int tls) => -1;
		public static void wt_client_disconnect(SafeClientHandle c) { }
		public static void wt_client_poll(SafeClientHandle c, int timeoutUs) { }
		public static int wt_client_send_stream(SafeClientHandle c, byte[] d, int l) => -1;
		public static int wt_client_send_datagram(SafeClientHandle c, byte[] d, int l) => -1;
		public static int wt_client_is_connected(SafeClientHandle c) => 0;
		public static int wt_client_get_mtu(SafeClientHandle c) => 1200;
		public static int wt_init() => 0;
		public static void wt_deinit() { }
		public static IntPtr wt_error_string(int errorCode) => IntPtr.Zero;
		public static IntPtr wt_version() => IntPtr.Zero;
#endif
	}
}