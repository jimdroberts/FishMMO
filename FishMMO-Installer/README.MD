# FishMMO-Installer

FishMMO-Installer is a cross-platform .NET 8 console tool that automates FishMMO server dependency setup. It provides an interactive menu-driven interface to install and configure all infrastructure components required to run a FishMMO game server.

## Supported Platforms

| Platform | Install Strategy |
|---|---|
| Windows 10/11 | EXE/ZIP installers + Windows services (UAC elevation) |
| Ubuntu/Debian | `apt-get` + `systemctl` |
| Arch Linux / CachyOS | `pacman` + `systemctl` |
| RHEL / Fedora | `dnf` / `yum` + `systemctl` |

---

## Menu Structure

```
=== FishMMO Installer ===
1 : Runtime & Tooling
2 : Database
3 : Web Server
4 : Unity & Build
5 : Configuration
0 : Quit
```

### 1 — Runtime & Tooling

| Option | Description |
|---|---|
| 1 | Install DotNet SDK `8.0.302` |
| 2 | Install ASP.NET Core Runtime `8.0.16` |
| 3 | Install Visual Studio Build Tools *(Windows only)* |

VS Build Tools installs the **`.NET desktop development`** and **`Desktop development with C++`** workloads plus the Windows 10 SDK silently via the VS 2022 bootstrapper.

### 2 — Database

| Option | Description |
|---|---|
| 1 | Install PostgreSQL |
| 2 | Install PgBouncer (connection pooler) |
| 3 | Install FishMMO Database (user / schema / initial EF migration) |
| 4 | Create a new EF Core database migration |
| 5 | Grant user permissions on the FishMMO database |
| 6 | **Delete FishMMO Database** *(destructive — prompts for confirmation)* |

### 3 — Web Server

| Option | Description |
|---|---|
| 1 | Install NGINX `1.29.5` and configure service startup |
| 2 | Install / Renew Let's Encrypt certificate for NGINX |

On **Windows** NGINX runs as a Windows service managed by NSSM `2.24` and TLS is automated via win-acme `2.2.9.1701`.  
On **Linux** NGINX is managed by systemd and TLS is automated via certbot.

### 4 — Unity & Build

| Option | Description |
|---|---|
| 1 | Install Unity Hub |
| 2 | Install Unity Editor `6000.3.2f1` with selectable modules |
| 3 | Build all C# projects under a selected root directory |

**Available Unity modules**

| Module ID | Display Name |
|---|---|
| `linux-il2cpp` | Linux Build Support (IL2CPP) |
| `linux-mono` | Linux Build Support (Mono) |
| `linux-dedicated-server` | Linux Dedicated Server Build Support |
| `mac-mono` | Mac Build Support (Mono) |
| `mac-il2cpp` | Mac Build Support (IL2CPP) |
| `mac-server` | Mac Dedicated Server Build Support |
| `windows-mono` | Windows Build Support (Mono) |
| `windows-il2cpp` | Windows Build Support (IL2CPP) |
| `windows-dedicated-server` | Windows Dedicated Server Build Support |
| `webgl` | Web Build Support |
| `android` | Android Build Support |
| `ios` | iOS Build Support |

**Default modules installed:** `linux-il2cpp`, `linux-dedicated-server`, `webgl`, `windows-mono`, `windows-il2cpp`, `windows-dedicated-server`

**Project build** scans the chosen directory recursively for `.csproj` files, builds dependency-order projects synchronously first, then runs the remaining builds in parallel.

### 5 — Configuration

| Option | Description |
|---|---|
| 1 | Configure AppSettings (secure setup wizard) |

The AppSettings wizard generates:

- `appsettings.json` — base configuration (`chmod 600` on Linux)
- `appsettings.Development.json` / `appsettings.Production.json` — environment overrides (`chmod 600`)
- `~/.config/fish/conf.d/fishmmo-secrets.fish` — fish shell secrets snippet (`chmod 600`)
- `fishmmo-secrets.env` — systemd / `.env` secrets file in the target directory (`chmod 600`)

---

## Key Component Versions

| Component | Version |
|---|---|
| .NET SDK | `8.0.302` |
| dotnet-ef | `5.0.17` |
| ASP.NET Core Runtime | `8.0.16` |
| PostgreSQL | latest via package manager (Linux) / EnterpriseDB installer (Windows) |
| PgBouncer | latest via package manager, default port `6432` |
| NGINX | `1.29.5` (Windows ZIP), package manager (Linux) |
| Unity Editor | `6000.3.2f1` (default, overridable at prompt) |
| win-acme | `2.2.9.1701` (Windows TLS automation) |
| NSSM | `2.24` (Windows service wrapper for NGINX) |
| Visual Studio Build Tools | VS 2022 bootstrapper (`aka.ms/vs/17/release/vs_buildtools.exe`) |

