[![](https://dcbadge.vercel.app/api/server/9JQEYjkSNk?style=full)](https://discord.gg/9JQEYjkSNk)
[Join our Discord](https://discord.gg/9JQEYjkSNk)

# FishMMO

A modular, open-source MMO framework built on **Unity 6.3 LTS**, **FishNet**, and **PostgreSQL**.

---

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Prerequisites](#prerequisites)
- [Installation Guide](#installation-guide)
  - [1. Clone the Repository](#1-clone-the-repository)
  - [2. Build the FishMMO-Installer](#2-build-the-fishmmo-installer)
  - [3. Run the Installer](#3-run-the-installer)
  - [4. Open the Unity Project](#4-open-the-unity-project)
- [Database Setup](#database-setup)
- [Redis Setup](#redis-setup)
- [Unity Project Setup](#unity-project-setup)
  - [Build World Scene Details](#build-world-scene-details)
  - [Versioning](#versioning)
  - [FishMMO Builds](#fishmmo-builds)
  - [Patching — PatchGenerator and Updater](#patching--patchgenerator-and-updater)
- [Configuration](#configuration)
  - [Constants.cs — Client Domains](#constantscs--client-domains)
  - [Server Configuration Files](#server-configuration-files)
  - [Logging Configuration](#logging-configuration)
  - [appsettings.json — Database & Redis](#appsettingsjson--database--redis)
  - [FishMMO-Auth — Signing Keys & KEK](#fishmmo-auth--signing-keys--kek)
- [Infrastructure Setup](#infrastructure-setup)
  - [Configure Unity Hub](#configure-unity-hub)
  - [Configure PostgreSQL](#configure-postgresql)
  - [Configure pgBouncer](#configure-pgbouncer)
  - [Configure Redis](#configure-redis)
  - [Configure NGINX](#configure-nginx)
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
  - [Systemd Services](#systemd-services)
  - [Port Reference](#port-reference)
- [Flow Diagram](#flow-diagram)
- [License](#license)

---

## Overview

FishMMO is a complete multiplayer online game framework consisting of:

| Component | Description |
|---|---|
| **FishMMO-Unity** | Unity project containing client, server, and shared game code |
| **FishMMO-Auth** | Transport-agnostic .NET authentication library (SRP-6a, token auth, TOTP 2FA) |
| **FishMMO-Database (FishMMO-DB)** | PostgreSQL data-access layer using Entity Framework Core + Npgsql |
| **FishMMO-Installer** | Cross-platform .NET 8 console tool that automates dependency installation |
| **FishMMO-Dependencies** | Centralised NuGet dependency library (netstandard2.1) |
| **FishMMO-Logger** | Flexible logging library with file, email, and console backends |
| **FishMMO-SharedUtility** | Pure C# utility library shared between client and server projects |
| **FishMMO-AppHealthMonitor** | Daemon that monitors, auto-restarts, and health-checks server processes |
| **FishMMO-WebServers** | ASP.NET Core web services — IPFetch, Patcher, and WebGL static server |
| **FishMMO-Patcher** | Client-side updater that applies versioned patch files |
| **FishMMO-Setup** | Configuration templates — nginx.conf, server .cfg files, appsettings.json |
| **FishMMO-DiscordBot** | Discord bot bridging in-game chat with a Discord guild |
| **FishMMO-CMS** | ASP.NET Core CMS for launcher news, announcements, and web content |

The server architecture uses three server types:

- **LoginServer** — Handles account creation, SRP-6a authentication, character select, and TOTP 2FA.
- **WorldServer** — Manages world state, character routing, and scene server orchestration.
- **SceneServer** — Runs gameplay simulation for individual world scenes (chat, combat, inventory, guilds, etc.).

All three are launched from a single `GameServer` executable with a command-line argument (`LOGIN`, `WORLD`, or `SCENE`).

---

## Supported Platforms

| Platform | Client | Server |
|---|---|---|
| Windows 10/11 | Yes | Yes |
| Linux (Ubuntu/Debian, Arch/CachyOS) | Yes | Yes |
| macOS | Yes | Yes |
| WebGL | Yes (via browser) | N/A |

| Requirement | Version |
|---|---|
| Unity | 6.3 LTS |
| .NET SDK | 8.0+ |
| PostgreSQL | 14+ |
| Redis | 6+ (optional, recommended for production) |
| Scripting Backend | IL2CPP |

---

## Prerequisites

- **Git** for cloning the repository
- **.NET 8.0 SDK** (the installer can install this for you)
- **Unity Hub** with **Unity 6.3 LTS** (the installer can install these for you)
- **PostgreSQL 14+** (the installer can install this for you)
- **Redis 6+** (optional but recommended — the installer can install this for you)
- Administrator/root privileges for system-level installs
- Internet connectivity

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

The **FishMMO-Installer** is the recommended way to set up all dependencies. It automates installing .NET, PostgreSQL, Redis, NGINX, PgBouncer, Unity Hub, building all C# projects, and setting up the database.

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

The installer presents a hierarchical menu system. Each sub-menu groups related tasks:

```
=== FishMMO Installer ===
1 : Runtime & Tooling     — .NET SDK, ASP.NET Runtime, VS Build Tools
2 : Database              — PostgreSQL, PgBouncer, Redis, DB management
3 : Web Server            — NGINX, Let's Encrypt SSL
4 : Unity & Build         — Unity Hub, Unity Editor, C# project builds
5 : Configuration         — appsettings.json setup
0 : Quit
```

#### Sub-Menu: Runtime & Tooling

```
1 : Install DotNet SDK
2 : Install ASP.NET Runtime
3 : Install Visual Studio Build Tools (Windows Only)
0 : Back
```

#### Sub-Menu: Database

```
1 : Install PostgreSQL
2 : Install PgBouncer (Connection Pooler)
3 : Install Redis (In-Memory Cache)
4 : Install FishMMO Database (User/Schema/Initial Migration)
5 : Create New Database Migration
6 : Grant User Permissions on Database
7 : Delete FishMMO Database (DANGEROUS!)
8 : Configure PgBouncer (generate pgbouncer.ini + userlist.txt, Linux)
0 : Back
```

#### Sub-Menu: Web Server

```
1 : Install NGINX (Web Server/Reverse Proxy)
2 : Install/Renew Let's Encrypt Certificate (NGINX)
3 : Deploy FishMMO nginx.conf (from FishMMO-Setup/)
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

#### Recommended Installation Order (Fresh Setup)

| Step | Menu | Action | Windows | Linux |
|---|---|---|---|---|
| 1 | Runtime & Tooling | Install DotNet SDK | Yes | Yes |
| 2 | Runtime & Tooling | Install ASP.NET Runtime | Yes | Yes |
| 3 | Runtime & Tooling | Install VS Build Tools | Yes | *(skip)* |
| 4 | Unity & Build | Build all C# Projects | Yes | Yes |
| 5 | Unity & Build | Install Unity Hub | Yes | Yes |
| 6 | Unity & Build | Install Unity Editor (+Modules) | Yes | Yes |
| 7 | Database | Install PostgreSQL | Yes | Yes |
| 8 | Database | Install Redis | Yes | Yes |
| 9 | Database | Install FishMMO Database | Yes | Yes |
| 10 | Database | Install PgBouncer | Yes | Yes |
| 11 | Database | Configure PgBouncer | *(manual)* | Yes |
| 12 | Web Server | Install NGINX | Yes | Yes |
| 13 | Web Server | Deploy FishMMO nginx.conf | Yes | Yes |
| 14 | Web Server | Install/Renew Let's Encrypt Certificate | Optional | Optional |
| 15 | Configuration | Configure appsettings.json | Yes | Yes |

> **"Build all C# Projects"** discovers and builds all `.csproj` files under the repository root, including:
> - `FishMMO-Dependencies` — copies dependency DLLs into `FishMMO-Unity/Assets/Dependencies/`
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

### 4. Open the Unity Project

After building all C# projects, open the Unity project to compile Unity-side scripts and perform Unity-specific setup:

1. Open **Unity Hub**
2. Click **ADD** → Select the `FishMMO-Unity` directory
3. Open the project with **Unity 6.3 LTS**
4. Wait for the initial asset import and script compilation to complete
5. Follow the [Unity Project Setup](#unity-project-setup) section below

---

## Database Setup

The FishMMO-Installer automates database creation (Database menu, option `4`), but here is what happens under the hood:

1. **PostgreSQL Installation** — The installer installs PostgreSQL via your platform's package manager.
2. **Database + User Creation** — Creates the `fish_mmo_postgresql` database and a dedicated `fishmmo` user role.
3. **EF Core Migration** — Creates and applies an initial Entity Framework Core migration.
4. **Permissions** — Grants the user full privileges on the `public` schema.

### Manual Database Setup (Without the Installer)

If you prefer to set up the database manually:

```bash
# Linux — become the postgres user
sudo -u postgres psql
```

```sql
CREATE USER fishmmo WITH PASSWORD 'your_secure_password';
CREATE DATABASE fish_mmo_postgresql OWNER fishmmo;
GRANT ALL PRIVILEGES ON DATABASE fish_mmo_postgresql TO fishmmo;
\c fish_mmo_postgresql
GRANT ALL ON SCHEMA public TO fishmmo;
```

Then apply the EF Core migrations:

```bash
cd FishMMO-Database/FishMMO-DB-Migrator
dotnet run
```

### Creating New Migrations

When your data model changes, create a new migration via the Installer (Database menu, option `5`) or manually:

```bash
cd FishMMO-Database/FishMMO-DB-Migrator
dotnet ef migrations add YourMigrationName
dotnet run
```

### Database Configuration

Configuration is loaded from `appsettings.json` (placed alongside the server executable at runtime). See [appsettings.json — Database & Redis](#appsettingsjson--database--redis).

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
| `Redis__Host` | Redis host |
| `Redis__Port` | Redis port |
| `Redis__Password` | Redis password |

Example (fish shell):

```fish
set -Ux FISHMMO_ENVIRONMENT Production
set -Ux Npgsql__Password super_secret
set -Ux Redis__Password redis_secret
```

Example (bash):

```bash
export FISHMMO_ENVIRONMENT=Production
export Npgsql__Password=super_secret
```

---

## Redis Setup

Redis is used for cross-server caching and pub/sub. It is optional but recommended for production deployments with multiple scene servers.

### Installation

Use the Installer (Database menu, option `3`) or install manually:

**Linux (Ubuntu/Debian):**
```bash
sudo apt install redis-server
sudo systemctl enable --now redis-server
```

**Linux (Arch/CachyOS):**
```bash
sudo pacman -S redis
sudo systemctl enable --now redis
```

**Windows:**
```powershell
# Using the installer, or manually via:
winget install Redis
```

### Configuration

Set a password for production use:

**Linux:** Edit `/etc/redis/redis.conf`:
```
requirepass your_redis_password
```

Then restart: `sudo systemctl restart redis`

**Configure in appsettings.json:**
```json
{
  "Redis": {
    "Host": "127.0.0.1",
    "Port": "6379",
    "Password": "your_redis_password"
  }
}
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
| **Development** | `127.0.0.1` (loopback) | Local testing |
| **Release** | `0.0.0.0` (all interfaces) | Production deployment |

The build process copies the appropriate `.cfg` and `appsettings.json` files from `FishMMO-Setup/Development/` or `FishMMO-Setup/Release/` into the build output.

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

    /// NGINX game WebSocket hostname for Bayou/WebGL clients.
    /// WebGL clients connect via wss://GameHost/ws/{port} instead of direct IP:port.
    public static readonly string GameHost = "game.fishmmo.com";
}
```

| Field | Purpose | Update When |
|---|---|---|
| `APIHost` | Base URL for IPFetch and Patcher API calls | You use a different domain or run without NGINX |
| `GameHost` | WebSocket hostname for WebGL game connections | You use a different domain for game traffic |

> For local development, override these to `https://localhost/` and `localhost` respectively, or configure your hosts file.

### Server Configuration Files

Each server type reads a `.cfg` file from its working directory. Templates are in `FishMMO-Setup/Development/` and `FishMMO-Setup/Release/`.

#### LoginServer.cfg

```ini
ServerName=LoginServer
MaximumClients=4000
Address=127.0.0.1
Port=7770
StaleSceneTimeout=5
```

#### WorldServer.cfg

```ini
ServerName=World Server
MaximumClients=4000
Address=127.0.0.1
Port=7780
StaleSceneTimeout=5
```

#### SceneServer.cfg

```ini
ServerName=Scene Server
MaximumClients=4000
Address=127.0.0.1
Port=7781
StaleSceneTimeout=5
```

| Key | Description | Default |
|---|---|---|
| `ServerName` | Display name for logs and monitoring | varies |
| `MaximumClients` | Maximum concurrent connections | 4000 |
| `Address` | Bind address (`127.0.0.1` for dev, `0.0.0.0` for production) | varies |
| `Port` | Listen port (Login=7770, World=7780, Scene=7781+) | varies |
| `StaleSceneTimeout` | Seconds before idle scenes are unloaded | 5 |

**Format:** Simple `key=value` per line. Lines starting with `#` or `;` are comments.

> **Production:** Edit the `Release/` templates before building. Each SceneServer needs a unique port if running multiple instances.

### Logging Configuration

Each server needs a `logging.json` file placed alongside the executable. There is no template in `FishMMO-Setup/` — create one for your environment:

```json
{
  "LogLevel": "Info",
  "Loggers": [
    {
      "Type": "File",
      "Config": {
        "FilePath": "logs/server.log",
        "Append": true,
        "MaxFileSizeMB": 10
      }
    }
  ]
}
```

**Log levels:** `Trace`, `Debug`, `Info`, `Warning`, `Error`, `Critical`.

**Multiple sinks** can be configured:

```json
{
  "LogLevel": "Info",
  "Loggers": [
    {
      "Type": "File",
      "Config": {
        "FilePath": "logs/server.log",
        "Append": true,
        "MaxFileSizeMB": 50
      }
    },
    {
      "Type": "Email",
      "Config": {
        "MinimumLevel": "Error",
        "SmtpServer": "smtp.example.com",
        "Port": 587,
        "EnableSsl": true,
        "Username": "alerts@example.com",
        "Password": "********",
        "From": "alerts@example.com",
        "To": "ops@example.com",
        "Subject": "FishMMO Alert"
      }
    }
  ]
}
```

> **Security:** Keep `logging.json` out of source control if it contains SMTP credentials. Load secrets from environment variables instead.

### appsettings.json — Database & Redis

Placed alongside every server executable and web service. Templates with `__REPLACE_ME__` placeholders are in `FishMMO-Setup/`. Fill in your actual credentials before deploying:

```json
{
  "Npgsql": {
    "Database": "fish_mmo_postgresql",
    "Username": "fishmmo",
    "Password": "your_secure_password",
    "Host": "127.0.0.1",
    "Port": "5432"
  },
  "Redis": {
    "Host": "127.0.0.1",
    "Port": "6379",
    "Password": "your_redis_password"
  }
}
```

If using PgBouncer, change `Npgsql.Port` to `6432` (see [Configure pgBouncer](#configure-pgbouncer)).

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
   local   fish_mmo_postgresql   fishmmo   md5
   host    fish_mmo_postgresql   fishmmo   127.0.0.1/32   md5
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

**Linux:** The installer's "Configure PgBouncer" option (Database menu, option `8`) generates `pgbouncer.ini` and `userlist.txt` for you. Otherwise, manually edit `/etc/pgbouncer/pgbouncer.ini`.

**Windows:** Edit `pgbouncer.ini` in the install directory.

##### pgbouncer.ini

```ini
[databases]
fish_mmo_postgresql = host=127.0.0.1 port=5432 dbname=fish_mmo_postgresql

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
psql -h 127.0.0.1 -p 6432 -U fishmmo -d fish_mmo_postgresql
```

**Windows:**
```powershell
sc.exe query pgbouncer
```

### Configure Redis

Redis is optional but recommended for production. It handles cross-server caching and pub/sub messaging.

1. **Install** via the Installer (Database menu, option `3`) or manually (see [Redis Setup](#redis-setup)).
2. **Set a password** in `redis.conf`: `requirepass your_redis_password`
3. **Update `appsettings.json`** with the Redis host, port, and password.
4. **Restart Redis:** `sudo systemctl restart redis`

### Configure NGINX

NGINX acts as the reverse proxy and SSL terminator for all FishMMO web services and WebSocket game traffic. The reference configuration is at `FishMMO-Setup/nginx.conf`.

#### Architecture

```
Internet
   │
   ▼ HTTPS (ports 80/443 only)
┌──────────┐
│  NGINX   │  ← SSL termination, rate limiting, WebSocket upgrade
└────┬─────┘
     │ HTTP (localhost only)
     ├──→ play.fishmmo.com  → WebGL Server  (localhost:8000)
     ├──→ api.fishmmo.com   → IPFetch       (localhost:8080)
     │                      → Patcher       (localhost:8090)
     └──→ game.fishmmo.com  → Game Servers  (localhost:7770-7899 via /ws/{port})
```

#### Subdomain Routing

| Subdomain | Backend | Port | Purpose |
|---|---|---|---|
| `play.fishmmo.com` | WebGL Server | 8000 | Serves Unity WebGL builds |
| `api.fishmmo.com` | IPFetch / Patcher | 8080 / 8090 | Login server discovery + patch delivery |
| `game.fishmmo.com` | Game Servers | 7770–7899 | WebSocket proxy for Bayou/WebGL clients |

#### Installation

Use the Installer:

1. **Web Server menu, option `1`** — Install NGINX
2. **Web Server menu, option `3`** — Deploy FishMMO nginx.conf

Or manually:

**Linux:**
```bash
# Install
sudo apt install nginx

# Copy the reference config
sudo cp FishMMO-Setup/nginx.conf /etc/nginx/nginx.conf
sudo chown root:root /etc/nginx/nginx.conf
sudo chmod 644 /etc/nginx/nginx.conf

# Create certbot webroot
sudo mkdir -p /var/www/certbot/.well-known/acme-challenge

# Open firewall
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# Test and start
sudo nginx -t
sudo systemctl enable --now nginx
sudo systemctl reload nginx
```

**Windows:**
```powershell
# Copy config
Copy-Item "FishMMO-Setup\nginx.conf" "C:\nginx\conf\nginx.conf" -Force

# Open firewall
netsh advfirewall firewall add rule name="FishMMO HTTP" dir=in action=allow protocol=TCP localport=80
netsh advfirewall firewall add rule name="FishMMO HTTPS" dir=in action=allow protocol=TCP localport=443

# Validate and restart
& "C:\nginx\nginx.exe" -t
sc.exe stop "FishMMO-NGINX"
sc.exe start "FishMMO-NGINX"
```

#### SSL Certificates (Let's Encrypt)

Use the Installer (Web Server menu, option `2`) to install or renew certificates:
- Enter your domains (e.g., `fishmmo.com,play.fishmmo.com,api.fishmmo.com,game.fishmmo.com`)
- Use staging mode first to validate the flow without hitting rate limits
- The installer updates the `ssl_certificate` / `ssl_certificate_key` paths in `nginx.conf` automatically

#### Key Security Features

| Feature | Detail |
|---|---|
| TLS 1.2 / 1.3 only | Older protocols disabled |
| OCSP Stapling | Faster TLS handshakes |
| Rate limiting | Per-IP zones for API, patch downloads, and WebGL assets |
| Connection limits | Per-IP connection caps per server block |
| Request body limits | 1KB default (prevents oversized payloads) |
| Slowloris mitigation | Header/body buffer and timeout limits |
| Hidden version | `server_tokens off` |

#### Game Server Port Mapping (WebSocket)

For WebGL clients, NGINX routes `wss://game.fishmmo.com/ws/{port}` to the correct backend. The port-to-address map is configured in `nginx.conf`:

```nginx
map $backend_port $backend_address {
    default       127.0.0.1;

    # Login Servers
    # 7770        192.168.1.10;       # Login Server A

    # World Servers
    # 7780        192.168.1.10;       # World Server A

    # Scene Servers
    # 7790        192.168.1.10;       # Scene Server A
    # 7791        192.168.1.11;       # Scene Server B
}
```

Allowed port range: **7770–7899** (130 server slots). Adjust the regex in the `game.fishmmo.com` server block if you need more.

#### Important Port Notes

| Port | Exposure | Purpose |
|---|---|---|
| 80, 443 | **Public** (forward through router) | NGINX (HTTP redirect + HTTPS) |
| 8000, 8080, 8090 | **Private** (localhost only) | Backend web servers |
| 7770–7899 | **Private** (behind NGINX) | Game servers (routed via WebSocket) |

> **Do NOT** publicly forward ports 8000, 8080, 8090, or 7770–7899. All traffic must go through NGINX on ports 80/443.

---

## Running the Servers

### Launch Order

Servers must be started in this exact order:

1. **PostgreSQL** — Must be running before any server starts.
2. **Redis** (optional) — Should be running before game servers start.
3. **PgBouncer** (optional) — If used, must be running before game servers start.
4. **LoginServer** — Must be running and registered in the database before World servers.
5. **WorldServer** — Must be running and registered before Scene servers.
6. **SceneServer(s)** — Can start once WorldServer is registered.
7. **IPFetch Server** — Should start after the LoginServer is registered.
8. **Patcher Server** — Can start independently; needs patch files.
9. **WebGL Server** — Can start independently; needs a WebGL build.

### Starting Game Servers

All three server types use the same `GameServer` executable with different launch arguments. Build the server first from Unity (`FishMMO → Build → Build Server`), then:

**Linux:**
```bash
# Start LoginServer
./GameServer LOGIN &
# Wait for LoginServer to register (~5-10 seconds)
sleep 10

# Start WorldServer
./GameServer WORLD &
sleep 5

# Start SceneServer(s)
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
- `appsettings.json` (database + Redis config)
- `logging.json` (logging config)
- `AddressableAssetsData/` (Addressable asset bundles from the build)
- `StreamingAssets/` (if any)

> The Unity build process copies the correct `.cfg` and `appsettings.json` from `FishMMO-Setup/` automatically.

### Starting Web Servers

**IPFetch Server (Login Server Discovery):**
```bash
cd FishMMO-WebServers/IPFetchASP.NET/IpFetchServer
dotnet run
# Or publish and run the binary:
# dotnet publish -c Release -o publish
# ./publish/IpFetchServer
```

**Patcher Server (Patch Delivery):**
```bash
cd FishMMO-WebServers/PatcherASP.NET/Patcher
dotnet run
```

**WebGL Server (Static File Serving):**
```bash
cd FishMMO-WebServers/WebGLServerASP.NET/WebGLServer
dotnet run
```

Place a `Patches/` directory alongside the Patcher server containing your generated patch ZIP files.

### Running Multiple Scene Servers

Each SceneServer needs a unique port. Copy `SceneServer.cfg` and change the port:

```ini
# SceneServer A — Port 7781
ServerName=Scene Server A
Port=7781

# SceneServer B — Port 7782
ServerName=Scene Server B
Port=7782
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
      "MonitoredPort": 7781,
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
      "MonitoredPort": 8080,
      "PortTypes": ["TCP"],
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
After=network.target postgresql.service redis.service pgbouncer.service

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
4. **Auth** — SRP-6a authentication handshake, optional TOTP 2FA.
5. **Character Select** — Choose or create a character.
6. **World Entry** — WorldServer routes the character to the correct SceneServer.
7. **Gameplay** — SceneServer handles all in-game simulation.

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

### Systemd Services

All FishMMO server components are designed to run as systemd services. Reference units:

#### GameServer Login

```ini
[Unit]
Description=FishMMO LoginServer
After=network.target postgresql.service redis.service pgbouncer.service
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

### Port Reference

| Port | Service | Exposure |
|---|---|---|
| 80 | NGINX (HTTP → HTTPS redirect) | Public |
| 443 | NGINX (HTTPS + WSS) | Public |
| 5432 | PostgreSQL | Private (localhost) |
| 6432 | PgBouncer | Private (localhost) |
| 6379 | Redis | Private (localhost) |
| 7770 | LoginServer | Private (behind NGINX for WebGL) |
| 7780 | WorldServer | Private (behind NGINX for WebGL) |
| 7781+ | SceneServer(s) | Private (behind NGINX for WebGL) |
| 8000 | WebGL Static Server | Private (behind NGINX) |
| 8080 | IPFetch Server | Private (behind NGINX) |
| 8090 | Patcher Server | Private (behind NGINX) |

> Desktop clients connect directly to game server ports (TCP/UDP). WebGL clients connect through NGINX via WebSocket on port 443.

---

## Flow Diagram

```mermaid
flowchart TB
    subgraph Internet
        Player["Player Client<br/>(Desktop / WebGL)"]
    end

    subgraph NGINX["NGINX Reverse Proxy (ports 80/443)"]
        SSL["SSL Termination<br/>Rate Limiting"]
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
        Scene1["SceneServer<br/>:7781<br/><i>Gameplay simulation</i>"]
        SceneN["SceneServer<br/>:778x<br/><i>Additional scenes</i>"]
    end

    subgraph DataLayer["Data Layer"]
        PgBouncer["pgBouncer<br/>:6432<br/><i>Connection pooler</i>"]
        PostgreSQL["PostgreSQL<br/>:5432"]
        Redis["Redis<br/>:6379<br/><i>Optional cache</i>"]
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

    Player -->|"HTTPS / WSS"| SSL
    SSL -->|"/loginserver"| IPFetch
    SSL -->|"/latest_version<br/>/{version}"| Patcher
    SSL -->|"play.fishmmo.com"| WebGL
    SSL -->|"game.fishmmo.com<br/>/ws/{port}"| Login
    SSL -->|"game.fishmmo.com<br/>/ws/{port}"| World
    SSL -->|"game.fishmmo.com<br/>/ws/{port}"| Scene1
    SSL -->|"news / content"| CMS

    Player -.->|"Direct TCP/UDP<br/>(non-WebGL)"| Login
    Player -.->|"Direct TCP/UDP<br/>(non-WebGL)"| World
    Player -.->|"Direct TCP/UDP<br/>(non-WebGL)"| Scene1

    Login --> PgBouncer
    World --> PgBouncer
    Scene1 --> PgBouncer
    SceneN --> PgBouncer
    PgBouncer --> PostgreSQL

    Login -.-> Redis
    World -.-> Redis
    Scene1 -.-> Redis
    SceneN -.-> Redis

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
