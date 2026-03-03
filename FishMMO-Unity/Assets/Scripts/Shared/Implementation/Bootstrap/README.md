# Bootstrap System

## Overview

The Bootstrap system manages the initialization, asset loading, scene loading, logging setup, version management, and graceful shutdown for FishMMO. It provides a sequential, event-driven startup pipeline where each bootstrap stage can load Addressable assets and scenes, then chain to the next stage. The system supports platform-specific load lists (Editor, Standalone, WebGL), integrates a structured logging framework bridging Unity's console with FishMMO's central `Log` manager, and handles dynamic Addressable load path overrides for remote asset servers.

## Directory Structure

```
Bootstrap/
├── BootstrapSystem.cs                   # Base class for all bootstrap stages
├── MainBootstrapSystem.cs               # Entry point: logging, version, shutdown
├── DynamicAddressableLoadPathSystem.cs  # Runtime Addressable URL override
└── Logging/
    ├── UnityConsoleFormatter.cs         # IConsoleFormatter for Unity rich text
    ├── UnityConsoleLogger.cs            # ILogger outputting to Unity console
    ├── UnityConsoleLoggerConfig.cs      # Configuration for UnityConsoleLogger
    └── UnityLoggerBridge.cs             # ILogHandler bridging Unity → FishMMO Log
```

## Inheritance Hierarchies

### Bootstrap Systems (MonoBehaviour)

```
MonoBehaviour
└── BootstrapSystem
    └── MainBootstrapSystem
```

External subclasses (outside this directory):
```
BootstrapSystem
├── ServerLauncher           (Server)
└── ClientPostbootSystem     (Client)
```

### Logging

```
IConsoleFormatter
└── UnityConsoleFormatter

FishMMO.Logging.ILogger
└── UnityConsoleLogger

ILogHandler (Unity)
└── UnityLoggerBridge

ILoggerConfig
└── UnityConsoleLoggerConfig
```

## BootstrapSystem

The base class for all bootstrap stages. Each `BootstrapSystem` manages two sequential loading phases — **Preload** and **Postload** — using the `AddressableLoadProcessor` to queue and process Addressable assets and scenes.

### Platform-Specific Load Lists

Each phase has three sets of load lists selected at compile time:

| Platform       | Compile Symbol   | Asset List             | Scene List            |
|----------------|------------------|------------------------|-----------------------|
| **Editor**     | `UNITY_EDITOR`   | `EditorPreloadAssets`  | `EditorPreloadScenes` |
| **WebGL**      | `UNITY_WEBGL`    | `WebGLPreloadAssets`   | `WebGLPreloadScenes`  |
| **Standalone** | (default)        | `PreloadAssets`        | `PreloadScenes`       |

The same pattern applies for Postload (`EditorPostloadAssets`, `PostloadAssets`, etc.).

### Loading Pipeline

```
StartBootstrap()
    │
    ├── Guard: skip if already started
    │
    └── InitializePreload()
            │
            ├── Enqueue platform-specific preload assets
            ├── Enqueue platform-specific preload scenes (with OnBootstrapPostProcess callback)
            ├── OnPreload() — virtual hook for subclass initialization
            ├── Subscribe to AddressableLoadProcessor.OnProgressUpdate
            └── AddressableLoadProcessor.BeginProcessQueue()
                    │
                    └── AddressableLoadProcessor_OnPreloadProgressUpdate(progress)
                            │
                            ├── Wait until progress >= 1.0
                            ├── Unsubscribe from OnProgressUpdate
                            └── OnCompletePreload()
                                    │
                                    └── InitializePostload()
                                            │
                                            ├── Enqueue platform-specific postload assets
                                            ├── Enqueue platform-specific postload scenes
                                            ├── OnPostLoad() — virtual hook
                                            ├── Subscribe to OnProgressUpdate
                                            └── BeginProcessQueue()
                                                    │
                                                    └── AddressableLoadProcessor_OnPostloadProgressUpdate(progress)
                                                            │
                                                            ├── Wait until progress >= 1.0
                                                            ├── Unsubscribe from OnProgressUpdate
                                                            └── OnCompleteProcessing()
                                                                    │
                                                                    └── Start preloaded BootstrapSystems
                                                                        (chaining to next stage)
```

