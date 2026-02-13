# FishMMO AppHealthMonitor

## Overview

**FishMMO AppHealthMonitor** is a daemon-style application that monitors, manages, and maintains the health of multiple configured applications. It provides automatic restarts with circuit breaker protection, resource usage monitoring, and a command-driven console interface for real-time control.

## Features

- **Multi-application monitoring** with per-app configuration, health checks, and resource tracking
- **Interactive console** with start, stop, force-kill, force-restart, status, and shutdown commands
- **Headless mode** for production daemon deployments (auto-starts monitoring, auto-shuts down after cycle completion, suppresses interactive prompt)
- **Circuit breaker** with configurable failure thresholds, exponential backoff with jitter, and automatic restart
- **CPU and memory monitoring** with configurable thresholds and transient failure tolerance
- **Port health checks** via TCP, UDP, and WebSocket with configurable timeouts and host address
- **Graceful and forced shutdown** with platform-specific signals (SIGTERM on Linux/macOS, CloseMainWindow on Windows)
- **Signal handling** — Ctrl+C always suppresses default termination; SIGTERM interception is enabled on supported Unix platforms to ensure `DisposeAsync` runs and child processes are cleaned up
- **Kill-before-launch lifecycle** preventing port-bind conflicts, with atomic process reference management
- **Drift-free health check cadence** via `PeriodicTimer`
- **Comprehensive startup validation** — executable paths, strict port/PortType consistency, duplicate names, upper-bound limits, host format, and config deserialization errors
- **Fail-fast startup policy** — any invalid application configuration aborts daemon startup
- **Thread-safe counters** using `Interlocked.Increment` for failure tracking with `Volatile`/`Interlocked` synchronization throughout
- **Race-free disposal** — cycle completion is captured before cancellation with bounded timeout awaiting
- **Cancellable console input** via .NET 8's native `ReadLineAsync(CancellationToken)` with EOF-triggered shutdown in interactive mode
- **IPv6-safe WebSocket probing** with double-bracket prevention for pre-bracketed addresses
- **Structured logging** with configurable output via FishMMO-Logger
- **Cross-platform** — Windows, Linux, macOS

## Architecture

| File | Responsibility |
|------|----------------|
| `Program.cs` | Entry point — config loading, logging init, Ctrl+C and SIGTERM lifecycle, exit codes on all error paths |
| `AppConfig.cs` | Configuration POCO with defaults, validation, path resolution, and deduplication |
| `DaemonOrchestrator.cs` | Daemon lifecycle, monitor creation/cleanup, start/stop signaling, race-free disposal |
| `HealthMonitor.cs` | Per-app monitoring loop — process lifecycle, health checks, circuit breaker, restarts |
| `CommandHandler.cs` | Console command registration, dispatch, headless mode, EOF-safe stdin behavior, bounded force-kill cleanup waiting |
| `HealthCheckerFactory.cs` | Creates `IHealthChecker` instances from `PortType` enums with null-guard and UDP-only warning |
| `TcpHealthChecker.cs` | TCP connect probe with timeout and input validation |
| `UdpHealthChecker.cs` | UDP send probe (fire-and-forget) with input validation |
| `WebSocketHealthChecker.cs` | WebSocket connect-only probe with IPv6 safety |
| `IHealthChecker.cs` | Health checker interface contract |
| `ConsoleCommand.cs` | Immutable command record with constructor validation |
| `HealthMonitorStatus.cs` | Immutable status snapshot record with computed `StateLabel` property |
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
Restart=on-failure
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

> Replace all sample `ApplicationExePath` values with real executable paths before running. Placeholder paths will fail startup validation.

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
      "CircuitBreakerFailureThreshold": 5
    }
  ]
}
```

> Path separators are handled cross-platform. Paths are resolved once via `Path.GetFullPath` at startup.

#### Global Options

| Option | Description | Default |
|--------|-------------|---------|
| **Headless** | Launches apps with no window; auto-starts monitoring; auto-shuts down after cycle completion; suppresses console prompt | `false` |

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
| **MemoryThresholdMB** | Memory threshold in MB (`0` = disabled) | `0` | 0–1048576 |
| **GracefulShutdownTimeoutSeconds** | Graceful shutdown wait | `1` | 1–120 |
| **ForceKillTimeoutSeconds** | Force-kill wait | `1` | 1–60 |
| **HealthCheckHost** | Target address for port checks | `127.0.0.1` | Valid host/IP |
| **ResourceCheckFailureThreshold** | Consecutive resource failures before restart | `1` | 1–100 |
| **InitialRestartDelaySeconds** | Initial backoff delay | `1` | 1–600 |
| **MaxRestartDelaySeconds** | Maximum backoff delay (with ±20% jitter) | `1` | 1–3600 |
| **MaxRestartAttempts** | Max consecutive restarts before giving up | `1` | 1–100 |
| **InitialHealthCheckDelaySeconds** | Delay before first health check after launch | `1` | 1–600 |
| **PostLaunchSettleDelaySeconds** | Settle delay after launch/restart | `1` | 1–300 |
| **PortCheckTimeoutMs** | TCP/UDP check timeout | `1` | 1–30000 |
| **WebSocketCheckTimeoutMs** | WebSocket check timeout | `1` | 1–60000 |
| **CircuitBreakerFailureThreshold** | Consecutive port failures to trigger restart | `1` | 1–100 |

> Values below the minimum are clamped automatically. Any invalid application entry causes startup to fail fast. `MonitoredPort` must always be in the range `0..65535`; when `PortTypes` is configured, `MonitoredPort` must be `1..65535`.

### Logging

Configured via `logging.json` (FishMMO-Logger). The daemon starts in fail-fast mode: if `logging.json` is missing or invalid, startup aborts with a non-zero exit code.

## Console Commands

| Command | Description |
|---------|-------------|
| `help` | List all commands |
| `start` | Start monitoring all applications |
| `stop` | Gracefully stop all monitored applications |
| `force-kill` | Immediately terminate all applications |
| `force-restart` | Immediately terminate and restart all applications |
| `status` | Show monitoring state (`ACTIVE`/`WAITING`), PID, state (`STARTING`/`HEALTHY`/`DOWN`/`EXHAUSTED`), restart count, port/resource failure counts |
| `shutdown` / `exit` | Gracefully stop the daemon and all applications |

## Known Limitations

- **UDP health checks** can only confirm the OS accepted a datagram, not that the application received it. Use TCP or WebSocket for reliable checks. A warning is logged when UDP is the only configured port type.
- **WebSocket TLS** is not supported — the checker connects via `ws://` only.
- **Health check host** defaults to `127.0.0.1`. IPv6-only applications should set `HealthCheckHost` to `"::1"`. Pre-bracketed IPv6 addresses (e.g., `[::1]`) are handled correctly.