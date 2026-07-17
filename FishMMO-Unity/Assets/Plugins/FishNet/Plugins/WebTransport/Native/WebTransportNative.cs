using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FishNet.Transporting.WebTransport.Native
{
	// ── SafeHandles (always available for the type system) ───

	public class SafeServerHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		public SafeServerHandle() : base(true) { }
		protected override bool ReleaseHandle()
		{
	#if !UNITY_WEBGL || UNITY_EDITOR
			if (!IsInvalid)
				WebTransportNative.wt_server_destroy_impl(handle);
	#endif
			return true;
		}
	}

	public class SafeClientHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		public SafeClientHandle() : base(true) { }
		protected override bool ReleaseHandle()
		{
	#if !UNITY_WEBGL || UNITY_EDITOR
			if (!IsInvalid)
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
		public const int WT_OK                 =  0;
		public const int WT_ERR_UNKNOWN        = -1;
		public const int WT_ERR_INVALID_STATE  = -2;
		public const int WT_ERR_CONNECT_FAILED = -3;
		public const int WT_ERR_TLS_FAILED     = -4;
		public const int WT_ERR_SEND_FAILED    = -5;
		public const int WT_ERR_BUFFER_FULL    = -6;
		public const int WT_ERR_NOT_FOUND      = -7;

		/// <summary>
		/// Thread-safe init guard. 0 = not initialized, 1 = initializing/in progress.
		/// Prevents double-init races when called from multiple threads.
		/// </summary>
		private static int _initGuard = 0;
		private static volatile bool _initialized = false;

		public static bool IsInitialized => _initialized;

		/// <summary>Ensure wt_init() is called exactly once before any native operations.</summary>
		public static void EnsureInitialized()
		{
			if (_initialized) return;

			/* Only one thread proceeds past this guard. */
			if (System.Threading.Interlocked.CompareExchange(ref _initGuard, 1, 0) != 0)
			{
				/* Another thread is initializing — spin-wait with generous timeout (~5s).
				 * MsQuic DLL loading / entropy gathering can take a moment on some systems. */
				for (int i = 0; i < 2500 && !_initialized; i++)
					System.Threading.Thread.SpinWait(2000);
				/* If still not initialized after timeout, proceed anyway —
				 * the caller will get an error from the native operation. */
				return;
			}

			/* Double-check — another thread may have completed init while we waited for the guard. */
			if (_initialized) { _initGuard = 0; return; }

	#if !UNITY_WEBGL || UNITY_EDITOR
			int result = wt_init();
			if (result != 0)
			{
				UnityEngine.Debug.LogError($"[WebTransport] wt_init() failed: {result}");
				_initGuard = 0;
				return;
			}
	#endif
			_initialized = true;
		}

		/// <summary>Call wt_deinit() and reset state. Safe to call even if not initialized.</summary>
		public static void Deinitialize()
		{
			if (!_initialized) return;
	#if !UNITY_WEBGL || UNITY_EDITOR
			wt_deinit();
	#endif
			_initialized = false;
			_initGuard = 0;
		}

	#if !UNITY_WEBGL || UNITY_EDITOR
		private const string LIB = "fishmmo_webtransport";

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl, EntryPoint = "wt_server_create")]
		public static extern SafeServerHandle wt_server_create(
			[MarshalAs(UnmanagedType.LPStr)] string certificatePath,
			[MarshalAs(UnmanagedType.LPStr)] string privateKeyPath,
			[MarshalAs(UnmanagedType.LPStr)] string alpn,
			[MarshalAs(UnmanagedType.LPStr)] string bindAddress,
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

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_server_start(SafeServerHandle server);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern void wt_server_stop(SafeServerHandle server);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern void wt_server_poll(SafeServerHandle server, int timeoutUs);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_server_send_stream(SafeServerHandle server, ulong connectionId, byte[] data, int length);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_server_send_datagram(SafeServerHandle server, ulong connectionId, byte[] data, int length);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern void wt_server_disconnect(SafeServerHandle server, ulong connectionId);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr wt_server_get_client_address(SafeServerHandle server, ulong connectionId);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_server_get_client_count(SafeServerHandle server);

		[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
		public static extern int wt_server_get_max_clients(SafeServerHandle server);

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
			[MarshalAs(UnmanagedType.LPStr)] string serverName,
			[MarshalAs(UnmanagedType.LPStr)] string address,
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