### Scene Post-Processing

When a scene finishes loading, `OnBootstrapPostProcess(Scene)` scans its root GameObjects for `BootstrapSystem` components and stores them. After the current stage completes (`OnCompleteProcessing`), it calls `StartBootstrap()` on each discovered system, forming the sequential chain.

### Virtual Hooks

| Method                 | When Called                                | Purpose                                           |
|------------------------|--------------------------------------------|----------------------------------------------------|
| `OnPreload()`          | After preload assets/scenes are enqueued   | Subclass initialization before preload begins      |
| `OnPostLoad()`         | After postload assets/scenes are enqueued  | Subclass initialization before postload begins     |
| `OnCompletePreload()`  | When preload progress reaches 1.0          | Triggers postload (can be overridden)              |
| `OnCompleteProcessing()` | When postload progress reaches 1.0       | Chains to next bootstrap stages (can be overridden)|
| `OnDestroying()`       | During OnDestroy cleanup                   | Custom cleanup logic                               |

## MainBootstrapSystem

The entry-point bootstrap system. Placed in the initial scene and automatically starts the bootstrap chain via `Awake() → StartBootstrap()`.

### Responsibilities

1. **Logging Initialization** — Configures `FishMMO.Logging.Log` with `UnityConsoleLogger`, `UnityConsoleFormatter`, and `UnityLoggerBridge`.
2. **Version Management** — Reads `VersionConfig` from asset and optionally validates against `version.txt` in standalone builds.
3. **Initial Scene Loading** — Enqueues `ServerLauncher` (server) or `ClientPreboot` (client) scene via `AddressableLoadProcessor`.
4. **Graceful Shutdown** — Handles `Application.wantsToQuit`, editor play mode changes, and async cleanup.

### Initialization Flow (OnPreload)

```
MainBootstrapSystem.OnPreload()
    │
    ├── Load VersionConfig (asset + optional version.txt validation)
    ├── Set GameVersion static string
    │
    ├── Register editor play mode state handler (editor only)
    ├── Register Application.wantsToQuit handler
    │
    ├── Configure FishMMO.Logging:
    │   ├── Register UnityConsoleLoggerConfig factory
    │   ├── Create UnityConsoleFormatter (editor only)
    │   ├── Create manual UnityConsoleLogger list (editor only)
    │   └── Log.Initialize(configFilePath, formatter, loggers, callback, configTypes)
    │
    └── Enqueue initial scene:
        ├── UNITY_SERVER → "ServerLauncher"
        └── !UNITY_SERVER → "ClientPreboot"
```

### Shutdown Flow

```
Application.wantsToQuit / Editor ExitingPlayMode / OnDestroy
    │
    └── InitiateShutdown()
            │
            ├── Guard: skip if already shutting down
            ├── GraphicsCleanup() — releases Addressable assets
            ├── UnityLoggerBridge.Shutdown() — restores Unity's default log handler
            │
            ├── Editor path:
            │   ├── Log.Shutdown().Wait() — synchronous
            │   └── canQuitApplication = true
            │
            └── Standalone path:
                └── PerformAsyncShutdown()
                        ├── Save logging config to disk
                        ├── await Log.Shutdown()
                        ├── canQuitApplication = true
                        └── Application.Quit()
```

## DynamicAddressableLoadPathSystem

A `MonoBehaviour` that overrides Addressable asset load paths at runtime by setting `Addressables.ResourceManager.InternalIdTransformFunc`. Used to redirect remote asset URLs based on runtime configuration (e.g., IP discovery).

### URL Transformation

| Original URL                           | RuntimeBaseUrl             | Transformed URL                        |
|-----------------------------------------|----------------------------|----------------------------------------|
| `http://old-host.com/path/to/asset`     | `http://new-host.com/`     | `http://new-host.com/path/to/asset`    |
| `http://127.0.0.1:8000/bundles/data`    | `http://cdn.example.com/`  | `http://cdn.example.com/bundles/data`  |
| `file:///local/asset`                   | (any)                      | `file:///local/asset` (unchanged)      |

