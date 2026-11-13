# FishMMO WebTransport: third-party notices

The FishMMO WebTransport sources in this folder (`src/`, `tests/`, `cmake/`, the build scripts and
`patches/`) are first-party FishMMO code under the MIT licence in [LICENSE](LICENSE), the same
licence as the repository root.

The library they build, `libfishmmo_webtransport` (`fishmmo_webtransport.dll` on Windows), contains
or loads the third-party components below. Their full licence texts are in the repository's
[THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md), and the shipped client and server builds carry
them in `THIRD-PARTY-NOTICES.txt`.

## MsQuic 2.5.9

- Upstream: <https://github.com/microsoft/msquic> (tag `v2.5.9`)
- Licence: MIT
- Copyright (c) Microsoft Corporation.
- How it gets in:
  - Linux, macOS and `build_windows.ps1 -Static`: CMake `FetchContent` downloads the `v2.5.9`
    source into `build/_deps/msquic-src` and links it statically into the library
    (`-DWT_STATIC_MSQUIC=ON`).
  - Windows default (`build_windows_nuget.ps1`): the prebuilt `msquic.dll` from the
    `Microsoft.Native.Quic.MsQuic.OpenSSL` 2.5.9 NuGet package (MIT, Microsoft) is shipped next to
    `fishmmo_webtransport.dll`. The `-Tls Schannel` flavour uses
    `Microsoft.Native.Quic.MsQuic.Schannel` instead (also MIT, no OpenSSL inside).
- MsQuic's own `THIRD-PARTY-NOTICES` lists one component, OpenSSL (below).

### The local MsQuic patch

`patches/msquic-listener-bind-specific-address.patch` changes `src/core/library.c` in MsQuic so a
listener can bind one specific address. It is applied by `cmake/ApplyPatchOnce.cmake` as the
`FetchContent` patch step, so it is in every source-built library (Linux, macOS, Windows `-Static`)
and not in the prebuilt NuGet `msquic.dll`. The patch was written for FishMMO; the code it
produces is a modified MsQuic and stays under MsQuic's MIT licence and copyright notice.

## quictls (OpenSSL with QUIC support) 3.1.7+quic

- Upstream: <https://github.com/quictls/openssl>, as pinned by MsQuic 2.5.9 in
  `submodules/quictls` (`openssl-3.1.7-quic1` plus two commits, `ff36838bb6`)
- Licence: Apache-2.0
- Copyright (c) 1998-2024 The OpenSSL Project Authors
- Copyright (c) 1995-1998 Eric A. Young, Tim J. Hudson
- How it gets in: MsQuic's TLS provider, linked statically into MsQuic in the source builds, and
  built into the NuGet `msquic.dll` of the OpenSSL flavour. The library never links the system
  OpenSSL (see README.md).

## Build-only dependencies (not shipped)

MsQuic's `clog`, `googletest` and `xdp-for-windows` submodules, CMake, Ninja, Zig and the C/C++
toolchains are used to build the library and are not part of its output.

## Runtime libraries the library loads

On Linux the library is dynamically linked against the system's `libnuma`, `libstdc++`,
`libgcc_s`, `libm` and `libc`; those come from the player's or server's operating system and are
not shipped with FishMMO.
