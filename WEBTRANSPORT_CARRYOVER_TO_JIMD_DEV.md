# Carry-over lists → `jimdroberts/FishMMO` `dev`

**Purpose:** When merging **our** working play/network path into **jimdroberts/FishMMO** as the main branch:

1. **Track 1 (§A–F):** WebTransport / wire stack  
2. **Track 2 (§H–M):** Gameplay / DB / character create / scene matchmaking / player prefabs (non-WT, but required for end-to-end login → world → scene on the gameserver)

List only (details later).

**Compare base (local):**

| Side | Ref |
|------|-----|
| Ours (working path) | `fishmmo-pr-dev` @ `0869c78e` (UnityEQ / this tree) |
| Main target | `upstream/dev` = [jimdroberts/FishMMO `dev`](https://github.com/jimdroberts/FishMMO/tree/dev) @ `1a1bbc87` (fetched) |

**How Track 1 was built:** `git diff --name-only upstream/dev...HEAD` filtered to WebTransport trees, plus files in that diff that reference WebTransport / native WT / TLS for QUIC handoff.

**How Track 2 was built:** same diff, excluding pure WT paths; grouped into create / DB / matchmaking / prefabs / login pipeline pieces that were fixed for the working gameserver path.

---

## A. Native WebTransport stack (`FishMMO-WebTransport/`)

Core native library (msquic / Schannel / Linux builds) and sources.

### Build / docs

- `FishMMO-WebTransport/CMakeLists.txt`
- `FishMMO-WebTransport/README.md`
- `FishMMO-WebTransport/build_windows.ps1`
- `FishMMO-WebTransport/build_windows_schannel.ps1` *(added on our side)*
- `FishMMO-WebTransport/build_local.bat` *(added)*
- `FishMMO-WebTransport/rebuild_only.bat` *(added)*
- `FishMMO-WebTransport/rebuild_only.ps1` *(added)*

### C++ sources (differ on our side)

- `FishMMO-WebTransport/src/client.cpp`
- `FishMMO-WebTransport/src/http3.cpp`
- `FishMMO-WebTransport/src/http3.h`
- `FishMMO-WebTransport/src/server.cpp`
- `FishMMO-WebTransport/src/server.h`
- `FishMMO-WebTransport/src/session.cpp`
- `FishMMO-WebTransport/src/session.h`
- `FishMMO-WebTransport/src/stream_manager.cpp`
- `FishMMO-WebTransport/src/stream_manager.h`
- `FishMMO-WebTransport/src/webtransport_api.cpp`
- `FishMMO-WebTransport/src/webtransport_api.h`
- `FishMMO-WebTransport/src/webtransport_internal.h`

### Tools (added on our side)

- `FishMMO-WebTransport/tools/gen_huffman.py`
- `FishMMO-WebTransport/tools/huffman_tables.inc`

### Same tree on both (no path-level diff in this compare, but part of WT package)

Worth verifying when merging (may still need full-folder sync):

- `FishMMO-WebTransport/src/client.h`
- `FishMMO-WebTransport/src/datagram_queue.cpp`
- `FishMMO-WebTransport/src/datagram_queue.h`
- `FishMMO-WebTransport/build_linux.sh`
- `FishMMO-WebTransport/build_macos.sh`
- `FishMMO-WebTransport/build_windows_cross.sh`
- other files under `FishMMO-WebTransport/` not listed as changed

---

## B. Unity FishNet WebTransport plugin

`FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/`

### C# / WebGL (differ on our side)

- `.../WebTransport.cs`
- `.../Core/ClientSocket.cs`
- `.../Core/ServerSocket.cs`
- `.../Core/Supporting.cs`
- `.../Native/WebTransportNative.cs`
- `.../WebGL/WebTransportJSLib.cs`
- `.../WebGL/plugin/WebTransport.jslib`
- `.../link.xml`
- `.../Plugins/README.md`
- `.../Plugins/linux_x86_64.meta` *(added)*
- `.../Plugins/windows_x86_64.meta` *(added)*

### Same plugin tree (no path-level diff here; confirm on merge)

- `.../Core/CommonSocket.cs`
- `.../Editor/WebTransportEditor.cs`
- `.../Editor/WebTransport.Editor.asmdef`
- `.../WebTransport.asmdef`
- `.../README.md`
- related `*.meta` for the above

---

## C. Native binaries (required to *run*, usually not in git)

Gitignored under:

- `FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/*/`

Deploy / rebuild artifacts (do **not** rely on source merge alone):

- Windows: `fishmmo_webtransport.dll` (+ `msquic.dll` if Schannel/dynamic path)
- Linux gameserver: `libfishmmo_webtransport.so` (and msquic if separate)

Build from `FishMMO-WebTransport` after sources are on jimd’s branch, then copy into the plugin Plugins folders for client/server packages.

---

## D. Downstream code that touches WebTransport (pipeline / host / auth over WT)

These files **differ** from `upstream/dev` and either reference WebTransport or sit on the login→world WT path that only works with our stack. Carry carefully; not pure plugin code.

### Client connection / host

- `FishMMO-Unity/Assets/Scripts/Client/Client.cs`
- `FishMMO-Unity/Assets/Scripts/Client/Connection/ClientConnectionManager.cs`
- `FishMMO-Unity/Assets/Scripts/Shared/Implementation/Constants.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/FishNetNetworkWrapper.cs`

### Auth over the WT session (handshake / cookie / token on streams)

- `FishMMO-Unity/Assets/Scripts/Client/Authentication/ClientLoginAuthenticator.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/Authentication/BaseServerAuthenticator.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/Authentication/ServerAuthenticator.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/Authentication/TokenServerAuthenticator.cs`
- `FishMMO-Auth/FishMMO-ClientAuth/Implementation/Auth/ClientAuthenticatorCore.cs`
- `FishMMO-Auth/FishMMO-ServerAuth/Implementation/Auth/SrpAuthenticatorCore.cs`

### TLS / certificate pinning (client → QUIC/TLS to login/world hosts)

- `FishMMO-Unity/Assets/Scripts/Client/Security/ClientSecurityBootstrap.cs`
- `FishMMO-Unity/Assets/Scripts/Client/Security/Editor/ClientSecurityBuildValidator.cs`
- `FishMMO-Unity/Assets/StreamingAssets/client-security.json`

### Server scenes (NetworkManager / transport component wiring)

- `FishMMO-Unity/Assets/Scenes/Server/LoginServer.unity`
- `FishMMO-Unity/Assets/Scenes/Server/WorldServer.unity`
- `FishMMO-Unity/Assets/Scenes/Server/SceneServer.unity`

### Proxy / edge for UDP-QUIC / WebTransport

- `FishMMO-Setup/nginx.conf`

### Root docs (Windows Schannel / WT build notes)

- `README.md`

---

## E. Optional / secondary (mention WT in diff but lower priority for pure wire stack)

Login UI that sets connection tokens then connects (uses Client → WT):

- `FishMMO-Unity/Assets/Scripts/Client/GUI/Login/Login/UITKLogin.cs`
- `FishMMO-Unity/Assets/Scripts/Client/GUI/Login/Login/UITKRegister.cs`
- `FishMMO-Unity/Assets/Scripts/Client/UI/Controls/Login/Login/UILogin.cs`
- `FishMMO-Unity/Assets/Scripts/Client/UI/Controls/Login/Login/UIRegister.cs`

(Other login GUI files may differ for non-WT reasons; only pull if token/connect path needs them.)

Possibly noise unless strip/link configs matter for WT plugin:

- `FishMMO-Unity/Assets/AddressableAssetsData/link.xml` *(if still present / differs)*

---

## F. Suggested carry-over buckets (for later fill-in)

| Bucket | Paths |
|--------|--------|
| **1 – Must-have native** | All of §A changed sources + rebuild scripts |
| **2 – Must-have Unity plugin** | All of §B changed plugin files |
| **3 – Must-have deploy artifacts** | §C native `.dll` / `.so` for gameserver + client |
| **4 – Session / handshake glue** | §D auth + ClientConnectionManager + Client.cs |
| **5 – TLS + edge** | client-security + ClientSecurity* + nginx |
| **6 – Server scene transport refs** | Login/World/Scene `.unity` (diff carefully) |

---

## G. Track 1 notes

- Auth / Client / Connection / TLS files appear in **both** Track 1 (§D) and Track 2 (login pipeline) — same files, dual reasons. Merge once carefully.
- Gameplay / DB / create / matchmaking / prefabs are **Track 2** below (not pure WebTransport).

---

# Track 2 — Gameplay / DB / create / matchmaking / prefabs

**Goal:** Login → character create/select → world → scene Ready → spawn without DB Error / wait-queue TTL / PacketId storms.  
**Does not** include `FishMMO-WebTransport/**` or `**/Plugins/WebTransport/**` (Track 1).

---

## H. Database (`FishMMO-Database/`) — create + scene handoff

### Character create / soft-delete / CSV columns

- `FishMMO-Database/FishMMO-DB/Npgsql/Entities/Scene/Character/CharacterEntity.cs`
- `FishMMO-Database/FishMMO-DB/Npgsql/Services/Scene/Character/CharacterService.cs`
- `FishMMO-Database/FishMMO-DB/Npgsql/Services/Interfaces/ICharacterService.cs`
- `FishMMO-Database/FishMMO-DB/Npgsql/Services/Scene/Character/CharacterAbilityService.cs`
- `FishMMO-Database/FishMMO-DB/Npgsql/Services/Scene/Character/CharacterPetService.cs`
- `FishMMO-Database/FishMMO-DB/Npgsql/EntityConfigurations/Scene/Character/CharacterAbilityEntityConfiguration.cs`
- `FishMMO-Database/FishMMO-DB/Npgsql/EntityConfigurations/Scene/Character/CharacterPetEntityConfiguration.cs`

### Scene queue / Ready / matchmaking SQL

- `FishMMO-Database/FishMMO-DB/Npgsql/Services/Scene/SceneService.cs`

### Account / login DB (if AutoVerify / account path was part of working login)

- `FishMMO-Database/FishMMO-DB/Npgsql/Services/Login/AccountService.cs`
- `FishMMO-Database/FishMMO-DB/Npgsql/Services/Interfaces/IAccountService.cs`

### Shared DB infrastructure (differs; may be required for above services)

- `FishMMO-Database/FishMMO-DB/Npgsql/Services/BaseService.cs`
- `FishMMO-Database/FishMMO-DB/Npgsql/Services/UnitOfWorkService.cs`
- `FishMMO-Database/FishMMO-DB/Npgsql/NpgsqlDbConfiguration.cs`
- `FishMMO-Database/FishMMO-DB/AppSettings.cs`
- `FishMMO-Database/FishMMO-DB/DatabaseConfigurationHelper.cs`
- `FishMMO-Database/FishMMO-DB/README.md`
- `FishMMO-Database/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj`
- `FishMMO-Database/FishMMO-DB-Migrator/Program.cs`

### Deploy artifact (not always in git)

- Built `FishMMO-DB.dll` → Unity `Assets/Dependencies` **and** Login/World/Scene **Managed** set (same version together)

---

## I. Login server — character create / select / world list

### Server systems

- `FishMMO-Unity/Assets/Scripts/Server/Implementation/LoginServer/CharacterCreate/CharacterCreateSystem.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/LoginServer/CharacterSelect/CharacterSelectSystem.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/LoginServer/ServerSelect/ServerSelectSystem.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/LoginServer/LoginServer/LoginServerSystem.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/LoginServer/AccountCreation/AccountCreationSystem.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/Account/TokenAccountManager.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/Server.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/ServerBehaviourRegistry.cs`

### Client UI (create / select / server connect)

- `FishMMO-Unity/Assets/Scripts/Client/GUI/Login/CharacterCreate/UITKCharacterCreate.cs`
- `FishMMO-Unity/Assets/Scripts/Client/GUI/Login/CharacterSelect/UITKCharacterSelect.cs`
- `FishMMO-Unity/Assets/Scripts/Client/GUI/Login/ServerSelect/UITKServerSelect.cs`
- `FishMMO-Unity/Assets/Scripts/Client/UI/Controls/Login/CharacterCreate/UICharacterCreate.cs`
- `FishMMO-Unity/Assets/Scripts/Client/UI/Controls/Login/CharacterSelect/UICharacterSelect.cs`
- `FishMMO-Unity/Assets/Scripts/Client/UI/Controls/Login/ServerSelect/UIServerSelect.cs`
- `FishMMO-Unity/Assets/Scripts/Client/GUI/Login/Login/UITKLogin.cs` *(also Track 1 §E)*
- `FishMMO-Unity/Assets/Scripts/Client/GUI/Login/Login/UITKRegister.cs` *(also Track 1 §E)*
- `FishMMO-Unity/Assets/Scripts/Client/UI/Controls/Login/Login/UILogin.cs` *(also Track 1 §E)*
- `FishMMO-Unity/Assets/Scripts/Client/UI/Controls/Login/Login/UIRegister.cs` *(also Track 1 §E)*

---

## J. World / scene matchmaking (enter-world handoff)

### WorldServer — wait queue, enqueue, assign, world_server_id rebind

- `FishMMO-Unity/Assets/Scripts/Server/Implementation/World/WorldServer/WorldScene/WorldSceneSystem.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/World/WorldServer/WorldServer/WorldServerSystem.cs`

### SceneServer — load → Ready (main-thread), spawn / load character

- `FishMMO-Unity/Assets/Scripts/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/World/SceneServer/CharacterInventory/CharacterInventorySystem.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.cs`

### Auth on world/scene after connect *(also Track 1 §D)*

- `FishMMO-Unity/Assets/Scripts/Server/Implementation/Authentication/BaseServerAuthenticator.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/Authentication/TokenServerAuthenticator.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/Authentication/ServerAuthenticator.cs`

### Client world hop (token re-fetch before world/scene connect) *(also Track 1 §D)*

- `FishMMO-Unity/Assets/Scripts/Client/Client.cs`
- `FishMMO-Unity/Assets/Scripts/Client/Connection/ClientConnectionManager.cs`

---

## K. Scene content / spawners (dedicated server scene Ready)

- `FishMMO-Unity/Assets/Scripts/Shared/Implementation/Entity/Spawner/ObjectSpawner.cs`
- `FishMMO-Unity/Assets/Scripts/Shared/Implementation/Entity/Spawner/Settings/SpawnableSettings.cs`

---

## L. Player prefabs / NetworkBehaviours / nameplate (PacketId + missing scripts)

### Race prefabs (FishNet NetworkBehaviours list rebuild)

- `FishMMO-Unity/Assets/Prefabs/Shared/Entity/PlayableCharacters/Elf.prefab`
- `FishMMO-Unity/Assets/Prefabs/Shared/Entity/PlayableCharacters/Human.prefab`
- `FishMMO-Unity/Assets/Prefabs/Shared/Entity/PlayableCharacters/Orc.prefab`

### Billboard (moved Client → Shared so dedicated server resolves NameLabels)

- `FishMMO-Unity/Assets/Scripts/Shared/Implementation/Entity/Billboard/Billboard.cs`
- `FishMMO-Unity/Assets/Scripts/Shared/Implementation/Entity/Billboard/Billboard.cs.meta`  
  *(replaces old `Assets/Scripts/Client/Billboard.cs` on jimd if still client-only)*

---

## M. Supporting auth / IPFetch / broadcasts / config (pipeline glue)

### Auth cores *(overlap Track 1)*

- `FishMMO-Auth/FishMMO-ClientAuth/Implementation/Auth/ClientAuthenticatorCore.cs`
- `FishMMO-Auth/FishMMO-ServerAuth/Implementation/Auth/SrpAuthenticatorCore.cs`
- `FishMMO-Auth/FishMMO-AuthShared/Implementation/Services/SrpService.cs`
- `FishMMO-Unity/Assets/Scripts/Client/Authentication/ClientLoginAuthenticator.cs`
- `FishMMO-Unity/Assets/Scripts/Shared/Implementation/Network/Authentication/AuthenticationBroadcasts.cs`

### IPFetch connection token (login + world handoff discovery)

- `FishMMO-WebServers/IPFetchASP.NET/IpFetchServer/Controllers/LoginServerController.cs`
- `FishMMO-WebServers/IPFetchASP.NET/IpFetchServer/Program.cs`

### Host / version / API

- `FishMMO-Unity/Assets/Scripts/Shared/Implementation/Constants.cs` *(also Track 1)*
- `FishMMO-Unity/Assets/Scripts/Client/Launcher/ClientApiSecret.cs`
- `FishMMO-Unity/Assets/Scripts/Client/Launcher/SystemUpdaterLauncher.cs`
- `FishMMO-Unity/Assets/Scripts/Client/Launcher/UnityWebRequestService.cs`
- `FishMMO-WebServers/FishMMO-WebShared/ClientGate.cs`

### Production server cfg / env examples (may encode ports/hosts used with WT)

- `FishMMO-Setup/Production/LoginServer.cfg`
- `FishMMO-Setup/Production/WorldServer.cfg`
- `FishMMO-Setup/Production/SceneServer.cfg`
- `FishMMO-Setup/Production/appsettings.json`
- `FishMMO-Setup/Development/appsettings.json`
- `FishMMO-Setup/Development/appsettings.Database.json`
- `FishMMO-Setup/Development/appsettings.IpFetchServer.Development.json`
- `FishMMO-Setup/Development/.env.example`
- `FishMMO-Setup/logging.json`
- `FishMMO-Setup/nginx.conf` *(also Track 1)*

### Client scenes (login GUI)

- `FishMMO-Unity/Assets/Scenes/Client/ClientLoginGUI.unity`
- Server scenes already in Track 1 §D: LoginServer / WorldServer / SceneServer `.unity`

---

## N. Track 2 buckets (for later fill-in)

| Bucket | Paths |
|--------|--------|
| **T2-1 – Character create DB** | §H CharacterService / Ability / Pet / CharacterEntity |
| **T2-2 – Scene DB + matchmaking** | SceneService + WorldSceneSystem + SceneServerSystem |
| **T2-3 – Login create/select systems** | §I server CharacterCreate/Select + UI |
| **T2-4 – Enter-world assign** | WorldSceneSystem + CharacterSystem.Loading + UpdateScene (world_id) |
| **T2-5 – Dedicated scene load** | ObjectSpawner + SpawnableSettings |
| **T2-6 – Player prefab NB layout** | Elf/Human/Orc + Billboard Shared move |
| **T2-7 – IPFetch + account** | LoginServerController, AccountService, TokenAccountManager |
| **T2-8 – Deploy** | Matching FishMMO-DB.dll Managed set for Login/World/Scene |

---

## O. Track 2 — lower priority / maybe noise (differs but not core play path)

Installer / bots / package manager / web projects (carry only if deploy toolchain needs them):

- `FishMMO-Installer/**` (several files)
- `FishMMO-AppHealthMonitor/**`
- `FishMMO-DiscordBot/**`
- `FishMMO-WebServers/PatcherASP.NET/**`
- `FishMMO-WebServers/WebGLServerASP.NET/**`
- `FishMMO-SharedUtility/**`
- `FishMMO-Unity/Packages/manifest.json`
- `FishMMO-Unity/Packages/packages-lock.json`
- `FishMMO-Unity/ProjectSettings/PackageManagerSettings.asset`
- `FishMMO-Unity/ProjectSettings/ProjectSettings.asset`
- `FishMMO-Unity/FishMMO-Unity.slnx`
- `FishMMO-Unity/Assets/AddressableAssetsData/link.xml` *(if present)*

---

## P. Dual-track files (merge once)

Appear for **both** WebTransport wire path and gameplay handoff:

- `FishMMO-Unity/Assets/Scripts/Client/Client.cs`
- `FishMMO-Unity/Assets/Scripts/Client/Connection/ClientConnectionManager.cs`
- `FishMMO-Unity/Assets/Scripts/Client/Authentication/ClientLoginAuthenticator.cs`
- `FishMMO-Unity/Assets/Scripts/Server/Implementation/Authentication/*`
- `FishMMO-Auth/**` authenticator cores (listed above)
- `FishMMO-Unity/Assets/StreamingAssets/client-security.json`
- `FishMMO-Unity/Assets/Scripts/Client/Security/**`
- `FishMMO-Setup/nginx.conf`
- `FishMMO-Unity/Assets/Scenes/Server/*.unity`
- `README.md`

---

*Generated as a checklist only. Next pass: per-file notes (what/why) and conflict strategy against jimd `dev`.*
