# FishMMO-Installer

FishMMO-Installer is a cross-platform .NET 8 console tool that automates FishMMO server dependency setup.

## Supported Platforms

| Platform | Install Strategy |
|---|---|
| Windows 10/11 | EXE/ZIP installers + Windows services |
| Ubuntu/Debian | `apt-get` + `systemctl` |
| Arch Linux / CachyOS | `pacman` (+ `yay` / `paru` for Unity Hub) + `systemctl` |

---

## What this installer can do

- Install DotNet SDK 8 + `dotnet-ef` `5.0.17`
- Install Visual Studio Build Tools (Windows)
- Install PgBouncer (connection pooler)
- Build all discovered C# projects under a selected root directory
- Install Unity Hub
- Install Unity Editor + selected modules
- Install NGINX and configure service startup
- Install/Renew Let's Encrypt certificates for NGINX
- Install PostgreSQL
- Create/manage FishMMO database and permissions

---

## Recent updates worth knowing

### Environment selection and configuration load flow

At startup, the installer resolves environment once and sets all three environment variables:

- `FISHMMO_ENVIRONMENT`
- `DOTNET_ENVIRONMENT`
- `ASPNETCORE_ENVIRONMENT`

Configuration is then loaded through shared design-time configuration logic, allowing `appsettings.json` plus environment overlays (`appsettings.{Environment}.json`).

### DotNet command reliability on Linux

Before DotNet commands execute, Linux runtime environment is prepared in-process:

- Adds `~/.dotnet` to `PATH` if missing
- Adds `~/.dotnet/tools` to `PATH` if missing
- Sets `DOTNET_ROOT` (`~/.dotnet` preferred, `/usr/share/dotnet` fallback)

This is especially important for EF migration commands run by the installer.

---

## Prerequisites

- .NET SDK 8+ to build and run this repository
- Administrator/root privileges for system-level installs
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

### Build and run

```bash
cd path/to/FishMMO-Installer
dotnet build
dotnet run --project FishMMO-Installer
```

Run binary directly:

- Windows: `FishMMO-Installer/bin/Debug/net8.0/FishMMO-Installer.exe`
- Linux: `./FishMMO-Installer/bin/Debug/net8.0/FishMMO-Installer`

---

## Operating system quick-start guides

### Windows quick start

1. Open **PowerShell as Administrator**.
2. Build and run:

```powershell
cd "path\to\FishMMO-Installer"
dotnet build
dotnet run --project FishMMO-Installer
```

3. Recommended installer order:
  - `1` Install DotNet
  - `2` Install Visual Studio Build Tools
  - `3` Install PgBouncer
  - `4` Build all C# Projects
  - `5` Install Unity Hub
  - `6` Install Unity Editor (+Modules)
  - `7` Install NGINX
  - `8` Install/Renew Let's Encrypt Certificate
  - `9` Install PostgreSQL
  - `A` Install FishMMO Database

### Ubuntu/Debian quick start

1. Open terminal.
2. Build and run:

```bash
cd /path/to/FishMMO-Installer
dotnet build
dotnet run --project FishMMO-Installer
```

3. Recommended installer order:
  - `1` Install DotNet
  - `3` Install PgBouncer
  - `4` Build all C# Projects
  - `5` Install Unity Hub
  - `6` Install Unity Editor (+Modules)
  - `7` Install NGINX
  - `8` Install/Renew Let's Encrypt Certificate
  - `9` Install PostgreSQL
  - `A` Install FishMMO Database

Notes:

- You will be prompted for `sudo` password for system operations.
- NGINX and PostgreSQL are managed with `systemctl`.

### Arch Linux / CachyOS quick start

1. Open terminal.
2. Build and run:

```bash
cd /path/to/FishMMO-Installer
dotnet build
dotnet run --project FishMMO-Installer
```

