# Server

**Short description:** `FishMMO.Server`, the Login, World and Scene servers. One assembly holds the server contracts
(`Core/`) and their implementation (`Implementation/`); each server type is an Addressable scene that `ServerLauncher`
loads.

It references `FishMMO.Shared`, `FishMMO.Core`, FishNet and the transport, and uses the precompiled database DLLs in
`Assets/Dependencies` (`FishMMO-DB.dll`, EF Core, Npgsql). It references no module. Modules add server systems by Addressables label and
character persistence by assembly attribute (see [../README.md](../README.md#how-a-module-plugs-in)).

## Folders

| Folder | Holds |
|---|---|
| `Core/` | The contracts and engine-light pieces: `IServer`, `ICoreServer`, `IServerBehaviour`, `IServerConfiguration` and `FileServerConfiguration` (the `.cfg` reader), `IngressGuard`, `MonotonicClock`, `RepeatingFaultLog`, `ShutdownCountdown`, [Collections](Core/Collections/README.md), [Authentication](Core/Authentication/README.md), and the login, world, kick and SMTP contracts. |
| [Implementation/](Implementation/README.md) | The composition root (`Server.cs`), `ServerLauncher`, `ServerBehaviour` and its registry, authentication and account managers, runtime data, SMTP, kick requests. |
| `Implementation/LoginServer/` | [Account creation](Implementation/LoginServer/AccountCreation/README.md), [character create](Implementation/LoginServer/CharacterCreate/README.md), [character select](Implementation/LoginServer/CharacterSelect/README.md), [server select](Implementation/LoginServer/ServerSelect/README.md), the [login server](Implementation/LoginServer/LoginServer/README.md) and the login queue. |
| `Implementation/World/WorldServer/` | The [world server](Implementation/World/WorldServer/WorldServer/README.md), [world scene](Implementation/World/WorldServer/WorldScene/README.md) placement and its [authentication](Implementation/World/WorldServer/Authentication/README.md). |
| `Implementation/World/SceneServer/` | The scene server's systems: [scene server](Implementation/World/SceneServer/SceneServer/README.md), [character](Implementation/World/SceneServer/Character/README.md) (load, save, persistence participants), [inventory](Implementation/World/SceneServer/CharacterInventory/README.md), [interactables](Implementation/World/SceneServer/Interactable/README.md), [chat](Implementation/World/SceneServer/Chat/README.md), [AI](Implementation/World/SceneServer/AI/README.md), [spawners](Implementation/World/SceneServer/Spawner/README.md), [pets](Implementation/World/SceneServer/Pet/README.md), [achievements](Implementation/World/SceneServer/Achievement/README.md), [hotkeys](Implementation/World/SceneServer/Hotkey/README.md), [naming](Implementation/World/SceneServer/Naming/README.md), [scene channels](Implementation/World/SceneServer/SceneChannel/README.md), abilities, the clock, navigation, waypoints. |

## Builds

Build the servers through **FishMMO Dashboard → Build**. The dedicated server is a Linux IL2CPP player; the build keeps
the database assemblies that EF Core creates by reflection (see
[Implementation/README.md](Implementation/README.md#database-assemblies-in-il2cpp-server-builds)). Each server reads
its `.cfg` file from its working directory; the templates are in `FishMMO-Setup/` at the repository root, and the main
[README](../../../../README.md#server-configuration-files) documents their keys.

## License

This project is subject to the FishMMO project license.
