# FishMMO-Unity

The Unity project: the game client (WebGL/WebGPU and standalone) and the Login, World and Scene servers (Linux
dedicated server, IL2CPP). Editor version **6000.6.8f1** (`ProjectSettings/ProjectVersion.txt`), Universal Render
Pipeline 17.6, FishNet networking over QUIC / WebTransport.

The repository root [README](../README.md) covers installation, the database, the web servers, configuration and
deployment. This file covers the Unity project itself.

## Layout

| Path | Holds |
|---|---|
| [Assets/FishMMO/](Assets/FishMMO/README.md) | All of FishMMO's code: the core (`Core`, `Shared`, `Client`, `Server`, `Editor`, `Tests`, `Shaders`) and the optional modules (`Modules/`). Start here. |
| `Assets/Scenes/` | `MainBootstrap`; `Client/` (`ClientPreboot`, `ClientPostboot`, `ClientLauncher`, `ClientLoginGUI`, `ClientWorldGUI`); `Server/` (`ServerLauncher`, `LoginServer`, `WorldServer`, `SceneServer`); the world scenes under `WorldScene/`; test scenes under `Test/`. |
| `Assets/Prefabs/`, `Assets/Settings/`, `Assets/Textures/`, `Assets/UI Toolkit/` | Content that belongs to the core: prefabs, URP assets, textures, UI themes. A module's content lives in its own folder. |
| `Assets/FishMMO/Templates/` | The core's ScriptableObject templates (races, character attributes, the core interaction triggers, weather exposure). Each module keeps its own in `Assets/FishMMO/Modules/<Module>/Templates/`, so removing a module removes its templates. |
| `Assets/AddressableAssetsData/` | The Addressables groups. Module systems and windows are found by label (`SceneServer_Systems`, `ClientWorldGUI_Panels`). |
| `Assets/Dependencies/` | Precompiled DLLs copied in by `FishMMO-Dependencies` (the database layer, EF Core, Npgsql, auth). |
| `Assets/Plugins/` | Third-party plugins (FishNet, KinematicCharacterController and others). |
| `Assets/Generated/` | Build output: world maps, portal views, planet surfaces. Gitignored and rebuilt by the build steps, like the procedural biome art in `Assets/Prefabs/Shared/Biomes/Generated/`. |
| `Assets/WebGLTemplates/` | The web player's page template. |
| [GeneratedFileTemplates/](GeneratedFileTemplates/README.md) | Templates for the gitignored `*.generated.cs` files (host config, certificate pins, API secret). |
| `Scripts/Rendering/` | Node scripts that capture and audit the WebGPU build in a browser. |

## Opening the project

Open `FishMMO-Unity` in Unity 6000.6.8f1. On first open, the generated files are restored from
`GeneratedFileTemplates/` (see its README); set the real values through **FishMMO Dashboard → Game Settings**.
Everything FishMMO adds to the editor is in the **FishMMO Dashboard** window, as dashboard tools and pages, not as Unity
menu items.

## Building

Build the client and the servers from **FishMMO Dashboard → Build**. The dashboard runs the build steps (procedural
art, world maps, portal views, the world scene details cache), builds Addressables, then builds the player. A plain
Unity build skips those steps. Headless builds take `-fishmmoOutputPath <dir>` (see the root README's build section).

## Tests

- EditMode and PlayMode tests: [Assets/FishMMO/Tests/](Assets/FishMMO/Tests/README.md), and each module's `Tests/`.
- The engine-free Core: `dotnet test FishMMO-Core.Tests` at the repository root.
- Removing a module and checking the rest still builds: [the module gate](Assets/FishMMO/README.md#removing-a-module-the-gate).

## License

This project is subject to the FishMMO project license.
