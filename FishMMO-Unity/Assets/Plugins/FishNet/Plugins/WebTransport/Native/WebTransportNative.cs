using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FishNet.Transporting.WebTransport.Native
{
    /// <summary>
    /// P/Invoke declarations for the FishMMO WebTransport native library.
    /// Wraps the C API from webtransport_api.h.
    ///
    /// Platform notes:
    ///   - Standalone (Win/Linux/Mac): DllImport loads the native library
    ///   - WebGL: All calls go through jslib (WebTransportJSLib.cs)
    ///   - Editor: Uses stubs; native library only loaded in builds
    /// </summary>
#if !UNITY_WEBGL || UNITY_EDITOR
    public static class WebTransportNative
    {
        /* ── Library name ────────────────────────────────────
         * Unity maps these per-platform via Plugin Importer:
         *   Windows: fishmmo_webtransport.dll
         *   Linux:   libfishmmo_webtransport.so
         *   macOS:   libfishmmo_webtransport.dylib
         * ─────────────────────────────────────────────────── */
#if UNITY_EDITOR_LINUX || (!UNITY_EDITOR && UNITY_STANDALONE_LINUX)
        private const string LIB = "fishmmo_webtransport";
#elif UNITY_EDITOR_WIN || (!UNITY_EDITOR && UNITY_STANDALONE_WIN)
        private const string LIB = "fishmmo_webtransport";
#elif UNITY_EDITOR_OSX || (!UNITY_EDITOR && UNITY_STANDALONE_OSX)
        private const string LIB = "fishmmo_webtransport";
#else
        private const string LIB = "fishmmo_webtransport";
#endif

        /* ── Opaque handles ───────────────────────────────── */

        /// <summary>
        /// Safe handle for a server instance.
        /// </summary>
        public class SafeServerHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeServerHandle() : base(true) { }
            protected override bool ReleaseHandle()
            {
                if (!IsInvalid)
                    wt_server_destroy(handle);
                return true;
            }
        }

        /// <summary>
        /// Safe handle for a client instance.
        /// </summary>
        public class SafeClientHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeClientHandle() : base(true) { }
            protected override bool ReleaseHandle()
            {
                if (!IsInvalid)
                    wt_client_destroy(handle);
                return true;
            }
        }

        /* ── Server API ────────────────────────────────────── */

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern SafeServerHandle wt_server_create(
            [MarshalAs(UnmanagedType.LPStr)] string certificatePath,
            [MarshalAs(UnmanagedType.LPStr)] string privateKeyPath,
            [MarshalAs(UnmanagedType.LPStr)] string alpn,
            [MarshalAs(UnmanagedType.LPStr)] string bindAddress,
            ushort port,
            uint maxClients,
            ref NativeCallbacks.ServerCallbacks callbacks,
            IntPtr context);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wt_server_destroy(IntPtr server);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wt_server_start(SafeServerHandle server);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wt_server_stop(SafeServerHandle server);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wt_server_poll(SafeServerHandle server, int timeoutUs);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wt_server_send_stream(
            SafeServerHandle server,
            ulong connectionId,
            byte[] data,
            int length);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wt_server_send_datagram(
            SafeServerHandle server,
            ulong connectionId,
            byte[] data,
            int length);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wt_server_disconnect(
            SafeServerHandle server, ulong connectionId);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr wt_server_get_client_address(
            SafeServerHandle server, ulong connectionId);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wt_server_get_client_count(SafeServerHandle server);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wt_server_get_max_clients(SafeServerHandle server);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wt_server_get_state(SafeServerHandle server);

        /* ── Client API ────────────────────────────────────── */

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern SafeClientHandle wt_client_create(
            ref NativeCallbacks.ClientCallbacks callbacks,
            IntPtr context);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wt_client_destroy(IntPtr client);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wt_client_connect(
            SafeClientHandle client,
            [MarshalAs(UnmanagedType.LPStr)] string serverName,
            [MarshalAs(UnmanagedType.LPStr)] string address,
            ushort port,
            int useTls);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wt_client_disconnect(SafeClientHandle client);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wt_client_poll(SafeClientHandle client, int timeoutUs);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wt_client_send_stream(
            SafeClientHandle client, byte[] data, int length);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wt_client_send_datagram(
            SafeClientHandle client, byte[] data, int length);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wt_client_is_connected(SafeClientHandle client);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wt_client_get_mtu(SafeClientHandle client);

        /* ── Utility ────────────────────────────────────────── */

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr wt_error_string(int errorCode);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr wt_version();
    }

    /// <summary>
    /// Delegate types matching the C callback function pointer types.
    /// Pinned at creation time to prevent GC collection while native code
    /// holds references. Marked [UnmanagedFunctionPointer] for AOT safety.
    /// </summary>
    public static class NativeCallbacks
    {
        /* ── Server delegates ──────────────────────────────── */

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ServerConnectDelegate(
            ulong connectionId, IntPtr remoteAddress);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ServerDisconnectDelegate(
            ulong connectionId, int errorCode);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ServerStreamDataDelegate(
            ulong connectionId, ulong streamId, IntPtr data, int length);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ServerDatagramDelegate(
            ulong connectionId, IntPtr data, int length);

        /// <summary>Bundled server callbacks matching wt_server_callbacks_t.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ServerCallbacks
        {
            public ServerConnectDelegate OnConnect;
            public ServerDisconnectDelegate OnDisconnect;
            public ServerStreamDataDelegate OnStreamData;
            public ServerDatagramDelegate OnDatagram;
        }

        /* ── Client delegates ──────────────────────────────── */

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ClientConnectDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ClientDisconnectDelegate(int errorCode);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ClientStreamDataDelegate(
            ulong streamId, IntPtr data, int length);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ClientDatagramDelegate(
            IntPtr data, int length);

        /// <summary>Bundled client callbacks matching wt_client_callbacks_t.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ClientCallbacks
        {
            public ClientConnectDelegate OnConnect;
            public ClientDisconnectDelegate OnDisconnect;
            public ClientStreamDataDelegate OnStreamData;
            public ClientDatagramDelegate OnDatagram;
        }
    }
#endif
}