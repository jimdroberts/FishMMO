# FishMMO AppHealthMonitor

## Overview

**FishMMO AppHealthMonitor** is a daemon-style application that monitors, manages, and maintains the health of multiple configured applications. It provides automatic restarts with circuit breaker protection, resource usage monitoring, and a command-driven console interface for real-time control.

## Features

- **Multi-application monitoring** with per-app configuration, health checks, and resource tracking
- **Interactive console** with start, stop, force-kill, force-restart, status, and shutdown commands
- **Headless mode** for production daemon deployments (suppresses interactive prompt)
- **Circuit breaker** with half-open probe recovery, exponential backoff with jitter, and configurable failure thresholds
- **CPU and memory monitoring** with configurable thresholds and transient failure tolerance
- **Port health checks** via TCP, UDP, and WebSocket with configurable timeouts and host address
- **Graceful and forced shutdown** with platform-specific signals (SIGTERM on Linux/macOS, CloseMainWindow on Windows)
- **Kill-before-launch lifecycle** preventing port-bind conflicts, with atomic process reference management
- **Drift-free health check cadence** via `PeriodicTimer`
- **Comprehensive startup validation** — executable paths, port/PortType consistency, duplicate names, upper-bound limits, and host format
- **Thread-safe status queries** with lock-free snapshots and `Volatile`/`Interlocked` synchronization
- **Race-free disposal** — cycle completion is captured before cancellation with bounded timeout awaiting
- **Cancellable console input** via .NET 8's native `ReadLineAsync(CancellationToken)` with EOF detection
- **Structured logging** with configurable output via FishMMO-Logger
- **Cross-platform** — Windows, Linux, macOS

## Architecture

| File | Responsibility |
|------|----------------|
| `Program.cs` | Entry point — config loading, logging init, Ctrl+C lifecycle, exit codes on all error paths |
| `AppConfig.cs` | Configuration POCO with defaults, validation, path resolution, and deduplication |
| `DaemonOrchestrator.cs` | Daemon lifecycle, monitor creation/cleanup, start/stop signaling, race-free disposal |
| `HealthMonitor.cs` | Per-app monitoring loop — process lifecycle, health checks, circuit breaker, restarts |
| `CommandHandler.cs` | Console command registration, dispatch, headless mode, EOF-safe stdin |
| `HealthCheckerFactory.cs` | Creates `IHealthChecker` instances from `PortType` enums |
| `TcpHealthChecker.cs` | TCP connect probe with timeout |
| `UdpHealthChecker.cs` | UDP send probe (fire-and-forget) |
| `WebSocketHealthChecker.cs` | WebSocket connect-only probe |
| `IHealthChecker.cs` | Health checker interface contract |
| `ConsoleCommand.cs` | Immutable command record |
| `HealthMonitorStatus.cs` | Immutable status snapshot record |
| `PortType.cs` | Enum: TCP, UDP, WebSocket |

## Getting Started

### Prerequisites
- .NET 8.0 SDK or newer
- Windows, Linux, or macOS

### Building

```bash
cd FishMMO-AppHealthMonitor
dotnet build
```

### Running

```bash
dotnet run --project AppHealthMonitor/AppHealthMonitor.csproj
```

Or run the compiled binary directly:
```bash
./AppHealthMonitor/bin/Debug/net8.0/AppHealthMonitor
```

### Installing .NET 8 SDK

**Windows:** Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0)

**Arch / CachyOS:**
```bash
sudo pacman -S dotnet-sdk
```

**Ubuntu:**
```bash
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0
```

### Running as a Systemd Service (Linux)

Create `/etc/systemd/system/apphealthmonitor.service`:
```ini
[Unit]
Description=FishMMO Application Health Monitor
After=network.target

[Service]
Type=simple
User=your-username
WorkingDirectory=/home/username/FishMMO-AppHealthMonitor
ExecStart=/home/username/FishMMO-AppHealthMonitor/AppHealthMonitor/bin/Release/net8.0/AppHealthMonitor
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

> For framework-dependent deployments, use:
> `ExecStart=/usr/bin/dotnet /path/to/AppHealthMonitor.dll`

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now apphealthmonitor
sudo systemctl status apphealthmonitor
```

