# WebTransport Native Plugins

Native binaries for the WebTransport (QUIC/HTTP3) transport.

## Deployment-time Build Required

Binaries for Windows and macOS are **not** checked into the repository.
The Linux x86_64 binary (`libfishmmo_webtransport.so`) is committed for
convenience since Linux is the primary deployment target. All other
platform binaries must be built from the [FishMMO-WebTransport](../../../../../../../FishMMO-WebTransport)
C++ project before starting the game server.

### Per-platform Builds

| Platform | Directory | Build Command |
|----------|-----------|---------------|
| **Linux x86_64** | `linux_x86_64/` | `cd FishMMO-WebTransport && ./build_linux.sh` |
| **Windows x86_64** | `windows_x86_64/` | `powershell -ExecutionPolicy Bypass -File build_windows_schannel.ps1` (recommended for Editor) or `powershell -File build_windows.ps1` (static msquic/quictls, needs Perl) |
| **macOS x86_64** | `mac_x86_64/` | `./build_macos.sh` |

### Why build on the deployment server?

Each platform's binary links against the locally-installed OpenSSL (`libssl`)
and msquic libraries. Pre-built binaries from a different machine may have ABI
incompatibilities with the deployment server's libraries.

### What gets built?

| Platform | Output |
|----------|--------|
| Linux | `libfishmmo_webtransport.so` |
| Windows | `fishmmo_webtransport.dll` + `msquic.dll` |
| macOS | `libfishmmo_webtransport.dylib` |

### Development note

During development on Linux, only `libfishmmo_webtransport.so` is typically
present (built locally). Windows and macOS binaries must be built on their
respective hosts. The Unity Editor on Windows requires:

```
Plugins/windows_x86_64/fishmmo_webtransport.dll
Plugins/windows_x86_64/msquic.dll
```

Without those, Play Mode fails with `DllNotFoundException: fishmmo_webtransport`
when connecting (WebGL is unaffected — it uses the browser WebTransport API).

Recommended Windows Editor/local build (no OpenSSL/Perl):

```powershell
cd FishMMO-WebTransport
powershell -ExecutionPolicy Bypass -File build_windows_schannel.ps1
```

Then refresh Unity Assets and enter Play Mode again.
