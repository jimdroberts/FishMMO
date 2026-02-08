# FishMMO-Installer

A cross-platform .NET 8.0 console application that automates the installation and configuration of all FishMMO server dependencies.

## Supported Platforms

| Platform | Package Managers |
|---|---|
| Windows 10/11 | Direct installers (EXE/ZIP) |
| Ubuntu / Debian | apt-get |
| CachyOS / Arch Linux | pacman, yay, paru (AUR) |

---

## Prerequisites

The only prerequisite is [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). If you don't have it, the installer can install it for you (option 1).

---

## Building & Running

### Windows

Open a terminal (PowerShell or Command Prompt) and run:

```powershell
cd path\to\FishMMO-Installer
dotnet build
dotnet run --project FishMMO-Installer
```

Or run the compiled binary directly:

```powershell
.\FishMMO-Installer\bin\Debug\net8.0\FishMMO-Installer.exe
```

### Ubuntu / Debian

```bash
cd path/to/FishMMO-Installer
dotnet build
dotnet run --project FishMMO-Installer
```

Or run the compiled binary directly:

```bash
./FishMMO-Installer/bin/Debug/net8.0/FishMMO-Installer
```

Some operations (package installs, `systemctl`, PostgreSQL superuser commands) require `sudo`. The installer will invoke `sudo` as needed — you may be prompted for your password.

### CachyOS / Arch Linux

```bash
cd path/to/FishMMO-Installer
dotnet build
dotnet run --project FishMMO-Installer
```

Or run the compiled binary directly:

```bash
./FishMMO-Installer/bin/Debug/net8.0/FishMMO-Installer
```

The installer uses `pacman` for system packages and detects `yay` or `paru` for AUR packages (Unity Hub). If you don't have an AUR helper, install one first:

```bash
sudo pacman -S --needed git base-devel
git clone https://aur.archlinux.org/yay.git && cd yay && makepkg -si
```

> **Note:** A post-build step automatically copies the `FishMMO-Database` project directory into the build output for EF Core migration support.

---

## Configuration

Database operations (options 6–9, A) require an `appsettings.json` file in the working directory with Npgsql connection settings:

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

## Menu Options

```
Welcome to the FishMMO Installer Tool.
Press a key (0-9, A):
1 : Install DotNet
2 : Install Visual Studio Build Tools (Windows Only)
3 : Install Unity Hub
4 : Install Unity Editor (+Modules)
5 : Install NGINX (Web Server/Reverse Proxy)
6 : Install PostgreSQL (Database Server)
7 : Install FishMMO Database (User/Schema/Initial Migration)
8 : Create new database migration
9 : Grant User Permissions on Database
A : Delete FishMMO Database (DANGEROUS!)
0 : Quit
```

### 1 — Install DotNet

Installs the .NET 8.0 SDK and the `dotnet-ef` 5.0.17 global tool (Entity Framework CLI).

| Platform | How it works |
|---|---|
| **Windows** | Downloads the official `dotnet-sdk-8.0.302-win-x64.exe` installer and runs a silent install (`/install /quiet /norestart`). |
| **Ubuntu / Debian** | Downloads the `dotnet-install.sh` script from Microsoft and runs it with `--version 8.0.302`. |
| **CachyOS / Arch** | Same as Ubuntu — uses `dotnet-install.sh`. |

After the SDK is installed, the installer checks for `dotnet-ef` and offers to install it globally via `dotnet tool install --global dotnet-ef --version 5.0.17`. On Linux, `~/.dotnet/tools` is automatically added to `PATH` and `DOTNET_ROOT` is set to `/usr/share/dotnet`.

### 2 — Install Visual Studio Build Tools (Windows Only)

Downloads the Visual Studio Build Tools bootstrapper and performs a silent installation with the following workloads:

- **.NET desktop development** — includes .NET Framework development tools.
- **Desktop development with C++** — MSVC 64-bit compiler for x86, x64, ARM, and ARM64.
- **Windows 10 SDK** (19041).

This option is skipped automatically on Linux.

### 3 — Install Unity Hub

Installs Unity Hub, which is required before installing any Unity Editor version (option 4).

| Platform | How it works |
|---|---|
| **Windows** | Downloads `UnityHubSetup.exe` from Unity's CDN and runs a silent install (`/S`). |
| **Ubuntu / Debian** | Adds the official Unity GPG key and apt repository (`hub.unity3d.com`), then runs `sudo apt-get install -y unityhub`. |
| **CachyOS / Arch** | Detects an AUR helper (`yay` or `paru`) and installs the `unityhub` AUR package. |

The installer detects whether Unity Hub is already present by running `unityhub -- --headless editors --installed`.

### 4 — Install Unity Editor (+Modules)

Installs a specific Unity Editor version with build modules using the Unity Hub CLI. Requires Unity Hub to be installed first (option 3).

**Prompts:**
1. **Unity version** — defaults to `6000.3.2f1` (Unity 6.3 LTS).
2. **Module selection** — displays a numbered list of available modules. Enter comma-separated numbers, or press Enter to accept the defaults.

