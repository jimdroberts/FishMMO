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
- Windows OS (tested)

### Building
1. Clone the repository or copy the source files.
2. Open a terminal in the project directory.
3. Run:
   ```powershell
   dotnet build
   ```

### Running
1. Ensure your `appsettings.json` is configured (see below).
2. Run the application:
   ```powershell
   dotnet run --project AppHealthMonitor/AppHealthMonitor.csproj
   ```

## Configuration

### appsettings.json

The main configuration file is `appsettings.json`. It should contain an `Applications` array, where each entry defines an application to monitor. Example:

```json
{
  "Applications": [
    {
      "Name": "MyApp",
      "ApplicationExePath": "C:/Path/To/MyApp.exe",
      "MonitoredPort": 12345,
      "PortTypes": ["Tcp", "Udp"],
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

#### Application Configuration Options
- **Name**: Display name for the application.
- **ApplicationExePath**: Full path to the executable to monitor.
- **MonitoredPort**: (Optional) Port number to check for application health.
- **PortTypes**: (Optional) List of port types to monitor (`Tcp`, `Udp`, or `None`).
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
