# Bootstrap System

**Short description:** The Bootstrap system manages sequential, event-driven initialization of FishMMO — including Addressable asset and scene loading, logging setup, version management, dynamic load-path overrides, and graceful shutdown — across Editor, Standalone, and WebGL platforms.

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

The Bootstrap system is the first code that executes when FishMMO launches. It provides a sequential, event-driven startup pipeline where each bootstrap stage loads Addressable assets and scenes through two phases (Preload and Postload), then chains to the next stage by discovering `BootstrapSystem` components in newly loaded scenes.

`MainBootstrapSystem` is the entry point placed in the initial Unity scene. It auto-starts via `Awake()`, initializes the structured logging framework (bridging Unity's `Debug.Log` with FishMMO's central `Log` manager), reads `VersionConfig` for game version validation, enqueues the first scene (`ServerLauncher` for server builds or `ClientPreboot` for client builds), and registers graceful shutdown handlers for both editor and standalone environments.

`DynamicAddressableLoadPathSystem` allows runtime overriding of remote Addressable asset URLs — useful for IP discovery or CDN redirection — by replacing the domain portion of HTTP/HTTPS URLs while leaving local paths unchanged.

The Logging subsystem (`Logging/` subdirectory) provides Unity-specific integration with `FishMMO.Logging`: a `UnityLoggerBridge` that intercepts all `Debug.Log` calls and routes them through the central `Log` manager, a `UnityConsoleLogger` that renders structured `LogEntry` objects with rich-text coloring back to the Unity console, a `UnityConsoleFormatter` for columnar multi-colored output, and a `UnityConsoleLoggerConfig` for per-level color theming and filtering. Re-entrant loop prevention is handled via the `IsLoggingInternally` flag.

## Supported Platforms

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows  | Yes       | Standalone builds; full Preload/Postload pipeline |
| Linux    | Yes       | Standalone and dedicated server builds |
| WebGL    | Yes       | Uses `WebGLPreloadAssets` / `WebGLPreloadScenes` compile-time lists |

| Requirement       | Version / Detail |
|-------------------|------------------|
| Unity             | 6.3 LTS          |
| Scripting Backend | IL2CPP           |

## Features

- **Two-phase Addressable loading pipeline** — each `BootstrapSystem` runs a Preload phase then a Postload phase, each with platform-specific asset and scene lists selected at compile time (`UNITY_EDITOR`, `UNITY_WEBGL`, default Standalone).
- **Automatic bootstrap chaining** — when a scene finishes loading, `OnBootstrapPostProcess` scans its root GameObjects for `BootstrapSystem` components and calls `StartBootstrap()` on each after the current stage completes.
- **Virtual hook points** — `OnPreload()`, `OnPostLoad()`, `OnCompletePreload()`, `OnCompleteProcessing()`, and `OnDestroying()` allow subclasses to inject custom logic at every stage of the pipeline.
- **Structured logging initialization** — `MainBootstrapSystem` configures `FishMMO.Logging.Log` with `UnityConsoleLogger`, `UnityConsoleFormatter`, `UnityLoggerBridge`, and a JSON-serializable `UnityConsoleLoggerConfig`.
- **Bidirectional Unity ↔ FishMMO log bridging** — `UnityLoggerBridge` intercepts Unity `Debug.Log` calls and routes them to `Log.Write()`; `UnityConsoleLogger` writes back to `Debug.Log` with `IsLoggingInternally = true` to prevent infinite recursion.
- **Rich-text columnar console output** — `UnityConsoleFormatter` renders timestamped, padded, color-coded log entries with exception details and additional data sections.
- **Per-level log filtering and color theming** — `UnityConsoleLoggerConfig` provides `AllowedLevels` (HashSet) and `LogLevelColors` (Dictionary) for runtime configuration.
- **Version management** — reads `VersionConfig` ScriptableObject and optionally validates against `version.txt` in standalone builds.
- **Dynamic Addressable load-path override** — `DynamicAddressableLoadPathSystem` replaces remote asset URL domains at runtime via `Addressables.ResourceManager.InternalIdTransformFunc`.
- **Graceful async shutdown** — handles `Application.wantsToQuit`, editor play-mode exit (`EditorApplication.playModeStateChanged`), and `OnDestroy` with deferred quit, logging config save, `Log.Shutdown()`, and Addressable asset release.
- **Duplicate-start guard** — `BootstrapSystem.StartBootstrap()` tracks `hasStartedBootstrap` to prevent re-entrant initialization.

## Prerequisites

