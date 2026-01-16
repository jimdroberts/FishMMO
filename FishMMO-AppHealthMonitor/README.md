# FishMMO AppHealthMonitor

## Overview

**FishMMO AppHealthMonitor** is a daemon-style application designed to monitor, manage, and maintain the health of multiple configured applications. It provides robust orchestration, automatic restarts, resource usage monitoring, and a command-driven console interface for real-time control. The tool is ideal for keeping critical services running, automatically handling failures, and providing operational insight through structured logging.

## Features

- Monitors multiple applications as defined in a configuration file
- Supports start, stop, force-kill, force-restart, and shutdown commands via an interactive console
- Tracks CPU and memory usage, with configurable thresholds
- Graceful and forced shutdown of monitored applications
- Circuit breaker and restart logic for fault tolerance
- Structured logging with configurable output

## Getting Started

### Prerequisites
- .NET 8.0 SDK or newer
- Supported operating systems: Windows, Linux (CachyOS, Ubuntu, and other distributions), macOS

---

## Platform-Specific Setup

### Windows Setup

#### Prerequisites
1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer
2. Open PowerShell or Command Prompt

#### Building
1. Clone the repository or copy the source files
2. Navigate to the project directory:
   ```powershell
   cd "C:\Path\To\FishMMO-AppHealthMonitor"
   ```
3. Build the project:
   ```powershell
   dotnet build
   ```

#### Configuration
1. Edit `AppHealthMonitor\appsettings.json`
2. Set your application paths using Windows-style paths with escaped backslashes:
   ```json
   {
     "Applications": [
       {
         "Name": "MyGameServer",
         "ApplicationExePath": "C:\\Path\\To\\Your\\GameServer.exe",
         "MonitoredPort": 7770,
         "PortTypes": ["TCP", "UDP"],
         "LaunchArguments": "LOGIN"
       }
     ]
   }
   ```

#### Running
Run the application:
```powershell
dotnet run --project AppHealthMonitor\AppHealthMonitor.csproj
```

Or run the compiled executable:
```powershell
.\AppHealthMonitor\bin\Debug\net8.0\AppHealthMonitor.exe
```

---

### Linux Setup (CachyOS with Fish Terminal)

#### Prerequisites
1. Install .NET 8.0 SDK:
   ```fish
   # CachyOS (using pacman)
   sudo pacman -S dotnet-sdk
   ```

#### Building
1. Clone the repository or copy the source files
2. Navigate to the project directory:
   ```fish
   cd ~/Dev/FishMMO-AppHealthMonitor
   ```
3. Build the project:
   ```fish
   dotnet build
   ```

#### Configuration
1. Edit `AppHealthMonitor/appsettings.json`
2. Set your application paths using Unix-style paths:
   ```json
   {
     "Applications": [
       {
         "Name": "MyGameServer",
         "ApplicationExePath": "/home/username/gameserver/GameServer",
         "MonitoredPort": 7770,
         "PortTypes": ["TCP", "UDP"],
         "LaunchArguments": "LOGIN"
       }
     ]
   }
   ```
   
   **Note:** Ensure your executable has execute permissions:
   ```fish
   chmod +x /home/username/gameserver/GameServer
   ```

#### Running
Run the application:
```fish
dotnet run --project AppHealthMonitor/AppHealthMonitor.csproj
```

Or run the compiled executable:
```fish
./AppHealthMonitor/bin/Debug/net8.0/AppHealthMonitor
```

#### Running as a Systemd Service (Optional)
Create a systemd service file at `/etc/systemd/system/apphealthmonitor.service`:
```ini
[Unit]
Description=FishMMO Application Health Monitor
After=network.target

[Service]
Type=simple
User=your-username
WorkingDirectory=/home/username/FishMMO-AppHealthMonitor
ExecStart=/usr/bin/dotnet /home/username/FishMMO-AppHealthMonitor/AppHealthMonitor/bin/Release/net8.0/AppHealthMonitor.dll
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start the service:
```fish
sudo systemctl daemon-reload
sudo systemctl enable apphealthmonitor
sudo systemctl start apphealthmonitor
sudo systemctl status apphealthmonitor
```

---

### Linux Setup (Ubuntu)

#### Prerequisites
1. Install .NET 8.0 SDK:
   ```bash
   # Ubuntu 22.04 or newer
   wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
   sudo dpkg -i packages-microsoft-prod.deb
   rm packages-microsoft-prod.deb
   
   sudo apt-get update
   sudo apt-get install -y dotnet-sdk-8.0
   ```

#### Building
1. Clone the repository or copy the source files
2. Navigate to the project directory:
   ```bash
   cd ~/FishMMO-AppHealthMonitor
   ```
3. Build the project:
   ```bash
   dotnet build
   ```

#### Configuration
1. Edit `AppHealthMonitor/appsettings.json`
2. Set your application paths using Unix-style paths:
   ```json
   {
     "Applications": [
       {
         "Name": "MyGameServer",
         "ApplicationExePath": "/home/username/gameserver/GameServer",
         "MonitoredPort": 7770,
         "PortTypes": ["TCP", "UDP"],
         "LaunchArguments": "LOGIN"
       }
     ]
   }
   ```
   
   **Note:** Ensure your executable has execute permissions:
   ```bash
   chmod +x /home/username/gameserver/GameServer
   ```

#### Running
Run the application:
```bash
dotnet run --project AppHealthMonitor/AppHealthMonitor.csproj
```

Or run the compiled executable:
```bash
./AppHealthMonitor/bin/Debug/net8.0/AppHealthMonitor
```

#### Running as a Systemd Service (Optional)
Create a systemd service file at `/etc/systemd/system/apphealthmonitor.service`:
```ini
[Unit]
Description=FishMMO Application Health Monitor
After=network.target