The transformation:
1. Detects URLs starting with `http://` or `https://`.
2. Finds the third `/` (end of domain/port).
3. Extracts the relative path after the domain.
4. Prepends `RuntimeBaseUrl` to the relative path.
5. Local/non-HTTP paths are returned unchanged.

## Logging Subsystem

### Architecture

```
Unity Debug.Log / Debug.LogError / etc.
    │
    └── UnityLoggerBridge (ILogHandler)
            │
            ├── Internal log? → Pass to Unity's default handler
            └── External log? → Forward to FishMMO.Logging.Log
                                    │
                                    └── Dispatches to registered ILogger instances
                                            │
                                            └── UnityConsoleLogger
                                                    │
                                                    └── Debug.Log (with IsLoggingInternally = true)
                                                        → Unity's default handler (no re-capture)
```

### UnityLoggerBridge

Replaces Unity's default `ILogHandler` at startup. Intercepts all `Debug.Log` calls and routes them through `FishMMO.Logging.Log`:

- **Internal logs** (`IsLoggingInternally = true`): Passed directly to Unity's original handler to prevent infinite recursion.
- **External logs** (standard Unity `Debug.Log` calls): Converted to `FishMMO.Logging.LogEntry` and forwarded to all registered loggers.

### UnityConsoleLogger

An `ILogger` implementation that formats and outputs `LogEntry` objects to Unity's console with rich text coloring. Supports:
- Per-level color configuration via `UnityConsoleLoggerConfig.LogLevelColors`.
- Enable/disable toggle.
- Level filtering via `AllowedLevels`.
- Columnar formatting matching `ConsoleFormatterHelpers` layout.

### UnityConsoleFormatter

An `IConsoleFormatter` that supports two output modes:
- `WriteStructuredLog(LogEntry)` — Formats a complete log entry with timestamp, level, source, message, exception, and additional data.
- `WriteColoredParts(level, source, columnWidth, parts)` — Writes multi-colored message segments for custom formatted output.

Both modes set `IsLoggingInternally = true` to prevent `UnityLoggerBridge` re-capture.

### UnityConsoleLoggerConfig

Configuration for `UnityConsoleLogger`, implementing `ILoggerConfig`:

| Property         | Type                             | Default                                                     |
|------------------|----------------------------------|-------------------------------------------------------------|
| `Enabled`        | `bool`                           | `true`                                                      |
| `AllowedLevels`  | `HashSet<LogLevel>`              | All levels (Critical, Error, Warning, Info, Debug, Verbose) |
| `LogLevelColors` | `Dictionary<LogLevel, string>`   | Critical/Error=red, Warning=yellow, Info=white, Debug=lime, Verbose=grey |

## Bootstrap Chain

The full startup chain across the codebase:

```
MainBootstrapSystem (initial scene)
    │
    ├── UNITY_SERVER path:
    │   └── Loads "ServerLauncher" scene
    │       └── ServerLauncher : BootstrapSystem
    │           └── Loads world/scene server scenes
    │
    └── Client path:
        └── Loads "ClientPreboot" scene
            └── ClientPostbootSystem : BootstrapSystem
                └── Loads client UI and gameplay scenes
```

## External Integration Points

- **AddressableLoadProcessor** — Core dependency for all asset and scene loading. Provides `EnqueueLoad`, `BeginProcessQueue`, `OnProgressUpdate`, `ReleaseAllAssets`.
- **FishMMO.Logging.Log** — Central logging manager. `MainBootstrapSystem` initializes it with Unity-specific loggers and formatters.
- **Constants** — Provides `GetWorkingDirectory()`, `Configuration.WorldScenePath`, `Configuration.LocalScenePath`.
- **VersionConfig** — ScriptableObject providing game version string.
- **ServerLauncher** — Server-side bootstrap subclass that continues the server startup chain.
- **ClientPostbootSystem** — Client-side bootstrap subclass that continues the client startup chain.
- **Application.wantsToQuit** — Unity lifecycle hook used for graceful async shutdown.
- **EditorApplication.playModeStateChanged** — Editor-only hook for shutdown on exiting play mode.