- Unity 6.3 LTS with IL2CPP scripting backend.
- Addressables package configured with valid asset groups and load paths.
- `FishMMO.Logging` assembly (provides `Log`, `ILogger`, `IConsoleFormatter`, `ILoggerConfig`, `LogEntry`, `LogLevel`, `ConsoleFormatterHelpers`).
- `AddressableLoadProcessor` — shared utility for queuing and processing Addressable loads.
- `VersionConfig` ScriptableObject assigned in the `MainBootstrapSystem` inspector.
- `Constants` class providing `GetWorkingDirectory()`.
- Initial Unity scene containing a GameObject with the `MainBootstrapSystem` component.

## Installation / Build

This is an integrated module within the FishMMO Unity project. No separate installation steps are required. The Bootstrap classes are compiled as part of the `FishMMO.Shared` assembly and are available to both server and client builds.

Ensure the initial scene in your build settings contains a `MainBootstrapSystem` component on a root GameObject, and that the `VersionConfig` asset reference is assigned in the Inspector.

## Quick Start Guides

### Running the Bootstrap Chain

1. Open the FishMMO Unity project.
2. Ensure the initial scene (e.g., `Bootstrap` or `Main`) is the first scene in **Build Settings**.
3. Verify a root GameObject in that scene has the `MainBootstrapSystem` component attached.
4. Assign the `VersionConfig` asset in the Inspector.
5. Enter Play Mode — `MainBootstrapSystem.Awake()` triggers `StartBootstrap()`, which runs the full Preload → Postload → chain pipeline.

### Adding a New Bootstrap Stage

1. Create a new class inheriting from `BootstrapSystem`.
2. Override `OnPreload()` and/or `OnPostLoad()` to add custom initialization logic.
3. Place the component on a root GameObject in the scene that the preceding stage loads.
4. The preceding stage's `OnBootstrapPostProcess` will discover it, and `OnCompleteProcessing` will call `StartBootstrap()` on it automatically.

### Configuring Logging

1. `MainBootstrapSystem` reads the logging config from `logging.json` in the working directory (configurable via `configFileName` in the Inspector).
2. In the Editor, a `UnityConsoleFormatter` and a manual `UnityConsoleLogger` (all levels enabled) are created automatically.
3. In Standalone builds, logging is driven entirely by the JSON config file; the formatter and manual loggers are `null`.

## Configuration

### MainBootstrapSystem Inspector Properties

| Property | Type | Description |
|----------|------|-------------|
| `configFileName` | `string` | Name of the logging configuration JSON file (default: `"logging.json"`) |
| `versionConfig` | `VersionConfig` | Reference to the VersionConfig ScriptableObject |

### BootstrapSystem Inspector Properties

Each `BootstrapSystem` exposes platform-specific load lists in the Inspector:

| Property | Platform | Phase | Type |
|----------|----------|-------|------|
| `EditorPreloadAssets` | Editor | Preload | `List<AddressableAssetKey>` |
| `EditorPostloadAssets` | Editor | Postload | `List<AddressableAssetKey>` |
| `EditorPreloadScenes` | Editor | Preload | `List<AddressableSceneLoadData>` |
| `EditorPostloadScenes` | Editor | Postload | `List<AddressableSceneLoadData>` |
| `PreloadAssets` | Standalone | Preload | `List<AddressableAssetKey>` |
| `PostloadAssets` | Standalone | Postload | `List<AddressableAssetKey>` |
| `PreloadScenes` | Standalone | Preload | `List<AddressableSceneLoadData>` |
| `PostloadScenes` | Standalone | Postload | `List<AddressableSceneLoadData>` |
| `WebGLPreloadAssets` | WebGL | Preload | `List<AddressableAssetKey>` |
| `WebGLPostloadAssets` | WebGL | Postload | `List<AddressableAssetKey>` |
| `WebGLPreloadScenes` | WebGL | Preload | `List<AddressableSceneLoadData>` |
| `WebGLPostloadScenes` | WebGL | Postload | `List<AddressableSceneLoadData>` |

### DynamicAddressableLoadPathSystem Inspector Properties

| Property | Type | Description |
|----------|------|-------------|
| `RuntimeBaseUrl` | `string` | Base URL to prepend to relative asset paths for remote Addressable loading |

### UnityConsoleLoggerConfig Properties (JSON)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Type` | `string` | `"UnityConsoleLoggerConfig"` | Config type identifier for serialization |
| `LoggerType` | `string` | `"UnityConsoleLogger"` | Logger type identifier for factory lookup |
| `Enabled` | `bool` | `true` | Whether the Unity console logger is active |
| `AllowedLevels` | `HashSet<LogLevel>` | All levels (Critical, Error, Warning, Info, Debug, Verbose) | Which log levels to process |
| `LogLevelColors` | `Dictionary<LogLevel, string>` | Critical/Error = `red`, Warning = `yellow`, Info = `white`, Debug = `lime`, Verbose = `grey` | Unity rich-text color per log level |

## Usage Examples

### Subclassing BootstrapSystem

