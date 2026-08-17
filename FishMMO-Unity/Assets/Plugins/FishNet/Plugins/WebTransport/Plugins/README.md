# WebTransport Native Plugins

Native binaries for the WebTransport (QUIC/HTTP3) transport.

## Deployment-time Build Required

**No native binaries are checked into the repository** — `FishMMO-Unity/.gitignore`
excludes every platform subdirectory of this folder:

```gitignore
/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/*/
```

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
| **Windows x86_64** | `windows_x86_64/` | `powershell -File build_windows.ps1` |
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
respective deployment hosts. The Unity Editor on Linux will load the `.so`
automatically; testing on Windows/macOS requires building those binaries first.