3. Recommended installer order:
  - `1` Install DotNet
  - `3` Install PgBouncer
  - `4` Build all C# Projects
  - `5` Install Unity Hub (requires `yay` or `paru`)
  - `6` Install Unity Editor (+Modules)
  - `7` Install NGINX
  - `8` Install/Renew Let's Encrypt Certificate
  - `9` Install PostgreSQL
  - `A` Install FishMMO Database

If you do not have an AUR helper:

```bash
sudo pacman -S --needed git base-devel
git clone https://aur.archlinux.org/yay.git
cd yay
makepkg -si
```

---

## Configuration

Database options require `appsettings.json` in the working directory:

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

## Current Menu

```text
Welcome to the FishMMO Installer Tool.
Press a key (0-9, A-D):
1 : Install DotNet
2 : Install Visual Studio Build Tools (Windows Only)
3 : Install PgBouncer (Connection Pooler)
4 : Build all C# Projects
5 : Install Unity Hub
6 : Install Unity Editor (+Modules)
7 : Install NGINX (Web Server/Reverse Proxy)
8 : Install/Renew Let's Encrypt Certificate (NGINX)
9 : Install PostgreSQL (Database Server)
A : Install FishMMO Database (User/Schema/Initial Migration)
B : Create new database migration
C : Grant User Permissions on Database
D : Delete FishMMO Database (DANGEROUS!)
0 : Quit
```

---

## Common usage examples

### Example A: Fresh Linux setup (NGINX + cert + DB)

1. Run installer.
2. Press `7` to install NGINX.
3. Press `8` to request certificate:
  - Enter domains (for example: `fishmmo.com,play.fishmmo.com,api.fishmmo.com,game.fishmmo.com`)
  - Enter email
  - Use staging first (`Y`) to validate flow
4. Press `9` to install PostgreSQL.
5. Press `A` to create FishMMO database and optional initial migration.

### Example B: Fresh Windows setup

1. Run installer from elevated PowerShell.
2. Press `7` to install NGINX (ZIP extraction + Windows service creation).
3. Press `8` to install/renew certificate with win-acme.
4. Press `9` then `A` for PostgreSQL + FishMMO DB setup.

### Example C: Add a new migration

1. Press `B`.
2. Enter migration name (letters/numbers, starts with a letter), for example: `AddGuildSettings`.
3. Installer will create migration and run database update.

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

## Menu Option Details

### 1 — Install DotNet

- Installs or validates DotNet SDK 8
- Installs `dotnet-ef` `5.0.17` (required tooling version)
- Uses SDK detection via `dotnet --list-sdks`
- Linux path setup includes `~/.dotnet` and `~/.dotnet/tools`

### 2 — Install Visual Studio Build Tools (Windows only)

Downloads and runs Build Tools installer silently with required workloads.

### 3 — Install PgBouncer (Connection Pooler)

- Windows: attempts `winget` package install first, then Chocolatey fallback
- Ubuntu/Debian: package install + `systemctl enable --now pgbouncer`
- Arch/CachyOS: package install + `systemctl enable --now pgbouncer`
- Prints post-install checklist for mapping FishMMO DB through pool port `6432`

### 4 — Build all C# Projects

- Prompts for root folder (auto-suggested from solution hierarchy)
- Recursively discovers `.csproj` files
- Cleans and builds each project with parallel execution
- Prints success/failure summary with per-project errors

### 5 — Install Unity Hub

- Windows: Unity Hub installer EXE
- Ubuntu/Debian: Unity apt repository + `apt-get install unityhub`
- Arch/CachyOS: AUR install with detected `yay`/`paru`

### 6 — Install Unity Editor (+Modules)

Installs editor via Unity Hub CLI with module selection.

Default editor version: `6000.3.2f1`.

### 7 — Install NGINX

#### Windows

- Downloads:
  - `https://github.com/nginx/nginx/releases/download/release-1.29.5/nginx-1.29.5.zip`
