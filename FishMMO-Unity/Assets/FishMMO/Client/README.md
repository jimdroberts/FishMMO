# Client

**Short description:** `FishMMO.Client`, the game client: the launcher and boot, login and connection, the UI Toolkit
windows and HUD, input, settings, audio, the WebGL bridge, and the core rendering (world passes, shadows, fog, the
fallback sky).

The assembly is constrained to `!UNITY_SERVER`, so a dedicated server build never compiles it. It references
`FishMMO.Shared` and `FishMMO.Core`, and no module.

## Folders

| Folder | Holds |
|---|---|
| `Client.cs`, `ClientPostbootSystem.cs`, `ClientServices.cs` | The client's composition root, the post-boot step, and the services modules start with the client (`IClientService`). |
| [Launcher/](Launcher/README.md) | The launcher window, patching and the boot into the game. |
| [Authentication/](Authentication/README.md) | The client side of SRP login, encrypted transport, token reconnect, account creation and two-factor. |
| [Connection/](Connection/README.md) | Connecting to the Login, World and Scene servers over QUIC / WebTransport. |
| `GUI/` | UI Toolkit windows (`UITKControl`, `UITKCharacterControl`), the theme, `GUI/Core/` (`UIManager`, `ModulePanelHost`, panel layout and scale), the HUD, nameplates and world labels, `GUI/World/` (inventory, abilities, map, chat, character windows). |
| `Input/` | `PlayerInputController`. The generated input actions are the `FishMMO.Client.Input.Generated` assembly in `Assets/Prefabs/Client/Input/`. |
| [Settings/](Settings/README.md) | The Options window and the persisted client settings. |
| `Rendering/` | Core rendering: `WorldPasses/` (the world passes feature, the light table, cached main-light shadows), `FishModulePassesFeature` and `ModuleRenderFeatures` (the host for module passes), `CoreSkyColour` (a plain sky when no sky module is installed), `GroundCoverQuery`, `CameraGroundCover`, `TrailBodies`, `RenderSwitches`. |
| `World/` | Client world presentation the core keeps: fog composition (`FogComposer`), target outline, colour vision, nameplate and combat displays, the boundary warning, `GpuDispatch`. |
| `Audio/` | UI sounds and synthesised audio. |
| `Security/` | Certificate pinning, the API secret, the patch manifest signature check. Generated files: `CertificatePins.generated.cs`, `ClientApiSecret.generated.cs`. |
| `WebGL/` | The `.jslib` bridges (frame loop, pointer lock, page lifecycle, persistent data, WebGPU capabilities) and the content preloader. |

## Editor assemblies

`FishMMO.Client.GUI.Editor` (UI tools) and `FishMMO.Client.Security.Editor` (the generated security files).

## License

This project is subject to the FishMMO project license.