---

## Architecture

| File | Responsibility |
|---|---|
| `Program.cs` | Entry point — environment setup, config load, interactive menu loop |
| `InstallationConstants.cs` | All download URLs, filenames, version strings, default config values, EF paths |
| `InstallerProcessHelper.cs` | Shared process execution, shell detection (fish → bash → cmd), file downloads, prompting utilities, Linux DotNet PATH/DOTNET_ROOT bootstrap |
| `DotNetInstaller.cs` | .NET SDK and ASP.NET Runtime install and version detection |
| `VSBuildToolsInstaller.cs` | VS Build Tools silent install (Windows only) |
| `PostgreSQLInstaller.cs` | PostgreSQL install, database/user creation, EF migrations, permission grants, database deletion |
| `PgBouncerInstaller.cs` | PgBouncer install and systemd service setup |
| `NGINXInstaller.cs` | NGINX install, Windows service registration (NSSM), Linux systemd configuration |
| `LetsEncryptInstaller.cs` | certbot (Linux) and win-acme (Windows) TLS certificate automation for NGINX |
| `UnityInstaller.cs` | Unity Hub and Unity Editor install via Unity Hub CLI with module selection |
| `ProjectBuildInstaller.cs` | Staged/parallel `dotnet build` across all discovered `.csproj` files |
| `AppSettingsInstaller.cs` | Interactive wizard to write `appsettings.json`, env overlays, and secrets env-var files |

---

## Configuration & Environment

At startup, the installer resolves the active environment name once and propagates it to all three standard variables:

```
FISHMMO_ENVIRONMENT
DOTNET_ENVIRONMENT
ASPNETCORE_ENVIRONMENT
```

Configuration is loaded via `DatabaseConfigurationHelper.BuildDesignTimeConfiguration`, merging `appsettings.json` with an optional `appsettings.{Environment}.json` overlay. The installer looks for `appsettings.json` first in the EXE directory, then falls back to `FishMMO-Database/FishMMO-DB/`.

Logging is bootstrapped from `logging.json` in the EXE directory before any other operation.

### Linux DotNet environment

Before any `dotnet` command runs on Linux, the installer prepares the runtime environment in-process (once per process lifetime):

- Adds `~/.dotnet` to `PATH` if missing
- Adds `~/.dotnet/tools` to `PATH` if missing
- Sets `DOTNET_ROOT` to `~/.dotnet` if it exists, otherwise `/usr/share/dotnet`

### EF Core project paths (relative to EXE output directory)

| Role | Path |
|---|---|
| EF project | `./FishMMO-Database/FishMMO-DB/FishMMO-DB.csproj` |
| Startup project | `./FishMMO-Database/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj` |
| Migrations output | `Migrations/` (inside the FishMMO-DB project) |

> **MSBuild note:** A `StashMigrations` target preserves generated migration files across `dotnet clean` and `dotnet build` cycles by temporarily copying them outside the output directory.

---

## Prerequisites

- .NET SDK 8+ to build and run this repository
- Administrator / root privileges for system-level installs
- Internet connectivity for downloads and package repositories

### Install .NET SDK (if not already installed)

#### Windows 10/11

Install from Microsoft:

- https://dotnet.microsoft.com/download/dotnet/8.0

Then verify:

```powershell
dotnet --list-sdks
```

#### Ubuntu/Debian

```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0
dotnet --list-sdks
```

#### Arch Linux / CachyOS

```bash
sudo pacman -S --noconfirm dotnet-sdk
dotnet --list-sdks
```

#### RHEL / Fedora

```bash
sudo dnf install -y dotnet-sdk-8.0
dotnet --list-sdks
```

---

## Build and Run

```bash
cd path/to/FishMMO-Installer
dotnet build
dotnet run --project FishMMO-Installer
```

Run the compiled binary directly:

- Windows: `FishMMO-Installer/bin/Debug/net8.0/FishMMO-Installer.exe`
- Linux: `./FishMMO-Installer/bin/Debug/net8.0/FishMMO-Installer`

> Ensure `appsettings.json` and `logging.json` are present in the same directory as the binary before running database operations.

---

## appsettings.json Example

Database operations require `appsettings.json` in the working directory:

```json
{
  "Npgsql": {
    "Database": "fishmmo",
    "Schema": "public",
    "Username": "fishmmo",
    "Password": "your_password",
    "Host": "localhost",
    "Port": "5432"
  }
}
```