- Extract root: `C:\nginx`
- Expected home: `C:\nginx\nginx-1.29.5`
- Service name: `FishMMO-NGINX`
- Uses NSSM wrapper for reliable Windows service mode
- Sets automatic startup and ensures service is running

#### Linux (Ubuntu + Arch/CachyOS)

- Installs `nginx`
- Enables and starts with `systemctl enable --now nginx`
- Verifies enabled/active status

### 8 — Install/Renew Let's Encrypt Certificate (NGINX)

Requires NGINX to be installed first.

#### Linux

- Installs certbot packages
- Uses HTTP-01 webroot issuance (`certbot certonly --webroot`)
- Validates ACME location compatibility in `nginx.conf`
- Updates SSL cert/key paths in `nginx.conf`
- Runs `nginx -t` then reloads NGINX

Default prompt values:

- nginx.conf: `~/Dev/FishMMO Dev/FishMMO-Setup/nginx.conf`
- ACME webroot: `/var/www/certbot`
- WebServers path: `~/Dev/FishMMO Dev/FishMMO-WebServers`

#### Windows

- Uses win-acme
- Uses filesystem validation and PEM output
- Resolves generated PEM files and updates `nginx.conf`
- Tests config and reloads NGINX

Notes:

- Wildcard domains (`*.example.com`) are intentionally rejected in this webroot workflow.
- Use staging mode first for safe validation.

### 9 — Install PostgreSQL

- Windows: unattended EDB installer flow
- Ubuntu/Debian: package install + `systemctl`
- Arch/CachyOS: package install + initdb (if needed) + `systemctl`

### A — Install FishMMO Database

- Creates FishMMO DB and user role (if missing)
- Grants database privileges
- Optionally creates/applies `Initial` EF migration

### B — Create new database migration

- Creates migration via EF CLI
- Applies migration via database update
- Migration name validation is enforced

### C — Grant User Permissions on Database

Grants comprehensive privileges on existing and future objects in `public` schema.

### D — Delete FishMMO Database (DANGEROUS)

Requires explicit `DELETE` confirmation and terminates active sessions before drop.

### 0 — Quit

Exits installer.

---

## Platform-Specific Summary

### Windows

- NGINX: ZIP install + NSSM-backed service (`FishMMO-NGINX`)
- Let's Encrypt: win-acme + PEM integration
- PostgreSQL: EDB unattended installer
- PgBouncer: package-manager installation attempt (`winget` then Chocolatey fallback)
- VS Build Tools: supported

### Ubuntu/Debian

- Package manager: `apt-get`
- PgBouncer: package install + systemd enable/start
- Service management: `systemctl`
- Let's Encrypt: certbot webroot flow

### Arch/CachyOS

- Package manager: `pacman`
- PgBouncer: package install + systemd enable/start
- Unity Hub: AUR helper (`yay`/`paru`)
- Service management: `systemctl`
- Let's Encrypt: certbot webroot flow

---

## Project Structure

| File | Responsibility |
|---|---|
| `Program.cs` | Entry point and interactive menu |
| `InstallationConstants.cs` | URLs, versions, filenames, key default paths |
| `InstallerProcessHelper.cs` | Process/shell helpers, download utilities, prompts/logging |
| `DotNetInstaller.cs` | DotNet SDK and EF tooling installation + EF commands |
| `VSBuildToolsInstaller.cs` | VS Build Tools (Windows) |
| `PgBouncerInstaller.cs` | PgBouncer installation + post-install pooling guidance |
| `ProjectBuildInstaller.cs` | Recursive C# project discovery + parallel clean/build |
| `UnityInstaller.cs` | Unity Hub and Unity Editor installation |
| `NGINXInstaller.cs` | NGINX installation and service management |
| `LetsEncryptInstaller.cs` | Certificate install/renew flows for NGINX |
| `PostgreSQLInstaller.cs` | PostgreSQL and FishMMO DB operations |

## License

See the main FishMMO repository for license information.