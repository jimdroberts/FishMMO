# FishMMO AppHealthMonitor

## Overview

**FishMMO AppHealthMonitor** is a daemon-style application designed to monitor, manage, and maintain the health of multiple configured applications. It provides robust orchestration, automatic restarts, resource usage monitoring, and a command-driven console interface for real-time control. The tool is ideal for keeping critical services running, automatically handling failures, and providing operational insight through structured logging.

## Features

- Monitors multiple applications as defined in a configuration file
- Supports start, stop, force-kill, force-restart, status, and shutdown commands via an interactive console
- Tracks CPU and memory usage, with configurable thresholds
- Headless mode for production daemon deployments
- Graceful and forced shutdown of monitored applications
- Circuit breaker and exponential backoff restart logic for fault tolerance
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
      "Headless": false,
      "CheckIntervalSeconds": 10,
      "LaunchDelaySeconds": 2,
      "CpuThresholdPercent": 80,
      "MemoryThresholdMB": 500,
      "GracefulShutdownTimeoutSeconds": 15,
      "ForceKillTimeoutSeconds": 5,
      "InitialRestartDelaySeconds": 5,
      "MaxRestartDelaySeconds": 60,
      "MaxRestartAttempts": 3,
      "InitialHealthCheckDelaySeconds": 30,
      "PostLaunchSettleDelaySeconds": 5,
      "PortCheckTimeoutMs": 2000,
      "WebSocketCheckTimeoutMs": 5000,
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
- **MonitoredPort**: (Optional) Port number to check for application health. Set to `0` or omit for process-only monitoring.
- **PortTypes**: (Optional) List of port types to monitor (`TCP`, `UDP`, `WebSocket`). Omit or use an empty array `[]` for process-only monitoring.
- **LaunchArguments**: (Optional) Command-line arguments for the application.
- **Headless**: (Optional) When `true`, launches the process with no visible window and shell execution disabled. Recommended for production daemon deployments (default: `false`).
- **CheckIntervalSeconds**: (Optional) How often to check application health in seconds (default: `10`).
- **LaunchDelaySeconds**: (Optional) Delay in seconds before launching the next application (default: `0`).
- **CpuThresholdPercent**: (Optional) CPU usage threshold percentage for restart, must be between `0` and `100`. Set to `0` for no limit (default: `0`).
- **MemoryThresholdMB**: (Optional) Memory usage threshold in megabytes for restart. Set to `0` for no limit (default: `0`).
- **GracefulShutdownTimeoutSeconds**: (Optional) Time in seconds to wait for graceful shutdown before force-kill (default: `10`).
- **ForceKillTimeoutSeconds**: (Optional) Time in seconds to wait for a force-killed process to exit (default: `5`).
- **InitialRestartDelaySeconds**: (Optional) Initial delay in seconds before first restart attempt (default: `5`).
- **MaxRestartDelaySeconds**: (Optional) Maximum delay in seconds between restart attempts using exponential backoff (default: `60`).
- **MaxRestartAttempts**: (Optional) Maximum restart attempts before the monitor gives up (default: `5`).
- **InitialHealthCheckDelaySeconds**: (Optional) Delay in seconds before the first full health check after launch, allowing the application time to initialize (default: `30`).
- **PostLaunchSettleDelaySeconds**: (Optional) Delay in seconds to wait after launching or restarting the application before resuming health checks (default: `5`).
- **PortCheckTimeoutMs**: (Optional) Timeout in milliseconds for TCP and UDP port health checks (default: `2000`).
- **WebSocketCheckTimeoutMs**: (Optional) Timeout in milliseconds for WebSocket port health checks (default: `5000`).
- **CircuitBreakerFailureThreshold**: (Optional) Consecutive port check failures required to trip the circuit breaker (default: `3`).
- **CircuitBreakerResetTimeoutMinutes**: (Optional) Time in minutes before the circuit breaker attempts to reset (default: `5`).

### Logging

Logging is configured via `logging.json` (see `LoggingConfigName` in `Program.cs`). Adjust this file to control log output, format, and destinations.

## Console Commands

When running, the daemon accepts the following commands:

- `help` — List all available commands
- `start` — Start monitoring all configured applications
- `stop` — Gracefully stop all monitored applications
- `force-kill` — Immediately terminate all monitored applications
- `force-restart` — Immediately terminate and restart all applications
- `status` — Display the current status of all monitored applications (PID, state, restart count)
- `shutdown` or `exit` — Gracefully stop the daemon and all applications

## Known Limitations

- **Console input on Linux**: The `shutdown` and `exit` commands signal the daemon to stop, but the console input reader (`Console.In.ReadLineAsync`) blocks until a line is submitted. After issuing `shutdown` or `exit`, you may need to press **Enter** one additional time (or use **Ctrl+C**) for the daemon process to fully exit. This is a .NET runtime limitation on Linux where standard input reads are not cancellable.