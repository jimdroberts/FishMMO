
## FishMMO-WebTransport

**C++ native library** wrapping Microsoft msquic v2.5.9 to provide QUIC/WebTransport (HTTP/3) networking for all FishMMO game servers and native clients. Ships with a C# P/Invoke interop layer in `FishNet/Plugins/WebTransport/`.

### Native C++ Library (src/)
1. **QUIC Server** — `wt_server_create` / `wt_server_start` with PEM certificate TLS termination. Per-connection state array, broadcast send, deferred shutdown queue.  
2. **QUIC Client** — `wt_client_create` / `wt_client_connect` with SNI support, platform-trust-store TLS validation, `QUIC_CREDENTIAL_FLAG_INDICATE_CERTIFICATE_RECEIVED` for diagnostics.  
3. **Session Layer** — Ref-counted `wt_session_t` bridging streams and datagrams with tagged union parent pointer (server or client).  
4. **Stream Manager** — Fixed-size slot array (4096 entries) with mutex-protected bidirectional stream lifecycle. Send, accept, and buffered receive.  
5. **Datagram Queue** — Thread-safe ring buffer for QUIC DATAGRAM frames (RFC 9221).  
6. **HTTP/3 WebTransport Handshake** — Full HTTP/3 (RFC 9114) implementation: SETTINGS frame exchange, QPACK header encoding/decoding (RFC 9204 static table), WebTransport CONNECT with Origin CORS validation. Auto-detects browser clients (HTTP/3) vs native clients (raw QUIC) by inspecting the first byte of the initial peer stream.  
7. **WebTransport API** — Public C API (`webtransport_api.h`) with init/deinit, server/client lifecycle, send/receive, and error-string functions.  
8. **Platform Support** — Linux (native CMake), Windows (Zig cross-compilation → `x86_64-windows-gnu`), macOS (native CMake). Static msquic linkage via FetchContent.

### C# Interop Layer (Assets/Plugins/FishNet/Plugins/WebTransport/)
9. **WebTransport Transport** — FishNet `Transport` implementation with Multipass integration, channel mapping (0=Reliable/Streams, 1=Unreliable/Datagrams), MTU enforcement (1200 bytes).  
10. **P/Invoke Layer** — `WebTransportNative.cs` with `SafeServerHandle`/`SafeClientHandle` (SafeHandle subclasses), `[DllImport("fishmmo_webtransport")]` declarations, `UnmanagedFunctionPointer` callback delegates. Thread-safe lazy initialization via `Interlocked.CompareExchange`.  
11. **ClientSocket** — Manages native QUIC client session with `ConcurrentQueue<Action>` event marshaling from QUIC worker threads to Unity main thread. `Interlocked.CompareExchange` stop-guard prevents double-free.  
12. **ServerSocket** — Bidirectional ID mappings (`Dictionary<int, ulong>` FishNet↔native), client tracking, deferred disconnect via `_disconnectingNext` HashSet. Same AllocHGlobal + main-thread-marshal pattern as client.  
13. **Native Callback Safety** — Native callbacks use `Marshal.AllocHGlobal` + `Buffer.MemoryCopy` to transfer data to managed heap, then `Marshal.FreeHGlobal` in `finally` blocks on the main thread.  
14. **WebGL JavaScript Bridge** — `WebTransport.jslib` implements W3C WebTransport API for browser clients: bidirectional stream reader/writer, datagram reader/writer, retry pumps with error recovery, stream congestion control (80-pending cap + 10s timeout reset), Emscripten 2.x/3.x `dynCall` compatibility.  
15. **C# WebGL Bridge** — `WebTransportJSLib.cs` wraps JavaScript interop calls for WebGL builds.  
16. **Packet Struct** — `Packet` value type backed by FishNet `ByteArrayPool` with double-dispose guard.  
17. **Editor Panel** — Informational `WebTransportEditor` showing transport configuration status.  
18. **Platform Native Binaries** — Pre-built libraries for Linux x86_64 (`libfishmmo_webtransport.so`), Windows x86_64 (`fishmmo_webtransport.dll` + `msquic.dll`), and macOS x86_64 (`libfishmmo_webtransport.dylib`).

### Build System
19. **Unified Build Script** — `build_all.sh` builds Linux (native CMake), Windows (Zig cross-compile), and macOS (native CMake, must run on Mac). Automatically downloads msquic NuGet package for Windows cross-compilation.  
20. **Cross-Compilation** — Windows `.dll` cross-compiled from Linux via Zig 0.13 targeting `x86_64-windows-gnu`. SAL annotation stubs and `[[nodiscard]]` patches for non-MSVC compilation. Links against msquic import library via `lld-link --out-implib`.


*End of FishMMO Feature List*