**Defaults** (press Enter at each prompt to accept):
- **Version:** `6000.3.2f1`
- **Modules:** Linux Build Support (IL2CPP), Linux Dedicated Server Build Support, Web Build Support, Windows Build Support (Mono), Windows Dedicated Server Build Support

**Available modules:**

| # | Module | Default |
|---|---|---|
| 1 | Linux Build Support (IL2CPP) | ✔ |
| 2 | Linux Build Support (Mono) | |
| 3 | Linux Dedicated Server Build Support | ✔ |
| 4 | Mac Build Support (Mono) | |
| 5 | Mac Build Support (IL2CPP) | |
| 6 | Mac Dedicated Server Build Support | |
| 7 | Windows Build Support (Mono) | ✔ |
| 8 | Windows Build Support (IL2CPP) | |
| 9 | Windows Dedicated Server Build Support | ✔ |
| 10 | Web Build Support | ✔ |
| 11 | Android Build Support | |
| 12 | iOS Build Support | |

The installer runs the Unity Hub CLI with `-- --headless install --version <version> --module <module1> --module <module2> ...` and works identically on all platforms.

### 5 — Install NGINX

Installs NGINX as a web server / reverse proxy.

| Platform | How it works |
|---|---|
| **Windows** | Downloads `nginx-1.24.0.zip` and extracts it to `C:\nginx`. You must start NGINX manually from that directory. |
| **Ubuntu / Debian** | Runs `sudo apt-get install -y nginx`, then starts and enables the service via `systemctl`. |
| **CachyOS / Arch** | Runs `sudo pacman -S --noconfirm nginx`, then starts and enables the service via `systemctl`. |

The installer checks if NGINX is already installed by running `nginx -v` before proceeding.

### 6 — Install PostgreSQL

Installs the PostgreSQL database server. Requires `appsettings.json` (for the port configuration).

| Platform | How it works |
|---|---|
| **Windows** | Downloads the official EDB installer and performs an unattended install. Prompts you to set the superuser password. Disables pgAdmin and Stack Builder components. |
| **Ubuntu / Debian** | Runs `sudo apt-get install -y postgresql postgresql-contrib`, starts and enables the service, then optionally updates the superuser password. |
| **CachyOS / Arch** | Runs `sudo pacman -S --noconfirm postgresql`, initializes the data directory with `sudo -u postgres initdb -D /var/lib/postgres/data`, starts and enables the service, then optionally updates the superuser password. |

### 7 — Install FishMMO Database

Creates the FishMMO database, application user role, and runs the initial EF Core migration. Requires `appsettings.json` and prompts for the PostgreSQL superuser password.

**Steps performed:**
1. Connects to PostgreSQL as superuser.
2. Creates the database specified in `appsettings.json` (if it doesn't already exist).
3. Creates the application user role with the configured username/password (if it doesn't already exist).
4. Grants all privileges on the database to the application user and transfers ownership.
5. Optionally creates and applies the `Initial` EF Core migration using `dotnet ef`.

### 8 — Create New Database Migration

Creates a new EF Core migration and applies it to the database. Prompts for a migration name (e.g., `AddPlayerInventory`).

**Steps performed:**
1. Runs `dotnet ef migrations add <MigrationName>` against the FishMMO-DB project.
2. Runs `dotnet ef database update` to apply the new migration.

No superuser credentials are needed — this uses the connection configured in the FishMMO-DB migrator project.

### 9 — Grant User Permissions on Database

Grants comprehensive permissions to a specified user on a database. Prompts for the PostgreSQL superuser password, target database name, and target username (both default to values from `appsettings.json`).

**Permissions granted:**
- `GRANT ALL PRIVILEGES ON DATABASE` to the user.
- `GRANT ALL PRIVILEGES ON ALL TABLES, SEQUENCES, FUNCTIONS` in the `public` schema.
- `ALTER DEFAULT PRIVILEGES` so all future tables, sequences, and functions are also accessible to the user.

### A — Delete FishMMO Database (DANGEROUS!)

**Permanently deletes** the FishMMO database. This is irreversible.

Prompts for the PostgreSQL superuser password, then requires you to type `DELETE` (all caps) to confirm. Before dropping the database, it terminates all active connections to prevent lock conflicts.

### 0 — Quit

Exits the installer.

---

## Project Structure

| File | Responsibility |
|---|---|
| `Program.cs` | Entry point, interactive menu loop, settings validation helpers |
| `InstallationConstants.cs` | URLs, version strings, filenames, and EF Core project paths |
| `InstallerProcessHelper.cs` | Process execution, console prompts, file downloads, package manager detection, logging |
| `DotNetInstaller.cs` | .NET SDK and dotnet-ef installation, EF migration/update commands |
| `UnityInstaller.cs` | Unity Hub and Unity Editor installation with module selection |
| `NGINXInstaller.cs` | NGINX installation and service management |
| `PostgreSQLInstaller.cs` | PostgreSQL installation, database/user/migration management, permission grants |
| `VSBuildToolsInstaller.cs` | Visual Studio Build Tools installation (Windows only) |

## License

See the main FishMMO repository for license information.