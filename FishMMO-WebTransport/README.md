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

Linux, macOS and `build_windows.ps1 -Static` configure CMake with
`-DWT_STATIC_MSQUIC=ON`, which fetches msquic `v2.5.9` through `FetchContent` and
links it — quictls included — into one self-contained library. The Windows
default takes a different route: it compiles only the seven wrapper sources
against the prebuilt `Microsoft.Native.Quic.MsQuic.OpenSSL` NuGet package and
ships `msquic.dll` alongside the wrapper.

Every path writes its output straight into the Unity plugin directory
(`../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/{platform}/`).

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

`build_windows.ps1` is the entry point and has two paths. The default is the
fast one: it forwards to `build_windows_nuget.ps1`, which downloads
`Microsoft.Native.Quic.MsQuic.OpenSSL` 2.5.9 from nuget.org and drives `cl` /
`link` over the seven wrapper sources directly — no CMake, no Perl, no quictls
build. Intermediates live in `build_win_nuget/`, and `last_tls.txt` there
records which flavour the DLL in the Unity folder was last linked against.

```powershell
# Prerequisites: Visual Studio 2022+ with the C++ desktop workload and the
# Windows 10 SDK. CMake is only needed for -Static:
#   winget install Kitware.CMake
#   winget install Ninja-build.Ninja

powershell -File build_windows.ps1                 # NuGet msquic, OpenSSL — what a server needs
powershell -File build_windows.ps1 -Static         # CMake + static msquic/quictls, one DLL
powershell -File build_windows.ps1 -Static -Clean
powershell -File build_windows.ps1 -Tls Schannel   # client-only, see "TLS provider" below

powershell -File rebuild_only.ps1                  # incremental; reuses the recorded flavour
```

`build_local.bat` and `rebuild_only.bat` are cmd.exe wrappers that forward their
arguments to `build_windows.ps1` and `rebuild_only.ps1`. `-Static` never uses
the NuGet package, so `-Tls` does not apply to it: it always builds quictls.

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

The cross-compile script downloads the same
`Microsoft.Native.Quic.MsQuic.OpenSSL` 2.5.9 package (into
`build_win/msquic-win-openssl/`), stubs out the SAL annotations `msquic.h`
carries for MSVC, compiles each source with `zig c++ -target
x86_64-windows-gnu`, and links the DLL with `zig c++ -shared
-Wl,--out-implib`. `msquic.dll` is copied next to the output.

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

## TLS provider

msquic ships in two TLS flavours and only one of them can host a server for this
library.

| Flavour | NuGet package | Certificates | Usable as |
|---|---|---|---|
| OpenSSL (quictls) | `Microsoft.Native.Quic.MsQuic.OpenSSL` | PEM files on disk | server **and** client |
| Schannel | `Microsoft.Native.Quic.MsQuic.Schannel` | Windows certificate store only | client only |

Schannel answers `QUIC_STATUS_NOT_SUPPORTED` to the PEM paths this API takes, so
a server built that way never binds a port: `wt_server_start` returns
`WT_ERR_TLS_BACKEND` (-8), and `ServerSocket.DescribeStartFailure` on the C# side
turns that code into the rebuild command. OpenSSL is therefore the default
everywhere — `build_windows.ps1`, `build_windows_nuget.ps1` and
`build_windows_cross.sh` all select it, and `-Static`/Linux/macOS build quictls
from source. `-Tls Schannel` is kept only as an escape hatch for client builds on
hosts that must not carry a second TLS stack.

`wt_tls_provider()` reports what the loaded binary actually got: `"openssl"`,
`"schannel"`, or `"unknown"` before `wt_init()`. The managed view is
`WebTransportNative.TlsProvider`, logged once at initialisation.

Certificates are never generated. `wt_server_create` takes a PEM certificate
path, and `wt_server_start` fails with `WT_ERR_TLS_FAILED` when it is NULL,
empty, or unreadable — msquic refuses `QUIC_CREDENTIAL_TYPE_NONE` for servers on
every provider, and this library mints nothing self-signed.

## ABI version

Native binaries are not tracked, so the library that loads at runtime can be
older than the C# calling it. That used to surface as an
`EntryPointNotFoundException` from whichever P/Invoke happened to run first,
which said nothing about the cause. Both sides now carry a version that must
match:

| Side | Constant | File |
|---|---|---|
| Native | `WT_ABI_VERSION` = **3** | `src/webtransport_api.h` |
| Managed | `WebTransportNative.ExpectedAbiVersion` = **3** | `../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Native/WebTransportNative.cs` |

`WebTransportNative.EnsureInitialized` calls `wt_abi_version()` — safe before
`wt_init()`, it touches no msquic state — and refuses to continue on a mismatch,
naming the rebuild step for the running platform
(`WebTransportNative.RebuildHint`). A missing library and a library that predates
the export itself are reported the same way.

Bump both whenever an exported function is added, removed, or changes signature
or semantics; `tests/check_abi_version.sh` greps the pair and fails on a
mismatch.

| ABI | Surface |
|---|---|
| 1 | original surface up to `wt_server_set_allow_native_clients` |
| 2 | `+ wt_server_set_limits` (2026-09-07) |
| 3 | `+ wt_abi_version`, `wt_tls_provider`, `WT_ERR_TLS_BACKEND` (2026-09-09) |

## API

See [src/webtransport_api.h](src/webtransport_api.h) for the complete C API surface:

| Function | Purpose |
|----------|---------|
| `wt_init()` / `wt_deinit()` | Lifecycle — initialises / closes the MsQuic API table |
| `wt_server_create()` / `wt_server_start()` / `wt_server_stop()` / `wt_server_destroy()` | Server lifecycle |
| `wt_server_poll()` | Drain pending shutdowns + datagrams (call each frame) |
| `wt_server_send_stream()` / `wt_server_send_datagram()` | Send to a client |
| `wt_server_disconnect()` | Disconnect a client |
| `wt_server_set_limits()` | Transport-level abuse limits, before start: per-IP connect interval and concurrent cap, half-open cap, per-connection inbound token bucket with flood kick, per-connection datagram-ring share, per-connection H3 stream-context cap (see below) |
| `wt_client_create()` / `wt_client_connect()` / `wt_client_disconnect()` / `wt_client_destroy()` | Client lifecycle |
| `wt_client_poll()` | Drain pending shutdowns + datagrams (call each frame) |
| `wt_client_send_stream()` / `wt_client_send_datagram()` | Send to the server |
| `wt_server_set_expected_authority()` / `wt_server_set_allow_native_clients()` | Restrict the `:authority` a browser may CONNECT to; allow or refuse raw-QUIC peers |
| `wt_error_string()` / `wt_version()` | Human-readable error message / library version string |
| `wt_abi_version()` | `WT_ABI_VERSION` of the loaded binary — callable before `wt_init()` |
| `wt_tls_provider()` | `"openssl"`, `"schannel"`, or `"unknown"` before `wt_init()` |

All functions return `WT_OK` (0) on success or a negative error code, from
`WT_ERR_UNKNOWN` (-1) to `WT_ERR_TLS_BACKEND` (-8); `wt_error_string()` names
them.

### Transport-level limits

Every limit below is enforced inside the library, before a byte reaches the host application.
Defaults apply unless `wt_server_set_limits()` is called before `wt_server_start()`; a negative
argument keeps the default and `0` disables that limit.

| Limit | Default | Scope | On breach |
|---|---|---|---|
| Connect interval | 100 ms | per source IP | QUIC refuse (`server_listener_cb`) |
| Concurrent connections | 16 | per source IP | QUIC refuse |
| Half-open connections (slot held, no WebTransport session yet) | 512 | server | QUIC refuse |
| Inbound message rate (streams + datagrams) | 500/s, burst 1000 | per connection | drop; after 200 consecutive refusals the poll thread disconnects the client |
| Datagram-ring share | 64 of 1024 entries | per connection | drop that client's datagram only |
| HTTP/3 stream contexts before the session is up | 64 | per connection | abort the stream |
| Bytes in flight to a peer that is not reading | 8 MB (`WT_MAX_TOTAL_SEND_BUF`) | per connection | `wt_server_send_stream` returns `WT_ERR_BUFFER_FULL`; the host disconnects the slow client |

A native (raw-QUIC) client only becomes a session when its first stream byte arrives, so an idle
native client counts as half-open until it sends; the H3 handshake deadline (15 s) still bounds it.