---

## Operational checks after installation

### NGINX

- Linux:

```bash
sudo systemctl status nginx
sudo nginx -t
```

- Windows (PowerShell):

```powershell
sc.exe query "FishMMO-NGINX"
```

### PostgreSQL

- Linux:

```bash
sudo systemctl status postgresql
```

- Windows: verify PostgreSQL service is running in Services or with `sc.exe query`.

### DotNet + EF tool

```bash
dotnet --list-sdks
dotnet tool list --global
```

Confirm `dotnet-ef` appears in the global tools list.

---

## NGINX setup guide (per OS)

This section shows exactly where to place `nginx.conf`, which ports must be opened/forwarded, and verification commands.

### Required networking model

- Public internet ports to expose:
  - `80/tcp` (Let's Encrypt HTTP-01 challenge + HTTP→HTTPS redirect)
  - `443/tcp` (all game/web/API traffic through NGINX)
- Keep backend app ports local-only (do **not** forward publicly):
  - `8000` (WebGL server)
  - `8080` (IPFetch)
  - `8090` (Patcher)
- If using current websocket routing (`wss://game.<domain>/ws/{port}`), keep game ports `7770-7899` private behind NGINX.

> Router/NAT forwarding itself is usually configured on your router UI (not on the host terminal). Terminal commands below open host firewall rules only.

### Linux (Ubuntu/Debian + Arch/CachyOS)

#### 1) Place your nginx.conf

Recommended production location:

```bash
sudo cp "/home/$USER/Dev/FishMMO Dev/FishMMO-Setup/nginx.conf" /etc/nginx/nginx.conf
sudo chown root:root /etc/nginx/nginx.conf
sudo chmod 644 /etc/nginx/nginx.conf
```

#### 2) Ensure certbot challenge webroot exists

```bash
sudo mkdir -p /var/www/certbot/.well-known/acme-challenge
sudo chown -R root:root /var/www/certbot
sudo chmod -R 755 /var/www/certbot
```

#### 3) Open host firewall ports 80/443

If using `ufw`:

```bash
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw status
```

If using `firewalld`:

```bash
sudo firewall-cmd --permanent --add-service=http
sudo firewall-cmd --permanent --add-service=https
sudo firewall-cmd --reload
sudo firewall-cmd --list-services
```

#### 4) Test and reload NGINX

```bash
sudo nginx -t
sudo systemctl enable --now nginx
sudo systemctl reload nginx
sudo systemctl status nginx
```

#### 5) Router/NAT forwarding

Forward these from router WAN to your Linux host LAN IP:

- `TCP 80` → `<server_lan_ip>:80`
- `TCP 443` → `<server_lan_ip>:443`

### Windows 10/11

#### 1) Place your nginx.conf

If installed by this installer, expected NGINX home is:

- `C:\nginx\nginx-1.29.5`

Copy config:

```powershell
Copy-Item "C:\path\to\FishMMO-Setup\nginx.conf" "C:\nginx\nginx-1.29.5\conf\nginx.conf" -Force
```

#### 2) Open Windows Firewall ports 80/443

Run PowerShell as Administrator:

```powershell
netsh advfirewall firewall add rule name="FishMMO NGINX HTTP" dir=in action=allow protocol=TCP localport=80
netsh advfirewall firewall add rule name="FishMMO NGINX HTTPS" dir=in action=allow protocol=TCP localport=443
netsh advfirewall firewall show rule name="FishMMO NGINX HTTP"
netsh advfirewall firewall show rule name="FishMMO NGINX HTTPS"
```

#### 3) Validate and restart NGINX service

```powershell
& "C:\nginx\nginx-1.29.5\nginx.exe" -t
sc.exe stop "FishMMO-NGINX"
sc.exe start "FishMMO-NGINX"
sc.exe query "FishMMO-NGINX"
```

#### 4) Router/NAT forwarding

Forward these from router WAN to your Windows host LAN IP:

- `TCP 80` → `<server_lan_ip>:80`
- `TCP 443` → `<server_lan_ip>:443`

### Verify externally after forwarding

From any external machine/network:

```bash
curl -I http://your-domain
curl -I https://your-domain
```

Expected outcome:

- HTTP responds with redirect to HTTPS (or ACME challenge path on renewal)
- HTTPS responds successfully with your configured certificate

### Important FishMMO port notes

- Do **NOT** public-forward `8000`, `8080`, `8090`.
- Do **NOT** public-forward `7770-7899` unless intentionally bypassing NGINX websocket path routing.
- Primary public surface should be only `80` and `443`.

---

## License

See the main FishMMO repository for license information.