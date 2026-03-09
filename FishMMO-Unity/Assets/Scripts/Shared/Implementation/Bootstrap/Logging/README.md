# Bootstrap Logging

**Short description:** Unity-specific logging integration that bridges Unity's `Debug.Log` pipeline with the central `FishMMO.Logging` framework, providing rich-text formatted console output, configurable log levels and color theming, and re-entrant loop protection to ensure all log output flows through a single consistent pipeline.

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Installation / Build](#installation--build)
- [Quick Start Guides](#quick-start-guides)
- [Configuration](#configuration)
- [Usage Examples](#usage-examples)
- [Operational Checks](#operational-checks)
- [Flow Diagram](#flow-diagram)
- [Project Structure](#project-structure)
- [License](#license)

## Overview

The Bootstrap Logging system provides Unity-specific integration with the `FishMMO.Logging` framework. It bridges Unity's built-in `Debug.Log` pipeline with the central `Log` manager, provides rich-text formatted console output, and supports configurable log levels and color theming. The system ensures all log output — whether originating from Unity or FishMMO — flows through a single, consistent pipeline with re-entrant loop protection.

The logging system is initialized early in the bootstrap sequence by `MainBootstrapSystem`:

1. `UnityLoggerBridge.Initialize(callback)` is called to intercept Unity's log pipeline.
2. Logger instances (including `UnityConsoleLogger`) are registered with the central `Log` manager.
3. On shutdown, `UnityLoggerBridge.Shutdown()` restores Unity's default handler.

### Re-Entrant Loop Prevention

The system must prevent an infinite loop: `UnityConsoleLogger` writes to `Debug.Log`, which triggers `UnityLoggerBridge`, which routes back to `Log.Write`, which triggers `UnityConsoleLogger` again.

This is solved by the `UnityLoggerBridge.IsLoggingInternally` flag:

1. Before calling `Debug.Log`, the logger/formatter sets `IsLoggingInternally = true`.
2. `UnityLoggerBridge.LogFormat` checks this flag — if `true`, it passes the message directly to Unity's default handler without re-routing.
3. After the `Debug.Log` call completes, the flag is reset to `false` in a `finally` block.

### Column Layout

Both `UnityConsoleLogger` and `UnityConsoleFormatter` use shared column widths from `ConsoleFormatterHelpers`:

```
[Timestamp]              [LEVEL]    [Source]              Message text...
|<-- TimestampColumnWidth -->|<-- LogLevelColumnWidth -->|<-- SourceColumnWidth -->|
```

- Timestamps are optional (controlled by `_includeTimestamps` in the formatter, omitted entirely in the logger).
- Exception details and additional data are indented to align under the message column.

## Supported Platforms

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows  | Yes       | Full Unity Editor and standalone support |
| Linux    | Yes       | Full Unity Editor and standalone support |
| WebGL    | Yes       | Rich-text tags stripped by browser console; log routing still functions |

| Requirement    | Version / Detail |
|----------------|------------------|
| Unity          | 6.3 LTS          |
| Scripting Backend | IL2CPP        |

## Features

- **Unified log pipeline** — All `Debug.Log`, `Debug.LogWarning`, `Debug.LogError`, and `Debug.LogException` calls are intercepted by `UnityLoggerBridge` and routed through the central `FishMMO.Logging.Log` manager.
- **Rich-text console output** — `UnityConsoleFormatter` produces columnar, color-coded output using Unity rich-text tags (`<color=...>`).
- **Configurable log levels** — `UnityConsoleLoggerConfig.AllowedLevels` controls which `LogLevel` values are processed (Critical, Error, Warning, Info, Debug, Verbose).
- **Configurable color theming** — Per-level color mapping via `UnityConsoleLoggerConfig.LogLevelColors` (named colors or hex codes).
- **Re-entrant loop protection** — `IsLoggingInternally` static flag prevents infinite recursion between `UnityConsoleLogger → Debug.Log → UnityLoggerBridge → Log.Write → UnityConsoleLogger`.
- **Structured log entries** — `LogEntry` objects carry Level, Source, Message, Timestamp, ExceptionDetails, and additional Data dictionary.
- **Exception detail rendering** — Exception details are indented and colored red beneath the main log line.
- **Additional data rendering** — Key/value data pairs are indented and colored cyan beneath the main log line.
- **Two formatter output modes** — `WriteStructuredLog(LogEntry)` for full columnar logs and `WriteColoredParts(level, source, columnWidth, parts)` for multi-segment colored messages.
- **Optional timestamps** — Timestamps can be included or excluded via the `includeTimestamps` constructor parameter on `UnityConsoleFormatter`.
- **Singleton bridge with clean lifecycle** — `Initialize(callback)` installs the bridge; `Shutdown()` restores Unity's original handler.
- **LogType-to-LogLevel conversion** — `UnityLoggerBridge` maps Unity `LogType.Error` → `LogLevel.Error`, `LogType.Assert` → `LogLevel.Critical`, `LogType.Warning` → `LogLevel.Warning`, `LogType.Log` → `LogLevel.Info`, `LogType.Exception` → `LogLevel.Critical`.
- **Internal log callback** — `UnityConsoleLogger` accepts an optional `Action<string>` callback for messages about the logger itself (defaults to `Console.WriteLine`).

## Prerequisites

- Unity 6.3 LTS with IL2CPP scripting backend.
- The `FishMMO.Logging` framework must be present in the project (provides `Log`, `ILogger`, `IConsoleFormatter`, `ILoggerConfig`, `LogEntry`, `LogLevel`, and `ConsoleFormatterHelpers`).
- `UnityEngine` assemblies (provides `Debug`, `ILogHandler`, `LogType`).

## Installation / Build

This is an integrated module within the FishMMO Unity project. No separate installation or build steps are required. The source files reside under `Assets/Scripts/Shared/Implementation/Bootstrap/Logging/` and are compiled as part of the standard Unity build pipeline.

## Quick Start Guides

### Initializing the Logging Bridge

The logging system is typically initialized by the bootstrap sequence. To initialize manually:

```csharp
// 1. Initialize the Unity logger bridge (intercepts all Debug.Log calls)
UnityLoggerBridge.Initialize(msg => Debug.Log(msg));

// 2. Create the logger config
var config = new UnityConsoleLoggerConfig();

// 3. Create and register the Unity console logger
var logger = new UnityConsoleLogger(config);
// Register logger with the central Log manager per your project's registration API
```

### Shutting Down

```csharp
// Restore Unity's original log handler
UnityLoggerBridge.Shutdown();
```

### Using the Formatter Directly

```csharp
// Create a formatter with timestamps enabled
var colors = new Dictionary<LogLevel, string>
{
    { LogLevel.Critical, "red" },
    { LogLevel.Error, "red" },
    { LogLevel.Warning, "yellow" },
    { LogLevel.Info, "white" },
    { LogLevel.Debug, "lime" },
    { LogLevel.Verbose, "grey" }
};
var formatter = new UnityConsoleFormatter(colors, includeTimestamps: true);

// Write a structured log entry
formatter.WriteStructuredLog(new LogEntry
{
    Level = LogLevel.Info,
    Source = "MySystem",
    Message = "Initialization complete.",
    Timestamp = DateTime.UtcNow
});

// Write colored parts
formatter.WriteColoredParts(
    LogLevel.Info,
    "MySystem",
    columnWidth: 20,
    ("lime", "Status:"), ("white", "Online")
);
```

## Configuration

### UnityConsoleLoggerConfig Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Type` | `string` | `"UnityConsoleLoggerConfig"` | Config type identifier for serialization |
| `LoggerType` | `string` | `"UnityConsoleLogger"` | Logger type identifier for factory lookup |
| `Enabled` | `bool` | `true` | Whether the logger is active |
| `AllowedLevels` | `HashSet<LogLevel>` | All levels (Critical, Error, Warning, Info, Debug, Verbose) | Which log levels to process |
| `LogLevelColors` | `Dictionary<LogLevel, string>` | See table below | Unity rich-text color per log level |

### Default Color Mapping

| LogLevel | Color | Hex Equivalent |
|----------|-------|----------------|
| Critical | `red` | `#FF0000` |
| Error | `red` | `#FF0000` |
| Warning | `yellow` | `#FFFF00` |
| Info | `white` | `#FFFFFF` |
| Debug | `lime` | `#00FF00` |
| Verbose | `grey` | `#808080` |

### Customizing Allowed Levels at Runtime

```csharp
// Only allow warnings and above
logger.SetAllowedLevels(new HashSet<LogLevel>
{
    LogLevel.Critical,
    LogLevel.Error,
    LogLevel.Warning
});
```

### Enabling / Disabling the Logger at Runtime

```csharp
logger.SetEnabled(false); // Disables all output
logger.SetEnabled(true);  // Re-enables output
```

## Usage Examples

### Standard Log Flow (Automatic)

When the bridge is initialized, any call to `Debug.Log("Hello")` anywhere in Unity code is automatically intercepted:

```
Unity Debug.Log("Hello")
  → UnityLoggerBridge.LogFormat(LogType.Log, ..., "Hello")
    → IsLoggingInternally == false → converts to LogLevel.Info
      → Log.Write(LogLevel.Info, "UNITY", "Hello")
        → UnityConsoleLogger.Log(entry)
          → IsLoggingInternally = true → Debug.Log(rich-text formatted output)
            → UnityLoggerBridge.LogFormat → IsLoggingInternally == true → pass-through to default handler
```

### Structured Log Entry with Exception and Data

```csharp
var entry = new LogEntry
{
    Level = LogLevel.Error,
    Source = "NetworkManager",
    Message = "Connection failed.",
    Timestamp = DateTime.UtcNow,
    ExceptionDetails = "System.Net.Sockets.SocketException: Connection refused",
    Data = new Dictionary<string, object>
    {
        { "Host", "192.168.1.100" },
        { "Port", 7770 }
    }
};

// Output via logger (respects AllowedLevels and IsEnabled)
await logger.Log(entry);

// Or output via formatter directly
formatter.WriteStructuredLog(entry);
```

### Multi-Segment Colored Output

```csharp
formatter.WriteColoredParts(
    LogLevel.Info,
    "Inventory",
    columnWidth: 15,
    ("lime", "Gold:"), ("yellow", "1500"),
    ("lime", "Items:"), ("white", "42")
);
```

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| Bridge initialization | Call `UnityLoggerBridge.Initialize(callback)` at startup | Internal callback logs `"Bridge initialized and set as ILogHandler."` |
| Bridge already initialized | Call `Initialize` a second time | Internal callback logs `"Bridge already initialized. Skipping re-initialization."` |
| Unity log interception | Call `Debug.Log("test")` after initialization | Message routed through `Log.Write` with source `"UNITY"` and level `Info` |
| Unity exception interception | Call `Debug.LogException(ex, null)` after initialization | Message routed through `Log.Write` with source `"UNITY"`, level `Critical`, and exception details |
| Log level filtering | Set `AllowedLevels` to `{Error, Critical}` only | `Info`, `Debug`, `Warning`, `Verbose` messages are silently dropped |
| Logger disabled | Call `logger.SetEnabled(false)` | No messages appear in the Unity console from this logger |
| Re-entrant loop prevention | `UnityConsoleLogger` calls `Debug.Log` | `IsLoggingInternally` flag ensures no infinite loop; message appears once |
| Bridge shutdown | Call `UnityLoggerBridge.Shutdown()` | Unity's original `ILogHandler` is restored; `Debug.Log` calls no longer routed through FishMMO |
| Rich-text colors | Trigger logs at each level | Each level renders with its configured color in the Unity Editor console |
| Timestamp toggle | Create formatter with `includeTimestamps: true` vs `false` | Timestamps appear or are replaced with whitespace padding |
| LogType conversion | `Debug.LogError` / `Debug.LogWarning` / `Debug.Log` / `Debug.LogException` | Maps to `Error` / `Warning` / `Info` / `Critical` respectively |
| Logger disposal | Call `logger.Dispose()` | Internal callback logs `"Disposed."` |

## Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Application Code                             │
│                                                                     │
│   Unity Debug.Log("...")          FishMMO Log.Write(level, src, msg)│
│         │                                    │                      │
│         ▼                                    ▼                      │
│   ┌─────────────────┐              ┌────────────────┐               │
│   │ UnityLoggerBridge│              │  Log (static)  │               │
│   │  (ILogHandler)   │──────────►  │  FishMMO.Logging│               │
│   └─────────────────┘              └───────┬────────┘               │
│                                            │                        │
│                              ┌─────────────┼─────────────┐          │
│                              ▼             ▼             ▼          │
│                    ┌──────────────┐ ┌────────────┐ ┌──────────┐     │
│                    │UnityConsole  │ │ File       │ │ Other    │     │
│                    │Logger        │ │ Logger     │ │ Loggers  │     │
│                    └──────┬───────┘ └────────────┘ └──────────┘     │
│                           │                                         │
│                           ▼                                         │
│                    Unity Debug.Log (with IsLoggingInternally=true)   │
│                           │                                         │
│                           ▼                                         │
│                    UnityLoggerBridge passes through to default       │
│                    handler (no re-capture loop)                      │
└─────────────────────────────────────────────────────────────────────┘
```

### Class Interaction Detail

| Source | Action | Target | Condition |
|--------|--------|--------|-----------|
| Application Code | `Debug.Log(...)` | `UnityLoggerBridge.LogFormat` | Always (bridge is installed as `ILogHandler`) |
| `UnityLoggerBridge` | Pass-through to default handler | Unity Console | `IsLoggingInternally == true` |
| `UnityLoggerBridge` | Convert `LogType` → `LogLevel`, call `Log.Write()` | `FishMMO.Logging.Log` | `IsLoggingInternally == false` |
| `FishMMO.Logging.Log` | Dispatch `LogEntry` | `UnityConsoleLogger.Log(entry)` | Logger registered and enabled |
| `UnityConsoleLogger` | Set `IsLoggingInternally = true`, call `Debug.Log(rich-text)` | Unity Console (via bridge pass-through) | Level is in `AllowedLevels` |
| Application Code | `Debug.LogException(ex)` | `UnityLoggerBridge.LogException` | Always |
| `UnityLoggerBridge` | `Log.Write(Critical, "UNITY", ...)` | `FishMMO.Logging.Log` | `IsLoggingInternally == false` |
| `UnityConsoleFormatter` | `WriteStructuredLog(entry)` / `WriteColoredParts(...)` | Unity Console (via `Debug.Log` / `Debug.LogError` / `Debug.LogWarning`) | Called directly by application code |

## Project Structure

### Directory Tree

```
Assets/Scripts/Shared/Implementation/Bootstrap/Logging/
├── README.md                       # This documentation file
├── UnityLoggerBridge.cs            # ILogHandler that intercepts Unity Debug.Log and routes to FishMMO.Logging
├── UnityConsoleLogger.cs           # ILogger that writes structured LogEntry objects to the Unity console
├── UnityConsoleFormatter.cs        # IConsoleFormatter for rich-text columnar output with colored parts
└── UnityConsoleLoggerConfig.cs     # ILoggerConfig with log level filtering and color definitions
```

### Class Responsibilities and Interfaces

| Class | Implements | Namespace | Responsibility |
|-------|-----------|-----------|----------------|
| `UnityLoggerBridge` | `UnityEngine.ILogHandler` | `FishMMO.Shared` | Singleton bridge; intercepts all Unity `Debug.Log` calls and forwards to `FishMMO.Logging.Log`; prevents re-entrant loops via `IsLoggingInternally` flag |
| `UnityConsoleLogger` | `FishMMO.Logging.ILogger` | `FishMMO.Shared` | Receives `LogEntry` from the central `Log` manager; renders rich-text formatted output to Unity console; filters by `AllowedLevels` and `IsEnabled`; `HandlesConsoleParts` returns `false` |
| `UnityConsoleFormatter` | `FishMMO.Logging.IConsoleFormatter` | `FishMMO.Shared` | Two output modes: `WriteStructuredLog(LogEntry)` for full columnar format, `WriteColoredParts(...)` for multi-segment colored messages; routes errors/warnings through `Debug.LogError`/`Debug.LogWarning` |
| `UnityConsoleLoggerConfig` | `FishMMO.Logging.ILoggerConfig` | `FishMMO.Shared` | Serializable config holding `Enabled`, `AllowedLevels`, and `LogLevelColors`; default type identifiers set via `nameof` |

### External Dependencies

| Dependency | Purpose |
|------------|---------|
| `FishMMO.Logging.Log` | Central static log manager that routes entries to registered loggers |
| `FishMMO.Logging.ILogger` | Interface implemented by `UnityConsoleLogger` |
| `FishMMO.Logging.IConsoleFormatter` | Interface implemented by `UnityConsoleFormatter` |
| `FishMMO.Logging.ILoggerConfig` | Interface implemented by `UnityConsoleLoggerConfig` |
| `FishMMO.Logging.LogEntry` | Structured log entry (Level, Source, Message, Timestamp, ExceptionDetails, Data) |
| `FishMMO.Logging.LogLevel` | Enum: Critical, Error, Warning, Info, Debug, Verbose |
| `FishMMO.Logging.ConsoleFormatterHelpers` | Shared column widths, padding, and Unity rich-text escaping |
| `UnityEngine.Debug` | Unity's built-in logging API |
| `UnityEngine.ILogHandler` | Unity interface for custom log handlers |

## License

This module is part of the FishMMO project and is distributed under the FishMMO project license. See the repository root for full license terms.