The FishNet transport does not run on these defaults. `WebTransport.cs`
serialises its own inbound budget — 200 messages/s, burst 400 — and
`ServerSocket.StartConnection` passes it, together with its own
`InboundOverflowKickThreshold` of 100, to `wt_server_set_limits` before
`wt_server_start`, leaving the five connection-shaped limits — connect interval,
per-IP cap, half-open cap, datagram-ring share, H3 stream contexts — at `-1`
(native default). `WebTransport.SetInboundRateLimit()` and
`WebTransport.SetNativeLimits()` change them from code. The same bucket is
mirrored in managed code on the QUIC worker thread, so a flood is charged before
any unmanaged copy as well as inside the library.

### Tests

`tests/` is added to the build only with `-DWT_BUILD_TESTS=ON`; all three shipped
build scripts pass `OFF`.

```bash
cmake -B build -DWT_STATIC_MSQUIC=ON -DWT_BUILD_TESTS=ON && cmake --build build

bash tests/run_startup_e2e.sh build   # ABI, TLS provider, certificate-vs-backend failure; no certificates needed
bash tests/run_limits_e2e.sh build    # the limits above on loopback: bucket, kick, both caps, a paced sender
bash tests/check_abi_version.sh       # native WT_ABI_VERSION == C# ExpectedAbiVersion
```

`run_limits_e2e.sh` mints a throwaway CA and a `localhost` leaf in a temp
directory and hands it to the test through `SSL_CERT_FILE`, because the native
client validates TLS strictly.

## Platform Support

| Platform | Status | Build Method |
|----------|--------|-------------|
| **Linux x86_64** | ✅ | Native CMake (`./build_linux.sh`) |
| **Windows x86_64** | ✅ | Native CMake on Windows (`build_windows.ps1`) or Zig cross-compile from Linux (`./build_windows_cross.sh`) |
| **macOS x86_64** | ✅ | Native CMake on Mac (`./build_macos.sh`) |

> **⚠️ Deployment-time build required.** Native binaries are **not** checked into the
> repository (they are gitignored). The C++ project must be compiled directly on each
> deployment server before starting the game server. The Linux, macOS and
> `-Static` Windows builds compile msquic and quictls from source into the
> library, so the result is tied to the toolchain and C++ runtime of the machine
> that produced it; a binary copied from another machine may not load. The
> managed side refuses a mismatched binary up front — see **ABI version** above.
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
> # macOS — compile before first run
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
├── CMakeLists.txt              CMake project (FetchContent msquic v2.5.9, static by default)
├── build_linux.sh              Linux native build
├── build_macos.sh              macOS native build
├── build_windows.ps1           Windows entry point — NuGet msquic by default, -Static for CMake
├── build_windows_nuget.ps1     Windows build against the prebuilt msquic NuGet (-Tls OpenSSL|Schannel)
├── build_windows_cross.sh      Windows cross-compile from Linux (Zig + the OpenSSL NuGet)
├── build_local.bat             cmd.exe wrapper for build_windows.ps1
├── rebuild_only.ps1            Incremental rebuild — prefers the NuGet tree, else the CMake cache
├── rebuild_only.bat            cmd.exe wrapper for rebuild_only.ps1
├── README.md
├── src/                        C++ source (7 .cpp + 8 .h)
└── tests/                      wt_limits_e2e, wt_startup_e2e, check_abi_version.sh (-DWT_BUILD_TESTS=ON)
```

### Generated, not tracked

| Path | Ignored by |
|---|---|
| `build/` | root `.gitignore` — the general `[Bb]uild/` rule |
| `build_win_nuget/` | root `.gitignore` — an explicit entry; intermediates of `build_windows_nuget.ps1` (per-flavour msquic extraction, shared `obj/`, `last_tls.txt`) |
| `build_win/` | root `.gitignore` — an explicit entry; intermediates of `build_windows_cross.sh` |
| `build_win_schannel/` | root `.gitignore` — an explicit entry; the pre-2026-09-09 name of `build_win_nuget/`, safe to delete |
| `openssl_cache.cmake` | root `.gitignore` — an explicit entry |

`[Bb]uild/` matches that exact directory name only, which is why every other
intermediate directory needs a line of its own. If you add a build script that
writes to a new one, add a matching entry to the root `.gitignore`.