[Service]
Type=simple
User=your-username
WorkingDirectory=/home/username/FishMMO-AppHealthMonitor
ExecStart=/usr/bin/dotnet /home/username/FishMMO-AppHealthMonitor/AppHealthMonitor/bin/Release/net8.0/AppHealthMonitor.dll
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start the service:
```bash
sudo systemctl daemon-reload
sudo systemctl enable apphealthmonitor
sudo systemctl start apphealthmonitor
sudo systemctl status apphealthmonitor
```

---

## Configuration

### appsettings.json

The main configuration file is `appsettings.json`. It should contain an `Applications` array, where each entry defines an application to monitor. Example:

```json
{
  "Applications": [
    {
      "Name": "MyApp",
      "ApplicationExePath": "/path/to/your/application",
      "MonitoredPort": 12345,
      "PortTypes": ["TCP", "UDP"],
      "LaunchArguments": "--option value",
      "CheckIntervalSeconds": 10,
      "LaunchDelaySeconds": 2,
      "CpuThresholdPercent": 80,
      "MemoryThresholdMB": 500,
      "GracefulShutdownTimeoutSeconds": 15,
      "InitialRestartDelaySeconds": 5,
      "MaxRestartDelaySeconds": 60,
      "MaxRestartAttempts": 3,
      "CircuitBreakerFailureThreshold": 5,
      "CircuitBreakerResetTimeoutMinutes": 10
    }
  ]
}
```

**Note:** Path separators are automatically handled cross-platform. Use forward slashes `/` for Unix-like systems (Linux, macOS) or backslashes `\\` for Windows. The .NET runtime normalizes paths appropriately.

#### Application Configuration Options
- **Name**: Display name for the application.
- **ApplicationExePath**: Full path to the executable to monitor (supports Windows, Linux, and macOS paths).
- **MonitoredPort**: (Optional) Port number to check for application health.
- **PortTypes**: (Optional) List of port types to monitor (`TCP`, `UDP`, `WebSocket`, or `None`).
- **LaunchArguments**: (Optional) Command-line arguments for the application.
- **CheckIntervalSeconds**: (Optional) How often to check application health (default: 10).
- **LaunchDelaySeconds**: (Optional) Delay before launching the next application (default: 0).
- **CpuThresholdPercent**: (Optional) CPU usage threshold for alerts/restarts.
- **MemoryThresholdMB**: (Optional) Memory usage threshold for alerts/restarts.
- **GracefulShutdownTimeoutSeconds**: (Optional) Time to wait for graceful shutdown before force-kill.
- **InitialRestartDelaySeconds**: (Optional) Delay before first restart attempt.
- **MaxRestartDelaySeconds**: (Optional) Maximum delay between restart attempts.
- **MaxRestartAttempts**: (Optional) Maximum restart attempts before circuit breaker trips.
- **CircuitBreakerFailureThreshold**: (Optional) Number of failures before circuit breaker trips.
- **CircuitBreakerResetTimeoutMinutes**: (Optional) Time before circuit breaker resets.

### Logging

Logging is configured via `logging.json` (see `loggingConfigName` in code). Adjust this file to control log output, format, and destinations.

## Console Commands

When running, the daemon accepts the following commands:

- `help` — List all available commands
- `start` — Start monitoring all configured applications
- `stop` — Gracefully stop all monitored applications
- `force-kill` — Immediately terminate all monitored applications
- `force-restart` — Immediately terminate and restart all applications
- `shutdown` or `exit` — Gracefully stop the daemon and all applications