```csharp
public class MyCustomBootstrap : BootstrapSystem
{
    public override void OnPreload()
    {
        // Custom initialization before preload assets finish loading.
        Log.Info("MyCustomBootstrap", "Preload phase started.");
    }

    public override void OnPostLoad()
    {
        // Custom initialization before postload assets finish loading.
        Log.Info("MyCustomBootstrap", "Postload phase started.");
    }

    public override void OnCompleteProcessing()
    {
        // Custom logic after all loading completes, before chaining.
        Log.Info("MyCustomBootstrap", "All loading complete.");
        base.OnCompleteProcessing(); // Chains to next bootstrap stages.
    }
}
```

### Overriding Addressable Load Paths at Runtime

```csharp
// Attach DynamicAddressableLoadPathSystem to a GameObject and set RuntimeBaseUrl:
// RuntimeBaseUrl = "http://cdn.example.com/"
//
// URL transformation examples:
//   "http://old-host.com/path/to/asset"   → "http://cdn.example.com/path/to/asset"
//   "http://127.0.0.1:8000/bundles/data"  → "http://cdn.example.com/bundles/data"
//   "file:///local/asset"                 → "file:///local/asset" (unchanged)
```

### Logging Configuration (logging.json)

```json
{
  "Loggers": [
    {
      "Type": "UnityConsoleLoggerConfig",
      "LoggerType": "UnityConsoleLogger",
      "Enabled": true,
      "AllowedLevels": ["Critical", "Error", "Warning", "Info", "Debug", "Verbose"],
      "LogLevelColors": {
        "Critical": "red",
        "Error": "red",
        "Warning": "yellow",
        "Info": "white",
        "Debug": "lime",
        "Verbose": "grey"
      }
    }
  ]
}
```

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| Bootstrap starts on Play | Enter Play Mode with `MainBootstrapSystem` in the initial scene | Console shows `[MainBootstrapSystem] Initializing...` and `Logging system initialized successfully.` |
| Version loaded | Check console output after bootstrap | `Loaded GameVersion: X.Y.Z` message appears |
| Preload phase completes | Watch console during startup | `Preload Complete` message for each `BootstrapSystem` |
| Postload phase completes | Watch console during startup | `Postload Complete` message for each `BootstrapSystem` |
| Bootstrap chaining works | Load a scene containing another `BootstrapSystem` | `OnCompleteProcessing` triggers `StartBootstrap()` on discovered systems |
| Duplicate start prevented | Attempt to call `StartBootstrap()` twice on same system | Warning: `BootstrapSystem tried to start multiple times. Ignoring.` |
| Logging bridge active | Call `Debug.Log("test")` during play | Message routed through `FishMMO.Logging.Log` with source `"UNITY"` |
| Re-entrant loop prevention | `UnityConsoleLogger` writes to `Debug.Log` | No infinite loop; `IsLoggingInternally` flag prevents re-capture |
| Graceful shutdown (Editor) | Exit Play Mode | `Editor exiting Play Mode. Initiating shutdown...` followed by synchronous `Log.Shutdown()` |
| Graceful shutdown (Standalone) | Close application | Async shutdown: logging config saved, `Log.Shutdown()` awaited, then `Application.Quit()` |
| Dynamic load path override | Set `RuntimeBaseUrl` and load remote assets | HTTP/HTTPS asset URLs have domain replaced; local paths unchanged |
| WebGL asset lists used | Build for WebGL platform | `WebGLPreloadAssets` / `WebGLPreloadScenes` selected at compile time |

## Flow Diagram

### Full Bootstrap Loading Pipeline