---

## Configuration

### appsettings.json

Each entry in the `Applications` array defines an application to monitor. Names must be unique.

```json
{
  "Headless": false,
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
      "ForceKillTimeoutSeconds": 5,
      "HealthCheckHost": "127.0.0.1",
      "ResourceCheckFailureThreshold": 2,
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

> Path separators are handled cross-platform. Paths are resolved once via `Path.GetFullPath` at startup.

#### Global Options

| Option | Description | Default |
|--------|-------------|---------|
| **Headless** | Launches apps with no window; suppresses console prompt | `false` |

#### Application Options

| Option | Description | Default | Range |
|--------|-------------|---------|-------|
| **Name** | Unique display name *(required)* | — | — |
| **ApplicationExePath** | Path to executable *(required, validated at startup)* | — | — |
| **MonitoredPort** | Port to health-check (`0` = process-only) | `0` | 0–65535 |
| **PortTypes** | Port types to check: `TCP`, `UDP`, `WebSocket` (auto-deduplicated) | `[]` | — |
| **LaunchArguments** | Command-line arguments | `""` | — |
| **CheckIntervalSeconds** | Health check interval | `5` | 5–3600 |
| **LaunchDelaySeconds** | Delay before launching the next monitor | `0` | 0–3600 |
| **CpuThresholdPercent** | CPU threshold for restart (`0` = disabled) | `0` | 0–100 |
| **MemoryThresholdMB** | Memory threshold in MB (`0` = disabled) | `0` | ≥ 0 |
| **GracefulShutdownTimeoutSeconds** | Graceful shutdown wait | `10` | 1–120 |
| **ForceKillTimeoutSeconds** | Force-kill wait | `5` | 1–60 |
| **HealthCheckHost** | Target address for port checks | `127.0.0.1` | Valid host/IP |
| **ResourceCheckFailureThreshold** | Consecutive resource failures before restart | `2` | 1–100 |
| **InitialRestartDelaySeconds** | Initial backoff delay | `5` | 1–600 |
| **MaxRestartDelaySeconds** | Maximum backoff delay (with ±20% jitter) | `60` | 1–3600 |
| **MaxRestartAttempts** | Max consecutive restarts before giving up | `5` | 1–100 |
| **InitialHealthCheckDelaySeconds** | Delay before first health check after launch | `30` | 1–600 |
| **PostLaunchSettleDelaySeconds** | Settle delay after launch/restart | `5` | 1–300 |
| **PortCheckTimeoutMs** | TCP/UDP check timeout | `2000` | 1–30000 |
| **WebSocketCheckTimeoutMs** | WebSocket check timeout | `5000` | 1–60000 |
| **CircuitBreakerFailureThreshold** | Port failures to trip circuit breaker (must be ≤ MaxRestartAttempts) | `3` | 1–100 |
| **CircuitBreakerResetTimeoutMinutes** | Time before half-open probe | `5` | 1–1440 |

> Values below the minimum are clamped automatically. Setting `MonitoredPort` without `PortTypes` (or vice versa) is rejected at startup.

### Logging

Configured via `logging.json` (FishMMO-Logger). If absent, library defaults apply.

## Console Commands

| Command | Description |
|---------|-------------|
| `help` | List all commands |
| `start` | Start monitoring all applications |
| `stop` | Gracefully stop all monitored applications |
| `force-kill` | Immediately terminate all applications |
| `force-restart` | Immediately terminate and restart all applications |
| `status` | Show PID, state (`STARTING`/`HEALTHY`/`DOWN`/`CIRCUIT OPEN`/`EXHAUSTED`), restart count |
| `shutdown` / `exit` | Gracefully stop the daemon and all applications |

## Known Limitations

- **UDP health checks** can only confirm the OS accepted a datagram, not that the application received it. Use TCP or WebSocket for reliable checks. A warning is logged when UDP is the only configured port type.
- **WebSocket TLS** is not supported — the checker connects via `ws://` only.
- **Health check host** defaults to `127.0.0.1`. IPv6-only applications should set `HealthCheckHost` to `"::1"`.
