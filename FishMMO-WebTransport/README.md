# FishMMO WebTransport

WebTransport-over-HTTP/3 (QUIC) native library for FishMMO, wrapping
[Microsoft msquic](https://github.com/microsoft/msquic) v2.5.9.

Provides the C ABI surface consumed by the C# `WebTransport` FishNet transport
plugin (`FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/`).

## Architecture

```
src/
├── webtransport_api.cpp/h     Public C API (P/Invoke surface)
├── webtransport_internal.h    Shared macros, atomics, platform abstractions
├── server.cpp/h               QUIC server — listener, connection array, broadcast
├── client.cpp/h               QUIC client — connection, polling, deferred shutdown
├── session.cpp/h              Per-connection session — ref-counted, streams + datagrams
├── stream_manager.cpp/h       Bidirectional stream lifecycle — slot array, send/accept
├── datagram_queue.cpp/h       Thread-safe ring buffer for QUIC DATAGRAM frames
└── http3.cpp/h                HTTP/3 WebTransport handshake — SETTINGS, CONNECT, QPACK
```

**Channel mapping:** Channel 0 → QUIC bidirectional streams (reliable), Channel 1 → QUIC DATAGRAM frames (unreliable).

**Wire format:** every application message on a stream is length-delimited with
a QUIC varint (RFC 9000 §16), because a stream delivers bytes rather than
messages and the peer's writes may be coalesced or split. Browser sessions
additionally carry the WEBTRANSPORT_STREAM header (type `0x41`, on the wire as
`40 41`, plus the Session ID) once at the start of each data stream, and encode
datagrams as HTTP/3 Datagrams (RFC 9297): a Quarter Stream ID varint —
the CONNECT stream ID divided by four — ahead of the payload. Native raw-QUIC
peers exchange length-delimited messages on streams and bare payloads in
datagrams.

> Framing is a wire-format change: peers built before it cannot interoperate
> with peers built after it. Deploy both ends together.

**Protocol detection:** The server auto-detects browser clients (HTTP/3 — first byte `0x00`) vs native clients (raw QUIC — any other first byte) on the initial peer stream. Both paths are handled transparently.

## Building

CMake fetches msquic from source and links statically. Output goes directly to the Unity plugin directory (`../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/{platform}/`).

### Linux

```bash
# Dependencies (Arch)
sudo pacman -S cmake openssl gcc

# Dependencies (Ubuntu/Debian)
sudo apt-get install cmake libssl-dev build-essential

# Build
./build_linux.sh
```

Output: `libfishmmo_webtransport.so` in the Unity `linux_x86_64` plugin directory.

### Windows (native)

Build directly on Windows with Visual Studio 2022.

```powershell
# Dependencies
winget install Kitware.CMake

# Build
powershell -File build_windows.ps1
```

### Windows (cross-compile from Linux)

Cross-compile from a Linux host using Zig. No Visual Studio required.

```bash
# Dependencies (Arch)
sudo pacman -S zig

# Dependencies (manual)
# Download Zig 0.13+ from https://ziglang.org/download/
# Place the `zig` binary anywhere on PATH or in /tmp/zig-*/

# Build
./build_windows_cross.sh
```

The cross-compile script downloads the msquic NuGet package for the import library and runtime DLL, compiles with `zig c++ -target x86_64-windows-gnu`, and links via `lld-link --out-implib`.

Output (both methods): `fishmmo_webtransport.dll` + `msquic.dll` in the Unity `windows_x86_64` plugin directory.

### macOS

Must be built on a Mac — msquic's quictls dependency contains platform-specific assembly that cannot be cross-compiled.

```bash
# Dependencies
brew install cmake openssl@3

# Build
./build_macos.sh
```

Output: `libfishmmo_webtransport.dylib` in the Unity `mac_x86_64` plugin directory.

## API

See [src/webtransport_api.h](src/webtransport_api.h) for the complete C API surface:

| Function | Purpose |
|----------|---------|
| `wt_init()` / `wt_deinit()` | Lifecycle — initialises / closes the MsQuic API table |
| `wt_server_create()` / `wt_server_start()` / `wt_server_stop()` / `wt_server_destroy()` | Server lifecycle |
| `wt_server_poll()` | Drain pending shutdowns + datagrams (call each frame) |
| `wt_server_send_stream()` / `wt_server_send_datagram()` | Send to a client |
| `wt_server_disconnect()` | Disconnect a client |
| `wt_client_create()` / `wt_client_connect()` / `wt_client_disconnect()` / `wt_client_destroy()` | Client lifecycle |
| `wt_client_poll()` | Drain pending shutdowns + datagrams (call each frame) |
| `wt_client_send_stream()` / `wt_client_send_datagram()` | Send to the server |
| `wt_error_string()` | Human-readable error message |

All functions return `WT_OK` (0) on success or a negative error code.

## Platform Support

| Platform | Status | Build Method |
|----------|--------|-------------|
| **Linux x86_64** | ✅ | Native CMake (`./build_linux.sh`) |
| **Windows x86_64** | ✅ | Native CMake on Windows (`build_windows.ps1`) or Zig cross-compile from Linux (`./build_windows_cross.sh`) |
| **macOS x86_64** | ✅ | Native CMake on Mac (`./build_macos.sh`) |

> **⚠️ Deployment-time build required.** Native binaries are **not** checked into the
> repository (they are gitignored). The C++ project must be compiled directly on each
> deployment server before starting the game server. Each platform's binary links
> against the locally-installed OpenSSL and msquic libraries — pre-built binaries
> from a different machine may have ABI incompatibilities.
>
> **Pre-built binaries:** There are none. No native binary for any platform is tracked —
> `FishMMO-Unity/.gitignore` excludes every platform subdirectory of the plugins folder
> (`/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/*/`), Linux included. Every
> platform must compile the WebTransport C++ project before first run, on a fresh clone
> as much as on a deployment server.
>
> **Stripping for production:** On Linux, use `strip` to reduce binary size and remove
> debug symbols before deployment:
> ```bash
> strip --strip-debug libfishmmo_webtransport.so
> ```
> This typically halves the binary size with no functional impact.
>
> **Quick reference for deployment builds:**
> ```bash
> # Linux server (most common) — compile before first run
> cd FishMMO-WebTransport && ./build_linux.sh
>
> # Windows server — compile before first run
> powershell -File build_windows.ps1
>
> # macOS (client-only, no server support) — compile before first run
> ./build_macos.sh
> ```
>
> The build outputs directly into the Unity plugin directory at
> `../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/{platform}/`.

Built libraries are gitignored — they live in the Unity plugins directory
(`../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/{platform}/`)
and must be rebuilt per-deployment.

## Project Structure

```
FishMMO-WebTransport/
├── CMakeLists.txt              CMake project (fetches msquic via FetchContent)
├── build_linux.sh              Linux native build
├── build_windows.ps1           Windows native build (PowerShell)
├── build_windows_cross.sh      Windows cross-compile from Linux (Zig)
├── build_windows_schannel.ps1  Windows build against Schannel / NuGet msquic
├── build_local.bat             Windows convenience wrapper (Schannel, or -Static)
├── build_macos.sh              macOS native build
├── rebuild_only.ps1            Recompile without re-fetching dependencies
├── rebuild_only.bat            cmd.exe wrapper for rebuild_only.ps1
├── README.md
└── src/                        C++ source (7 .cpp + 7 .h)
```

### Generated, not tracked

| Path | Ignored by |
|---|---|
| `build/` | root `.gitignore` — the general `[Bb]uild/` rule |
| `build_win_schannel/` | root `.gitignore` — an explicit entry; `[Bb]uild/` matches that exact directory name only, so it does not cover this one |
| `openssl_cache.cmake` | root `.gitignore` — an explicit entry |

If you add a build script that writes to a new intermediate directory, add a matching entry
to the root `.gitignore` — only the exact name `build/` is covered by the general rule.