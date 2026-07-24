[![](https://dcbadge.vercel.app/api/server/9JQEYjkSNk?style=full)](https://discord.gg/9JQEYjkSNk)
[Join our Discord](https://discord.gg/9JQEYjkSNk)

# FishMMO

A modular, open-source MMO framework built on **Unity 6.3 LTS**, **FishNet**, **QUIC/WebTransport**, and **PostgreSQL**.

---

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Prerequisites](#prerequisites)
- [Installation Guide](#installation-guide)
  - [1. Clone the Repository](#1-clone-the-repository)
  - [2. Build the FishMMO-Installer](#2-build-the-fishmmo-installer)
  - [3. Run the Installer](#3-run-the-installer)
  - [4. Build the WebTransport C++ Library](#4-build-the-webtransport-c-library)
  - [5. Open the Unity Project](#5-open-the-unity-project)
- [Database Setup](#database-setup)
- [Unity Project Setup](#unity-project-setup)
  - [Build World Scene Details](#build-world-scene-details)
  - [Versioning](#versioning)
  - [FishMMO Builds](#fishmmo-builds)
  - [Patching — PatchGenerator and Updater](#patching--patchgenerator-and-updater)
- [Configuration](#configuration)
  - [Constants.cs — Client Domains](#constantscs--client-domains)
  - [Server Configuration Files](#server-configuration-files)
  - [Logging Configuration](#logging-configuration)
  - [Configuration Files — `FishMMO-Setup/`](#configuration-files--fishmmo-setup)
  - [FishMMO-Auth — Signing Keys & KEK](#fishmmo-auth--signing-keys--kek)
- [Infrastructure Setup](#infrastructure-setup)
  - [Configure Unity Hub](#configure-unity-hub)
  - [Configure PostgreSQL](#configure-postgresql)
  - [Configure pgBouncer](#configure-pgbouncer)
  - [Configure NGINX](#configure-nginx)
  - [NGINX Stream Configuration (UDP Game Traffic)](#nginx-stream-configuration-udp-game-traffic)
  - [TLS Certificate Setup for Game Servers](#tls-certificate-setup-for-game-servers)
- [Web Server Configuration](#web-server-configuration)
  - [ClientGate Middleware](#clientgate-middleware)
  - [Web Server Security & Environment Variables](#web-server-security--environment-variables)
- [Running the Servers](#running-the-servers)
  - [Launch Order](#launch-order)
  - [Starting Game Servers](#starting-game-servers)
  - [Starting Web Servers](#starting-web-servers)
  - [Running Multiple Scene Servers](#running-multiple-scene-servers)
- [FishMMO-AppHealthMonitor](#fishmmo-apphealthmonitor)
- [Optional Services](#optional-services)
  - [FishMMO-DiscordBot](#fishmmo-discordbot)
  - [FishMMO-CMS](#fishmmo-cms)
- [Client Setup](#client-setup)
  - [Client TLS Certificate Pinning](#client-tls-certificate-pinning)
- [Production Deployment](#production-deployment)
  - [Linux Config Hardening](#linux-config-hardening)
  - [Firewall Configuration](#firewall-configuration)
  - [Systemd Services](#systemd-services)
  - [Port Reference](#port-reference)
  - [Certbot Deploy Hook (TLS Renewal)](#certbot-deploy-hook-tls-renewal)
- [Architecture](#architecture)
  - [Connection Pipeline](#connection-pipeline)
  - [Server Initialization Order](#server-initialization-order)
  - [Flow Diagram](#flow-diagram)
- [License](#license)

---

## Overview

FishMMO is a complete multiplayer online game framework consisting of:

| Component | Description |
|---|---|
| **FishMMO-Unity** | Unity project containing client, server, and shared game code (560+ C# files) — full prediction pipeline, modular character visuals, threat-based AI, ECA trigger system |
| **FishMMO-Auth** | Transport-agnostic .NET authentication library (SRP-6a, X25519 ECDH, token auth, TOTP 2FA) |
| **FishMMO-Database (FishMMO-DB)** | PostgreSQL data-access layer using Entity Framework Core + Npgsql (36+ tables, 38+ services) |
| **FishMMO-WebTransport** | C++ native library wrapping MsQuic (QUIC/HTTP3) — P/Invoked from C# as a FishNet transport plugin |
| **FishMMO-Installer** | Cross-platform .NET 8 console tool that automates dependency installation |
| **FishMMO-Dependencies** | Centralised NuGet dependency aggregator (54 packages, netstandard2.1) — copies DLLs to Unity |
| **FishMMO-Logger** | Flexible logging library with file, email, and console backends |
| **FishMMO-SharedUtility** | Pure C# utility library shared between client and server projects |
| **FishMMO-AppHealthMonitor** | Daemon that monitors, auto-restarts, and health-checks server processes |
| **FishMMO-WebServers** | ASP.NET Core 8.0 web services — IPFetch (8080), Patcher (8090), and WebGL static server (8000) |
| **FishMMO-Patcher** | Client-side updater that applies versioned patch files |
| **FishMMO-Setup** | Configuration templates — nginx.conf, server .cfg files, appsettings.json, deploy hooks, stream config generator |
| **FishMMO-DiscordBot** | Discord bot bridging in-game chat with a Discord guild |
| **FishMMO-CMS** | ASP.NET Core CMS for launcher news, announcements, and web content |

The server architecture uses three server types:

- **LoginServer** — Handles account creation, SRP-6a authentication, character select, and TOTP 2FA.
- **WorldServer** — Manages world state, character routing, and scene server orchestration.
- **SceneServer** — Runs gameplay simulation for individual world scenes (chat, combat, inventory, guilds, etc.).

All three are launched from a single `GameServer` executable with a command-line argument (`LOGIN`, `WORLD`, or `SCENE`).

### Networking Architecture

FishMMO uses **QUIC/WebTransport** (RFC 9000) as its sole transport protocol — there is no TCP or WebSocket fallback for game traffic:

| Layer | Technology | Purpose |
|---|---|---|
| **Transport** | QUIC (UDP) via MsQuic C++ library | Encrypted, multiplexed transport with 0-RTT support |
| **Reliable Channel** | QUIC bidirectional streams | FishNet Channel 0 (game state, RPCs, broadcasts) |
| **Unreliable Channel** | QUIC DATAGRAM frames (RFC 9221) | FishNet Channel 1 (position updates, snapshots) |
| **WebGL/Browser** | Browser WebTransport API → HTTP/3 | Native browser QUIC without plugin |
| **Reverse Proxy** | NGINX L4 UDP stream + L7 HTTP | L7: TLS termination for HTTP APIs. L4: zero-copy UDP forwarding (no TLS termination for game traffic) |
| **HTTP TLS** | TLS 1.2/1.3 terminated by NGINX | NGINX handles HTTPS for `api.fishmmo.com` and `play.fishmmo.com` |
| **Game TLS** | QUIC/TLS 1.3 terminated by each game server | NGINX **cannot** terminate QUIC TLS — raw UDP packets pass through unmodified. Each game server **must** have certificates configured |

#### Default Architecture: Loopback + NGINX Reverse Proxy

All backend servers (LoginServer, WorldServer, SceneServer, IPFetch, Patcher, WebGL) are configured to **bind to `127.0.0.1` (loopback) by default**. NGINX acts as the sole public-facing reverse proxy, forwarding traffic to these loopback-bound upstream servers. This is a deliberate security posture:

- **Backend servers are never directly exposed to the internet** — only NGINX ports 80 and 443 (and UDP 7770-7999 via the stream module) are public.
- **NGINX handles TLS for HTTP APIs** (api.fishmmo.com, play.fishmmo.com) at Layer 7.
- **NGINX does NOT terminate TLS for game traffic.** QUIC/WebTransport uses UDP at Layer 4 — NGINX forwards raw encrypted QUIC packets without inspecting them. Each game server **must** have valid TLS certificates configured in its `.cfg` file (`CertificatePath`/`PrivateKeyPath`) because it terminates its own QUIC/TLS session. There is no way for NGINX to do this on the game server's behalf.
- **The real client IP is recovered via a one-time connection token** from the HTTP API, since game servers only see NGINX's loopback address.

**When to bind to `0.0.0.0` or a specific IP:** Only when a backend server runs on a **different machine** than NGINX. In that case, change the `Address` in the server's `.cfg` file (or the `BACKEND_IP` env var for stream config generation) to the server's network-facing IP, and ensure NGINX's `proxy_pass` targets that IP instead of `127.0.0.1`.

For the complete end-to-end connection flow, see [CONNECTION_PIPELINE.md](CONNECTION_PIPELINE.md). For server startup, see [SERVER_INITIALIZATION_ORDER.md](SERVER_INITIALIZATION_ORDER.md).

---

## Supported Platforms

| Platform | Client | Server |
|---|---|---|
| Windows 10/11 | Yes | Yes |
| Linux (Ubuntu/Debian, Arch/CachyOS) | Yes | Yes |
| macOS | Yes | Yes |
| WebGL | Yes (via browser WebTransport) | N/A |

| Requirement | Version |
|---|---|
| Unity | 6.3 LTS (6000.3.2f1) |
| .NET SDK | 8.0+ |
| PostgreSQL | 14+ |
| CMake | 3.20+ (for WebTransport C++ library) |
| C++ Compiler | GCC 9+/Clang 10+/MSVC 2022+ (C11/C++17) |
| Scripting Backend | IL2CPP |

---

## Prerequisites

- **Git** for cloning the repository
- **.NET 8.0 SDK**
- **Unity Hub** with **Unity 6.3 LTS** (the installer can install these for you)
- **CMake 3.20+** and a C++17 compiler (for building the WebTransport native library)
- **OpenSSL 3+** development headers (libssl-dev / openssl-devel)
- **PostgreSQL 14+** (the installer can install this for you)
- Administrator/root privileges for system-level installs
- Internet connectivity

> **Note:** The C++ WebTransport library links statically against msquic (fetched automatically by CMake), so you do **not** need to install msquic separately. OpenSSL is the only system-level dependency needed beyond a C++ compiler.

---

## Installation Guide

### 1. Clone the Repository

```bash
git clone https://github.com/jimdroberts/FishMMO.git
cd FishMMO
```

> **Tip:** Use the `dev` branch if it is more up-to-date than `main`:
> ```bash
> git checkout dev
> ```

### 2. Build the FishMMO-Installer

The **FishMMO-Installer** is the recommended way to set up all dependencies. It automates installing .NET, PostgreSQL, NGINX, PgBouncer, Unity Hub, building all C# projects, and setting up the database.

```bash
cd FishMMO-Installer
dotnet build
dotnet run --project FishMMO-Installer
```

Or run the compiled binary directly:
- **Windows:** `FishMMO-Installer\bin\Debug\net8.0\FishMMO-Installer.exe`
- **Linux:** `./FishMMO-Installer/bin/Debug/net8.0/FishMMO-Installer`

> **Windows:** Run from an elevated PowerShell (Administrator) for system-level installs.
> **Linux:** You will be prompted for `sudo` when needed.

### 3. Run the Installer

The installer supports two modes: **interactive menu** (default) and **CLI-driven** (for headless/automated deployment).

#### Interactive Menu Mode (default)

Run with no arguments to enter the menu:

```
=== FishMMO Installer ===
1 : Runtime & Tooling     — .NET EF Tool, ASP.NET Runtime, VS Build Tools
2 : Database              — PostgreSQL, PgBouncer, DB management
3 : Web Server            — NGINX, Let's Encrypt SSL, Firewall, Services
4 : Unity & Build         — Unity Hub, Unity Editor, C# project builds
5 : Configuration         — appsettings.json setup
0 : Quit
```

#### Sub-Menu: Runtime & Tooling

```
1 : Install DotNet EF Tool (dotnet-ef)
2 : Install ASP.NET Runtime
3 : Install Visual Studio Build Tools (Windows Only)
0 : Back
```

#### Sub-Menu: Database

```
1 : Install PostgreSQL
2 : Install PgBouncer (Connection Pooler)
3 : Install FishMMO Database (User/Schema/Initial Migration)
4 : Create New Database Migration
5 : Grant User Permissions on Database
6 : Delete FishMMO Database (DANGEROUS!)
7 : Configure PgBouncer (generate pgbouncer.ini + userlist.txt, Linux)
0 : Back
```

#### Sub-Menu: Web Server

```
1 : Install NGINX (Web Server/Reverse Proxy)
2 : Install/Renew Let's Encrypt Certificate (NGINX)
3 : Deploy FishMMO nginx.conf (from FishMMO-Setup/)
4 : Configure Firewall Rules (open ports 80, 443)
5 : Register FishMMO Web Servers as Services (systemd / NSSM)
0 : Back
```

#### Sub-Menu: Unity & Build

```
1 : Install Unity Hub
2 : Install Unity Editor (+Modules)
3 : Build all C# Projects
4 : Build FishMMO-Unity (Client/Server/Addressables)
0 : Back
```

#### Sub-Menu: Configuration

```
1 : Configure appsettings.json
0 : Back
```

#### CLI / Non-Interactive Mode

For headless servers and CI/CD pipelines, use CLI arguments:

```bash
# Show help
FishMMO-Installer --help

# Show version
FishMMO-Installer --version

# Install a single component
FishMMO-Installer --component postgresql

# Run health checks on all installed components
FishMMO-Installer --validate

# Simulate without making changes
FishMMO-Installer --dry-run --component nginx

# Unattended installation from a config file
FishMMO-Installer --non-interactive -f install-config.json
```

**`install-config.json` format:**

```json
{
  "components": ["dotnet-sdk", "postgresql", "nginx", "firewall", "systemd-services"],
  "configureFirewall": true,
  "firewallPorts": [80, 443],
  "registerSystemdServices": true,
  "validateAfterInstall": true
}
```

**Available component names for CLI use:**
`dotnet-sdk`, `aspnet-runtime`, `vs-build-tools`, `postgresql`, `pgbouncer`, `fishmmo-db`, `nginx`, `letsencrypt`, `unity-hub`, `unity-editor`, `build-projects`, `build-unity`, `appsettings`, `firewall`, `systemd-services`

> **Pre-flight checks** automatically run before any CLI-mode installation, verifying internet connectivity, disk space, memory, admin/sudo access, and port conflicts.

> **Download integrity:** Downloaded files are verified against SHA256 checksums in `checksums.json`. Corrupt or tampered downloads are rejected.

#### Pre-built Install Config Templates

`FishMMO-Setup/Development/` includes three ready-to-use install configs:

| Template | Purpose |
|---|---|
| `install-config.quickstart.json` | Minimal dev setup (dotnet-ef, postgresql, fishmmo-db, appsettings) |
| `install-config.web.json` | Web server setup (nginx, letsencrypt, firewall, systemd-services) |
| `install-config.full.json` | Complete production setup (all components including UDP game port firewall rules) |

The full template maps to:
```json
{
  "components": ["dotnet-ef", "aspnet-runtime", "postgresql", "pgbouncer",
    "fishmmo-db", "nginx", "letsencrypt", "firewall", "systemd-services", "appsettings"],
  "configureFirewall": true,
  "firewallPorts": [80, 443, "7770-7999/udp"],
  "registerSystemdServices": true,
  "webServers": ["ipfetch", "patcher", "webgl"],
  "validateAfterInstall": true
}
```

#### Recommended Installation Order (Fresh Setup)

| Step | Menu | Action | Windows | Linux |
|---|---|---|---|---|
| 1 | Runtime & Tooling | Install DotNet EF Tool | Yes | Yes |
| 2 | Runtime & Tooling | Install ASP.NET Runtime | Yes | Yes |
| 3 | Runtime & Tooling | Install VS Build Tools | Yes | *(skip)* |
| 4 | Unity & Build | Build all C# Projects | Yes | Yes |
| 5 | Unity & Build | Install Unity Hub | Yes | Yes |
| 6 | Unity & Build | Install Unity Editor (+Modules) | Yes | Yes |
| 7 | Database | Install PostgreSQL | Yes | Yes |
| 8 | Database | Install FishMMO Database | Yes | Yes |
| 9 | Database | Install PgBouncer | Yes | Yes |
| 10 | Database | Configure PgBouncer | *(manual)* | Yes |
| 11 | Web Server | Install NGINX | Yes | Yes |
| 12 | Web Server | Deploy FishMMO nginx.conf | Yes | Yes |
| 13 | Web Server | Configure Firewall Rules | Yes | Yes |
| 14 | Web Server | Install/Renew Let's Encrypt Certificate | Optional | Optional |
| 15 | Web Server | Register FishMMO Web Services | Optional | Yes |
| 16 | Configuration | Configure appsettings.json | Yes | Yes |

> **"Build all C# Projects"** discovers and builds all `.csproj` files under the repository root, including:
> - `FishMMO-Dependencies` — copies 54 dependency DLLs into `FishMMO-Unity/Assets/Dependencies/`
> - `FishMMO-Auth` — authentication library (copies DLL to Unity Dependencies)
> - `FishMMO-Database/FishMMO-DB` — database library
> - `FishMMO-Logger` — logging library (copies DLL to Unity Dependencies)
> - `FishMMO-SharedUtility` — shared utility library (copies DLL to Unity Dependencies)
> - `FishMMO-WebServers/IPFetchASP.NET` — login server discovery API
> - `FishMMO-WebServers/PatcherASP.NET` — patch delivery server
> - `FishMMO-WebServers/WebGLServerASP.NET` — WebGL static file server
> - `FishMMO-AppHealthMonitor` — server health monitor daemon
> - `FishMMO-DiscordBot` — Discord chat bridge bot
> - `FishMMO-CMS` — content management system

### 4. Build the WebTransport C++ Library

The WebTransport native library (`libfishmmo_webtransport`) is a C++ shared library that wraps Microsoft's [MsQuic](https://github.com/microsoft/msquic) QUIC implementation. It is P/Invoked from C# as FishNet's transport plugin. **This must be built before running the game server** — the Unity project expects the native binary at `Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/{platform}/`.

#### Linux (Native Build)

**Prerequisites:**
```bash
# Arch / CachyOS
sudo pacman -S cmake openssl gcc

# Ubuntu / Debian
sudo apt-get install cmake libssl-dev build-essential

# Fedora
sudo dnf install cmake openssl-devel gcc-c++
```

**Build:**
```bash
cd FishMMO-WebTransport
./build_linux.sh
```

**Output:** `libfishmmo_webtransport.so` placed directly into `../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/linux_x86_64/`

#### Windows (Native Build)

**Prerequisites:**
- Visual Studio 2022 with C++ workload
- CMake 3.20+ (`winget install Kitware.CMake`)
- OpenSSL (`vcpkg install openssl:x64-windows`)

**Build:**
```powershell
cd FishMMO-WebTransport
.\build_windows.ps1
```

**Output:** `fishmmo_webtransport.dll` + `msquic.dll` in `.../Plugins/windows_x86_64/`

#### Windows Cross-Compile from Linux (Zig)

If you develop on Linux but need a Windows `.dll` for client builds:

**Prerequisites:**
```bash
# Arch / CachyOS
sudo pacman -S zig
# Or download from https://ziglang.org/download/
```

**Build:**
```bash
cd FishMMO-WebTransport
./build_windows_cross.sh
```

This script:
1. Downloads the msquic NuGet package (`Microsoft.Native.Quic.MsQuic.Schannel/2.5.9`)
2. Extracts headers and DLL from the NuGet package
3. Compiles all `.cpp` files with `zig c++ -target x86_64-windows-gnu`
4. Links into a DLL importing `msquic.dll`
5. Copies both `fishmmo_webtransport.dll` and `msquic.dll` to the Unity plugins directory

#### macOS (Native Build)

Must be built on a Mac:
```bash
brew install cmake openssl@3
cd FishMMO-WebTransport
./build_macos.sh
```

**Output:** `libfishmmo_webtransport.dylib` in `.../Plugins/mac_x86_64/`

#### What CMake Does

The `CMakeLists.txt`:
- Fetches msquic v2.5.9 from GitHub (`FetchContent`) and statically links it
- Uses `quictls` (OpenSSL fork) as msquic's TLS backend
- Finds system OpenSSL for the wrapper library's own TLS needs
- Compiles 7 `.cpp` source files (server, client, session, stream_manager, datagram_queue, http3, webtransport_api)
- Outputs the shared library directly to the Unity project's Plugins directory

#### Build Options

| CMake Option | Default | Description |
|---|---|---|
| `BUILD_SHARED_LIBS` | ON | Build as shared library (`.so`/`.dylib`/`.dll`) |
| `WT_STATIC_MSQUIC` | ON | Statically link msquic (recommended) |
| `WT_BUILD_TESTS` | OFF | Build test programs |

### 5. Open the Unity Project

After building all C# projects and the WebTransport native library, open the Unity project:

1. Open **Unity Hub**
2. Click **ADD** → Select the `FishMMO-Unity` directory
3. Open the project with **Unity 6.3 LTS**
4. Wait for the initial asset import and script compilation to complete
5. Follow the [Unity Project Setup](#unity-project-setup) section below

---

## Database Setup

The FishMMO-Installer automates database creation (Database menu, option `3`), but here is what happens under the hood:

1. **PostgreSQL Installation** — The installer installs PostgreSQL via your platform's package manager (option `1`).
2. **Database + User Creation** — Creates the `fishmmo` database and a dedicated `fishmmo` user role (option `3`).
3. **EF Core Migration** — Creates and applies an initial Entity Framework Core migration (option `3`).
4. **Permissions** — Grants the user full privileges on the `public` schema (options `3` and `5`).

### Manual Database Setup (Without the Installer)

If you prefer to set up the database manually:

```bash
# Linux — become the postgres user
sudo -u postgres psql
```

```sql
CREATE USER fishmmo WITH PASSWORD 'your_secure_password';
CREATE DATABASE fishmmo OWNER fishmmo;
GRANT ALL PRIVILEGES ON DATABASE fishmmo TO fishmmo;
\c fishmmo
GRANT ALL ON SCHEMA public TO fishmmo;
```

Then apply the EF Core migrations:

```bash
cd FishMMO-Database/FishMMO-DB-Migrator
dotnet run
```

### Creating New Migrations

When your data model changes, create a new migration via the Installer (Database menu, option `4`) or manually:

```bash
cd FishMMO-Database/FishMMO-DB
dotnet ef migrations add YourMigrationName
cd ../FishMMO-DB-Migrator
dotnet run
```

### Database Configuration

**Environment-based overrides:** The database library supports layered configuration in this priority order:

1. `appsettings.json` (required)
2. `appsettings.{Environment}.json` (optional — e.g., `appsettings.Development.json`)
3. Environment variables (highest priority, use `__` for nesting)

Set the environment via `FISHMMO_ENVIRONMENT` (preferred), `DOTNET_ENVIRONMENT`, or `ASPNETCORE_ENVIRONMENT`.

**Environment variable overrides:**

| Variable | Maps To |
|---|---|
| `FISHMMO_ENVIRONMENT` | Environment name (`Development`, `Production`) |
| `Npgsql__Host` | PostgreSQL host |
| `Npgsql__Port` | PostgreSQL port |
| `Npgsql__Database` | Database name |
| `Npgsql__Username` | Database user |
| `Npgsql__Password` | Database password |

Example (fish shell):

```fish
set -Ux FISHMMO_ENVIRONMENT Production
set -Ux Npgsql__Password super_secret
```

Example (bash):

```bash
export FISHMMO_ENVIRONMENT=Production
export Npgsql__Password=super_secret
```

---

## Unity Project Setup

### Build World Scene Details

This caches important game world details (spawn points, teleporters, boundaries, scene metadata) for both clients and servers. **Run this whenever you add or modify a scene.**

**Unity Menu:** `FishMMO → Build → Rebuild World Scene Details`

This generates `WorldSceneDetailsCache` assets that are loaded at runtime by the WorldServer and SceneServer for scene routing and character placement.

### Versioning

Manage the project's semantic versioning from the Unity Editor.

**Unity Menu:** `FishMMO → Version → ...`

| Option | Effect |
|---|---|
| Increment Major | Increases the major version number |
| Increment Minor | Increases the minor version number |
| Increment Patch | Increases the patch version number |
| Reset Version | Resets all version fields to zero |

Each action updates `VersionConfig.asset` and Unity's bundle version. The final version is written to `version.txt` in the build output directory.

> **Optional:** Enable automatic patch increments by uncommenting the `UpdateBuildVersion()` call in `OnPostprocessBuild`.

### FishMMO Builds

Use the custom build menu in Unity to create clients, servers, and Addressables.

**Unity Menu:** `FishMMO → Build → ...`

The FishMMO Dashboard provides a comprehensive build interface:

| Build Option | Description |
|---|---|
| **Build Client** | Standalone client executable (Windows/Linux/macOS) |
| **Build Server** | Headless server executable (Login/World/Scene from one binary) |
| **Build Addressables** | Builds all Addressable asset bundles required by client and server |
| **Build All** | Builds Addressables, then client, then server in sequence |

**Build Environments:**

| Environment | Address Binding | Use Case |
|---|---|---|
| **Development** | `127.0.0.1` (loopback) | Local testing — all servers on the same machine |
| **Release** | Configurable via `.cfg` files | Production deployment — default is `127.0.0.1` (behind NGINX); change to server's network IP only if NGINX is on a separate machine |

The build process copies the appropriate `.cfg` and `appsettings.json` files from `FishMMO-Setup/Development/` or `FishMMO-Setup/Production/` into the build output.

> **Important:** Build Addressables first, then build client/server. The client and server depend on the Addressable bundles produced in the first step.

### Patching — PatchGenerator and Updater

#### PatchGenerator

A custom Unity Editor window for creating delta patches between game builds.

**Unity Menu:** `FishMMO → Patch → Patch Generator`

1. Select the **new** and **old** build directories.
2. Configure options, exclusions, and version details.
3. Click **Generate Patch** to create delta files and a manifest.

Patch files are ZIP archives named `<from_version>-<to_version>.zip` (e.g., `1.0.0-1.0.1.zip`).

#### Updater

The FishMMO Updater is a standalone .NET 8 executable that applies versioned patches to the client. It is launched automatically by the game launcher.

```
Updater.exe -version=1.0.0 -latestversion=1.0.1 -pid=1234 -exe=FishMMOClient.exe
```

Features: transactional patching with per-file backup and rollback, parallel file operations, SHA-256 hash verification on both sides of each diff, automatic client restart after successful patch.

#### Patch Server

Build the **PatcherASP.NET** web server and point it to a directory containing your generated patch `.zip` files. Clients query `/latest_version` to check for updates and download patches via `/{version}`.

---

## Configuration

### Constants.cs — Client Domains

The file [`FishMMO-Unity/Assets/Scripts/Shared/Implementation/Constants.cs`](FishMMO-Unity/Assets/Scripts/Shared/Implementation/Constants.cs) contains domain endpoints used by the client to connect to your infrastructure.

```csharp
public static class Configuration
{
    /// Unified API Host URL. NGINX routes to the correct backend by path.
    public static readonly string APIHost = "https://api.fishmmo.com/";

    /// Game server hostname. Clients connect to this host via QUIC/WebTransport (UDP).
    /// NGINX forwards game traffic at Layer 4 to loopback-bound game servers.
    public static readonly string GameHost = "game.fishmmo.com";
}
```

| Field | Purpose | Update When |
|---|---|---|
| `APIHost` | Base URL for IPFetch and Patcher API calls — NGINX reverse-proxies to loopback web servers | You use a different domain or run without NGINX |
| `GameHost` | Hostname for game QUIC/WebTransport connections — NGINX forwards UDP to loopback game servers | You use a different domain for game traffic |

> For local development, override these to `https://localhost/` and `localhost` respectively, or configure your hosts file. When running without NGINX, change `APIHost` to point directly to your web server's address and set `GameHost` to the game server's address.

### Server Configuration Files

Each server type reads a `.cfg` file from its working directory. Templates are in `FishMMO-Setup/Development/` and `FishMMO-Setup/Production/`.

#### LoginServer.cfg

```ini
ServerName=LoginServer
MaximumClients=4000
Address=127.0.0.1
Port=7770
StaleSceneTimeout=5
CertificatePath=/etc/fishmmo/certs/fullchain.pem
PrivateKeyPath=/etc/fishmmo/certs/privkey.pem
```

#### WorldServer.cfg

```ini
ServerName=World Server
MaximumClients=4000
Address=127.0.0.1
Port=7780
StaleSceneTimeout=5
CertificatePath=/etc/fishmmo/certs/fullchain.pem
PrivateKeyPath=/etc/fishmmo/certs/privkey.pem
```

#### SceneServer.cfg

```ini
ServerName=Scene Server
MaximumClients=4000
Address=127.0.0.1
Port=7790
StaleSceneTimeout=5
CertificatePath=/etc/fishmmo/certs/fullchain.pem
PrivateKeyPath=/etc/fishmmo/certs/privkey.pem
```

| Key | Description | Default |
|---|---|---|
| `ServerName` | Display name for logs and monitoring | varies |
| `MaximumClients` | Maximum concurrent connections | 4000 |
| `Address` | Bind address — `127.0.0.1` when behind NGINX (default); set to the server's network IP only if NGINX runs on a different machine | `127.0.0.1` |
| `Port` | Listen port (Login=7770, World=7780, Scene=7790+) | varies |
| `StaleSceneTimeout` | Seconds before idle scenes are unloaded | 5 |
| `CertificatePath` | PEM certificate for QUIC/TLS (game servers terminate their own TLS) | platform-dependent |
| `PrivateKeyPath` | PEM private key for QUIC/TLS | platform-dependent |
| `AutoVerifyAccounts` | Skip email verification (Development only — never in Production) | `true` (Dev) / `false` (Prod) |

**Format:** Simple `key=value` per line. Lines starting with `#` or `;` are comments. SMTP settings (LoginServer only) may also be present in the `.cfg` file and can be overridden via environment variables.

> **Certificate paths are required on every game server.** `CertificatePath` and `PrivateKeyPath` must point to valid PEM files on each server. NGINX cannot terminate QUIC TLS — it only forwards raw UDP. If certificates are missing, the server will fail to start or clients will be unable to connect. See [TLS Certificate Setup for Game Servers](#tls-certificate-setup-for-game-servers).

> **Production:** Edit the `Production/` templates before building. Each SceneServer needs a unique port if running multiple instances (e.g., 7790, 7791, 7792...).

> **About the bind address:** All server `.cfg` templates default to `Address=127.0.0.1` — servers listen on loopback and NGINX forwards public traffic to them via `proxy_pass`. This keeps backend servers off the public network. Only change `Address` if the server runs on a different machine than NGINX; in that case, set it to the machine's network-facing IP and update the corresponding NGINX upstream or stream config to point to that IP.

#### Platform-Specific Certificate Paths

| Platform | Default Certificate Path |
|---|---|
| Linux | `/etc/fishmmo/certs/fullchain.pem` |
| Windows | `C:\ProgramData\FishMMO\certs\fullchain.pem` |
| macOS | `/usr/local/share/fishmmo/certs/fullchain.pem` |
| Other | `certs/fullchain.pem` (relative to working directory) |

### Logging Configuration

All projects share a single canonical `logging.json` at [`FishMMO-Setup/logging.json`](FishMMO-Setup/logging.json). Each project's build copies it into the output directory automatically — operators only need to edit the one source file.

```json
{
  "LoggingManager": {
    "ConsoleAllowedLevels": ["Info", "Warning", "Error", "Critical", "Debug"]
  },
  "Loggers": []
}
```

**Log levels:** `Verbose`, `Debug`, `Info`, `Warning`, `Error`, `Critical`.

**Adding file logging** (edit `FishMMO-Setup/logging.json`):

```json
{
  "LoggingManager": {
    "ConsoleAllowedLevels": ["Info", "Warning", "Error", "Critical"]
  },
  "Loggers": [
    {
      "Type": "FileLoggerConfig",
      "LoggerType": "FileLogger",
      "Enabled": true,
      "AllowedLevels": ["Info", "Warning", "Error", "Critical", "Debug"],
      "LogDirectory": "logs",
      "FileName": "server.log"
    }
  ]
}
```

**Adding email alerts** (append to the `Loggers` array):

```json
{
  "Type": "EmailLoggerConfig",
  "LoggerType": "EmailLogger",
  "Enabled": true,
  "AllowedLevels": ["Error", "Critical"],
  "SmtpServer": "smtp.example.com",
  "Port": 587,
  "EnableSsl": true,
  "Username": "alerts@example.com",
  "Password": "********",
  "From": "alerts@example.com",
  "To": "ops@example.com",
  "Subject": "FishMMO Alert"
}
```

> **Security:** Keep SMTP credentials out of source control. Load secrets from environment variables or a separate secrets file. See [FishMMO-Logger README](FishMMO-Logger/README.md) for full configuration details.

**Runtime override:** Place a modified `logging.json` in the working directory — it takes precedence over the bundled copy. The log level can also be overridden via the `FISHMMO_LOG_LEVEL` environment variable (e.g. `Debug`, `Verbose`).

### Configuration Files — `FishMMO-Setup/`

All project configuration lives in [`FishMMO-Setup/`](FishMMO-Setup/) as the single source of truth. **Non-sensitive defaults (host, port, database name) are stored in JSON. Secrets (passwords, tokens, API keys) are set via environment variables** and override the JSON values at runtime. Each template includes a `_comment` field listing the env var names to set.

**Directory structure:**

```
FishMMO-Setup/
├── logging.json                              # Shared logging — all projects
├── nginx.conf                                # NGINX reverse-proxy config (L4 UDP + L7 HTTP)
├── gen-fishmmo-stream-config.sh              # Generates per-port NGINX UDP stream configs
├── Development/                              # Dev / local configurations
│   ├── appsettings.json                      # Unity server Npgsql (dev)
│   ├── appsettings.AppHealthMonitor.json      # Process supervisor config
│   ├── appsettings.DiscordBot.json           # Discord token + Npgsql
│   ├── appsettings.IpFetchServer.json        # IP-fetch web server base
│   ├── appsettings.IpFetchServer.Development.json  # Dev overrides (DB connection string)
│   ├── appsettings.Patcher.json              # Patch delivery web server
│   ├── appsettings.WebGLServer.json          # Static asset web server
│   ├── appsettings.CMS.json                  # CMS web app
│   ├── appsettings.Database.json             # Npgsql pool / retry template
│   ├── install-config.full.json              # Full production install template
│   ├── install-config.quickstart.json         # Minimal dev install template
│   ├── install-config.web.json               # Web server install template
│   ├── LoginServer.cfg / WorldServer.cfg / SceneServer.cfg
├── Production/                                # Production configurations
│   ├── appsettings.json                      # Unity server Npgsql
│   ├── appsettings.IpFetchServer.Production.json  # Prod overrides (empty — must set env vars)
│   ├── LoginServer.cfg / WorldServer.cfg / SceneServer.cfg
├── deploy-hooks/
│   └── certbot-fishmmo.sh                   # Let's Encrypt renewal deploy hook
```

**How it works:** Each project's `.csproj` copies the appropriate file from `FishMMO-Setup/` into its build output directory, renaming it to `appsettings.json` (or `logging.json`). At runtime, applications resolve config with a **working-directory-first** pattern: if a modified file exists in the working directory, it overrides the bundled copy. Environment variables (prefixed `FISHMMO_` or using `__` separator) provide the highest-priority overrides.

**Unity Npgsql example** ([`Development/appsettings.json`](FishMMO-Setup/Development/appsettings.json)):

```json
{
  "_comment": "Non-sensitive defaults only. Override secrets via env vars: Npgsql__Password, Npgsql__Username",
  "Npgsql": {
    "Database": "fishmmo",
    "Username": "",
    "Password": "",
    "Host": "127.0.0.1",
    "Port": "5432"
  }
}
```

**Production Npgsql example** ([`Production/appsettings.json`](FishMMO-Setup/Production/appsettings.json)):

```json
{
  "_comment": "Non-sensitive defaults only. Override secrets via env vars: Npgsql__Password, Npgsql__Username",
  "Npgsql": {
    "Database": "fishmmo",
    "Username": "",
    "Password": "",
    "Host": "127.0.0.1",
    "Port": "5432"
  }
}
```

If using PgBouncer, change `Npgsql.Port` to `6432` (see [Configure pgBouncer](#configure-pgbouncer)).

**Database pool and retry configuration** ([`Development/appsettings.Database.json`](FishMMO-Setup/Development/appsettings.Database.json)) — sets `CommandTimeout: 10`, `ConnectionTimeout: 15`, `MinPoolSize: 5`, `MaxPoolSize: 100`, Npgsql retry policy (3 retries, 20ms base, 10ms jitter), and optional query performance tracking.

> **Security:** Never commit `appsettings.json` with real passwords. Use environment variables for secrets in production (see [Database Setup](#database-setup) for environment variable override syntax).

### FishMMO-Auth — Signing Keys & KEK

The authentication system uses HMAC-SHA256 for token signing. In production, signing keys are wrapped with an AES-256 Key Encryption Key (KEK).

#### Setting the KEK

Set the environment variable `FISHMMO_SIGNING_KEY_KEK_BASE64` to a 32-byte base64-encoded AES-256 key:

```bash
# Generate a new KEK (do this once, store securely)
KEK=$(openssl rand -base64 32)
echo "Your KEK: $KEK"

# Set it in your environment
export FISHMMO_SIGNING_KEY_KEK_BASE64="$KEK"
```

**Linux (systemd):** Add to your service unit file:
```ini
[Service]
Environment=FISHMMO_SIGNING_KEY_KEK_BASE64=your_base64_kek_here
```

**Linux (fish shell, persistent):**
```fish
set -Ux FISHMMO_SIGNING_KEY_KEK_BASE64 your_base64_kek_here
```

#### How It Works

1. The LoginServer generates a fresh HMAC-SHA256 signing key at startup.
2. The key is wrapped using AES-256-GCM with the KEK and an AAD bound to the LoginServer's database ID.
3. The wrapped key envelope is stored in the database.
4. World/Scene servers fetch and unwrap the signing key to validate client tokens.
5. Keys are rotated each time the LoginServer restarts.

> **Without a KEK:** If `FISHMMO_SIGNING_KEY_KEK_BASE64` is not set, the LoginServer will log a warning and tokens will not be issued. Client authentication will fail on World/Scene servers.

#### Auth Protocol Constants

The authentication library has several compile-time security constants (see `BaseAuthenticatorCore`):

| Constant | Default | Description |
|---|---|---|
| `AuthStaleTtlSeconds` | 15 | Stale-auth sweep interval |
| `AuthHardDeadlineSeconds` | 60 | Hard authentication deadline |
| `MaxPendingAuthConnections` | 10,000 | Concurrent pending auth cap |
| `HandshakeIpDebounceSeconds` | 0.25 | Per-IP handshake rate limit |
| `MaxGlobalHandshakesPerSecond` | 500 | Global handshake cap |
| `MaxTotpAttempts` | 5 | TOTP attempts per connection |
| `MaxTotpFailuresPerUsername` | 15 | Per-account TOTP lockout threshold |
| `TotpUsernameLockoutDuration` | 5 min | TOTP lockout duration |

---

## Infrastructure Setup

### Configure Unity Hub

1. **Add the Project:**
   - Click **ADD** in Unity Hub.
   - Select the `FishMMO-Unity` directory.

2. **Install Required Modules:**
   - Go to the **Installs** tab.
   - Click the gear icon next to your Unity 6.3 LTS version → **Add Modules**.
   - Install:
     - **Linux Build Support (IL2CPP and Mono)**
     - **Linux Dedicated Server Build Support**
     - **Mac Build Support (IL2CPP and Mono)**
     - **WebGL Build Support**
     - **Windows Build Support (IL2CPP)**
     - **Windows Dedicated Server Build Support**

3. Open the **FishMMO-Unity** project from Unity Hub and wait for full compilation.

### Configure PostgreSQL

The installer handles PostgreSQL installation. For manual setup, ensure:

1. PostgreSQL is running and listening on `localhost:5432`.
2. The `fishmmo` user can connect with password authentication.
3. `pg_hba.conf` allows local password connections:
   ```
   local   fishmmo   fishmmo   md5
   host    fishmmo   fishmmo   127.0.0.1/32   md5
   ```

**Securing PostgreSQL (Production):**

The installer's `PostgreSQLHardening` module applies:
- Password-based authentication only (no trust)
- Restricted listen addresses (`localhost` or private network)
- Connection limits
- Query logging for audit trails

### Configure pgBouncer

PgBouncer is a lightweight PostgreSQL connection pooler that sits between your game servers and PostgreSQL, reducing connection overhead. Each game server opens many short-lived connections — PgBouncer multiplexes these onto a smaller pool of persistent connections.

#### Installation

Use the Installer (Database menu, option `2`):
- **Linux:** Package manager install + `systemctl enable --now pgbouncer`
- **Windows:** `winget` (preferred) or Chocolatey fallback

#### Configuration

After installation, configure PgBouncer to pool connections to your FishMMO database.

**Linux:** The installer's "Configure PgBouncer" option (Database menu, option `7`) generates `pgbouncer.ini` and `userlist.txt` for you. Otherwise, manually edit `/etc/pgbouncer/pgbouncer.ini`.

**Windows:** Edit `pgbouncer.ini` in the install directory.

##### pgbouncer.ini

```ini
[databases]
fishmmo = host=127.0.0.1 port=5432 dbname=fishmmo

[pgbouncer]
listen_addr = 127.0.0.1
listen_port = 6432

auth_type = md5
auth_file = /etc/pgbouncer/userlist.txt

pool_mode = transaction
default_pool_size = 20
min_pool_size = 5
max_client_conn = 200
max_db_connections = 50

server_idle_timeout = 300
client_idle_timeout = 0
query_timeout = 30

log_connections = 0
log_disconnections = 0
log_pooler_errors = 1

admin_users = fishmmo
stats_users = fishmmo
```

##### userlist.txt

```
"fishmmo" "your_secure_password"
```

> On Linux, generate the password hash:
> ```bash
> psql -c "SELECT concat('\"', usename, '\" \"', passwd, '\"') FROM pg_shadow WHERE usename = 'fishmmo';" postgres
> ```

##### Update appsettings.json

Point your game servers at PgBouncer (port `6432`) instead of PostgreSQL directly:

```json
{
  "Npgsql": {
    "Port": "6432"
  }
}
```

##### Verify

**Linux:**
```bash
sudo systemctl status pgbouncer
sudo systemctl restart pgbouncer
# Test connection through PgBouncer
psql -h 127.0.0.1 -p 6432 -U fishmmo -d fishmmo
```

**Windows:**
```powershell
sc.exe query pgbouncer
```

### Configure NGINX

NGINX is the **single public-facing reverse proxy** for all FishMMO traffic. Every backend server binds to `127.0.0.1` (loopback) and NGINX forwards public traffic to them — backend servers are never directly reachable from the internet. The configuration in `FishMMO-Setup/nginx.conf` implements a dual-layer architecture:

- **Layer 7 (HTTP/HTTPS):** Terminates TLS for `api.fishmmo.com` and `play.fishmmo.com`, then reverse-proxies requests to the loopback-bound ASP.NET web servers.
- **Layer 4 (UDP Stream):** Forwards raw QUIC/UDP packets to loopback-bound game servers. **NGINX does not and cannot terminate QUIC TLS** — the game servers receive the encrypted QUIC packets directly and must have their own certificates.

> **Critical:** Because NGINX only forwards UDP at Layer 4, **every game server must be provisioned with valid TLS certificates** via the `CertificatePath` and `PrivateKeyPath` settings in its `.cfg` file. NGINX's Let's Encrypt integration covers the HTTP hostnames only. See [TLS Certificate Setup for Game Servers](#tls-certificate-setup-for-game-servers) for the full procedure.

**NGINX upstream routing table:**

| Public Hostname | Protocol | NGINX Role | Upstream Backend | Backend Bind |
|---|---|---|---|---|
| `api.fishmmo.com` | HTTPS | TLS termination → reverse proxy | IPFetch (`127.0.0.1:8080`) + Patcher (`127.0.0.1:8090`) | loopback |
| `play.fishmmo.com` | HTTPS | TLS termination → reverse proxy | WebGL Static Server (`127.0.0.1:8000`) | loopback |
| `game.fishmmo.com` | UDP (QUIC) | L4 stream forward | Game Servers (`127.0.0.1:7770-7999`) | loopback |
| `game.fishmmo.com:443` | TCP | Returns 444 (close) | — | — |

> **If a backend server is on a different machine than NGINX:** Change the `proxy_pass` (HTTP) or stream `server` block (UDP) to target that machine's network IP instead of `127.0.0.1`, and update the server's `.cfg` `Address` accordingly.

Deploy the nginx config via the Installer (Web Server menu, option `3`) or manually:

```bash
sudo cp FishMMO-Setup/nginx.conf /etc/nginx/nginx.conf
sudo nginx -t && sudo nginx -s reload
```

### NGINX Stream Configuration (UDP Game Traffic)

Game traffic uses UDP ports **7770-7999** forwarded at Layer 4 through NGINX's stream module. Each port gets its own `server {}` block. By default, streams forward to **`127.0.0.1`** — the game servers are expected to be on the same machine, bound to loopback.

> **NGINX does not terminate TLS for these streams.** It forwards the raw encrypted QUIC packets. Each game server terminates its own QUIC/TLS session, so certificates must be configured in every server's `.cfg` file (see [TLS Certificate Setup for Game Servers](#tls-certificate-setup-for-game-servers)).

#### Auto-Generating Stream Configs

Use the `gen-fishmmo-stream-config.sh` script to generate per-port configs:

```bash
sudo ./FishMMO-Setup/gen-fishmmo-stream-config.sh
```

This script:
- Generates individual `.conf` files in `/etc/nginx/stream.d/` for each port
- **Port ranges:**
  - **7770-7779** — Login Servers
  - **7780-7789** — World Servers
  - **7790-7999** — Scene Servers
- Configurable via environment variables:

| Variable | Default | Description |
|---|---|---|
| `STREAM_DIR` | `/etc/nginx/stream.d` | Output directory for generated `.conf` files |
| `BACKEND_IP` | `127.0.0.1` | IP where game servers are listening — change only if servers run on a different machine |
| `LOGIN_START` / `LOGIN_END` | `7770` / `7779` | Login server port range |
| `WORLD_START` / `WORLD_END` | `7780` / `7789` | World server port range |
| `SCENE_START` / `SCENE_END` | `7790` / `7999` | Scene server port range |
| `PROXY_TIMEOUT` | `300s` | Idle session timeout |

- Uses atomic temp-directory-then-replace to avoid leaving partial configs
- Validates generated configs with `nginx -t` before replacing live files

**Configuration applied per port:**

```nginx
server {
    listen 7770 udp;
    proxy_pass 127.0.0.1:7770;
    proxy_timeout 300s;
    proxy_upload_rate 100m;
    proxy_download_rate 100m;
}
```

> **Multi-machine deployment:** If game servers run on a separate machine from NGINX, set `BACKEND_IP` to that machine's IP and update each server's `.cfg` `Address` to bind to its own network interface instead of `127.0.0.1`.

Reload with zero downtime:
```bash
sudo nginx -s reload
```

### TLS Certificate Setup for Game Servers

> **This is not optional.** NGINX cannot terminate QUIC/TLS for game traffic — it only forwards raw UDP packets at Layer 4. Every LoginServer, WorldServer, and SceneServer **must** have valid TLS certificates configured in its `.cfg` file (`CertificatePath` and `PrivateKeyPath`). Without certificates, game servers will fail to start or clients will be unable to connect.

Unlike a typical web setup where NGINX terminates all TLS, **each FishMMO game server terminates its own QUIC/TLS session on loopback**. NGINX forwards raw UDP packets at Layer 4 without inspecting or decrypting them. This means:

- Game data is **end-to-end encrypted** — NGINX never sees plaintext game traffic.
- TLS certificates must be **present on each game server machine** (by default, the same machine as NGINX).
- The real client IP is recovered via a **one-time connection token** from the HTTP API (IPFetch issues a SHA-256 hashed token, the client passes it in the QUIC handshake), since the game server only sees NGINX's `127.0.0.1` as the source address.

> **Multi-machine:** If game servers run on separate machines, copy the certificates to each machine and update the `CertificatePath`/`PrivateKeyPath` in each server's `.cfg` file. NGINX itself does not need certs for the game ports (it only forwards UDP).

#### Certificate Files

Game servers read PEM certificates from the paths in their `.cfg` file. The defaults are:

| File | Path |
|---|---|
| Full chain certificate | `/etc/fishmmo/certs/fullchain.pem` |
| Private key | `/etc/fishmmo/certs/privkey.pem` |

**Permissions:** `chmod 640`, owned by the `fishmmo` service user.

#### Initial Certificate Setup (Let's Encrypt)

```bash
# Install certbot and obtain a wildcard certificate
sudo certbot certonly --manual --preferred-challenges dns \
  -d fishmmo.com -d '*.fishmmo.com'

# Create the game server cert directory
sudo mkdir -p /etc/fishmmo/certs

# Install the certbot deploy hook (auto-copies renewed certs)
sudo ln -s $(pwd)/FishMMO-Setup/deploy-hooks/certbot-fishmmo.sh \
  /etc/letsencrypt/renewal-hooks/deploy/fishmmo.sh

# Run the hook manually for first setup
sudo FishMMO-Setup/deploy-hooks/certbot-fishmmo.sh
```

#### Certificate Renewal

The certbot deploy hook at `FishMMO-Setup/deploy-hooks/certbot-fishmmo.sh` runs automatically after each successful certificate renewal. It:

1. **Validates** the renewed certificate (checks existence, non-empty, not expired)
2. **Copies** `fullchain.pem` and `privkey.pem` to `/etc/fishmmo/certs/`
3. **Sets ownership** and permissions (`640`, user `fishmmo`)
4. **Reloads NGINX** with zero downtime
5. **Restarts game servers** (MsQuic reads certs once at startup — a restart is required to pick up renewed certs)

> **Important:** MsQuic does not auto-reload TLS certificates. After certificate renewal, game servers must be restarted. For zero-downtime rolling restart, use systemd template units with the certbot hook's built-in restart logic.

---

## Web Server Configuration

### ClientGate Middleware

The IPFetch and Patcher web servers use a **ClientGate** middleware that validates the `X-FishMMO-Client` header on every request. This prevents generic crawlers and unauthorized clients from accessing the API.

**How it works:**
- An HMAC-SHA256 shared secret is read from the `FISHMMO_CLIENT_GATE_SECRET` environment variable
- The header format is: `v1.<timestamp>.<nonce>.<base64url-hmac>`
- Replay protection via a 20,000-entry nonce cache with a 30-second timestamp window
- The secret must be at least 32 bytes; comma-separated values enable key rotation

**In Production:** The server **refuses to start** if `FISHMMO_CLIENT_GATE_SECRET` is not set.
**In Development:** Logs a warning and passes all requests through.
**WebGL Server:** Does not use ClientGate (static content, publicly accessible).

Generate a secret:
```bash
openssl rand -base64 32
# Set it:
export FISHMMO_CLIENT_GATE_SECRET="your_base64_secret_here"
```

### Web Server Security & Environment Variables

All three web servers (IPFetch, Patcher, WebGL) run on Kestrel bound to **`127.0.0.1` (localhost only)** — they are designed to sit behind NGINX. NGINX handles TLS termination and public exposure; the web servers themselves never accept connections from the public internet. This is enforced at the Kestrel level, not just by firewall rules.

**Key environment variables:**

| Variable | Used By | Purpose |
|---|---|---|
| `FISHMMO_ENVIRONMENT` | All servers | Sets `DOTNET_ENVIRONMENT` and `ASPNETCORE_ENVIRONMENT` |
| `FISHMMO_CLIENT_GATE_SECRET` | IPFetch, Patcher | HMAC shared secret for API request signing (**required in Production**) |
| `ConnectionStrings__NpgsqlConnection` | IPFetch | PostgreSQL connection string |

**Production safety checks** (refuse to start if unmet):
- `FISHMMO_CLIENT_GATE_SECRET` must be set (IPFetch, Patcher)
- Trusted proxy IPs/networks must be configured in `ForwardedHeaders` (or set `ForwardedHeaders:AllowUnconfigured=true` to bypass)
- Npgsql connection must use `Ssl Mode=Require` or stricter (IPFetch)

**Web server port bindings** (all localhost):

| Server | Config Key | Default Port |
|---|---|---|
| IPFetch Server | `WebServer:HttpPort` | 8080 |
| Patcher Server | `WebServer:HttpPort` | 8090 |
| WebGL Server | `WebServer:HttpPort` | 8000 |

---

## Running the Servers

### Launch Order

Servers must be started in this exact order:

1. **PostgreSQL** — Must be running before any server starts.
2. **PgBouncer**  — If used, must be running before game servers start.
3. **LoginServer** — Must be running and registered in the database before World servers.
4. **WorldServer** — Must be running and registered before Scene servers.
5. **SceneServer(s)** — Can start once WorldServer is registered.
6. **IPFetch Server** — Should start after the LoginServer is registered.
7. **Patcher Server** — Can start independently; needs patch files.
8. **WebGL Server** — Can start independently; needs a WebGL build.

### Starting Game Servers

All three server types use the same `GameServer` executable with different launch arguments. Build the server first from Unity (`FishMMO → Build → Build Server`).

**Recommended:** Use the [AppHealthMonitor](FishMMO-AppHealthMonitor/README.md) daemon for production deployments — it provides automatic restarts, health checks, and process supervision:

```bash
cd FishMMO-AppHealthMonitor
dotnet build
dotnet run --project AppHealthMonitor/AppHealthMonitor.csproj
```

Install as a systemd service (Linux) via the [FishMMO-Installer](FishMMO-Installer/README.MD):
```bash
FishMMO-Installer --component apphealthmonitor-service
```

**Manual startup (dev/testing):**

**Linux:**
```bash
./GameServer LOGIN &
sleep 10
./GameServer WORLD &
sleep 5
./GameServer SCENE &
```

**Windows:**
```powershell
# Start LoginServer
Start-Process GameServer.exe -ArgumentList "LOGIN"
Start-Sleep 10

# Start WorldServer
Start-Process GameServer.exe -ArgumentList "WORLD"
Start-Sleep 5

# Start SceneServer(s)
Start-Process GameServer.exe -ArgumentList "SCENE"
```

Each server needs these files in its working directory:
- `GameServer` (the executable)
- `LoginServer.cfg` / `WorldServer.cfg` / `SceneServer.cfg` (matching the server type)
- `logging.json` (shared logging config, copied from `FishMMO-Setup/` at build)
- `appsettings.json` (database config, copied from `FishMMO-Setup/` at build)
- `AddressableAssetsData/` (Addressable asset bundles from the build)
- `StreamingAssets/` (if any)
- TLS certificate and private key at the paths specified in the `.cfg` file

> The Unity build process copies the correct `.cfg`, `appsettings.json`, and `logging.json` from `FishMMO-Setup/` automatically via `BuildExecutor.CopyConfigurationFiles()`.

> **Bind address:** Server `.cfg` files default to `Address=127.0.0.1` — the server listens on loopback and expects NGINX to forward public traffic to it. If you are running without NGINX or on a separate machine, change `Address` to `0.0.0.0` (all interfaces) or the machine's specific network IP. NGINX's stream config (`BACKEND_IP`) and HTTP upstream blocks must point to the same address.

### Starting Web Servers

All web servers load `appsettings.json` and `logging.json` from `FishMMO-Setup/` (copied to the build output at build time). Place a modified copy in the working directory for local overrides.

**For production,** install them as OS services via the [FishMMO-Installer](FishMMO-Installer/README.MD):

```bash
# Linux (systemd):
FishMMO-Installer --component systemd-services

# Windows (NSSM):
FishMMO-Installer --component windows-services
```

This configures automatic startup, crash recovery, environment variables (`FISHMMO_ENVIRONMENT=Production`, `FISHMMO_CLIENT_GATE_SECRET`, etc.), and log file capture. See the [Installer README](FishMMO-Installer/README.MD) for details.

**For development:**

> All web servers bind to `127.0.0.1` by default (configured via `WebServer:HttpPort` in their appsettings). NGINX reverse-proxies to them. To test a web server directly without NGINX, configure its `appsettings.json` to bind to `0.0.0.0` or a specific IP.

**IPFetch Server (Login Server Discovery):**
```bash
cd FishMMO-WebServers/IPFetchASP.NET/IpFetchServer
dotnet build   # copies configs from FishMMO-Setup/
dotnet run
```

**Patcher Server (Patch Delivery):**
```bash
cd FishMMO-WebServers/PatcherASP.NET/Patcher
dotnet build
dotnet run
```

**WebGL Server (Static File Serving):**
```bash
cd FishMMO-WebServers/WebGLServerASP.NET/WebGLServer
dotnet build
dotnet run
```

Place a `Patches/` directory alongside the Patcher server containing your generated patch ZIP files.

### Running Multiple Scene Servers

Each SceneServer needs a unique port. Copy `SceneServer.cfg` and change the port:

```ini
# SceneServer A — Port 7790
ServerName=Scene Server A
Port=7790

# SceneServer B — Port 7791
ServerName=Scene Server B
Port=7791
```

Launch each with `./GameServer SCENE` from its own working directory (or pass a custom config path). The WorldServer will distribute characters across all registered scene servers.

---

## FishMMO-AppHealthMonitor

The AppHealthMonitor is a daemon that monitors, auto-restarts, and health-checks your server processes. It performs **process liveness checks, TCP/UDP/WebSocket port probes, CPU and memory threshold monitoring, exponential-backoff restarts, and circuit breaker protection**.

### Build

```bash
cd FishMMO-AppHealthMonitor
dotnet build -c Release
```

### Configure appsettings.json

Place `appsettings.json` in the AppHealthMonitor's working directory:

```json
{
  "Headless": false,
  "Applications": [
    {
      "Name": "LoginServer",
      "ApplicationExePath": "/path/to/GameServer",
      "MonitoredPort": 7770,
      "PortTypes": ["TCP", "UDP"],
      "LaunchArguments": "LOGIN",
      "CheckIntervalSeconds": 30,
      "LaunchDelaySeconds": 2,
      "InitialHealthCheckDelaySeconds": 30,
      "PostLaunchSettleDelaySeconds": 5,
      "GracefulShutdownTimeoutSeconds": 10,
      "InitialRestartDelaySeconds": 5,
      "MaxRestartDelaySeconds": 60,
      "MaxRestartAttempts": 5,
      "CircuitBreakerFailureThreshold": 3
    },
    {
      "Name": "WorldServer",
      "ApplicationExePath": "/path/to/GameServer",
      "MonitoredPort": 7780,
      "PortTypes": ["TCP", "UDP"],
      "LaunchArguments": "WORLD",
      "CheckIntervalSeconds": 30,
      "LaunchDelaySeconds": 2,
      "InitialHealthCheckDelaySeconds": 30,
      "PostLaunchSettleDelaySeconds": 5,
      "GracefulShutdownTimeoutSeconds": 10,
      "InitialRestartDelaySeconds": 5,
      "MaxRestartDelaySeconds": 60,
      "MaxRestartAttempts": 5,
      "CircuitBreakerFailureThreshold": 3
    },
    {
      "Name": "SceneServer",
      "ApplicationExePath": "/path/to/GameServer",
      "MonitoredPort": 7790,
      "PortTypes": ["TCP", "UDP"],
      "LaunchArguments": "SCENE",
      "CheckIntervalSeconds": 30,
      "LaunchDelaySeconds": 2,
      "InitialHealthCheckDelaySeconds": 30,
      "PostLaunchSettleDelaySeconds": 5,
      "GracefulShutdownTimeoutSeconds": 10,
      "InitialRestartDelaySeconds": 5,
      "MaxRestartDelaySeconds": 60,
      "MaxRestartAttempts": 5,
      "CircuitBreakerFailureThreshold": 3
    },
    {
      "Name": "IPFetch Server",
      "ApplicationExePath": "/path/to/IpFetchServer",
      "MonitoredPort": 0,
      "PortTypes": [],
      "LaunchArguments": "",
      "CheckIntervalSeconds": 30,
      "LaunchDelaySeconds": 2,
      "InitialHealthCheckDelaySeconds": 30,
      "PostLaunchSettleDelaySeconds": 5,
      "GracefulShutdownTimeoutSeconds": 10,
      "InitialRestartDelaySeconds": 5,
      "MaxRestartDelaySeconds": 60,
      "MaxRestartAttempts": 5,
      "CircuitBreakerFailureThreshold": 3
    }
  ]
}
```

| Key | Description |
|---|---|
| `Headless` | `true` for production (auto-starts monitoring, no interactive console). `false` for development. |
| `Name` | Friendly name shown in logs and `status` command. |
| `ApplicationExePath` | Absolute or relative path to the executable. |
| `LaunchArguments` | Server type selector: `LOGIN`, `WORLD`, `SCENE`, or empty for web services. |
| `MonitoredPort` | Must match the port in the corresponding `.cfg` file. `0` = process-only monitoring. |
| `PortTypes` | Health check protocol(s): `TCP`, `UDP`, `WebSocket`. |
| `CheckIntervalSeconds` | Probe interval (minimum 5). |
| `InitialHealthCheckDelaySeconds` | Delay before first probe after launch (minimum 1). |
| `PostLaunchSettleDelaySeconds` | Pause after launch/restart before resuming probes. |
| `CircuitBreakerFailureThreshold` | Consecutive failures across launches that trip the circuit breaker. |
| `InitialRestartDelaySeconds` | Base delay for exponential backoff. |
| `MaxRestartDelaySeconds` | Cap for exponential backoff. |
| `MaxRestartAttempts` | After this many restarts, the circuit breaker may trip. |

### Console Commands (Headless = false)

| Command | Description |
|---|---|
| `help` | List all commands |
| `start <name>` | Start monitoring a specific application |
| `stop <name>` | Stop monitoring a specific application |
| `status` | Show status of all monitored applications (PID, state, restart/failure counters) |
| `restart <name>` | Force-restart a specific application |
| `kill <name>` | Force-kill a specific application |
| `shutdown` | Gracefully shut down all applications and exit |
| `exit` | Alias for `shutdown` |

### Running as a Systemd Service (Linux)

Create `/etc/systemd/system/apphealthmonitor.service`:

```ini
[Unit]
Description=FishMMO Application Health Monitor

[Service]
Type=simple
User=fishmmo
WorkingDirectory=/opt/fishmmo/AppHealthMonitor
ExecStart=/opt/fishmmo/AppHealthMonitor/AppHealthMonitor
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now apphealthmonitor
```

---

## Optional Services

### FishMMO-DiscordBot

The Discord bot bridges in-game chat with a Discord guild, provides account linking, dynamic channel management, and moderation commands.

#### Prerequisites
- A Discord application with a bot token (create at https://discord.com/developers/applications)
- Required privileged intents: `MESSAGE CONTENT`, `GUILD MEMBERS`
- Required scopes: `bot`, `applications.commands`
- Bot must have permissions: Read/Send/Manage Messages, Manage Channels, Timeout/Ban Members

#### Build & Configure

```bash
cd FishMMO-DiscordBot
dotnet build FishMMO-DiscordBot.sln -c Release
```

Create `appsettings.json` in the output directory:

```json
{
  "Discord": {
    "Token": "YOUR_DISCORD_BOT_TOKEN",
    "Prefix": "!",
    "GuildId": "0000000000000000000"
  },
  "FishMMO": {
    "ApiUrl": "http://localhost:5000/api/",
    "ApiKey": "YOUR_FISHMMO_API_KEY",
    "ChatPollIntervalMs": 1500
  },
  "ChannelMappings": {
    "World": "discord-channel-id",
    "Trade": "discord-channel-id",
    "Admin": "discord-admin-channel-id"
  },
  "DynamicChannels": {
    "Enabled": true,
    "CategoryId": "discord-category-id",
    "AutoArchiveMinutes": 60
  },
  "RateLimits": {
    "PerUserPerMinute": 10,
    "PerChannelPerMinute": 60
  },
  "Linking": {
    "CodeLengthChars": 8,
    "CodeTtlSeconds": 300
  }
}
```

#### Run

```bash
dotnet run --project FishMMO-DiscordBot/FishMMO-DiscordBot.csproj
```

### FishMMO-CMS

The CMS serves launcher news, announcements, and web content. It is an ASP.NET Core application.

> **Note:** The CMS is in early development. Controller endpoints (account registration, admin operations) have TODO stubs — the business logic has not been implemented yet.

```bash
cd FishMMO-CMS
dotnet build FishMMO-CMS.slnx -c Release
dotnet run --project FishMMO-CMS.Server/FishMMO-CMS.Server.csproj
```

Place `appsettings.json` with database connection details alongside the executable.

---

## Client Setup

### Building the Client

1. Open the FishMMO-Unity project in Unity.
2. **Build Addressables:** `FishMMO → Build → Build Addressables`
3. **Build Client:** `FishMMO → Build → Build Client`
4. The output goes to the configured build directory with the client executable, data files, and Addressable bundles.

### Client Launcher Flow

1. **Launcher starts** — Fetches news HTML from the CMS, resolves the API host, checks for updates.
2. **Version check** — Queries `api.fishmmo.com/latest_version`. If outdated, downloads and applies patches.
3. **Login** — Client connects to the LoginServer discovered via `api.fishmmo.com/loginserver`.
4. **QUIC/WebTransport Handshake** — X25519 ECDH key agreement + stateless cookie challenge → AES-256-GCM encrypted session.
5. **Auth** — SRP-6a authentication handshake, optional TOTP 2FA.
6. **Character Select** — Choose or create a character.
7. **World Entry** — WorldServer routes the character to the correct SceneServer.
8. **Gameplay** — SceneServer handles all in-game simulation.

### Client TLS Certificate Pinning

The client pins TLS certificates to prevent man-in-the-middle attacks on API and WebSocket connections.

**Configuration file:** `FishMMO-Unity/Assets/StreamingAssets/client-security.json`

```json
{
  "pins": [
    {
      "host": "api.fishmmo.com",
      "spkiHashes": [
        "base64_sha256_spki_hash_1",
        "base64_sha256_spki_hash_2"
      ]
    },
    {
      "host": "game.fishmmo.com",
      "spkiHashes": [
        "base64_sha256_spki_hash_1"
      ]
    }
  ]
}
```

> **Development builds:** Empty pins are allowed (TOFU / trust-on-first-use mode).
> **Release builds:** At least one pin per host is **required**. The build will fail otherwise.

To generate SPKI pin hashes from your certificate:

```bash
# From a live server
openssl s_client -connect api.fishmmo.com:443 -servername api.fishmmo.com </dev/null 2>/dev/null \
  | openssl x509 -pubkey -noout \
  | openssl pkey -pubin -outform der \
  | openssl dgst -sha256 -binary \
  | openssl base64

# From a certificate file
openssl x509 -in cert.pem -pubkey -noout \
  | openssl pkey -pubin -outform der \
  | openssl dgst -sha256 -binary \
  | openssl base64
```

---

## Production Deployment

### Linux Config Hardening

The installer applies several hardening measures for production Linux deployments:

- **Core dump disabling** — Prevents sensitive memory from being written to disk.
- **ptrace restrictions** — Restricts process tracing to root only (`kernel.yama.ptrace_scope = 1`).
- **File permissions** — `appsettings.json` set to `600` or `640`, owned by the service user.
- **PostgreSQL hardening** — Password auth only, restricted listen addresses, connection limits.

### Firewall Configuration

The installer can automate host firewall rules for the ports NGINX requires:

- **Linux:** Uses `ufw` (preferred) or `firewalld` — adds rules for ports 80/tcp and 443/tcp.
- **Windows:** Uses `netsh advfirewall` — adds inbound rules for the same ports.
- **Menu:** Web Server → `4` | **CLI:** `--component firewall` or `install-config.json` `"configureFirewall": true`

**Standard deployment (NGINX on same machine as backend servers):**

Only NGINX's public ports need to be open. Backend servers bind to loopback and are invisible to the public network:

```bash
# HTTP/HTTPS (the only ports that need to be publicly accessible)
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# UDP game ports — NGINX listens publicly, forwards to loopback game servers
sudo ufw allow 7770:7999/udp
```

**Multi-machine deployment (NGINX on separate machine from backend servers):**

Only the NGINX machine opens public ports. Backend servers only need to accept connections from NGINX's IP (private network):

```bash
# On the NGINX machine:
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 7770:7999/udp

# On each backend server machine (restrict to NGINX's IP):
sudo ufw allow from <nginx-ip> to any port 8000,8080,8090 proto tcp
sudo ufw allow from <nginx-ip> to any port 7770:7999 proto udp
```

> Backend HTTP ports (`8000`, `8080`, `8090`) should **never** be opened to the public — NGINX is the only entry point.

### Systemd Services

All FishMMO server components are designed to run as systemd services. Reference units:

#### GameServer Login

```ini
[Unit]
Description=FishMMO LoginServer
Requires=postgresql.service

[Service]
Type=simple
User=fishmmo
WorkingDirectory=/opt/fishmmo/LoginServer
ExecStart=/opt/fishmmo/LoginServer/GameServer LOGIN
Restart=on-failure
RestartSec=5
Environment=FISHMMO_ENVIRONMENT=Production
Environment=FISHMMO_SIGNING_KEY_KEK_BASE64=your_kek_here

[Install]
WantedBy=multi-user.target
```

#### GameServer World

```ini
[Unit]
Description=FishMMO WorldServer
After=fishmmo-login.service
Requires=fishmmo-login.service

[Service]
Type=simple
User=fishmmo
WorkingDirectory=/opt/fishmmo/WorldServer
ExecStart=/opt/fishmmo/WorldServer/GameServer WORLD
Restart=on-failure
RestartSec=5
Environment=FISHMMO_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

#### GameServer Scene

```ini
[Unit]
Description=FishMMO SceneServer
After=fishmmo-world.service
Requires=fishmmo-world.service

[Service]
Type=simple
User=fishmmo
WorkingDirectory=/opt/fishmmo/SceneServer
ExecStart=/opt/fishmmo/SceneServer/GameServer SCENE
Restart=on-failure
RestartSec=5
Environment=FISHMMO_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

Enable all services:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now fishmmo-login
sudo systemctl enable --now fishmmo-world
sudo systemctl enable --now fishmmo-scene
sudo systemctl enable --now apphealthmonitor
```

#### Web Server Systemd Services (Installer-Generated)

The installer can automatically generate and register systemd units for the ASP.NET web servers (Web Server menu, option `5`, or CLI `--component systemd-services`). It finds each server's publish directory, generates a `.service` file with the correct working directory, `ExecStart`, `User`, and `EnvironmentFile`, and runs `systemctl enable --now`.

Generated services:
- **`fishmmo-ipfetch.service`** — IPFetch Web Server on port 8080
- **`fishmmo-patcher.service`** — Patcher Web Server on port 8090
- **`fishmmo-webgl.service`** — WebGL Web Server on port 8000

Each service unit includes `EnvironmentFile=-/path/to/fishmmo-secrets.env` so database passwords, signing keys, and the ClientGate secret are never baked into the unit file. Generate the env file via the Configuration menu, option `1` → choose a component → `3` (Generate secrets environment-variable file).

```bash
# Verify after installer-generated registration:
systemctl status fishmmo-ipfetch fishmmo-patcher fishmmo-webgl
```

### Port Reference

| Port | Service | Protocol | Listens On | Public Exposure |
|---|---|---|---|---|
| 80 | NGINX (HTTP → HTTPS redirect) | TCP | `0.0.0.0` | Public |
| 443 | NGINX (HTTPS) | TCP | `0.0.0.0` | Public |
| 5432 | PostgreSQL | TCP | `127.0.0.1` | None (loopback only) |
| 6432 | PgBouncer | TCP | `127.0.0.1` | None (loopback only) |
| 7770-7779 | LoginServer(s) | UDP (QUIC) | `127.0.0.1` by default | Via NGINX L4 stream only |
| 7780-7789 | WorldServer(s) | UDP (QUIC) | `127.0.0.1` by default | Via NGINX L4 stream only |
| 7790-7999 | SceneServer(s) | UDP (QUIC) | `127.0.0.1` by default | Via NGINX L4 stream only |
| 8000 | WebGL Static Server | TCP (HTTP) | `127.0.0.1` | Via NGINX reverse proxy only |
| 8080 | IPFetch Server | TCP (HTTP) | `127.0.0.1` | Via NGINX reverse proxy only |
| 8090 | Patcher Server | TCP (HTTP) | `127.0.0.1` | Via NGINX reverse proxy only |

> **All backend services bind to loopback by default.** NGINX is the only component with public-facing listeners. Desktop/native clients connect to NGINX's public IP for both HTTP APIs and UDP game traffic — from the client's perspective, it talks to `api.fishmmo.com:443` and `game.fishmmo.com:7770`; NGINX transparently proxies to the loopback-bound backend. If a backend server runs on a different machine, change its bind address and the corresponding NGINX upstream/stream target.

### Certbot Deploy Hook (TLS Renewal)

The `FishMMO-Setup/deploy-hooks/certbot-fishmmo.sh` script handles TLS certificate lifecycle:

```
┌────────────────────────────────────────────────────┐
│ certbot renew → new certs in /etc/letsencrypt/    │
│     ↓                                              │
│ certbot-fishmmo.sh (deploy hook)                   │
│     ├─ Validate renewed certificate                │
│     ├─ Copy certs → /etc/fishmmo/certs/            │
│     ├─ chown fishmmo:fishmmo, chmod 640            │
│     ├─ nginx -s reload (zero-downtime)             │
│     └─ systemctl restart fishmmo-login fishmmo-world │
│        + fishmmo-scene@* (rolling restart)          │
└────────────────────────────────────────────────────┘
```

**Install the hook:**
```bash
sudo mkdir -p /etc/letsencrypt/renewal-hooks/deploy
sudo ln -s $(pwd)/FishMMO-Setup/deploy-hooks/certbot-fishmmo.sh \
  /etc/letsencrypt/renewal-hooks/deploy/fishmmo.sh
```

**Test the renewal pipeline:**
```bash
sudo certbot renew --dry-run --deploy-hook /etc/letsencrypt/renewal-hooks/deploy/fishmmo.sh
```

---

## Architecture

### Connection Pipeline

The full end-to-end connection flow is documented in [CONNECTION_PIPELINE.md](CONNECTION_PIPELINE.md). Key phases:

| Phase | Description |
|---|---|
| 1. Launcher Startup | News fetch, version check, HMAC-signed API requests |
| 2. Server Discovery | IPFetch returns active login servers + one-time connection token |
| 3. QUIC/WebTransport | QUIC Initial → TLS 1.3 handshake → encrypted tunnel |
| 4. X25519 ECDH | Stateless cookie challenge → key agreement → AES-256-GCM session |
| 5. SRP-6a Auth | Zero-knowledge password proof, async worker channel, token issuance |
| 6. TOTP 2FA | 6-digit code or recovery code, failure lockout |
| 7. Token & Character | Token hash persisted, character CRUD, server list |
| 8. World Server | Token auth with HMAC verification, renewal token |
| 9. Scene Server | Same token auth flow, gameplay begins |
| 10. Token Lifecycle | Auto-renewal after each auth, revocation on logout/pause/quit |

### Server Initialization Order

The 5-phase server startup sequence is documented in [SERVER_INITIALIZATION_ORDER.md](SERVER_INITIALIZATION_ORDER.md):

| Phase | What Happens |
|---|---|
| 1. MainBootstrap | Unity loads bootstrap scene, initializes logging, enqueues ServerLauncher |
| 2. ServerLauncher | Determines server type (args), loads server scenes additively |
| 3. Server Scene | Server.Start() — finds NetworkManager, creates CoreServer, fetches external IP |
| 4. Finalize Setup | Data containers initialized, behaviours registered, physics + network started |
| 5. Runtime | Frame-based LateUpdate, client connections, periodic callbacks |

Key guarantee: **RuntimeDataContainers are always initialized before ServerBehaviours** — no race conditions between logic and data.

### Flow Diagram

```mermaid
flowchart TB
    subgraph Internet
        Player["Player Client<br/>(Desktop / WebGL)"]
    end

    subgraph NGINX["NGINX Reverse Proxy (ports 80/443)"]
        SSL["NGINX<br/>L7: TLS termination (HTTP APIs)<br/>L4: UDP forward (no TLS for QUIC)"]
    end

    subgraph WebServices["ASP.NET Web Services"]
        IPFetch["IPFetch Server<br/>:8080<br/><i>Login server discovery</i>"]
        Patcher["Patcher Server<br/>:8090<br/><i>Patch delivery</i>"]
        WebGL["WebGL Server<br/>:8000<br/><i>Static file serving</i>"]
        CMS["CMS Server<br/><i>News &amp; content</i>"]
    end

    subgraph GameServers["Game Servers (GameServer executable)"]
        Login["LoginServer<br/>:7770<br/><i>SRP-6a auth, account mgmt</i>"]
        World["WorldServer<br/>:7780<br/><i>World state, routing</i>"]
        Scene1["SceneServer<br/>:7790<br/><i>Gameplay simulation</i>"]
        SceneN["SceneServer<br/>:779x<br/><i>Additional scenes</i>"]
    end

    subgraph DataLayer["Data Layer"]
        PgBouncer["pgBouncer<br/>:6432<br/><i>Connection pooler</i>"]
        PostgreSQL["PostgreSQL<br/>:5432"]
    end

    subgraph Monitoring["Monitoring"]
        HealthMon["AppHealthMonitor<br/><i>Process lifecycle,<br/>health checks,<br/>auto-restart</i>"]
    end

    subgraph BuildPipeline["Build & Patch Pipeline"]
        Unity["Unity Editor<br/><i>Build clients & servers</i>"]
        PatchGen["PatchGenerator<br/><i>Create delta patches</i>"]
        Updater["Updater<br/><i>Apply patches on client</i>"]
    end

    subgraph OptionalServices["Optional Services"]
        DiscordBot["DiscordBot<br/><i>Chat bridge</i>"]
    end

    Player -->|"HTTPS :443"| SSL
    Player -->|"QUIC/UDP :7770-7999"| SSL
    SSL -->|"/loginserver"| IPFetch
    SSL -->|"/latest_version<br/>/{version}"| Patcher
    SSL -->|"/ (static)"| WebGL
    SSL -->|"UDP stream :7770"| Login
    SSL -->|"UDP stream :7780"| World
    SSL -->|"UDP stream :7790+"| Scene1
    SSL -->|"news / content"| CMS

    Player -.->|"Direct UDP/QUIC<br/>(only if NGINX on<br/>separate machine)"| Login
    Player -.->|"Direct UDP/QUIC<br/>(only if NGINX on<br/>separate machine)"| World
    Player -.->|"Direct UDP/QUIC<br/>(only if NGINX on<br/>separate machine)"| Scene1

    Login --> PgBouncer
    World --> PgBouncer
    Scene1 --> PgBouncer
    SceneN --> PgBouncer
    PgBouncer --> PostgreSQL

    IPFetch --> PostgreSQL
    World --> Scene1
    World --> SceneN

    HealthMon -.->|"Monitor & restart"| Login
    HealthMon -.->|"Monitor & restart"| World
    HealthMon -.->|"Monitor & restart"| Scene1
    HealthMon -.->|"Monitor & restart"| SceneN
    HealthMon -.->|"Monitor & restart"| IPFetch

    DiscordBot -->|"Chat API"| Scene1

    Unity --> PatchGen
    PatchGen --> Patcher
    Patcher -->|"Serves patches"| Player
    Player -->|"Downloads & applies"| Updater
```

---

## License

This project is licensed under the **MIT License**. See [LICENSE](LICENSE) for details.
