# FishMMO WebTransport Native Library

WebTransport-over-HTTP/3 (QUIC) native library for FishMMO, built on [msquic](https://github.com/microsoft/msquic).

## Requirements

- **CMake** 3.20+
- **OpenSSL** dev libraries
- **C/C++ compiler**: GCC/Clang (Linux/macOS), MSVC 2022 (Windows)
- **msquic**: Fetched automatically via CMake `FetchContent` (or use system package)

### Linux (Arch)
```bash
sudo pacman -S cmake openssl gcc
```

### Linux (Ubuntu/Debian)
```bash
sudo apt-get install cmake libssl-dev build-essential
```

### Windows
Install [Visual Studio 2022](https://visualstudio.microsoft.com/) with C++ workload and [CMake](https://cmake.org/).
OpenSSL can be installed via [vcpkg](https://vcpkg.io/):
```batch
vcpkg install openssl:x64-windows
```

## Building

### Linux
```bash
./build_linux.sh
```

### Windows
```batch
build_windows.bat
```

### macOS
```bash
cmake -S . -B build -DWT_BUILD_TESTS=OFF -DBUILD_SHARED_LIBS=ON
cmake --build build --config Release -j$(sysctl -n hw.ncpu)
cp build/libfishmmo_webtransport.dylib unity/mac_x86_64/
```

## Output

| Platform   | Output File                          |
|------------|--------------------------------------|
| Linux      | `unity/linux_x86_64/libfishmmo_webtransport.so` |
| Windows    | `unity/windows_x86_64/fishmmo_webtransport.dll` |
| macOS      | `unity/mac_x86_64/libfishmmo_webtransport.dylib` |

Copy these into the Unity project at:
```
Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/{platform}/
```

## API

See [src/webtransport_api.h](src/webtransport_api.h) for the full C API surface designed for P/Invoke from C#.
