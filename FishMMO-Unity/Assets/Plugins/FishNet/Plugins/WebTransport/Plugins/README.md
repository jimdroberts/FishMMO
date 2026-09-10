# WebTransport Native Plugins

Native binaries for the WebTransport (QUIC/HTTP3) transport.

## Deployment-time Build Required

**No native binaries are checked into the repository** — `FishMMO-Unity/.gitignore`
excludes every platform subdirectory of this folder:

```gitignore
/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/*/
/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/windows_x86_64.meta
/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/mac_x86_64.meta
```

The first rule ends in `/`, so it matches directories only. Unity still generates a sidecar
`.meta` next to each platform folder, which that rule leaves untracked but *not* ignored —
hence the two explicit entries. `linux_x86_64.meta` is deliberately absent from the list: it
is tracked, so the folder keeps a stable Unity GUID on the platform everyone develops on.
Add a matching `.meta` line if a new platform folder is introduced.

Every platform, Linux included, must be built from the
[FishMMO-WebTransport](../../../../../../../FishMMO-WebTransport) C++ project
before starting the game server. The build writes its output straight into the
matching subdirectory here.

> Earlier revisions of this file claimed the Linux `.so` was committed. It is
> not, and never was tracked — the ignore rule above predates that note.

### Per-platform Builds

| Platform | Directory | Build Command |
|----------|-----------|---------------|
| **Linux x86_64** | `linux_x86_64/` | `cd FishMMO-WebTransport && ./build_linux.sh` |
| **Windows x86_64** | `windows_x86_64/` | `powershell -File build_windows.ps1` (or `build_local.bat`) |
| **Windows x86_64, from Linux** | `windows_x86_64/` | `cd FishMMO-WebTransport && ./build_windows_cross.sh` (Zig) |
| **macOS x86_64** | `mac_x86_64/` | `./build_macos.sh` |

`rebuild_only.ps1` / `rebuild_only.bat` are the incremental Windows path: they
reuse whichever tree already exists rather than reconfiguring.

### Why build on the deployment server?

Not because of a system OpenSSL — the library does not link one. Linux, macOS
and `build_windows.ps1 -Static` configure CMake with `-DWT_STATIC_MSQUIC=ON`,
which fetches msquic `v2.5.9` and links it, with its own quictls, statically
into the output. (Linking the system `libssl`/`libcrypto` *as well* put two
OpenSSL implementations in one library and segfaulted the server on the first
certificate load, which is why `CMakeLists.txt` only adds `OpenSSL::SSL` when
`WT_STATIC_MSQUIC=OFF`.) What ties a binary to its host is the toolchain and C++
runtime it was compiled with — plus, on Linux, `libnuma`, which the NUMA-aware
msquic static archive needs. A binary copied from another machine may fail to
load; the managed side catches the older-than-the-caller case up front through
the ABI check (`WT_ABI_VERSION` / `WebTransportNative.ExpectedAbiVersion`, both
**3** at present).

### What gets built?

| Platform | Output |
|----------|--------|
| Linux | `libfishmmo_webtransport.so` |
| Windows (default, NuGet msquic) | `fishmmo_webtransport.dll` + `msquic.dll` |
| Windows (`-Static`) | `fishmmo_webtransport.dll` only — msquic is inside it |
| macOS | `libfishmmo_webtransport.dylib` |

The Windows default does not build msquic at all: it compiles the seven wrapper
sources against the prebuilt **`Microsoft.Native.Quic.MsQuic.OpenSSL`** 2.5.9
NuGet package and ships that package's `msquic.dll` next to the wrapper. The
OpenSSL flavour is required for a server — `-Tls Schannel` selects the other
package, whose msquic reads the Windows certificate store and cannot load the
PEM files the server configs point at, so `wt_server_start` fails with
`WT_ERR_TLS_BACKEND` (-8) and no port is bound. Schannel is a client-only
escape hatch.

### Development note

During development on Linux, only `libfishmmo_webtransport.so` is typically
present (built locally). Windows binaries can be produced from a Linux host with
`build_windows_cross.sh`; macOS must be built on a Mac, because msquic's quictls
dependency carries platform-specific assembly. The Unity Editor on Linux will
load the `.so` automatically; testing on Windows/macOS requires building those
binaries first.

Full build details, the TLS-provider table, the ABI-version contract and the
transport-level limits live in
[FishMMO-WebTransport/README.md](../../../../../../../FishMMO-WebTransport/README.md).
