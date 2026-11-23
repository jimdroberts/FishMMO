# Assets/FishMMO

All of FishMMO's own code lives here. The core is what every game needs: log in, create a character, enter a world
scene, move and save. The gameplay still in the core (combat, abilities, items, NPCs and others) is moving into modules
. A module is a feature a game may leave out: delete its folder and the game still compiles,
builds and runs.

## Layout

| Folder | Assemblies | Holds |
|---|---|---|
| [Core/](Core/README.md) | `FishMMO.Core` | The engine-free runtime: entities, systems, stats, effects, tags, and the module catalogue. References nothing but the .NET base library. |
| [Shared/](Shared/README.md) | `FishMMO.Shared` (+ editor assemblies) | Everything both peers run: characters and their controllers, prediction, the NPC character, interactables, templates, broadcasts, world scene data, the editor dashboard, and the seams the modules plug into. |
| [Client/](Client/README.md) | `FishMMO.Client` (+ editor assemblies) | The game client: launcher, login, connection, UI Toolkit windows and HUD, input, settings, rendering. |
| [Server/](Server/README.md) | `FishMMO.Server` | The Login, World and Scene servers: the server core contracts and their implementation. |
| [Editor/](Editor/README.md) | `FishMMO.GeneratedFiles.Editor`, `FishMMO.MockContent.Editor`, `FishMMO.AbilityContent.Editor`, `FishMMO.Render.Scratch` | Editor-only content tools that are not part of a module. |
| [Tests/](Tests/README.md) | `FishMMO.UnitTests`, `FishMMO.UnitTests.PlayMode`, `FishMMO.TestHarness*` | The core's tests and the test harness (simulation scenes, the Core Gate world). |
| Shaders/ | | The core's shader library: the lit surface stack, the light table, shadow caching, culling and instancing includes. |
| [Modules/](#modules) | `FishMMO.<Module>` and its side assemblies | The optional features. |

Scenes, prefabs, templates, materials and settings that are not part of a module stay where they were, under
`Assets/Scenes`, `Assets/Prefabs`, `Assets/FishMMO/Templates` and so on.

Classes keep their namespaces (`FishMMO.Shared`, `FishMMO.Server.*`, `FishMMO.Client`) when they move into a module. The
assembly is the boundary, and scenes, prefabs and assets keep resolving their scripts.

## Modules

| Module | Id | What it is | Remove together with |
|---|---|---|---|
| [Abilities](Modules/Abilities/README.md) | `abilities` | Abilities and their casting, cooldowns, knowledge, the ability book and cast bar. | Ability Crafting, Achievements, Archetypes, Commerce, Hotkeys, Items, NPCs & AI, Pets, Wildlife, Staff, World Interactables, Matchmaking, Social, World Design |
| [Ability Crafting](Modules/AbilityCrafting/README.md) | `abilitycrafting` | Crafting new abilities at an ability crafter. | |
| [Achievements](Modules/Achievements/README.md) | `achievements` | Achievement progress, tiers, rewards and the window. | World Design |
| [Appearance](Modules/Appearance/README.md) | `appearance` | Race proportions and blend shapes, hidden body regions, equipment drawn on the body. | |
| [Ambient Life](Modules/AmbientLife/README.md) | `ambient-life` | Birds, sea life, motes, leaves and the life of the world's sites (client presentation). | |
| [Archetypes](Modules/Archetypes/README.md) | `archetypes` | A player's archetype: set, announced, saved, checked. | |
| [Buffs](Modules/Buffs/README.md) | `buffs` | Buffs and debuffs, their templates and ticking, shields and deflection, the buff strips. | Abilities, Achievements, Archetypes, NPCs & AI, Pets, Social, World Interactables, and everything that depends on them |
| [Chat](Modules/Chat/README.md) | `chat` | Conversation: the channels, cross-server delivery, the chat window. | |
| [Combat](Modules/Combat/README.md) | `combat` | Damage, healing, death and revive; the combat text and death dialog. | Abilities and everything that depends on it |
| [Commerce](Modules/Commerce/README.md) | `commerce` | Merchants, mail, corpse loot and player trade. | |
| [Factions](Modules/Factions/README.md) | `factions` | Faction standings, the aggression toggle and their harm relation. | |
| [Hotkeys](Modules/Hotkeys/README.md) | `hotkeys` | The hotkey bar: bindings, persistence, keys, stacked or paged bars. | |
| [Housing](Modules/Housing/README.md) | `housing` | Plots, building, rent, guests and the house vault. | |
| [Items](Modules/Items/README.md) | `items` | The inventory, bank and predicted equipment, the inventory system and the item windows. | |
| [Leaderboards](Modules/Leaderboards/README.md) | `leaderboards` | PvP and PvE boards read from the database. | |
| [Map](Modules/Map/README.md) | `map` | The world map and minimap, fog of war, map notes and marker filters. | World Design |
| [Matchmaking](Modules/Matchmaking/README.md) | `matchmaking` | Group finder, dungeon finder and instances, arenas. | |
| [Naming](Modules/Naming/README.md) | `naming` | The name generator and the names scene objects are given. | World Design |
| [Mounts](Modules/Mounts/README.md) | `mounts` | Saddles, the Mount ability (a mount is a buff), how each mount moves, the drawn mount, Dismount, the Stablemaster. | |
| [NPCs & AI](Modules/NPCs/README.md) | `npcs` | The server's NPC brains, threat and spawners; the NPC designer. | Pets, Wildlife, Staff, World Design |
| [Pets](Modules/Pets/README.md) | `pets` | Pets and summons, the pet system and the pet control panel. | |
| [Quests](Modules/Quests/README.md) | `quests` | Quests, the quest log and quest givers. | |
| [Social](Modules/Social/README.md) | `social` | Friends, party and guild. | |
| [Staff and Support](Modules/Staff/README.md) | `staff` | `/gm` and `/admin`, the staff console, reports and tickets. | |
| [Terrain](Modules/Terrain/README.md) | `terrain` | The client's GPU drawing of the ground: trees, props, grass, scatter. | World Test Bed, World Design, Naming |
| [Travel](Modules/Travel/README.md) | `travel` | Waypoint discovery and fast travel; the travel spells (Portal to Bind, Mark, Recall, Gate to Gate Stones, Call to Arms). | |
| [Water](Modules/Water/README.md) | (none) | The sea, shores, rivers, lakes and lava. | All seven world modules |
| [Wildlife](Modules/Wildlife/README.md) | `wildlife` | Prey, predators and territorial animals outside factions. | World Design |
| [World Design](Modules/WorldDesign/README.md) | (none, editor only) | The world generator and its tools. | |
| [World Interactables](Modules/WorldInteractables/README.md) | `world-interactables` | Containers, NPC dialogue, gathering nodes, lore, shrines, capture points, switches, bindstones. | World Design |
| [World Simulation](Modules/WorldSimulation/README.md) | `world-simulation` | Sky, time, weather, climate, biomes and the atlas. | All seven world modules; Abilities (the weather ability modifier) and everything that depends on it |
| [World Test Bed](Modules/WorldTestBed/README.md) | (none, development only) | The world test bed's probes and switches. | World Design, Naming |

"Remove together with" lists the modules whose assemblies reference this one, directly or through another module, test
assemblies included. The seven world modules are World Simulation, Water, Terrain, Ambient Life, Naming, World Design
and World Test Bed.

### A module's folder

```
Assets/FishMMO/Modules/<Name>/
├── Runtime/    FishMMO.<Name>          Both peers: data, broadcasts, character parts, the GameModule declaration
├── Server/     FishMMO.<Name>.Server   Server systems (ServerBehaviour assets), persistence participants
├── Client/     FishMMO.<Name>.Client   Windows (prefab, UXML, USS) and client hooks; constrained to !UNITY_SERVER
├── Editor/     FishMMO.<Name>.Editor   Dashboard pages and tools; Editor/RenderScratch holds the panel renders
├── Tests/      FishMMO.<Name>.Tests    EditMode tests, including wiring tests for every seam the module uses
├── Templates/                          The module's ScriptableObject content
└── README.md
```

A module has only the folders it needs. The server assembly is limited to the Editor and the dedicated-server
platforms; the client assembly is constrained to `!UNITY_SERVER`.

### How a module plugs in

The core never names a module. A module reaches the game through seams the core exposes, and each module's README lists
the ones it uses.

| Seam | Where | What it does |
|---|---|---|
| `[assembly: GameModule(typeof(X))]` | Core | Lists the module among the installed modules (`ModuleCatalog`). Its `Install` may add systems, archetype components and effects to the Core world. |
| `[assembly: CharacterPart(typeof(X))]` | Shared, `CharacterPart.cs` | Adds a controller to every character at wake. No prefab carries it; its state rides the character's spawn payload. |
| `[assembly: CharacterPersistence(typeof(X))]` | Server, `CharacterPersistence.cs` | A participant in the character's load and save, over the module's own tables. |
| `[assembly: CharacterCreationContributor(typeof(X))]` | Server, Login server | Adds rows when a character is created. |
| `[assembly: HarmRelation(typeof(X))]` | Shared, `Relations/HarmRule.cs` | A relation in the rule that decides who may harm whom. |
| `[assembly: OperatorCommandProvider(typeof(X))]` | Server, `OperatorCommandContracts.cs` | Adds `/gm` and `/admin` commands (hosted by the Staff module). |
| Addressables label `SceneServer_Systems` | `Server.AdoptModuleSystems` | A `ServerBehaviour` asset with this label is adopted by the scene server at start. |
| `[assembly: ModuleServerSystem(typeof(T), "SceneServer")]` | `Server.AdoptDeclaredModuleSystems` | The scene server creates a `T` at start, unless an asset of that type was already adopted — for a module system with nothing to author. |
| `PartySignals` | Server, `World/SceneServer/Party/PartySignals.cs` | A server-to-server message addressed to a party, carried by the party chat pump to every scene server hosting a member and shown to no client. The Chat module posts and dispatches; Travel's Call to Arms uses it. |
| `GateStoneChoice` | Shared core, `Entity/Travel/GateStoneChoice.cs` | The client's choice of a Gate Stone for a Gate spell: Travel begins and ends it, the map shows it. |
| Addressables label `ClientWorldGUI_Panels` | `ModulePanelHost` | A window prefab with this label is placed into ClientWorldGUI when that scene loads. |
| `GameMenuEntries`, `CharacterMenuEntries` | Client | Entries in the game menu and in character right-click menus. |
| `ClientServices`, `SceneServerModuleServices` | Client, Server | Services started with the client or the scene server. |
| `CachedScriptableObject` + `[TemplateOf("Type")]` | Shared | Core content holds a module's template (an achievement, an ability, a merchant) without naming its type; the inspector filters to that type. |
| `IAbilityController`, `IAbilityPersistence`, `AbilityGrants` | Shared | The cast in progress for prediction and streaming; abilities in the character save; abilities taught by content and an NPC's kit (installed by the Abilities module). |
| `ICharacterMount`, `MountMotion`, `ItemSlot.MountSaddle` | Shared | The mount a player rides and how it moves (read by the motor every tick), and the saddle socket. |
| `IPersistedPet`, `IPetRoster`, `NPC.CompanionOwner` | Shared | Pets in the character save and the summon pre-filter; whom a companion fights for (harm rule, loot rights, credit). |
| `ICharacterBuffs`, `IBuffPersistence`, `IBuffTemplateInfo`, `DamageMitigation` | Shared | Buffs applied by weather, read by the target frame, dismissed, cleared on death, saved; a hit mitigated by shields and deflection (installed by the Buffs module). |
| `IWildlifeSpecies`, `WildlifeRules` | Shared | A species' role, carcass time, herd size, habitats and speeds for the harm rule, the AI and the spawners. |
| `TemplateCategory` hooks, `FishMMODashboard` host surface, `DashboardHooks` | Shared editor | A module's dashboard category with its own inspector, landing page, list cache and asset follow-ups; new-asset folders and extra inspector buttons. |
| `ModuleRenderFeatures` | Client, `FishModulePassesFeature` | Render passes hosted by the core renderer feature on the three URP renderers. |
| `ServerPreloadLabels` | Shared, Bootstrap | Addressables labels the server loads at start. |
| `[DashboardTool]`, `[assembly: DashboardPage]`, `DashboardFullPages` | Editor dashboard | Buttons and pages in the FishMMO Dashboard. |

What a removed module leaves behind:

- **Database tables and rows** stay in place, unused. There is one schema; nothing is dropped.
- **Scene objects** that belong to the world (plots, waypoints, dungeon entrances, merchants) stay in the scenes. Without
  the module they do nothing when used.
- **Scripts in world scenes** that point into the module show as missing scripts. The asset audit accepts these only
  while the module is removed (see below).

### Removing a module (the gate)

Every module was checked by removing it. To repeat the check:

1. **Snapshot** `Assets/AddressableAssetsData/AssetGroups/*.asset` and `Assets/AddressableAssetsData/link.xml`. When
   Unity saves, it deletes the removed module's entries from the groups, and every batch run rewrites `link.xml`.
2. **Record the removed GUIDs.** Write the GUID of every `.meta` file in the folders being removed, one per line, to
   `Library/FishMMOGate/RemovedGuids.txt` (or to a file named by the `FISHMMO_GATE_REMOVED_GUIDS` environment variable).
   `AssetReferenceAuditTests` then accepts world scenes' missing scripts and dangling references into those modules.
3. **Move** the module's folder and its `.meta` out of `Assets`, together with every module in its "Remove together
   with" column.
4. **Run** the EditMode tests in batch mode with the editor closed:
   `Unity -batchmode -projectPath FishMMO-Unity -runTests -testPlatform EditMode -testResults <file>`.
   Expect no compile errors and no failures beyond the known ones. If a server build ran last, add
   `-buildTarget Linux64 -standaloneBuildSubtarget Player`, or the run finds no tests.
5. **Restore** the folders, the groups and `link.xml`, and delete `RemovedGuids.txt`.

The world-module gate went further: with all seven world modules removed, a client and a server were built and a new
character logged into the Core Gate world (`Tests/TestHarness/Editor/CoreGateSceneGenerator.cs`), lit and shadowed
.

### Writing a module README

Each module README follows one template: a short description, a facts table (module id, assemblies, depends on,
depended on by, remove together with), the folders, the core seams it uses, what stays in the core, removing it, and
then any feature reference.

## License

This project is subject to the FishMMO project license.
