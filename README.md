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
  - [Client Launcher Flow](#client-launcher-flow)
  - [Client Settings and Options](#client-settings-and-options)
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
| **FishMMO-Unity** | Unity project containing client, server, and shared game code (900+ C# files) — a unified client-side prediction pipeline with lag-compensated hit resolution, modular character visuals, tick-driven archetype AI with threat and pet systems, ECA trigger system |
| **FishMMO-Auth** | Transport-agnostic .NET authentication library (SRP-6a, X25519 ECDH, token auth, TOTP 2FA) |
| **FishMMO-Database (FishMMO-DB)** | PostgreSQL data-access layer using Entity Framework Core + Npgsql (41 entities/tables, 42 services) |
| **FishMMO-WebTransport** | C++ native library wrapping MsQuic (QUIC/HTTP3) — P/Invoked from C# as a FishNet transport plugin |
| **FishMMO-Installer** | Cross-platform .NET 8 console tool that automates dependency installation |
| **FishMMO-Dependencies** | Centralised NuGet dependency aggregator (43 packages, netstandard2.1) — copies DLLs to Unity |
| **FishMMO-Logger** | Flexible logging library with file, email, and console backends |
| **FishMMO-SharedUtility** | Pure C# utility library shared between client and server projects |
| **FishMMO-AppHealthMonitor** | Daemon that monitors, auto-restarts, and health-checks server processes |
| **FishMMO-WebServers** | ASP.NET Core 8.0 web services — IPFetch (8080), Patcher (8090), and WebGL static server (8000) |
| **FishMMO-Patcher** | Client-side updater that applies versioned patch files |
| **FishMMO-Setup** | Configuration templates — nginx.conf, server .cfg files, appsettings.json, deploy hooks, stream config generator |
| **FishMMO-DiscordBot** | Discord bot bridging in-game chat with a Discord guild |
| **FishMMO-CMS** | ASP.NET Core 8.0 account-management web API (registration, password/2FA self-service, admin account actions) — **scaffold only, every handler is a TODO stub** |

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
1 : Configure Database Secrets (DB credentials — username, password, host)
2 : Install PostgreSQL
3 : Install PgBouncer (Connection Pooler)
4 : Install FishMMO Database (User/Schema/Initial Migration)
5 : Create New Database Migration
6 : Grant User Permissions on Database
7 : Delete FishMMO Database (DANGEROUS!)
8 : Configure PgBouncer (generate pgbouncer.ini + userlist.txt, Linux)
9 : Configure Server Keys (gate secret, HMAC key, KEK → database)
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
1 : Configure appsettings.json (web servers — IPFetch, Patcher, WebGL)
2 : Configure Discord Bot
3 : Configure CMS
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

# Unattended installation from a config file (-f is an alias for --config)
FishMMO-Installer --non-interactive -f install-config.json

# Unattended install using the bundled quickstart template
FishMMO-Installer --quickstart

# Other flags
FishMMO-Installer --list-components          # Print component names and exit
FishMMO-Installer --accept-defaults          # (-y / --yes) Skip confirmation prompts
FishMMO-Installer --generate-checksums       # Generate SHA256 hashes for downloaded files
FishMMO-Installer --log-file /path/to.log    # Tee log output to a file
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

**Available component names for CLI use** (`--list-components` prints this list):
`dotnet-ef`, `aspnet-runtime`, `vs-build-tools`, `postgresql`, `pgbouncer`, `fishmmo-db`, `nginx`, `letsencrypt`, `unity-hub`, `unity-editor`, `build-projects`, `build-unity`, `appsettings`, `create-migration`, `firewall`, `systemd-services`, `all`

> **Pre-flight checks** automatically run before any CLI-mode installation, verifying internet connectivity, disk space, memory, admin/sudo access, and port conflicts.

> **Download integrity:** Downloaded files are verified against SHA256 checksums in `checksums.json`. Corrupt or tampered downloads are rejected.

#### Pre-built Install Config Templates

`FishMMO-Setup/Development/` includes three ready-to-use install configs:

| Template | Purpose |
|---|---|
| `install-config.quickstart.json` | Minimal dev setup (postgresql, fishmmo-db, nginx, firewall) |
| `install-config.web.json` | Web server setup (postgresql, fishmmo-db, nginx, letsencrypt, firewall, systemd-services) |
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
| 7 | Database | Configure Database Secrets (DB credentials) | Yes | Yes |
| 8 | Database | Install PostgreSQL | Yes | Yes |
| 9 | Database | Install FishMMO Database | Yes | Yes |
| 10 | Database | Install PgBouncer | Yes | Yes |
| 11 | Database | Configure PgBouncer | *(manual)* | Yes |
| 12 | Database | **Configure Server Keys** (REQUIRED) | Yes | Yes |
| 13 | Web Server | Install NGINX | Yes | Yes |
| 14 | Web Server | Deploy FishMMO nginx.conf | Yes | Yes |
| 15 | Web Server | Configure Firewall Rules | Yes | Yes |
| 16 | Web Server | Install/Renew Let's Encrypt Certificate | Optional | Optional |
| 17 | Web Server | Register FishMMO Web Services | Optional | Yes |
| 18 | Configuration | Configure appsettings.json (web servers) | Yes | Yes |

> **"Configure Server Keys" (step 12) is required.** Without the gate secret, KEK, and connection token HMAC key in the database, game servers and web servers will refuse to start. This step runs `SecurityKeyInstaller` which generates all three keys and stores them in the `deployment_secrets` and `connection_token_keys` tables.
>
> **After the Installer, open Unity and run these Editor tools** (see [Unity Project Setup](#unity-project-setup)):
> - **FishMMO Dashboard → Game Settings → Client Secret** — pulls the gate secret from the database and writes `ClientApiSecret.generated.cs`
> - **FishMMO Dashboard → Game Settings → Certificate Pins** — connects to your live hosts and writes `CertificatePins.generated.cs`
>
> **"Build all C# Projects"** discovers and builds all `.csproj` files under the repository root, including:
> - `FishMMO-Dependencies` — resolves 43 NuGet packages and copies their DLLs into `FishMMO-Unity/Assets/Dependencies/`
> - `FishMMO-Auth` — authentication library (copies DLL to Unity Dependencies)
> - `FishMMO-Database/FishMMO-DB` — database library
> - `FishMMO-Logger` — logging library (copies DLL to Unity Dependencies)
> - `FishMMO-SharedUtility` — shared utility library (copies DLL to Unity Dependencies)
> - `FishMMO-WebServers/IPFetchASP.NET` — login server discovery API
> - `FishMMO-WebServers/PatcherASP.NET` — patch delivery server
> - `FishMMO-WebServers/WebGLServerASP.NET` — WebGL static file server
> - `FishMMO-AppHealthMonitor` — server health monitor daemon
> - `FishMMO-DiscordBot` — Discord chat bridge bot
> - `FishMMO-CMS` — account-management web API (scaffold)

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

> **The Linux binary is committed to the repository.** If you're deploying on Linux, you can skip this build step. Windows and macOS binaries must be built on their respective platforms.

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

**Output:** `fishmmo_webtransport.dll` in `.../Plugins/windows_x86_64/` (msquic is statically linked — no separate `msquic.dll` needed)

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
5. Copies both `fishmmo_webtransport.dll` and `msquic.dll` to the Unity plugins directory (cross-compile requires `msquic.dll` alongside the main DLL — unlike native VS builds where msquic is statically linked)

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

The FishMMO-Installer automates database creation (Database menu, option `4` — *Install FishMMO Database*), but here is what happens under the hood:

1. **PostgreSQL Installation** — The installer installs PostgreSQL via your platform's package manager (option `2`).
2. **Database + User Creation** — Creates the `fishmmo` database and a dedicated `fishmmo` user role (option `4`).
3. **EF Core Migration** — Creates and applies an initial Entity Framework Core migration (option `4`).
4. **Permissions** — Grants the user full privileges on the `public` schema (options `4` and `6`).

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

When your data model changes, create a new migration via the Installer (Database menu, option `5` — *Create New Database Migration*) or manually:

```bash
cd FishMMO-Database/FishMMO-DB
dotnet ef migrations add YourMigrationName \
  --startup-project ../FishMMO-DB-Migrator \
  --output-dir ../../Migrations

# The scaffolder writes the model snapshot to the project default rather than
# --output-dir, where the SDK glob also compiles it — move it and remove the folder.
mv Migrations/NpgsqlDbContextModelSnapshot.cs ../../Migrations/
rmdir Migrations

cd ../FishMMO-DB-Migrator
dotnet run
```

Three things about this are not obvious:

- **`--startup-project` is mandatory.** `FishMMO-DB` targets `netstandard2.1`, which has no runtime, so the EF tooling refuses to run against it alone. `FishMMO-DB-Migrator` is the only executable project that references it.
- **Migrations live at the monorepo root**, in `Migrations/`, which is `.gitignore`d and compiled into `FishMMO-DB` via `<Compile Include="..\..\Migrations\*.cs" />`. Hence `--output-dir ../../Migrations` and the snapshot move above.
- **Review the scaffold before running it.** The model currently carries two pre-existing drifts (`character_pet.abilities` and `character_abilities.ability_events` are `text` in the database but `List<int>` in the model) that every new migration re-scaffolds as a destructive `ALTER … TYPE integer[]`. Strip those from `Up` and `Down` unless you are deliberately converting them.

To review the generated SQL without touching a database — `dotnet ef database update` ignores `FISHMMO_CONNECTION_STRING` and resolves the design-time factory, which points at your **live** local database:

```bash
dotnet ef migrations script PreviousMigration YourMigrationName \
  --startup-project ../FishMMO-DB-Migrator --idempotent
```

### Database Configuration

**Environment-based overrides:** The database library supports layered configuration in this priority order:

1. `appsettings.json` (required)
2. `appsettings.{Environment}.json` (optional — e.g., `appsettings.Development.json`)
3. Environment variables (highest priority, use `__` for nesting)

Set the environment via `FISHMMO_ENVIRONMENT` (preferred), `DOTNET_ENVIRONMENT`, or `ASPNETCORE_ENVIRONMENT`.

**Environment variable overrides for database credentials:**

DB credentials are resolved by the `DatabaseSecrets` class, checking environment variables first, then the platform secrets file (`/etc/fishmmo/db-secrets.env` on Linux, `%ProgramData%\FishMMO\db-secrets.env` on Windows):

| Variable | Maps To |
|---|---|
| `FISHMMO_ENVIRONMENT` | Environment name (`Development`, `Production`) |
| `FISHMMO_DB_HOST` | PostgreSQL host |
| `FISHMMO_DB_PORT` | PostgreSQL port |
| `FISHMMO_DB_NAME` | Database name |
| `FISHMMO_DB_USERNAME` | Database user |
| `FISHMMO_DB_PASSWORD` | Database password |

Example (fish shell):

```fish
set -Ux FISHMMO_ENVIRONMENT Production
set -Ux FISHMMO_DB_PASSWORD super_secret
```

Example (bash):

```bash
export FISHMMO_ENVIRONMENT=Production
export FISHMMO_DB_PASSWORD=super_secret
```

**Every timestamp is stored in UTC.** The timestamp columns are `timestamp without time zone`, and `CURRENT_TIMESTAMP` is a `timestamptz` — assigning it converts to the *session's* time zone, so on any server not running in UTC the value written was local time while everything reading it compared against `DateTime.UtcNow`. Every default and every raw-SQL write now uses `timezone('UTC', CURRENT_TIMESTAMP)` instead. This matters most for `last_pulse`: the liveness query already compared in UTC, so a non-UTC database made servers appear stale or fresh by exactly the UTC offset.

> Rows written before this change keep whatever offset they were written with. `last_pulse` corrects itself on the next heartbeat; historical `time_created` values do not.

**Unapplied migrations are caught at startup.** Migrations are generated per developer and applied locally rather than shared through source control, so pulling someone else's entity change brings no migration with it and nothing applies one on your behalf — the server starts and authenticates perfectly, and the mismatch surfaces much later as a query failing on a column that does not exist, which typically reaches a player as *missing data* rather than as a schema problem. `NpgsqlDbContextFactory.ValidateSchemaAsync` reports what it finds and `Server.VerifyDatabaseSchema` logs it with the command that fixes it:

| State | Meaning | Startup | Fix |
|---|---|---|---|
| **Pending migrations** | Migrations exist that this database has not run | **Refused** | `dotnet ef database update` |
| **Check unavailable** | The migration history could not be read at all | Proceeds, warns | Depends on the reason logged |

Pending migrations are fatal on purpose. A server running against a database that is behind the migration set does not fail loudly — it fails as missing player data, and every write it accepts meanwhile is made against a schema the model does not agree with. A check that could not run at all is only a warning: that leaves the schema unverified rather than known-bad, and a failed *diagnostic* should not take down a server that is otherwise fine. The check runs concurrently with behaviour initialization and is joined just before the transport opens, so it costs no startup latency.

> **Model drift is not detected.** An entity changed with no migration generated for it leaves nothing pending, so this check passes and the schema is quietly wrong. A drift check lived here and never worked once — EF builds `ModelSnapshot.Model` with an empty convention set, so the relational model its differ needs is never attached and the comparison threw on every startup. EF Core 5 exposes no supported way to rebuild it at runtime, so the check was removed rather than left reporting a failure forever ([#162](https://github.com/tindolt/FishMMO/issues/162)). **Generate a migration whenever you change an entity** — nothing will remind you. Catching this properly belongs in CI, where the design-time package is available and a scaffolded migration can be asserted empty.

---

## Unity Project Setup

### Client Security Setup (REQUIRED before building)

Before building the client, you must generate two security files from within Unity. Both live in the **FishMMO Dashboard → Game Settings** panel (`FishMMO → FishMMO Dashboard`, Ctrl+Shift+D), alongside the Host Configuration and Constants sections.

**1. Client Secret** (Game Settings → *Client Secret* section)
- Reads the gate secret from the `deployment_secrets` database table
- Writes `ClientApiSecret.generated.cs` — the shared secret for `X-FishMMO-Client` HMAC header signing
- Requires database access (uses the same `FISHMMO_DB_*` credentials)
- Run once per deployment or when the gate secret is rotated

**2. Certificate Pins** (Game Settings → *Certificate Pins* section)
- **Fetch Pins** connects to your live hosts over TLS to download leaf certificates; **Write Pins to File** then saves them
- Computes SHA-256 SPKI hashes and writes `CertificatePins.generated.cs`
- Minimum 2 pins (active + backup) required for release builds
- Only hosts serving HTTPS on port 443 work — QUIC-only game hostnames will fail, so pin the API/IPFetch/play hosts
- Run whenever TLS certificates are renewed with new key pairs

> **Without these files, release client builds will fail.** The build validator blocks any build with missing or sentinel-placeholder values. Development builds are exempt.

### Build World Scene Details

This caches important game world details (spawn points, teleporters, boundaries, scene metadata) for both clients and servers. **Run this whenever you add or modify a scene.**

**Unity Menu:** `FishMMO → Rebuild World Scene Details`, or `FishMMO → FishMMO Dashboard` → **World Scene Details** in the World category

This generates `WorldSceneDetailsCache` assets that are loaded at runtime by the WorldServer and SceneServer for scene routing and character placement.

### Versioning

Manage the project's semantic versioning from the **FishMMO Dashboard**.

**Unity Menu:** `FishMMO → FishMMO Dashboard` (Ctrl+Shift+D) → Select **Version** in the Core category

The Version panel provides:

| Control | Effect |
|---|---|
| **Increment Major** | Major++, resets Minor and Patch to 0 |
| **Increment Minor** | Minor++, resets Patch to 0 |
| **Increment Patch** | Patch++ |
| **Pre-Release Tag** | Free-text tag (e.g., `alpha`, `beta`, `rc1`) appended to the version |
| **Reset to 0.0.0** | Resets all version fields to zero (with confirmation) |

The panel also shows the current full version, asset path, and Addressable registration status. The `VersionConfig.asset` is automatically registered as an Addressable under the `Shared_Static_Permanent` group and label. The final version is written to `version.txt` in the build output directory during the build process.

### FishMMO Builds

Use the **FishMMO Dashboard** to configure and execute builds.

**Unity Menu:** `FishMMO → FishMMO Dashboard` (Ctrl+Shift+D) → Select **Build** in the Core category

The Build panel provides:

**Configuration:**
| Setting | Options | Description |
|---|---|---|
| **Build Type** | Server / Client | Selects which executable to build |
| **OS Target** | Windows x64 / Linux x64 / WebGL | Target platform for the build |
| **Environment** | Development / Production | Controls `.cfg` and `appsettings.json` copy source and loopback binding |

**Actions:**
| Button | Description |
|---|---|
| **Apply Platform Settings** | Switches the Unity Editor to the selected build target |
| **Build Addressables** | Builds all Addressable asset bundles required by client and server |
| **Build Game** | Executes the build for the selected Build Type, OS Target, and Environment |
| **Update Linker** | Regenerates the IL2CPP link.xml file |

The Dashboard also shows the current build settings, active build target, and background task progress (compilation, asset import).

**Build Settings via Menu:** Individual build settings can also be changed via `FishMMO → Build → Build Type`, `FishMMO → Build → OS Target`, and `FishMMO → Build → Environment`.

**Build Environments:**

| Environment | Address Binding | Use Case |
|---|---|---|
| **Development** | `127.0.0.1` (loopback) | Local testing — all servers on the same machine |
| **Production** | Configurable via `.cfg` files | Production deployment — default is `127.0.0.1` (behind NGINX); change to server's network IP only if NGINX is on a separate machine |

The build process copies the appropriate `.cfg` and `appsettings.json` files from `FishMMO-Setup/Development/` or `FishMMO-Setup/Production/` into the build output.

> **Important:** Build Addressables first, then build the game. The game build depends on the Addressable bundles produced in the first step.

**Headless builds and the build-target switch.** A CLI build (`-batchmode -executeMethod`) that has to switch build target used to be rejected for running while scripts were compiling, producing no artifact — and re-running the identical command immediately afterwards succeeded, because by then the target already matched and no switch happened. `SwitchActiveBuildTarget` is synchronous and already recompiles for the new target, and the `AssetDatabase.Refresh(ForceUpdate)` that follows settles the define symbols; the extra `ForceEditorScriptRecompile` on top of that queued a *further* compile, and under `-executeMethod` nothing turns the editor loop over until the method returns, so it stayed pending for the rest of the invocation. That recompile is now skipped in batch mode — interactive Dashboard builds keep it, where the editor loop can service it. `RunCliBuild` also fails fast, naming the reason, if scripts somehow are still compiling: nothing on that thread can advance a pending compile in batch mode, so waiting cannot help and sleeping would block the very thread that runs compilation.

**A batch-mode build never reports success without a player.** The two failure modes above were both silent in the same way, and the shared guard that starts every build could still reproduce it: when scripts are compiling, `CustomBuildTool` declines to start and returns. Interactively that is right — it warns, offers a dialog, and the player tries again. Under `-batchmode` nothing services the pending compile and nothing surfaces the warning, so the process exited `0` having produced no player, while Addressables had already run and written real bundles; the output directory still held the *previous* build, and the only way to notice was to compare a timestamp. Adding a single editor script is enough to trigger it, because the recompile outlives the Addressables step and the guard then fires. In batch mode that path now logs an error naming the cause and exits `1`.

**Headless builds need an explicit output path.** `BuildExecutor` falls back to `EditorUtility.SaveFolderPanel` when it is not given a root path, and under `-batchmode` that returns an empty string and cancels the player build — while Addressables has *already* run and written real bundles, and the process still exits `0`. The result is a build that reports success and produces no player. `RunCliBuild` therefore resolves an output path before building: `-fishmmoOutputPath <dir>` when supplied, otherwise `Builds/Server` or `Builds/Client` (chosen by the entry point invoked) in the **parent of the editor's working directory** — the repository root when Unity is launched from `FishMMO-Unity/`. Because that default depends on the working directory, CI should pass the path explicitly.

The CLI entry points are `BuildClientCLI`, `BuildServerCLI`, `BuildAddressablesCLI`, `BuildClientWithAddressablesCLI` and `BuildServerWithAddressablesCLI` on `FishMMO.Shared.CustomBuildTool.Core.CustomBuildTool`. OS target is read from `-fishmmoOSTarget`; `-fishmmoBuildType` applies only to `BuildAddressablesCLI`, which otherwise defaults to Client.

```bash
Unity -batchmode -quit -projectPath FishMMO-Unity \
      -executeMethod FishMMO.Shared.CustomBuildTool.Core.CustomBuildTool.BuildServerWithAddressablesCLI \
      -fishmmoOSTarget Linux -fishmmoOutputPath /srv/fishmmo/Builds/Server
```

### Patching — PatchGenerator and Updater

#### PatchGenerator

A custom Unity Editor window for creating delta patches between game builds.

**Unity Menu:** `FishMMO → FishMMO Dashboard` (Ctrl+Shift+D) → Select **Patch Generator** in the Core category

1. Select the **new** and **old** build directories.
2. Configure options, exclusions, and version details.
3. Click **Generate Patch** to create delta files and a manifest.

Patch files are ZIP archives named `<from_version>-<to_version>.zip` (e.g., `1.0.0-1.0.1.zip`). This naming scheme is a contract between the generator, the patcher server's index, the launcher's download path (`Constants.GetPatchFileName`), and the Updater's lookup — changing it requires changing all four.

#### Updater

The FishMMO Updater is a standalone .NET 8 executable that applies **one** patch archive to the client. It is launched automatically by the game launcher.

```
Updater.exe -version=1.0.0 -latestversion=1.0.1 -pid=1234 -exe=FishMMOClient.exe [-patches=D:\FishMMO\Patches]
```

It looks for exactly one archive — `{version}-{latestversion}.zip` in the patches directory, by default `Patches/` resolved relative to its own base directory — and applies it. `-patches=` overrides that directory and **must be absolute**; a relative, missing, or unreadable path is ignored with a warning and the default is used. The launcher applies the identical rule to its own `Launcher.PatchDirectory` setting and passes the resolved path here explicitly, so the two cannot disagree — and a disagreement is silent: the Updater finds no archive, does nothing, restarts the client at the same version, and the launcher detects the same mismatch again on the next run, forever. `-patches` changes only where a **verified** archive is read from; the launcher has already checked it against the server-supplied SHA-256 before the Updater is invoked at all, and it does **not** relocate the install. **There is no multi-step chaining**: if the server only publishes `1.0.0-1.0.1` and `1.0.1-1.0.2`, a `1.0.0` client cannot reach `1.0.2` in one run. Publish a direct `1.0.0-1.0.2` patch for every version you intend to support upgrading from.

Behaviour:

| Aspect | Detail |
|---|---|
| **Transaction** | Per-file backup with full rollback on any critical failure; parallel file writes, sequential deletes and moves for finalization |
| **Verification** | SHA-256 hash verification on both sides of each diff |
| **Archive lifecycle** | Deleted after a successful apply; **left in place after a failure** so the same run can be retried without re-downloading |
| **Exit code** | **Always 0.** Failure is not signalled via exit code — by the time the Updater finishes, the launcher it was reporting to has already been killed by PID. Read its console output for the result |
| **Launcher shutdown** | `Process.CloseMainWindow()` on Windows; on Linux/macOS the Updater P/Invokes `kill(pid, SIGTERM)` from `libc`, because `CloseMainWindow` is Windows-only. A forced kill follows if the graceful request is not honoured |
| **Restart** | The client executable named by `-exe` is started again on every exit path, patched or not |
| **Single writer** | A lock file under the install root makes one updater the only process working on it. Two updaters over the same tree would interleave their moves, and each one's rollback would describe a state the other had already changed |
| **Crash recovery** | Every displaced or created file is recorded in an append-only journal inside the staging directory *before* the filesystem is touched, and the journal is `fsync`ed so it survives a power loss. An updater that dies mid-apply leaves an install that is neither version; the next updater to run replays the journal and restores it before attempting anything else |
| **Manifest checks** | Refuses a manifest in which two entries write the same target path, and refuses an archive whose manifest describes a different upgrade than the one requested — the filename says `{from}-{to}`, but nothing had checked that the contents agreed |
| **Permissions** | Original POSIX permission bits are re-applied to each replacement, and a newly created file is detected as executable from its own content (ELF, Mach-O or shebang) rather than its extension. On Linux and macOS a patched binary that loses its executable bit is an install that no longer starts |
| **Parallelism** | Bounded to the machine's core count, so a large patch does not saturate the I/O queue of the disk it is rewriting |

> The `Patches` directory *name* is a three-way contract: the Unity client resolves it via `Constants.Configuration.PatchesDirectoryName` / `Constants.GetPatchesDirectory()`, the standalone Updater defaults to `"Patches"` (it cannot reference the Unity assembly), and the patch server reads it from `Patches:DirectoryName` in `appsettings.Patcher.json`. Change one, change all three. The player-facing `Launcher.PatchDirectory` override sits on top of that default and is carried across the launcher → Updater handoff by `-patches=`, so overriding it does not require touching any of the three.

#### Patch Server

Build the **PatcherASP.NET** web server and place your generated patch `.zip` files in the directory named by `Patches:DirectoryName` (default `Patches`, resolved **relative to the application base directory** — the server refuses to start if it resolves outside). The files are indexed and hashed at startup, and only indexed files are reachable — no caller-controlled string is ever concatenated into a filesystem path.

| Route | Behaviour |
|---|---|
| `GET`/`HEAD /latest_version` | `{ latest_version }` |
| `GET`/`HEAD /latest_version?from={clientVersion}` | `{ latest_version, up_to_date: true }` when the client is current; `{ latest_version, patch_available: false }` when no indexed patch upgrades that version; otherwise `{ latest_version, patch_available: true, sha256, size }` so the launcher can verify the download |
| `GET /{version}` | Streams `{version}-{latest}.zip` as `application/octet-stream` (range requests enabled, rate-limited by the `PatchDownload` policy). Returns **204 No Content** when the client is already current, and **404** when no patch path exists from that version |

Both `/latest_version` forms send a weak `ETag`, `Cache-Control: public, max-age=30`, and an `X-FishMMO-Version-Signature` HMAC over the manifest (signed with the shared gate secret, so a spoofed origin cannot forge a version answer). A matching `If-None-Match` short-circuits to `304`. Download responses carry `X-Patch-Sha256`, `X-Patch-Size`, a strong `ETag`, and `Cache-Control: public, max-age=3600, immutable`.

---

## Configuration

### Constants.cs — Client Domains

The file [`FishMMO-Unity/Assets/Scripts/Shared/Implementation/Constants.cs`](FishMMO-Unity/Assets/Scripts/Shared/Implementation/Constants.cs) contains domain endpoints used by the client to connect to your infrastructure.

These values are **not literals in `Constants.cs`** — each one reads from `GeneratedHostConfig`, an IL-embedded generated class whose real values are substituted at build time by CI or the FishMMO-Installer. The committed sentinel values are intentionally invalid, and the build validator blocks release builds that still contain them.

```csharp
public static class Configuration
{
    /// Unified API Host URL. NGINX routes to the correct backend by path.
    public static readonly string APIHost = GeneratedHostConfig.ApiHost;

    /// Game server hostname. Clients connect to this host via QUIC/WebTransport (UDP).
    /// NGINX forwards game traffic at Layer 4 to loopback-bound game servers.
    public static readonly string GameHost = GeneratedHostConfig.GameHost;

    /// Launcher HTML/news page URL.
    public static readonly string LauncherHtmlUrl = GeneratedHostConfig.LauncherHtmlUrl;
}
```

| Field | `GeneratedHostConfig` source | Purpose |
|---|---|---|
| `APIHost` | `ApiHost` (CI: `FISHMMO_API_HOST`) | Base URL for IPFetch and Patcher API calls — NGINX reverse-proxies to loopback web servers |
| `GameHost` | `GameHost` (CI: `FISHMMO_GAME_HOST`) | Hostname for game QUIC/WebTransport connections — NGINX forwards UDP to loopback game servers |
| `LauncherHtmlUrl` | `LauncherHtmlUrl` (CI: `FISHMMO_ROOT_DOMAIN`) | Page the launcher fetches its news panel from |
| `SmtpFromAddress` / `SmtpFromName` | `SmtpFromAddress` / `SmtpFromName` | From-address and display name for verification emails |

Write these values from the Unity Editor via **FishMMO Dashboard → Game Settings**, which regenerates `HostConfig.generated.cs`. `GeneratedHostConfig` also carries `PlayHost` (WebGL/player-facing hostname) and `RootDomain` (TLS certs and email).

> For local development, point the hosts at `https://localhost/` and `localhost`, or configure your hosts file. Prefer the `GlobalSettings` override mechanism (see `ApiHostResolver`) over editing generated constants for one-off testing. When running without NGINX, set `APIHost` to your web server's address directly and `GameHost` to the game server's address.

### Server Configuration Files

Each server type reads a `.cfg` file from its working directory. Templates are in `FishMMO-Setup/Development/` and `FishMMO-Setup/Production/`.

> Only the templates under `FishMMO-Setup/` are tracked. The `.cfg` files a server actually reads are per-deployment copies and are gitignored, so edits to them never reach source control — change the template, not the copy. In the Editor, `Constants.GetWorkingDirectory()` resolves to the repository root, so running a server from Play Mode drops `LoginServer.cfg`, `WorldServer.cfg`, and `SceneServer.cfg` there; the root `.gitignore` excludes those three paths by name. Runtime `appsettings.json` for the web servers is copied to `$(OutDir)` / `$(PublishDir)`, both under `bin/`, which the general `[Bb]in/` rule already covers.

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
StaleInstanceSceneTimeout=2
MaxInstanceLifetimeMinutes=120
CertificatePath=/etc/fishmmo/certs/fullchain.pem
PrivateKeyPath=/etc/fishmmo/certs/privkey.pem
```

| Key | Description | Default |
|---|---|---|
| `ServerName` | Display name for logs and monitoring | varies |
| `MaximumClients` | Maximum concurrent connections | 4000 |
| `Address` | Bind address — `127.0.0.1` when behind NGINX (default); set to the server's network IP only if NGINX runs on a different machine | `127.0.0.1` |
| `Port` | Listen port (Login=7770, World=7780, Scene=7790+) | varies |
| `StaleSceneTimeout` | **Minutes** an empty **open-world** scene instance stays loaded before it is unloaded and its scene row deleted. SceneServer only. The templates ship `5`; the code's fallback when the key is missing is `60` | 5 |
| `StaleInstanceSceneTimeout` | **Minutes** an empty **Group/PvP (instanced)** scene stays loaded. Deliberately shorter than `StaleSceneTimeout`: a dungeon instance belongs to one character or party, so once it empties it is unlikely to be wanted again, while each one holds a full physics scene. Long enough to survive a wipe-and-run-back or a relog. SceneServer only; the code's fallback is `5` | 2 |
| `MaxInstanceLifetimeMinutes` | **Minutes** an instanced scene may exist before it is closed regardless of who is inside. SceneServer only. Measured from the scene row's creation, so it includes the time spent queued and loading. `StaleInstanceSceneTimeout` only ever reclaims an **empty** instance, so an occupied one had no upper bound at all — and since a party may hold only one instance at a time, a dungeon nobody finishes locks that party out of every other. Occupants are warned at 10 / 5 / 1 minutes and then returned to the open world through the ordinary leave-instance path. Open-world scenes are exempt. A dungeon difficulty that declares its own `LifetimeMinutes` overrides this for instances opened at it — a timed challenge is one of the few levers that changes how a dungeon plays without touching a number in combat — and this remains the backstop for everything that declares nothing. Code's fallback when the key is missing is `120` | 120 |
| `CertificatePath` | PEM certificate for QUIC/TLS (game servers terminate their own TLS) | platform-dependent |
| `PrivateKeyPath` | PEM private key for QUIC/TLS | platform-dependent |
| `ConnectionTokenHmacKeyBase64` | **Leave empty.** Loaded at runtime from the `connection_token_keys` database table | empty |
| `AutoVerifyAccounts` | LoginServer only — skip email verification at account creation and at login (Development builds only, never in Production) | `true` (Dev) / `false` (Prod) |
| `AllowedOrigins` | LoginServer only — comma-separated CORS origins permitted for WebGL clients | `https://play.fishmmo.com` |
| `Smtp:Host` / `Smtp:Port` / `Smtp:Username` / `Smtp:Password` / `Smtp:FromAddress` / `Smtp:FromName` / `Smtp:UseSsl` | LoginServer only — verification-email relay. Port 465 = implicit TLS (`UseSsl=true`); port 587 = STARTTLS (`UseSsl=false`) | `localhost` / `465` / — / — / `noreply@fishmmo.com` / `FishMMO` / `true` |
| `LoginQueueUpdateRateSeconds` | LoginServer only — how often queued clients receive position updates | `2.0` |
| `LoginQueueMaxSize` | LoginServer only — queue capacity; excess clients are rejected outright | `500` |
| `LoginQueueAdmissionRatePerSecond` | LoginServer only — admission rate from the queue | `5.0` |
| `LoginQueueTimeoutSeconds` | LoginServer only — maximum wait before a queued client is timed out | `300` |

**Format:** Simple `key=value` per line. Lines starting with `#` or `;` are comments. The SMTP settings can be overridden via `FISHMMO_SMTP_HOST`, `FISHMMO_SMTP_PORT`, `FISHMMO_SMTP_USERNAME`, `FISHMMO_SMTP_PASSWORD`, `FISHMMO_SMTP_FROM_ADDRESS`, `FISHMMO_SMTP_FROM_NAME`, and `FISHMMO_SMTP_USE_SSL`.

> **IPv6 is not supported at the native QUIC layer.** The `EnableIPv6` and `IPv6Address` keys appear commented out in the templates and are reserved for future implementation — IPv6 clients must reach the servers through an IPv6-enabled NGINX L4 proxy.

> The Production login-queue defaults (`500` / `5.0` / `300`) are conservative, sized for roughly 500 concurrent players. The template's own tuning note suggests `1000–8000`, `10–20/s`, and `120–600s` for a launch-scale deployment.

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

> **Security:** Keep SMTP credentials out of source control. Load them via environment variables (e.g., `FISHMMO_SMTP_PASSWORD`). Application secrets (gate secret, KEK, connection token HMAC key) are stored in the database — see [FishMMO-Auth — Signing Keys & KEK](#fishmmo-auth--signing-keys--kek). See [FishMMO-Logger README](FishMMO-Logger/README.md) for full configuration details.

**Runtime override:** Place a modified `logging.json` in the working directory — it takes precedence over the bundled copy. The log level can also be overridden via the `FISHMMO_LOG_LEVEL` environment variable (e.g. `Debug`, `Verbose`).

**Level policy (why it is a data-handling decision, not just noise control):** `Warning` and
above are the tiers that get shipped off-host, aggregated and retained longest. Two rules follow
for server code, and both have been violated in this codebase before:

- *A healthy path never logs above `Debug`.* Every Login→World and World→Scene hop mints a
  connection token; logging the **successful** mint at `Warning` produced one line per hop on a
  busy server and buried the failures the tier exists for.
- *Per-player identifiers do not travel with success.* That same line named the client's
  resolved real IP, putting one player-attributable record into a widely-retained tier on every
  zone change, for no diagnostic gain — the failure branch already identifies the connection
  that could not be served.

Failures keep their `Warning`/`Error` level and their identifying detail. See
[Security Properties](CONNECTION_PIPELINE.md#security-properties).

### Configuration Files — `FishMMO-Setup/`

All project configuration lives in [`FishMMO-Setup/`](FishMMO-Setup/) as the single source of truth. **Non-sensitive defaults (host, port, database name) are stored in JSON. Database credentials are set via environment variables (`FISHMMO_DB_*`) or the platform secrets file (`/etc/fishmmo/db-secrets.env`). Application secrets (gate secret, KEK, connection token HMAC key) are stored in the database and loaded by each server at startup — no env vars or secrets files are needed for them.** Each template includes a `_comment` field listing the env var names to set.

**Directory structure:**

```
FishMMO-Setup/
├── logging.json                              # Shared logging — all projects
├── nginx.conf                                # NGINX reverse-proxy config (L4 UDP + L7 HTTP)
├── Development/                              # Dev / local configurations
│   ├── .env.example                          # Template for FISHMMO_* environment variables
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
│   ├── README.md                             # Production deployment notes
│   ├── appsettings.json                      # Unity server Npgsql
│   ├── appsettings.AppHealthMonitor.json      # Process supervisor config
│   ├── appsettings.DiscordBot.json           # Discord token + Npgsql
│   ├── appsettings.IpFetchServer.Production.json  # Prod overrides (empty — must set env vars)
│   ├── appsettings.Patcher.json              # Patch delivery web server
│   ├── appsettings.WebGLServer.json          # Static asset web server
│   ├── appsettings.CMS.json                  # CMS web app
│   ├── LoginServer.cfg / WorldServer.cfg / SceneServer.cfg
```

> **Not in this repository:** `nginx.conf` and the deployment docs below also reference two operator-supplied shell scripts — `gen-fishmmo-stream-config.sh` (NGINX UDP stream config generator) and the certbot deploy hook `certbot-fishmmo.sh`. Neither is shipped here; the documented behaviour is the contract they must satisfy.

**How it works:** Each project's `.csproj` copies the appropriate file from `FishMMO-Setup/` into its build output directory, renaming it to `appsettings.json` (or `logging.json`). At runtime, applications resolve config with a **working-directory-first** pattern: if a modified file exists in the working directory, it overrides the bundled copy. Environment variables (prefixed `FISHMMO_` or using `__` separator) provide the highest-priority overrides.

**Unity Npgsql example** ([`Development/appsettings.json`](FishMMO-Setup/Development/appsettings.json)):

```json
{
  "_comment": "Non-sensitive defaults only. Username and Password are resolved from FISHMMO_DB_USERNAME / FISHMMO_DB_PASSWORD env vars or /etc/fishmmo/db-secrets.env (chmod 600). NEVER put credentials in this file.",
  "Npgsql": {
    "Database": "fishmmo",
    "Host": "127.0.0.1",
    "Port": "5432"
  }
}
```

**Production Npgsql example** ([`Production/appsettings.json`](FishMMO-Setup/Production/appsettings.json)):

```json
{
  "_comment": "Non-sensitive defaults only. Username and Password are resolved from FISHMMO_DB_USERNAME / FISHMMO_DB_PASSWORD env vars or /etc/fishmmo/db-secrets.env (chmod 600). NEVER put credentials in this file.",
  "Npgsql": {
    "Database": "fishmmo",
    "Host": "127.0.0.1",
    "Port": "5432"
  }
}
```

If using PgBouncer, change `Npgsql.Port` to `6432` (see [Configure pgBouncer](#configure-pgbouncer)).

**Database pool and retry configuration** ([`Development/appsettings.Database.json`](FishMMO-Setup/Development/appsettings.Database.json)) — sets `CommandTimeout: 10`, `ConnectionTimeout: 15`, `MinPoolSize: 5`, `MaxPoolSize: 100`, Npgsql retry policy (3 retries, 20ms base, 10ms jitter), optional query performance tracking, and `ConnectionPoolHealth` thresholds (warn at 70%, critical at 85%, checked every 60s).

> **Security:** Never commit `appsettings.json` with real passwords. Use environment variables for secrets in production (see [Database Setup](#database-setup) for environment variable override syntax).

### FishMMO-Auth — Signing Keys & KEK

The authentication system uses HMAC-SHA256 for token signing. In production, signing keys are wrapped with an AES-256 Key Encryption Key (KEK).

#### Setting the KEK

The KEK is stored in the `deployment_secrets` database table under the key `signing_key_kek`. It is generated and stored by the FishMMO-Installer's **SecurityKeyInstaller**:

```bash
# From the Installer interactive menu:
# Database > Configure Server Keys

# Or via CLI — the argument is a comma-separated list of region IDs;
# each region gets its own keyId + HMAC key pair:
FishMMO-Installer --configure-server-secrets default
```

> The CLI form needs the PostgreSQL superuser password: supply it via `FISHMMO_PG_SUPERUSER_PASSWORD` or answer the prompt.

All game servers load the KEK from the database at startup via `IDeploymentSecretService`. No environment variable or secrets file is needed to distribute the KEK between machines.

#### How It Works

1. The LoginServer generates a fresh HMAC-SHA256 signing key at startup.
2. The key is wrapped using AES-256-GCM with the KEK and an AAD bound to the LoginServer's database ID.
3. The wrapped key envelope is stored in the database.
4. World/Scene servers fetch and unwrap the signing key to validate client tokens.
5. Keys are rotated each time the LoginServer restarts.

> **Without a KEK:** If the `signing_key_kek` row is missing from the `deployment_secrets` table, the LoginServer will log a warning and tokens will not be issued. Client authentication will fail on World/Scene servers. Run the Installer's **Database > Configure Server Keys** to populate it.

#### Auth Protocol Constants

The authentication library has several compile-time security constants (in `BaseAuthenticatorCore` and `SrpAuthenticatorCore`, under `FishMMO-Auth/FishMMO-ServerAuth/Implementation/Auth/`):

| Constant | Default | Declared In | Description |
|---|---|---|---|
| `AuthStaleTtlSeconds` | 15 | `BaseAuthenticatorCore` | Stale-auth sweep interval |
| `AuthHardDeadlineSeconds` | 60 | `BaseAuthenticatorCore` | Hard authentication deadline |
| `MaxPendingAuthConnections` | 10,000 | `BaseAuthenticatorCore` | Concurrent pending auth cap |
| `HandshakeIpWindowSeconds` | 2 | `BaseAuthenticatorCore` | Sliding window for the per-IP handshake limiter |
| `HandshakeIpBurstLimit` | 8 | `BaseAuthenticatorCore` | Phase-2 handshake completions allowed from one IP per window — sustains 4/sec/IP while tolerating a NAT burst |
| `MaxGlobalHandshakesPerSecond` | 500 | `BaseAuthenticatorCore` | Global X25519 handshake cap per 1-second window |
| `MaxTotpAttempts` | 5 | `SrpAuthenticatorCore` | TOTP attempts per connection |
| `MaxTotpFailuresPerUsername` | 15 | `SrpAuthenticatorCore` | Per-account TOTP lockout threshold |
| `TotpUsernameLockoutDuration` | 5 min | `SrpAuthenticatorCore` | TOTP lockout duration |

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

Use the Installer (Database menu, option `3` — *Install PgBouncer*):
- **Linux:** Package manager install + `systemctl enable --now pgbouncer`
- **Windows:** `winget` (preferred) or Chocolatey fallback

#### Configuration

After installation, configure PgBouncer to pool connections to your FishMMO database.

**Linux:** The installer's "Configure PgBouncer" option (Database menu, option `8`) generates `pgbouncer.ini` and `userlist.txt` for you. Otherwise, manually edit `/etc/pgbouncer/pgbouncer.ini`.

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

`FishMMO-Setup/nginx.conf` includes `/etc/nginx/stream.d/*.conf` and expects those per-port configs to be produced by a generator script installed at `/usr/local/bin/gen-fishmmo-stream-config.sh`:

```bash
sudo /usr/local/bin/gen-fishmmo-stream-config.sh
```

> **The script is not shipped in this repository** — it is a deployment-side artifact you provide. The contract below is what `nginx.conf` assumes of it; write the generator to match, or hand-write the `stream.d/*.conf` files.

The generator should:
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
| `PROXY_TIMEOUT` | `30s` | UDP session idle timeout — how long NGINX keeps a session mapping alive after the last packet |

- Use atomic temp-directory-then-replace so a failed run cannot leave partial configs
- Validate generated configs with `nginx -t` before replacing live files

**Configuration applied per port:**

```nginx
server {
    listen 7770 udp;
    proxy_pass 127.0.0.1:7770;
    proxy_timeout 30s;
    proxy_upload_rate 100m;
    proxy_download_rate 100m;
}
```

> **Keep `PROXY_TIMEOUT` in step with `StaleSceneTimeout`.** The game servers' idle disconnect threshold is `StaleSceneTimeout` in the `.cfg` files (default `5` seconds). `proxy_timeout` must be `>= StaleSceneTimeout` plus a margin — the 30s default is deliberately generous — but a `proxy_timeout` far larger than the server's threshold leaves NGINX holding session mappings for connections the server has already freed, wasting memory and file descriptors.
>
> **The stream module has no health checking**, not even passive. If a game server crashes, NGINX keeps forwarding datagrams to the dead upstream until the stream config is regenerated and reloaded. See the comment block at the top of `FishMMO-Setup/nginx.conf` for external health-check patterns (systemd timer, cron, or Consul Template).

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
sudo install -m 755 certbot-fishmmo.sh \
  /etc/letsencrypt/renewal-hooks/deploy/fishmmo.sh

# Run the hook manually for first setup
sudo /etc/letsencrypt/renewal-hooks/deploy/fishmmo.sh
```

#### Certificate Renewal

> **The deploy hook is not shipped in this repository** — you supply `certbot-fishmmo.sh`. What follows is the contract it must satisfy for the game servers to pick up renewed certificates.

The certbot deploy hook runs automatically after each successful certificate renewal. It must:

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
- An HMAC-SHA256 shared secret is loaded from the `deployment_secrets` database table (key `client_gate_secret`) at startup via `IDeploymentSecretService` and held in `GateSecretHolder`
- The header format is: `v1.<timestamp>.<nonce>.<base64url-hmac>`
- Replay protection via a 100,000-entry nonce cache with a 30-second timestamp window
- The secret must be at least 32 bytes; comma-separated values enable key rotation

**In Production:** The server **refuses to start** if the `client_gate_secret` row is not present in the `deployment_secrets` table.
**In Development:** Logs a warning and passes all requests through.
**WebGL Server:** Does not use ClientGate (static content, publicly accessible).

The gate secret is generated and stored by the FishMMO-Installer's SecurityKeyInstaller (Database menu > Configure Server Keys, or CLI `--configure-server-secrets <regions>`). The client-side copy is obtained from the **FishMMO Dashboard → Game Settings → Client Secret** section in the Unity Editor.

### Web Server Security & Environment Variables

All three web servers (IPFetch, Patcher, WebGL) run on Kestrel bound to **`127.0.0.1` (localhost only)** — they are designed to sit behind NGINX. NGINX handles TLS termination and public exposure; the web servers themselves never accept connections from the public internet. This is enforced at the Kestrel level, not just by firewall rules.

**Key environment variables:**

| Variable | Used By | Purpose |
|---|---|---|
| `FISHMMO_ENVIRONMENT` | All servers | Sets `DOTNET_ENVIRONMENT` and `ASPNETCORE_ENVIRONMENT` |

> **Application secrets (gate secret, KEK, connection token HMAC key) are NOT configured via environment variables.** They are stored in the database (`deployment_secrets` and `connection_token_keys` tables) and loaded by each server at startup via `IDeploymentSecretService` and `IConnectionTokenKeyService`. See [FishMMO-Auth -- Signing Keys & KEK](#fishmmo-auth--signing-keys--kek) and [ClientGate Middleware](#clientgate-middleware).

**Production safety checks** (refuse to start if unmet):
- The `client_gate_secret` row must exist in the `deployment_secrets` table (IPFetch, Patcher)
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

All three server types use the same `GameServer` executable with different launch arguments. Build the server first from Unity (**FishMMO Dashboard → Build**: set Build Type to *Server*, then **Build Game**).

**Recommended:** Use the [AppHealthMonitor](FishMMO-AppHealthMonitor/README.md) daemon for production deployments — it provides automatic restarts, health checks, and process supervision:

```bash
cd FishMMO-AppHealthMonitor
dotnet build
dotnet run --project AppHealthMonitor/AppHealthMonitor.csproj
```

> The installer's `systemd-services` component registers only the three **web** servers (`fishmmo-ipfetch`, `fishmmo-patcher`, `fishmmo-webgl`). The AppHealthMonitor unit is not exposed as a CLI component — write it by hand as shown in [Running as a Systemd Service (Linux)](#running-as-a-systemd-service-linux) below.

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
# Linux (systemd) — registers fishmmo-ipfetch, fishmmo-patcher, fishmmo-webgl
FishMMO-Installer --component systemd-services
```

This configures automatic startup, crash recovery, environment variables (`FISHMMO_ENVIRONMENT=Production`, `FISHMMO_DB_PASSWORD`, etc.), and log file capture. It requires each web server to have been published first — the installer looks for `FishMMO-WebServers/<project>/bin/Release/net8.0/publish` and skips any server it cannot find there.

> **Linux only.** `systemd-services` is a no-op on Windows. The installer contains an NSSM-based Windows service registration path (`FishMMO-IpFetch`, `FishMMO-Patcher`, `FishMMO-WebGL`), but it is not currently reachable from any CLI component name — configure Windows services via NSSM manually. See the [Installer README](FishMMO-Installer/README.MD) for details.

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

**Scene servers are not bound to a world server.** Each one pulls from a single global pending-scene queue, so any scene server may end up hosting scenes for any world server. This is what lets you scale zone capacity independently of world instances, and it is why scene rows, population counts and routing caches are all keyed by world server ID as well as scene name.

**Multiple instances of one open-world scene are channels.** When every instance of a scene is at its authored `MaxClients`, the WorldServer enqueues another and holds arriving clients in its routing queue (they see a position and a reason) until it is ready. Players move between instances of their current scene through the channel picker; the switch is refused in combat and rate-limited per character via `characters.last_channel_switch_utc`, so it survives the disconnect the switch performs.

An empty instance is unloaded once it has been stale for `StaleInstanceSceneTimeout` minutes (the templates ship `2`), which also deletes its scene row. Open-world scenes use the separate, longer `StaleSceneTimeout`.


### Locking a server and scheduling maintenance

Both `world_servers.locked` / `world_servers.shutdown_at_utc` and the matching `scene_servers`
columns are the **authority** for a server's lifecycle state. Servers never write them on their
own: each one reads its own row back on every pulse (~5s) and adopts what it finds. Anything that
can write those rows therefore controls the servers — the in-game commands below, a CMS, or plain
`psql` — exactly as `kick_requests` already works for accounts.

**Locking drains, it does not evict.** A locked server keeps the players it has and stops
receiving new ones: a locked world refuses logins, and a locked scene server is skipped by the
world server's open-world routing and stops taking scene-load requests. Instance routing
deliberately ignores the lock, because a character bound to a dungeon can only go to the one
server hosting it — refusing there would evict them from their instance rather than drain them.

**A locked world still admits accounts above `AccessLevel.Player`**, so locking a world for
maintenance does not lock out the people doing the maintenance. A world with a *shutdown*
scheduled admits nobody.

In-game commands, all requiring `AccessLevel.Admin` (3):

| Command | Effect |
|---|---|
| `/admin status` | Reports this scene server's state and that of the worlds it hosts scenes for |
| `/admin lockserver` | Locks the character's world server — no new logins except elevated accounts |
| `/admin unlockserver` | Reopens the world |
| `/admin shutdown <seconds>` | Locks the world and schedules its shutdown. Players are warned as the countdown passes 15m / 10m / 5m / 2m / 1m / 30s / 10s, then disconnected with a maintenance notice |
| `/admin stopshutdown` | Cancels the scheduled world shutdown. **Leaves the world locked** — reopening is a separate decision |
| `/admin lockscene` | Locks the scene server the admin is standing on |
| `/admin unlockscene` | Reopens that scene server |
| `/admin shutdownscene <seconds>` | Locks and shuts down that one scene server; its players are warned, disconnected, and can rejoin on another |
| `/admin stopshutdownscene` | Cancels it, leaving the scene server locked |

`lockworld` / `unlockworld` are accepted as aliases of `lockserver` / `unlockserver`.

#### Player commands for instanced content

A party may hold **one** dungeon instance at a time, so a run that is finished with — or that
went wrong — otherwise blocks the party from starting anything else until it empties and ages
out. These two commands exist so that is a decision rather than a wait. Both are available at
`AccessLevel.Player`.

| Command | Effect |
|---|---|
| `/leaveinstance` (`/exitinstance`) | Returns the character to the open world at the point it entered from. The system-level guarantee that instanced content cannot trap a player: a dungeon is expected to provide an exit teleporter, but that is content data, and a character bound to an instance is routed back into it on every login — so quitting is not an escape. Refused in combat, so it is not one either |
| Instance panel (Escape → Dungeon) | Shows the dungeon's name and difficulty, how long it has left, and everyone in it, marking who leads. **Leadership is the owning party's leader**, so it follows a promotion rather than staying with whoever opened the run — and it follows the party away from a leader who has logged out, so a run cannot be left with nobody able to manage it. The leader can remove others and can hide the run from the dungeon finder; anyone can leave, and everyone sees the visibility. Removal is immediate and returns the target to the open world at the point they entered from — it is not a disconnect, so they are not routed back into the instance they were just removed from |
| Dungeon finder (a dungeon entrance) | Lists the runs of that dungeon currently open at the chosen difficulty, with who is running each and how full it is. Joining one enters the dungeon **and joins that group's party**, so it is refused for anyone already in a party with somebody else. Opening a new run picks a difficulty and whether to list it for strangers |
| Find Group (dungeon finder) | Queues the character for that dungeon at that difficulty. The finder panel stays open and shows the wait in a strip above the list; **closing the panel leaves the queue, and walking more than a few metres from the entrance drops the player from it** (told why), so nobody is ever moved into a dungeon they have wandered away from. The finder first fills runs already **open to others** that have room — the late-join path, and how a partial group gets its missing members — and otherwise forms a fresh party once enough players have gathered (the difficulty's `GroupFinderSize`, else the run's capacity; never fewer than two or than `MinimumPartySize`). Matching joins a party, so it is refused for anyone in a party with somebody else. A matched player is moved as soon as they are out of combat and back at the door, and dropped from the group if they stay away for a minute. Off per difficulty via `GroupFinderEnabled` |
| Arena board (an `ArenaBoard` interactable) | Queues the character for a PvP arena at a chosen format (1v1, 2v2, 3v3, 4v4, 8v8 and so on, as the `ArenaTemplate` lists them). The board panel stays open and shows the wait; **closing it leaves the queue, and walking away from the board drops the player**. A party queues together through **Queue as Party** (leader only, every member at the board, party no larger than a team) and is kept on one team. Matches form first-come, atomically across scene servers; everyone is moved into the arena instance, waits for all seats to arrive, then a **10 second countdown** (with per-second cue hooks for audio and effects) starts the match. **Team Deathmatch** scores kills, **Capture the Flag** scores deliveries of the enemy flag to your own stand, **King of the Hill** scores time holding a control point; the dead respawn at their team's spawn, and the match ends on the score limit, the clock, or a walkover. Leaving a live match forfeits it. A results screen shows the winner, the team scores and a pedestal of the top scorers, and everyone is returned to the world. Arenas **count against the one-instance-per-party rule**, and every result moves the character's `PvP Rank`, `PvP Wins`, `PvP Losses` and `PvP Matches` attributes |
| `/team` (`/tm`) | Arena team chat: reaches your teammates in the match you are standing in, and nobody else. Not persisted |
| `/spectatearena <matchId>` | Game masters only. Enters a running arena match as a spectator, who can neither hit nor be hit; leave with `/leaveinstance` or wait for the match to close |
| `/closedungeon` (`/closeinstance`) | Ends the party's current instance. From **inside**, everyone in it is returned to the open world and the scene is unloaded; from **outside**, the instance is retired only if it is empty, and the hosting scene server reclaims the scene on its own. Party leader only, since it removes everybody — a character with no party is its own leader. Refused while anyone inside is in combat, because the eviction path deliberately skips state validation (there is nothing to validate against when a lifetime cap expires) and reaching it from a command would otherwise make it an instant escape from a losing fight |

Because this and the instance panel's controls are leader-only, a party whose leader logged out
used to be unable to close, hide or manage the run it was holding — and since a party may hold
only one instance, that locked every member out of every dungeon until the row aged out. Party
leadership now moves off a holder who is not logged in anywhere, once they have been gone long
enough that it cannot be confused with the gap while somebody walks through a teleporter. See
`leadershipAbsenceGraceSeconds` on the scene server's `PartySystem` asset, which must comfortably
exceed the slowest scene load on the shard.

Commands are refused silently to anyone below `AccessLevel.Admin` — the server gives no
indication the command exists, so an unprivileged player cannot probe for command names — but
every refused attempt is logged at warning level with the character and account that tried it.

> **Note on automatic restarts.** The AppHealthMonitor restarts server processes. A world server
> deregisters itself on a graceful shutdown, so it comes back clean; a scene server clears its own
> consumed shutdown (and the lock that came with it) as it exits, for the same reason. Either way
> a scheduled shutdown will be followed by a restart unless you stop the monitor first.

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
| `CpuThresholdPercent` | CPU usage above this percentage counts as a resource failure. Production templates use `80`. |
| `MemoryThresholdMB` | Memory usage above this counts as a resource failure. Production templates use `1024` for game servers, `512` for web services. |
| `ResourceCheckFailureThreshold` | Consecutive CPU/memory breaches before the process is restarted. Production templates use `2`. |
| `HealthCheckHost` | Host the port probe connects to. `127.0.0.1` for loopback-bound servers. |
| `PortCheckTimeoutMs` | TCP/UDP probe timeout. Production templates use `2000`. |
| `WebSocketCheckTimeoutMs` | WebSocket probe timeout. Production templates use `5000`. |
| `GracefulShutdownTimeoutSeconds` | Time allowed for a graceful stop before escalating. |
| `ForceKillTimeoutSeconds` | Time allowed for the forced kill to take effect. |
| `LaunchDelaySeconds` | Delay before launching the process. |

> The example above is trimmed for readability. The shipped templates in `FishMMO-Setup/{Development,Production}/appsettings.AppHealthMonitor.json` also set `CpuThresholdPercent`, `MemoryThresholdMB`, `ResourceCheckFailureThreshold`, `HealthCheckHost`, `PortCheckTimeoutMs`, `WebSocketCheckTimeoutMs`, and `ForceKillTimeoutSeconds` — start from those rather than from this snippet. The Production template sets `"Headless": true` and paths under `/opt/fishmmo/`.

### Console Commands (Headless = false)

| Command | Description |
|---|---|
| `help` | List all commands |
| `start` | Start monitoring **all** configured applications |
| `stop` | Gracefully terminate monitored applications and return to the waiting state |
| `force-kill` | Immediately terminate all monitored applications, bypassing graceful shutdown |
| `force-restart` | Immediately terminate and then restart all applications |
| `status` | Show status of all monitored applications (PID, state, restart/failure counters) |
| `shutdown` | Gracefully shut down the daemon and all applications |
| `exit` | Alias for `shutdown` |

> The commands operate on **all** configured applications — none of them take a per-application name argument.

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
    "Token": "",
    "DefaultGuildId": 0
  },
  "ConnectionStrings": {
    "Npgsql": "Host=localhost;Port=5432;Database=fishmmo;Username=;Password=;"
  },
  "ChatPollingIntervalSeconds": 5,
  "BridgeMessageMaxLength": 2000,
  "RateLimiting": {
    "MaxMessagesPerWindow": 10,
    "WindowSeconds": 60
  }
}
```

| Key | Description |
|---|---|
| `Discord:Token` | Bot token. **Set via the `Discord__Token` environment variable** — an empty token fails at startup, and the token must never be committed. |
| `Discord:DefaultGuildId` | Discord guild snowflake. `0` disables it; dynamic channel creation from game chat needs a real ID. |
| `ConnectionStrings:Npgsql` | PostgreSQL connection. Set via `ConnectionStrings__Npgsql` in production. |
| `ChatPollingIntervalSeconds` | How often the bot polls the game chat tables (default `5`). |
| `BridgeMessageMaxLength` | Maximum bridged message length (default `2000`, Discord's limit). |
| `RateLimiting:MaxMessagesPerWindow` / `WindowSeconds` | Per-window message cap (defaults `10` per `60` seconds). |

The templates live at `FishMMO-Setup/{Development,Production}/appsettings.DiscordBot.json`.

#### Run

```bash
dotnet run --project FishMMO-DiscordBot/FishMMO-DiscordBot.csproj
```

### FishMMO-CMS

An ASP.NET Core 8.0 web API for **account management** — the out-of-game counterpart to the in-game authentication the LoginServer performs. It exposes player self-service routes under `api/Account` (`register`, `verify`, `change-password`, `2fa/setup`) and operator routes under `api/Admin` (`accounts/search`, `ban`, `unban`, `access-level`, `revoke-tokens`, `reset-2fa`, `force-password-reset`).

> **This is not a news CMS.** The launcher's news panel is fetched from `Constants.Configuration.LauncherHtmlUrl`, baked at build time from `GeneratedHostConfig.LauncherHtmlUrl` (CI substitutes `FISHMMO_ROOT_DOMAIN`). Nothing in this project serves it.

> **Status: scaffold — do not deploy.** The routes exist and are reachable, but **every handler body is a `TODO` stub** returning a canned success response. There is no database wiring, no auth service registration, no persistence, no caller authentication on `api/Account`, and **no authorization at all on `api/Admin`** — any caller could invoke every administrative route. Keep it off any network until those are implemented.

```bash
cd FishMMO-CMS
dotnet build FishMMO-CMS.slnx -c Release
dotnet run --project FishMMO-CMS.Server
```

Configuration is layered: the build copies `FishMMO-Setup/Development/appsettings.CMS.json` (and the Production variant) into the output directory, then `./appsettings.json` and `./appsettings.{Environment}.json` in the working directory override it. Edit the templates under `FishMMO-Setup/`, not the copies in `bin/`. Swagger UI is served at `/swagger` in the Development environment only.

See [FishMMO-CMS/README.md](FishMMO-CMS/README.md) for the full endpoint table and implementation status.

---

## Client Setup

### Building the Client

1. Open the FishMMO-Unity project in Unity.
2. Open the **FishMMO Dashboard** (`FishMMO → FishMMO Dashboard`, Ctrl+Shift+D) and select **Build** in the Core category.
3. Set **Build Type** to *Client*, pick the **OS Target** and **Environment**, then click **Apply Platform Settings**.
4. Click **Build Addressables**, then **Build Game**. (Addressables must be built first — the player build depends on those bundles.)
5. The output goes to the configured build directory with the client executable, data files, and Addressable bundles.

> Build Type, OS Target, and Environment can also be set from the `FishMMO → Build → …` menus. The build actions themselves live only in the Dashboard.

### Client Launcher Flow

1. **Launcher starts** — Fetches the news HTML from `Constants.Configuration.LauncherHtmlUrl` (baked at build time from `GeneratedHostConfig.LauncherHtmlUrl`, which CI substitutes from `FISHMMO_ROOT_DOMAIN`) and extracts the configured `div` class. `IHtmlContentFetcher` yields the **parsed node**, not formatted text: UI Toolkit's rich text has no link tag, so a news link cannot be markup inside a label — it has to become a real element that can receive a click, which means the view walks the tree itself rather than being handed a formatted string. Every href is routed through `LauncherLinkPolicy`, which parses the URI and allows only absolute `http`/`https` before it reaches `Application.OpenURL`. News is cosmetic and does **not** block the version check.

   `IsNewsUrlConfigured` decides whether the request is worth issuing at all. An empty URL and a `FISHMMO_SENTINEL_PLACEHOLDER` the build never substituted mean the same thing — no feed — so both are treated alike, the same sentinel convention `ClientCertificatePinning` uses to screen unsubstituted values out of the pin set; without it a working copy fetched a host that cannot resolve and reported a failure on every run for a feed that was never configured. On both that path and a genuine fetch failure the pane is filled with `ClientLauncher.newsFallbackSummary`, a serialized `[TextArea]` field a deployment can reword without a code change. It is filled rather than hidden because hiding it collapsed the panel into a header stacked directly on a footer, which reads as a broken window rather than as a launcher with no news today.
2. **Version check** — Queries `{APIHost}latest_version?from={clientVersion}`. If a patch is available, the launcher downloads it into `{patch directory}/{from}-{to}.zip` (see `Launcher.PatchDirectory` below; `<install root>/Patches` by default), verifies it against the `sha256` the server reported, then hands off to the external Updater and quits so the Updater can replace the client binaries. Download progress carries a `DownloadStats` snapshot — bytes transferred against the manifest's expected total, current throughput, and an ETA — so the bar shows a real total from its first frame rather than waiting on response headers. If `Launcher.AutoUpdate` is off, the launcher parks on an **Update** button with the patch size instead of starting a download the player has not agreed to. If the server reports `patch_available: false`, the launcher enters `PatchUnavailable` — retrying cannot help, the player needs a full reinstall.
3. **Play** — The launcher loads the `ClientPostboot` scene, calls `StartBootstrap()` on its `ClientPostbootSystem`, and unloads its own scene.
4. **Login** — Client connects to the LoginServer discovered via `api.fishmmo.com/LoginServer`.
5. **QUIC/WebTransport Handshake** — X25519 ECDH key agreement + stateless cookie challenge → AES-256-GCM encrypted session.
6. **Auth** — SRP-6a authentication handshake, optional TOTP 2FA.
7. **Character Select** — Choose or create a character.
8. **World Entry** — WorldServer routes the character to the correct SceneServer.
9. **Gameplay** — SceneServer handles all in-game simulation.

#### Launcher Rendering and Settings

The launcher renders through `ILauncherView`, an interface describing the presentation surface in terms of intent (*show this status*) rather than widget manipulation, so all version-check and patch logic stays in `ClientLauncher` and never branches on what is drawing it. `UITKClientLauncher` is the only implementation. The uGUI adapter and the `HtmlToTmpTextConverter` behind it were deleted with the UI Toolkit conversion, along with the scene's `Canvas` root, so `ResolveView()` has nothing to fall back to: an unassigned or wrongly-typed **Launcher View Component** is reported as an error and leaves the view null. Saying so beats silently constructing a view over serialized fields that no longer exist.

The panel is a `UIDocument` bound to `UILauncher.uxml` and the shared `PanelSettings`. Its brand banner (`Assets/Sprites/FishMMOBanner.png`) has its height recomputed from its own resolved width on every geometry change, because USS has no aspect-ratio property and a fixed height on a full-width element can only be wrong: `scale-and-crop` fills the strip but slices the top and bottom off the artwork, `scale-to-fit` keeps it whole but leaves the panel background showing down both sides, and at 3.2:1 against a panel nowhere near that shape either was visible. The ratio is read from the assigned image rather than hardcoded, so replacing the artwork needs no code change. In the footer, Quit sits at the far left and Play/Connect at the far right, separated by the whole width of the panel — the affirmative action where the eye and the cursor finish, the destructive one far enough away that neither is hit by accident.

On handoff the launcher hides its UI and unloads its own scene. Addressables is asked first, because that is how a shipped build loaded the scene and its handle has to be released to free the bundle; `SceneManager` is the fallback, because Addressables only tracks scenes it loaded itself and silently no-ops for any other — which is exactly the editor case, where `ClientLauncher.unity` is opened directly and the launcher UI otherwise stayed on screen over the login screen for the rest of the session. `UITKClientLauncher` also watches `sceneLoaded` and dismisses itself the moment `ClientPostboot` arrives, whoever loaded it, as a second and independent guarantee against the same failure.

Settings live in the shared `Configuration.GlobalSettings` file alongside the game's other options, so the launcher and the in-game Options panel cannot end up with two stores. Every getter clamps: the file is plain text a player can edit, and a timeout of `0` or a retry count of `100000` would otherwise be honoured literally and look like a launcher bug.

| Key | Default | Range | Description |
|---|---|---|---|
| `Launcher.AutoUpdate` | `true` | — | Start downloading an available update without asking. Off parks the launcher on an Update button showing the patch size — the difference between a convenience and a cost on a metered connection |
| `Launcher.RequestTimeout` | serialized field | 5–300 s | Per-request timeout for version checks and patch downloads |
| `Launcher.MaxRetries` | serialized field | 0–10 | Retries after an initial failure. Honoured by the patch download only — the news fetch is cosmetic and deliberately runs with none |
| `Launcher.RetryDelay` | serialized field | 0–30 s | Delay between retries |
| `Launcher.PatchDirectory` | *(empty)* | absolute path | Where patch archives are downloaded to and read from. Empty uses the install's own `Patches` folder. Created if missing; anything unusable falls back to the default with a warning. **Not** a "move the game" setting — the Updater patches files relative to its own location, so the install root is fixed by construction |
| `Launcher.WindowWidth` / `Launcher.WindowHeight` | *(unset)* | ≥ 480 × 360 | Last window size the player chose. Written shortly after a resize settles rather than at shutdown, because the Updater terminates the launcher rather than closing it. Clamped against the current display, so a size saved on a larger monitor cannot come back with the footer buttons off-screen |

On Windows, **Browse** opens the native folder dialog (`NativeFolderPicker`, a `SHBrowseForFolder` shell call). Unity exposes no runtime folder picker, so the button is hidden on other platforms — the path text field sets the folder on every platform and nothing is unreachable without it.

> The launcher is a plain `MonoBehaviour` and only exists in **Standalone** builds. In the Editor and on WebGL the `ClientPreboot` bootstrap loads `ClientPostboot` directly, because neither can run the external updater. A transient-state watchdog (`transientStateTimeoutSeconds`, default `120`) recovers the UI if a launcher coroutine dies mid-flight.

### Client Settings and Options

Every client setting lives in the same `Configuration.cfg` beside the client executable that the
launcher uses — one file, one store, one owner. `ClientSettings` creates and loads it, names every
key exactly once, clamps everything read out of it, and owns the single debounced write that puts
it back on disk.

#### When settings are loaded and applied

Loading happens in two phases, and the split matters.

1. **`BeforeSceneLoad`** — `ClientSettingsBootstrap` creates and loads `Configuration.GlobalSettings`
   before any scene's `Awake`, so the first panel to register already has settings to read. It also
   creates the `PlayerControls` asset and applies the player's saved key bindings — *without*
   enabling an action map, so the bindings are inspectable and editable from the login screen while
   nothing in the world becomes live early.
2. **`MainBootstrapSystem.OnApplyClientBootSettings`** — raised during client preload, immediately
   after the bootstrap system installs its boot-time frame-rate cap and forces `vSyncCount` to 0.
   The saved display mode, quality level, brightness, VSync, frame-rate cap, audio levels and
   interface scale are applied there.

The order is not incidental. Those two bootstrap lines are a *default* for a client with no
preference, and anything applied before them is silently overwritten by them — so the hook fires
after, and the player's choice always wins. A scene with no `MainBootstrapSystem` (the UI validation
and unit-test scenes) is covered by an `AfterSceneLoad` backstop; applying is idempotent either way.

> Before this existed, nothing loaded the settings file at client start-up at all: the store was
> created lazily by whichever of the launcher or the Options panel asked first, and the Options
> panel ships closed. In a client started past the launcher, neither ran — key binding overrides
> were skipped without a word, panel positions were never restored, the theme loaded from nothing,
> and a saved resolution was never applied.

Brightness drives **both** `RenderSettings.ambientLight` and `ambientIntensity`, because which one
a scene reads depends on its ambient mode: `ambientLight` applies only under `AmbientMode.Flat`, and
every world scene is authored `AmbientMode.Skybox`, where it is ignored outright. Either way it is
re-applied on every `sceneLoaded`, since ambient is per-scene state baked into whichever scene is
active and the client loads several.

Writes are debounced, and there is exactly **one** pending write in the client. `Configuration.Save`
serialises and rewrites the whole file, so a slider bound straight to it rewrites the file once per
frame for as long as it is held. Everything — settings, window positions and launcher options alike
— coalesces onto a short quiet period, driven by a hidden `DontDestroyOnLoad` pump rather than by a
panel's `Update`, so a scene with no panels open cannot stop the clock on a write that is already
owed. It is forced out when the Options panel closes, on focus loss and pause, and on quit.

**In the Editor the disk write is skipped** — `Constants.GetWorkingDirectory()` resolves to the
repository root there rather than to an install directory — so settings behave normally in play mode
but do not rewrite the checked-out file. **On WebGL it is not skipped**: the working directory is
`Application.persistentDataPath`, an Emscripten IDBFS mount, and the write is followed by an
explicit IndexedDB sync — without which the file is written, read back correctly all session, and
gone by the next visit.

Numbers are stored culture-invariantly. Writing with the machine's own locale while reading
invariantly meant that on any comma-decimal system `0.75` was stored as `"0,75"` and read back as
**75**, the comma taken as a digit-group separator — so interface scale, brightness, volumes and
window positions all round-tripped to roughly a hundred times their value before being clamped.

#### Options panel

`FishMMO → Options` (or the `O` key, or Escape → Menu → Options) opens a five-tab panel. Every row
that belongs to a list — audio channels, gameplay toggles, theme colours, key bindings — is
generated from the table that defines it rather than authored in the UXML, so a configuration key
cannot be lost by editing a scene.

| Tab | Contents |
|---|---|
| **Display** | Resolution, refresh rate, fullscreen mode, quality level, brightness, frame-rate limit, VSync |
| **Audio** | Master volume; mute when unfocused. The other five channels are stored and applied but not offered — nothing in the client owns an `AudioSource` yet, and a slider that saves perfectly while changing nothing audible is worse than a missing one |
| **Gameplay** | Damage numbers, healing numbers, achievement popups, ignore party invites, ignore guild invites |
| **Key Bindings** | Every rebindable binding in the Player action map, with conflict detection |
| **UI** | Interface scale, window snap grid, window layout reset, the ten theme colours, and shareable UI profiles |

**Display settings are staged, not live.** Every other setting can be undone by the control that set
it; a display mode cannot — pick one the monitor will not show and the player cannot see the control
that would put it back. The three display dropdowns write to a pending selection, **Apply** commits
it and arms a 12-second countdown, and **Keep** is the only thing that writes it to the file. Closing
the panel with a mode unconfirmed restores the previous one immediately rather than leaving the
player waiting out a countdown whose prompt is no longer on screen.

#### Configuration keys

| Key | Default | Range | Description |
|---|---|---|---|
| `Resolution Width` / `Resolution Height` | *(unset)* | a mode the display reports | Written only by **Keep**. A saved mode the display no longer offers is refused at boot and the current mode is kept |
| `Refresh Rate` | *(unset)* | Hz offered at that resolution | Display refresh rate. Separate from the render cap below — deriving one from the other capped every player at their monitor's rate |
| `Fullscreen` | `FullScreenWindow` | a `FullScreenMode` value | Stored as the enum value, **not** a dropdown index: the list is built per platform, so an index means a different mode on a build without exclusive fullscreen |
| `Quality Level` | *(unset)* | a level name | Stored by **name**, not index — quality levels can be reordered between builds and an index would silently select a different one |
| `Brightness` | `1.0` | 0–1 | Scene ambient light — both `ambientLight` (Flat scenes) and `ambientIntensity` (Skybox scenes). Re-applied on every scene load |
| `Frame Rate Limit` | `60` (the boot-time menu cap) | tick rate – display rate, capped at 500 | Floored at the network tick rate: FishNet derives ticks from the update loop, so a lower frame rate cannot deliver them on schedule. With no preference stored the bootstrap cap stands, rather than jumping to the display's fastest mode — which is what made that cap dead on arrival. A saved value the display no longer offers falls back to the fastest available, since that player *did* express a preference |
| `VSync` | `false` | — | While on, `Application.targetFrameRate` is ignored entirely and the frame-rate limit above does nothing. The panel says so |
| `Audio.Volume.Master` | `1.0` | 0–1 | Applied to `AudioListener.volume`. Stored as the slider position; applied as its square, so the middle of the slider lands near the middle of the perceived range |
| `Audio.Volume.Music` | `0.6` | 0–1 | Below the rest deliberately: it is the only channel that plays continuously, and a score mixed level with combat effects buries the cues a player reacts to |
| `Audio.Volume.Effects` / `.Ambient` / `.Interface` / `.Voice` | `1.0` / `0.8` / `0.8` / `1.0` | 0–1 | Read through `ClientAudioSettings.EffectiveVolume(channel)` by anything that plays a sound. Stored and applied, but **not currently offered in the panel** — see `ClientAudioSettings.PlayableChannels` |
| `Audio.MuteWhenUnfocused` | `false` | — | Silences the client while its window has no focus. Applied on top of Master rather than by writing zero into it, so the saved level survives alt-tabbing |
| `ShowDamage` / `ShowHeals` / `ShowAchievementCompletion` | `true` | — | Floating combat text and achievement popups |
| `IgnorePartyInvites` / `IgnoreGuildInvites` | `false` | — | Invitations are **declined**, not dropped: an invitation silently discarded leaves the inviter staring at a prompt that never resolves and the server holding an invitation that blocks the next one |
| `UI.Scale` | `1.0` | 0.75–1.5 | Interface scale, applied by dividing the shared `PanelSettings` reference resolution. Restored to the authored value when Editor play mode ends, so a play session cannot dirty the asset — `QualitySettings` (current level and its `vSyncCount`) is protected the same way, for the same reason |
| `UI.SnapGridSize` | `8` | 0–32 points | Grid that dragged panels snap to. `0` disables snapping |
| `UI.Panel.<name>.X` / `.Y` | *(unset)* | panel points | Where the player dragged each window. Re-clamped into the viewport on restore, so a position saved on a 21:9 monitor is still reachable on a 16:9 one |
| `InputBindingOverrides` | *(unset)* | JSON | Key binding overrides for the whole asset. A value that cannot be parsed is discarded with a log and the defaults are used — it used to abort input initialisation entirely, leaving the player in the world with no controls and no way to reach the panel that would reset them |
| `<Name>ColorR/G/B/A` | *(unset)* | 0–255 | The ten themeable colours. Presence is decided by the `R` channel, so a legitimately black colour is not mistaken for an absent one. Clearing one **removes** the keys rather than emptying them, so a reset does not leave forty dead lines in the file |

#### UI profiles

The **UI** tab can save the window layout, interface scale, snap grid and colour scheme to a file of
its own under `<install root>/UIProfiles/<name>.cfg`, and load one back. This is deliberately *not*
`Configuration.cfg`: that file holds the whole client's settings, including its API host, launcher
state and this machine's display mode, none of which is meaningful on another player's computer and
some of which is actively wrong there. A profile carries only the parts worth sharing, so it can be
handed to somebody else as a plain text file.

`Configuration.cfg` stays the source of truth. Loading a profile writes its keys into the global
store and saves; nothing reads a profile at runtime, so a profile that is later deleted cannot take
the player's interface with it. A profile is applied **wholesale**, including the absence of a key —
a window it says nothing about returns to where the stylesheet puts it, because merging somebody
else's layout over yours produces an arrangement neither of you has ever seen. Every value is
re-validated on the way in: panel coordinates are re-clamped into the viewport, the scale is clamped
to the range the slider offers, and a key the format does not define is ignored rather than copied
through.

#### Key bindings

The **Key Bindings** tab lists every rebindable binding in the `Player` action map, one row per
binding — composite parts (`Move / up`, `Move / down`, …) get their own rows, because that is where
the keys a player actually wants to change live. Gamepad rows are captioned as such and only accept
gamepad controls; keyboard rows only accept keyboard and mouse controls.

Three rules govern the prompt, and all three hold on every row:

- **Escape cancels.** It can never be bound to anything. The cancelling key press is consumed by the
  rebind, so it cancels the prompt without also closing the Options panel behind it.
- **Backspace clears.** It can never be bound to anything either. Pressing it while a row is
  listening leaves that binding *unbound* — the action stops working until something is bound to it
  again. The row's own `↺` restores the key the game shipped with.
- **Duplicates are not allowed.** A rebind that would put two bindings on one control is undone and
  reported, naming the binding that already holds the key. Two actions on one key produces a client
  where one of them appears to have stopped working with nothing on screen saying why, and the
  player who created it is the least likely person to suspect the settings screen.

Those three combine into the way keys are swapped: clear the binding that holds the key you want,
then bind it. Without the clear, refusing duplicates would make a swap impossible — there would be
no way to free a key.

The left mouse button is also unbindable. `PerformInteractiveRebinding` suppresses the events it
matches, so with the left button eligible the first click after starting a rebind — including the
click meant to cancel it — was swallowed and bound to the action instead. Other mouse buttons stay
bindable.

A per-row `↺` restores that row's shipped key, and is refused if doing so would create a duplicate.
**Reset All Keys** discards every rebind at once after a confirmation, and is the unconditional way
back: the state it produces is the shipped one and cannot collide with itself. Both it and the status
line sit outside the scrolling list, so neither is off-screen behind forty rows of bindings.

Escape cannot be captured, so `Escape → Menu → Options` is reachable no matter what a player binds.

#### Default bindings

| Action | Keyboard / Mouse | Gamepad |
|---|---|---|
| Move | `W` `A` `S` `D` | Left stick |
| Look | Mouse | Right stick |
| Jump | `Space` | A / Cross |
| Crouch | `C` | L3 |
| Sprint | `Left Shift` | Left trigger |
| Interact | `E` | X / Square |
| Toggle Mouse Mode | `Tab` | Select |
| Toggle First Person | `F1` | — |
| Cancel *(interrupt cast)* | `Escape` | B / Circle |
| Close Last UI | `Escape` | B / Circle |
| Menu | `Escape` | Start |
| Chat | `Enter`, `/` | — |
| Hotkeys 1–10 | `1`–`9`, `0` | — |
| Inventory | `I` | — |
| Abilities | `K` | — |
| Equipment | `P` | — |
| Guild | `G` | — |
| Party | `Y` | — |
| Friends | `T` | — |
| Achievements | `J` | — |
| Factions | `U` | — |
| Minimap | `M` | — |
| Lore | `L` | — |
| Pet | `V` | — |
| Options | `O` | — |

`Escape` drives Cancel, Close Last UI and Menu together by design — Cancel interrupts a cast, Close
Last UI closes the top panel, and Menu opens the menu when neither of the first two had anything to
do. That authored overlap is exempt from duplicate detection; overlaps a player creates are not.

Windows the **server** opens have no key, and that is deliberate: a merchant, bank, loot window, NPC
dialogue, shrine, gathering node, trade container, **mailbox** or **dungeon finder** is opened by a
server reply after it has validated an interaction with something in the world. A key would either
open a window the server never populated or claim to open a mailbox the character is not standing in
front of — and a player who found the empty window would reasonably report it as broken. The scene
channel picker and the instance panel are reached from the game menu instead, because both are rare,
deliberate acts rather than windows a player flicks in and out of.

### Client TLS Certificate Pinning

The client pins TLS certificates to prevent man-in-the-middle attacks on API and WebTransport connections. Pins are **IL-embedded at compile time** via `FishMMO-Unity/Assets/Scripts/Client/Security/CertificatePins.generated.cs` -- no separate configuration file is needed.

> **Development builds:** Empty pins are allowed (TOFU / trust-on-first-use mode).
> **Release builds:** At least 2 pins (active + backup) are **required**. The build will fail if fewer than 2 valid pins are configured or if sentinel placeholders are still present.

**Generating pins via Unity Editor:** Use the **FishMMO Dashboard → Game Settings → Certificate Pins** section. **Fetch Pins** connects to your live hosts over TLS and extracts SPKI SHA-256 hashes; **Write Pins to File** saves them to `CertificatePins.generated.cs`. This embeds the pins at compile time -- no separate config file or CI substitution is needed. Only hosts serving HTTPS on port 443 can be fetched — QUIC-only game hostnames will fail.

To generate SPKI pin hashes manually from your certificate:

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

The installer can automatically generate and register systemd units for the ASP.NET web servers (Web Server menu, option `5`, or CLI `--component systemd-services`). It finds each server's publish directory, generates a `.service` file with the correct working directory, `ExecStart`, `User`, and `EnvironmentFile`/`Environment` entries, and runs `systemctl enable --now`.

Generated services:
- **`fishmmo-ipfetch.service`** — IPFetch Web Server on port 8080
- **`fishmmo-patcher.service`** — Patcher Web Server on port 8090
- **`fishmmo-webgl.service`** — WebGL Web Server on port 8000

The generated service units set `Environment=ASPNETCORE_ENVIRONMENT=Production` and `Environment=FISHMMO_ENVIRONMENT=Production`, plus `Restart=always` / `RestartSec=5` and `After=network.target postgresql.service`. **`User=` is set to the account that ran the installer**, not a dedicated service user — edit the unit if you want the servers to run as `fishmmo`. Each server must have been published to `bin/Release/net8.0/publish` first; the installer skips any it cannot find. Application secrets (gate secret, KEK, connection token HMAC key) are **not** set via environment variables or env files -- they are loaded from the database at startup by each server. Database credentials can be provided via `EnvironmentFile=-/etc/fishmmo/db-secrets.env` or the `FISHMMO_DB_*` environment variables.

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

A certbot deploy hook handles the TLS certificate lifecycle. **The script is operator-supplied — it is not shipped in this repository** — and must perform the following:

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
sudo install -m 755 certbot-fishmmo.sh \
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
| 9. Scene Server | Same token auth flow, order-independent scene handshake, gameplay begins |
| 9a. Scene-Routing Queue | When there is nowhere to route yet — capacity, a scene still loading, or a combat-logout body only one instance can return — the client waits on the World server and is told its position and the reason |
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

### Gameplay Simulation

Combat and movement run through one prediction pipeline rather than per-subsystem ones. The
details are in
[`Assets/Scripts/Shared/Implementation/Entity/Prediction/README.md`](FishMMO-Unity/Assets/Scripts/Shared/Implementation/Entity/Prediction/README.md);
the shape is:

| Layer | What it does |
|---|---|
| **One entry point** | `CharacterPredictionController` owns the single `[Replicate]` / `[Reconcile]` pair on a character and drives every subsystem through it in `Order`: KCC movement (80) → buffs (85) → cooldowns (90) → equipment (93) → attributes (95) → abilities (100). The order encodes real data dependencies — contributors settle before the authoritative total that subsumes them is installed. |
| **Input, not state** | `CharacterReplicateData` carries only input, quantised on the producer *before* it predicts, so the owner never simulates at a precision the wire cannot carry. |
| **A loss-detecting delta chain** | `CharacterReconcileData` is delta-encoded against the previous state the server *sent*, with a one-byte sequence so a dropped datagram costs "no correction until the next snapshot" rather than "a wrong correction for a second". |
| **Lag compensation** | Hits resolve against where the caster's client *saw* its peers. The client contributes its full round trip plus its interpolation buffer; the server adds its own replicate queue depth and rewinds every character in the scene for the duration of one query. Every latency term cancels exactly. |
| **Observers are told, not simulating** | State forwarding is deliberately off. Observers receive position via `NetworkTransform` and everything else via explicit broadcasts, and the spawn payload carries an observer-shaped form of each so a late joiner reconstructs what a continuous observer holds. |
| **Server-only physics** | A physics query is not reproducible across peers, so every target selector and hit action runs on the server, inside the caster's rewound world, and caps its result only after ranking and per-body dedupe. |

Behaviour is pinned by roughly **1,080 EditMode tests**
([`Assets/UnitTests/README.md`](FishMMO-Unity/Assets/UnitTests/README.md)), including a
closed-loop fixture that composes both halves of the lag-compensation derivation across a spread
of round-trip times and asserts the server resolves to the position the shooter was rendering.

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
        CMS["CMS Server<br/><i>Account management API<br/>(scaffold — stubs only)</i>"]
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
    SSL -->|"api/Account<br/>api/Admin"| CMS

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