```
MainBootstrapSystem.Awake()
    │
    └── StartBootstrap()
            │
            ├── Guard: skip if already started
            │
            └── InitializePreload()
                    │
                    ├── Enqueue platform-specific preload assets
                    │   ├── UNITY_EDITOR  → EditorPreloadAssets / EditorPreloadScenes
                    │   ├── UNITY_WEBGL   → WebGLPreloadAssets / WebGLPreloadScenes
                    │   └── default       → PreloadAssets / PreloadScenes
                    │
                    ├── OnPreload() — MainBootstrapSystem overrides:
                    │   ├── Load VersionConfig (asset + optional version.txt validation)
                    │   ├── Set GameVersion static string
                    │   ├── Register editor play-mode state handler (editor only)
                    │   ├── Register Application.wantsToQuit handler
                    │   ├── Configure FishMMO.Logging:
                    │   │   ├── Register UnityConsoleLoggerConfig factory
                    │   │   ├── Create UnityConsoleFormatter (editor only)
                    │   │   ├── Create manual UnityConsoleLogger list (editor only)
                    │   │   └── Log.Initialize(configFilePath, formatter, loggers, callback, configTypes)
                    │   └── Enqueue initial scene:
                    │       ├── UNITY_SERVER  → "ServerLauncher"
                    │       └── !UNITY_SERVER → "ClientPreboot"
                    │
                    ├── Subscribe to AddressableLoadProcessor.OnProgressUpdate
                    └── AddressableLoadProcessor.BeginProcessQueue()
                            │
                            └── OnPreloadProgressUpdate(progress)
                                    │
                                    ├── Wait until progress >= 1.0
                                    ├── Unsubscribe from OnProgressUpdate
                                    └── OnCompletePreload()
                                            │
                                            └── InitializePostload()
                                                    │
                                                    ├── Enqueue platform-specific postload assets/scenes
                                                    ├── OnPostLoad() — virtual hook
                                                    ├── Subscribe to OnProgressUpdate
                                                    └── BeginProcessQueue()
                                                            │
                                                            └── OnPostloadProgressUpdate(progress)
                                                                    │
                                                                    ├── Wait until progress >= 1.0
                                                                    ├── Unsubscribe from OnProgressUpdate
                                                                    └── OnCompleteProcessing()
                                                                            │
                                                                            └── Start preloaded BootstrapSystems
                                                                                (chaining to next stage)
```

### Bootstrap Chain Across Builds

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

### Logging Flow

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

### URL Transformation (DynamicAddressableLoadPathSystem)

| Original URL | RuntimeBaseUrl | Transformed URL |
|---|---|---|
| `http://old-host.com/path/to/asset` | `http://new-host.com/` | `http://new-host.com/path/to/asset` |
| `http://127.0.0.1:8000/bundles/data` | `http://cdn.example.com/` | `http://cdn.example.com/bundles/data` |
| `file:///local/asset` | (any) | `file:///local/asset` (unchanged) |

## Project Structure

### Directory Tree

```
Bootstrap/
├── BootstrapSystem.cs                   # Base class for all bootstrap stages
├── MainBootstrapSystem.cs               # Entry point: logging, version, shutdown
├── DynamicAddressableLoadPathSystem.cs  # Runtime Addressable URL override
├── README.md                            # This file
└── Logging/
    ├── UnityConsoleFormatter.cs         # IConsoleFormatter for Unity rich text
    ├── UnityConsoleLogger.cs            # ILogger outputting to Unity console
    ├── UnityConsoleLoggerConfig.cs      # ILoggerConfig with level filtering and colors
    ├── UnityLoggerBridge.cs             # ILogHandler bridging Unity → FishMMO Log
    └── README.md                        # Logging subsystem documentation
```

### Inheritance Hierarchies

#### Bootstrap Systems (MonoBehaviour)

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

#### Logging Interfaces

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

### Virtual Hooks (BootstrapSystem)

| Method | When Called | Purpose |
|--------|------------|---------|
| `OnPreload()` | After preload assets/scenes are enqueued | Subclass initialization before preload begins |
| `OnPostLoad()` | After postload assets/scenes are enqueued | Subclass initialization before postload begins |
| `OnCompletePreload()` | When preload progress reaches 1.0 | Triggers postload (can be overridden) |
| `OnCompleteProcessing()` | When postload progress reaches 1.0 | Chains to next bootstrap stages (can be overridden) |
| `OnDestroying()` | During OnDestroy cleanup | Custom cleanup logic |

### External Dependencies

| Dependency | Purpose |
|------------|---------|
| `AddressableLoadProcessor` | Queuing and processing Addressable asset/scene loads; progress events |
| `FishMMO.Logging.Log` | Central static log manager for routing entries to registered loggers |
| `FishMMO.Logging.ILogger` | Interface implemented by `UnityConsoleLogger` |
| `FishMMO.Logging.IConsoleFormatter` | Interface implemented by `UnityConsoleFormatter` |
| `FishMMO.Logging.ILoggerConfig` | Interface implemented by `UnityConsoleLoggerConfig` |
| `FishMMO.Logging.LogEntry` | Structured log entry (Level, Source, Message, Timestamp, ExceptionDetails, Data) |
| `FishMMO.Logging.LogLevel` | Enum: Critical, Error, Warning, Info, Debug, Verbose |
| `FishMMO.Logging.ConsoleFormatterHelpers` | Shared column widths, padding, and Unity rich-text escaping |
| `Constants` | Provides `GetWorkingDirectory()` |
| `VersionConfig` | ScriptableObject providing game version string |
| `UnityEngine.Debug` | Unity's built-in logging API |
| `UnityEngine.ILogHandler` | Unity interface for custom log handlers |
| `UnityEngine.AddressableAssets.Addressables` | Addressable asset system; `InternalIdTransformFunc` for URL overrides |

## License

This module is part of the FishMMO project and is distributed under the FishMMO project license.
