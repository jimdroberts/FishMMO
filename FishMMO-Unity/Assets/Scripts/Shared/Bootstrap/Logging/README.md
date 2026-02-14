# Logging System (Unity Integration)

## Overview

The Logging system provides Unity-specific integration with the `FishMMO.Logging` framework. It bridges Unity's built-in `Debug.Log` pipeline with the central `Log` manager, provides rich-text formatted console output, and supports configurable log levels and color theming. The system ensures all log output — whether originating from Unity or FishMMO — flows through a single, consistent pipeline with re-entrant loop protection.

## Directory Structure

```
Logging/
├── UnityLoggerBridge.cs          # ILogHandler that intercepts Unity Debug.Log and routes to FishMMO.Logging
├── UnityConsoleLogger.cs         # ILogger that writes structured LogEntry objects to the Unity console
├── UnityConsoleFormatter.cs      # IConsoleFormatter for rich-text columnar output with colored parts
└── UnityConsoleLoggerConfig.cs   # ILoggerConfig with log level filtering and color definitions
```

## Architecture

### Log Flow Diagram

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

### Re-Entrant Loop Prevention

The system must prevent an infinite loop: `UnityConsoleLogger` writes to `Debug.Log`, which triggers `UnityLoggerBridge`, which routes back to `Log.Write`, which triggers `UnityConsoleLogger` again.

This is solved by the `UnityLoggerBridge.IsLoggingInternally` flag:

1. Before calling `Debug.Log`, the logger/formatter sets `IsLoggingInternally = true`.
2. `UnityLoggerBridge.LogFormat` checks this flag — if `true`, it passes the message directly to Unity's default handler without re-routing.
3. After the `Debug.Log` call completes, the flag is reset to `false` in a `finally` block.

## Class Responsibilities

### UnityLoggerBridge

Implements Unity's `ILogHandler` interface and replaces Unity's default log handler at initialization. All `Debug.Log`, `Debug.LogWarning`, `Debug.LogError`, and `Debug.LogException` calls are intercepted.

| Scenario | Behavior |
|----------|----------|
| `IsLoggingInternally == true` | Pass-through to Unity's original default handler (no re-routing) |
| `IsLoggingInternally == false` | Convert `LogType` → `LogLevel`, forward to `Log.Write()` with source `"UNITY"` |

**Lifecycle:**
- `Initialize(callback)` — Stores Unity's default handler, installs itself as `Debug.unityLogger.logHandler`.
- `Shutdown()` — Restores Unity's original default handler.

### UnityConsoleLogger

Implements `FishMMO.Logging.ILogger` to receive structured `LogEntry` objects from the central `Log` manager and render them to the Unity console with rich-text formatting.

**Features:**
- Configurable enabled/disabled state.
- Log level filtering via `AllowedLevels` hash set.
- Columnar layout matching `ConsoleFormatter` (level, source, message).
- Exception details and additional data rendered with indentation.
- `HandlesConsoleParts` returns `false` — this logger does not handle `WriteColoredParts`.

### UnityConsoleFormatter

Implements `IConsoleFormatter` to provide two output modes:

| Method | Purpose |
|--------|---------|
| `WriteStructuredLog(LogEntry)` | Full columnar log with timestamp, level, source, message, exceptions, and data |
| `WriteColoredParts(level, source, columnWidth, parts)` | Multi-segment colored output for custom formatted messages |

Both methods use `ConsoleFormatterHelpers` for consistent column widths, padding, and rich-text escaping. Timestamps are configurable via the `includeTimestamps` constructor parameter.

### UnityConsoleLoggerConfig

Implements `ILoggerConfig` for serialization and logger factory integration.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Type` | `string` | `"UnityConsoleLoggerConfig"` | Config type identifier |
| `LoggerType` | `string` | `"UnityConsoleLogger"` | Logger type identifier |
| `Enabled` | `bool` | `true` | Whether the logger is active |
| `AllowedLevels` | `HashSet<LogLevel>` | All levels | Which levels to process |
| `LogLevelColors` | `Dictionary<LogLevel, string>` | See below | Unity rich-text color per level |

**Default Color Mapping:**

| LogLevel | Color |
|----------|-------|
| Critical | `red` |
| Error | `red` |
| Warning | `yellow` |
| Info | `white` |
| Debug | `lime` |
| Verbose | `grey` |

## Column Layout

Both `UnityConsoleLogger` and `UnityConsoleFormatter` use shared column widths from `ConsoleFormatterHelpers`:

```
[Timestamp]              [LEVEL]    [Source]              Message text...
|<-- TimestampColumnWidth -->|<-- LogLevelColumnWidth -->|<-- SourceColumnWidth -->|
```

- Timestamps are optional (controlled by `_includeTimestamps` in the formatter, omitted entirely in the logger).
- Exception details and additional data are indented to align under the message column.

## External Dependencies

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

## Integration with Bootstrap

The logging system is initialized early in the bootstrap sequence by `MainBootstrapSystem`:

1. `UnityLoggerBridge.Initialize(callback)` is called to intercept Unity's log pipeline.
2. Logger instances (including `UnityConsoleLogger`) are registered with the central `Log` manager.
3. On shutdown, `UnityLoggerBridge.Shutdown()` restores Unity's default handler.