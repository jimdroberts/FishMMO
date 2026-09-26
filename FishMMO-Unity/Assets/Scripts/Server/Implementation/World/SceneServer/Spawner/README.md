# Spawner System

**Short description:** Server-only, condition-driven framework for spawning and respawning networked objects in FishMMO: spawners are authored in world scenes, baked into server-only tables, and run by the scene server with configurable selection strategies, conditional gating, physics-based placement, and a pre-warmed object pool that gives a map a fixed memory footprint.

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Installation / Build](#installation--build)
- [Quick Start Guide](#quick-start-guide)
- [Configuration](#configuration)
- [Usage Examples](#usage-examples)
- [Operational Checks](#operational-checks)
- [Flow Diagram](#flow-diagram)
- [Project Structure](#project-structure)
- [License](#license)

## Overview

The Spawner system spawns and respawns networked objects — NPCs, world items, gathering nodes, containers — with configurable selection (linear, random, weighted), conditional respawn gating (OR and AND lists), bounding-box placement with ground detection, pooling through FishNet, and per-object respawn timers.

### Authored in the scene, run from a table

World scenes are built into **one addressable bundle shared by clients and servers**, so anything left in a scene ships to every player. A spawner is therefore split in two:

| Piece | Where it lives | What it does |
|---|---|---|
| `ObjectSpawner` | A world scene, on a GameObject tagged `EditorOnly` | Design-time placement and settings. Inert at runtime; never reaches a build. |
| `SceneSpawnTable` | `Assets/Prefabs/Server/SceneServer/SpawnTables/<Scene>.asset` | The scene's spawners, baked: every authored value plus where each spawner stood. |
| `SpawnTableCatalogue` | `…/SpawnTables/SpawnTableCatalogue.asset` | Every table, by scene name. Referenced by `SpawnerSystem.asset`. |
| `SpawnerSystem` / `SpawnerHost` | Scene server | On each world scene load, runs one `SpawnerRuntime` per table entry for that scene instance; drops them on unload. |
| `SpawnerRuntime` | Scene server | The spawner itself: pool reservation, spawning, respawn timers, conditions. The spawned objects' `ISpawnOwner`. |

Every table and the catalogue are **addressable in `Server_Static_Permanent`**, addressed by path and labelled for the server's boot load. Client builds drop every group with "Server" in its name, so the data never reaches a client bundle. `ServerAddressables.Register` does this whenever an asset is created, and again on every bake (so every build re-checks it). It is idempotent, so the group file only changes when an entry was missing or wrong. `SpawnTableBakeTests.ServerDataAssets_AreAddressableInTheServerGroupOnly` checks that each table and catalogue is in that group and in no group a client builds.

The bake runs from `FishMMO Dashboard → World → Spawn Tables → Rebuild Spawn Tables`, **whenever a world scene is saved**, and **before the addressables step of every build** — the dashboard's *Build Addressables* and *Build Game* (client and server) and the CLI build alike. Every build refuses to continue while any spawner is not tagged `EditorOnly` or still carries a `NetworkObject`. The tables are generated but checked in, like the world scene details cache. Their `[SerializeReference]` ids are numbered in table order, so rebaking an unchanged scene leaves its table byte-identical and a build does not dirty the tree.

**Dashboard → World → Spawn Tables** shows the tables read-only. The landing page lists every world scene with its spawner count, flags tables missing from the catalogue or left behind by a scene that is no longer a world scene, and has **Rebuild Spawn Tables**, which offers to save modified scenes first and then reports each problem and build blocker. Selecting a table shows each spawner's placement, schedule, conditions and spawnables (prefab, chance, respawn window, NPC overrides), warns about entries that spawn nothing, and has **Rebake This Scene** and **Open Scene**. Edit spawners in the scene, not the table, because the next bake overwrites the table.

It used to be a `NetworkBehaviour` on its own scene `NetworkObject` that disabled itself off the server, which shipped every spawner's configuration to every player and spent a scene network object on each.

### Deterministic memory footprint

Objects are never destroyed and re-instantiated on respawn — they are returned to FishNet's object pool with `DespawnType.Pool` and drawn back out with `GetPooledInstantiated`. That recycling alone is not enough to make a map's cost predictable, because the pool fills *lazily*: the first NPC of each kind is instantiated the moment a player walks into range, so a freshly loaded map hitches as it is explored and only reaches its true heap size once every spawner has fired at least once.

`SpawnerPool` closes that. Each spawner reserves `MaxSpawnCount + PrewarmHeadroom` instances of every prefab it can select, at scene start, de-duplicated across spawners that share a prefab. The result is a one-time load cost and a footprint you can plan capacity against, rather than one discovered under load.

The pool outlives the scenes. FishNet leaves a despawned object in whatever scene it was in, so every pooled instance that had ever been spawned used to die with its world scene, while the reservation still counted it: the next instance of a dungeon skipped the prewarm and instantiated lazily in play. `PersistentPool` (Shared) now moves every instance going back into the pool (and every prewarmed one) out of the world scenes (`DontDestroyOnLoad`). Only objects alive at the unload go with the scene, and `SpawnerRuntime.Stop` takes exactly those out of the reservation (`SpawnerPool.Forget`), so the next load makes them again up front.

Despawns with no spawner pool the same way. A corpse whose NPC has no spawner (`NPC.ReturnToPool`'s fallback) and ground loot or a scripted container (`Interactable.Despawn`'s fallback) go through `PersistentPool.Despawn` too, so they no longer sit in the pool inside the world scene and die with it; so do a pet (`Pet.Despawn`) and a boss's add. The rule is in Shared because the NPC, pet and interactable paths are Shared code. It never moves a scene object: FishNet does not pool one, it disables it in place for its own scene to re-enable.

This is also why the per-spawner override settings matter for memory and not only for content: one NPC prefab that becomes a weak variant at one spawner and an elite at another is **one** pool bucket. Duplicating the prefab to make the variant would be a second bucket and a second fixed slice of the budget.

## Supported Platforms

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows  | Yes       | Dedicated server only; not compiled into clients |
| Linux    | Yes       | Dedicated server only; not compiled into clients |
| WebGL    | N/A       | Server-side only system |

- **Unity Version:** Unity 6.3 LTS
- **Scripting Backend:** IL2CPP

## Features

- Server-only spawn lifecycle (spawn, despawn, respawn); nothing about spawners reaches a client
- Scene-authored, table-run: `ObjectSpawner` authoring components baked into `SceneSpawnTable`s
- One set of spawners per loaded scene instance, so stacked instances of a scene each populate themselves
- Three spawn selection strategies: Linear (sequential), Random (uniform), Weighted (proportional odds via `SpawnChance`)
- Conditional respawn gating with OR and AND condition lists (`RespawnCondition`, serialized data)
- Bounding-box placement with physics SphereCast ground detection in the scene instance's own physics scene
- FishNet object pooling via `GetPooledInstantiated()` / `Despawn(DespawnType.Pool)`
- Object pool pre-warming via `SpawnerPool.Reserve(...)`
- Per-spawner NPC overrides: attribute database, AI archetype, additional or replacement abilities, faction, corpse decay, and a random uniform scale range
- Weighted item roll tables so one world-item prefab can serve every ground pickup in the game from a single pool bucket
- NPC brains prepared before the network spawn: `AIBrainHost.Prepare` adds the brain, gives it the catalogue's (or the spawner's) archetype, and warps its NavMeshAgent onto the mesh at the spawn point
- Per-object configurable respawn timers (fixed or randomized between min/max)
- One sweep per frame for the whole scene server (`SpawnerScheduler`), and only spawners that actually have something to respawn are visited at all
- Auto-calculated vertical offset (`YOffset`) from prefab collider dimensions, computed at bake time
- Editor gizmo visualization of bounding box and spawn area, with a warning label on a spawner that would ship
- `SpawnersClearedCondition` for boss encounters: a camp does not return while the spawner guarding it still has a live creature
- NPC corpse decay: dead NPCs remain visible for `NPC.CorpseDecayDuration` seconds (default 30s) before returning to the object pool. AI is suspended during corpse state and the NPC is immortal. `NPCSpawnableSettings.CorpseDecayDurationOverride` allows per-spawner override. Pets bypass the corpse timer and despawn immediately.
- Re-rolled attributes on each spawn: NPC seed, RNG, gender, and name are regenerated in `OnStartServer` so pooled NPCs get fresh randomized attributes each time.
- NPC packs: a spawner can make its NPCs one pack (`Pack`), with a tactic, a shared focus and a role per entry; respawns rejoin it. See [NPC packs](#npc-packs).

## Prerequisites

- **Unity 6.3 LTS**
- **FishNetworking** — `NetworkObject`, `ServerManager`, `SceneManager` load/unload events, object pooling
- **FishMMO Shared Core** — `ISpawnable`, `ISpawnOwner`, `ICharacterDamageController`
- **FishMMO Server AI** — `AIBrainHost` for NPC brains

## Installation / Build

Integrated module within the FishMMO Server assembly. The scene server's system list (`SceneServer.unity`) runs `SpawnerSystem`, and `SpawnerSystem.asset` names `SpawnTableCatalogue.asset`; `SpawnerSystem.asset` is in the `Server_Static_Permanent` addressable group, which pulls every table into the server-only bundle.

Scenes authored before the move carry networked spawners. `FishMMO Dashboard → World → Spawn Tables → Migrate Scene Spawners` tags every spawner `EditorOnly`, removes the `NetworkObject` that existed only for it, and bakes the tables. `[MovedFrom]` on the settings classes lets old scenes' `[SerializeReference]` records resolve to the server assembly.

## Quick Start Guide

1. Add an `ObjectSpawner` component to an empty GameObject in a world scene (`Add Component → FishMMO/Server/Object Spawner`). The GameObject is tagged `EditorOnly` automatically, and the tag cannot be left off. `ObjectSpawner.OnValidate` re-tags a spawner whenever it is loaded, pasted or edited, and `SpawnerTagEnforcer` re-tags every spawner as its scene is saved, which catches a tag changed on the GameObject afterwards. Each correction is logged and can be undone.
2. Configure `Spawnables` — pick a settings type (NPC, item) and assign its prefab.
3. Set `InitialSpawnCount` and `MaxSpawnCount` to control concurrency.
4. Choose a `SpawnType` (Linear, Random, or Weighted).
5. Optionally configure `BoundingBoxSize` and enable `RandomSpawnPosition` for area-based placement.
6. Optionally add respawn conditions to `OrConditions` / `TrueConditions`.
7. Optionally make the NPCs one pack: enable `Pack` and give each NPC entry a `PackRole` (see [NPC packs](#npc-packs)).
8. Save the scene. The scene's table is baked on save; check the console for bake warnings.

### Shipped spawners

| Scene | Spawner | Produces |
|---|---|---|
| StartScene A | `NPCSpawner` | Up to 4 of: banker, general merchant, ability crafter, ground loot |
| StartScene A | `OrcSpawner` | Up to 9 of: orc, orc warrior, orc mage |
| StartScene B | `MerchantSpawnArea` | One of: general merchant, banker, ability crafter |
| Tutorial Spawners | `Banker Spawner`, `Ability Crafter Spawner`, `General Merchant Spawner` | Up to 5 of their one townsperson each |
| Tutorial Spawners | `World Item Spawner` | Up to 10 ground loot items |
| Dungeon | `Orc 2`, `Orc 3` | One orc each, weighted: `an orc` 0.5, `an orc warrior` 0.3, `an orc mage` 0.2; prefab respawn cadence |
| Dungeon | `Chest` | `Dungeon Chest`: fixed position on the floor, respawns 5–10 minutes after it is emptied |

`SpawnTableBakeTests.EveryShippedSpawner_SpawnsSomething` fails on any spawner whose entries are empty or name no prefab, which the runtime skips without a word. The Dungeon's three spawners and the tutorial banker shipped that way until 2026-09-16.

**Placement.** With `RandomSpawnPosition` on, the object lands at the ground hit plus the spawnable's `YOffset` — a sphere's radius, but a box's or capsule's **full height**, which suits prefabs whose pivot is at their centre. A prefab whose pivot is at its base (the `Dungeon Chest`) turns random placement off and has its spawner stand on the floor. The ground probe starts at the top of the bounding box and reaches down its full height, so a spawner placed above the floor needs a box at least twice that tall; the Dungeon orc spawners stand 0.5 m up with a 1.5 m box.

## Configuration

### ObjectSpawner (authoring) and SpawnerDefinition (baked)

| Field                | Type                          | Default        | Description                                          |
|----------------------|-------------------------------|----------------|------------------------------------------------------|
| `InitialSpawnCount`  | `int`                         | 0              | Objects spawned immediately on start                 |
| `MaxSpawnCount`      | `int`                         | 1              | Maximum concurrent spawned objects                   |
| `UniqueSpawnables`   | `bool`                        | false          | At most one live instance of each entry              |
| `SpawnType`          | `ObjectSpawnType`             | Linear         | Selection strategy for choosing from the list        |
| `RandomRespawnTime`  | `bool`                        | true           | If true, randomizes between min/max respawn times; otherwise the maximum is used |
| `InitialRespawnTime` | `float`                       | 0              | Respawn delay for an object with no settings and no cadence of its own |
| `RespawnCheckIntervalMinimum` | `float`              | 3              | Shortest delay between this spawner's respawn checks |
| `RespawnCheckIntervalMaximum` | `float`              | 6              | Longest delay; each check re-rolls inside the range so spawners do not poll in lockstep |
| `RandomSpawnPosition`| `bool`                        | true           | If true, picks random position within bounding box   |
| `BoundingBoxSize`    | `Vector3`                     | (1, 1, 1)      | Size of the spawn area                               |
| `SphereRadius`       | `float`                       | 0.5            | SphereCast radius for ground detection               |
| `Spawnables`         | `List<SpawnableSettings>`     | —              | `[SerializeReference]` list of spawnable configurations |
| `OrConditions`       | `List<RespawnCondition>`      | —              | Any condition true → respawn allowed (logical OR)    |
| `TrueConditions`     | `List<RespawnCondition>`      | —              | All conditions must be true (logical AND)            |
| `PrewarmPool`        | `bool`                        | true           | Instantiate this spawner's prefabs into the pool at scene start |
| `PrewarmHeadroom`    | `int`                         | 1              | Extra pooled instances beyond `MaxSpawnCount`, covering corpses that have not yet decayed |
| `Pack`               | `NPCPackSettings`             | disabled       | Makes this spawner's NPCs one pack; see [NPC packs](#npc-packs) |

The baked `SpawnerDefinition` adds `Name`, `Position` and `Rotation` — where the spawner stood.

Turn `PrewarmPool` off only for spawners whose prefabs are large and rarely used, where paying the cost on demand is preferable to paying it always.

### SpawnableSettings

| Field               | Type             | Default | Description                                          |
|---------------------|------------------|---------|------------------------------------------------------|
| `NetworkObject`     | `NetworkObject`  | —       | The prefab to spawn                                  |
| `MinimumRespawnTime`| `float`          | 0       | Minimum respawn delay (seconds)                      |
| `MaximumRespawnTime`| `float`          | 0       | Maximum respawn delay (seconds)                      |
| `SpawnChance`       | `float [0–1]`    | 0.5     | Selection weight for weighted spawn mode             |
| `YOffset`           | `float`          | auto    | Vertical offset from ground, calculated from collider at bake time |

### NPCSpawnableSettings

Per-spawner overrides that let one NPC prefab serve a whole zone's worth of variants. Everything but the archetype is applied in `OnSpawned`, which runs after the object leaves the pool and before `ServerManager.Spawn` — that is, before `NPC.OnStartServer` rolls attributes and learns abilities, and before the spawn payload is written to clients. The archetype goes to `AIBrainHost.Prepare` once the NPC is in its scene.

| Field | Type | Default | Description |
|---|---|---|---|
| `AttributeBonusOverride` | `NPCAttributeDatabase` | — | Replaces the prefab's attribute database |
| `CorpseDecayDurationOverride` | `float` | 0 | Corpse decay seconds; 0 = prefab default |
| `EmptyCorpseDecayDurationOverride` | `float` | 0 | Empty-corpse decay seconds; 0 = prefab default |
| `LootTableOverride` | `LootTableTemplate` | — | Replaces the prefab's loot table |
| `ArchetypeOverride` | `AIArchetypeTemplate` | — | Replaces the prefab's whole AI brain; empty = the prefab's brain catalogue entry |
| `AdditionalAbilities` | `List<AbilityTemplate>` | — | Abilities granted on top of the prefab's list |
| `ReplacePrefabAbilities` | `bool` | false | Replace the prefab's ability list rather than extending it |
| `FactionOverride` | `RaceTemplate` | — | Replaces the prefab's race template / faction source |
| `MinimumScale` / `MaximumScale` | `float` | 1 / 1 | Random uniform scale range; 1..1 leaves the prefab scale alone |
| `PackRole` | `NPCGroupRole` | DPS | This entry's role in the spawner's pack; read only when the spawner's `Pack` is enabled |

Every override resolves to "this spawner's value, or the prefab's" — never to whatever a pooled instance carried from its last spawner.

The scale is deliberately uniform: a non-uniform scale would desynchronise the NavMeshAgent's radius and height from the collider.

### NPC packs

A spawner can make every NPC it produces one pack (an `NPCGroup`, server-only): they share a focus, join each other's fights and arrange themselves by a tactic while they orbit. The pack is authored in the spawn data and nowhere else — no scene component, no prefab change — and baked into the table with the rest of the spawner.

**One spawner, one pack.** The spawner founds the pack when its first NPC spawns and adds every NPC it spawns after that, each with its entry's `PackRole`. A member leaves on death, despawn and pool reset; when the last one leaves the pack is released, and the next spawn founds a new one. So a respawn rejoins the pack while any member of it still stands, and after a wipe the pack comes back as a new one. To place several packs, place several spawners — the pool is shared, so a second spawner of the same wolves costs no extra prefab bucket.

**`Pack` (`NPCPackSettings`), on the spawner:**

| Field | Default | Description |
|---|---|---|
| `Enabled` | false | The spawner's NPCs are one pack. Off, the spawner behaves exactly as it always did |
| `Tactic` | None | Surround, Flank, FocusFire or Kite: where members fighting the pack's focus stand while they orbit it (see the AI README, *Packs*) |
| `FocusTargeting` | true | The pack's focus follows whoever its living tank is fighting |
| `TacticOrbitRadius` | 5 | Metres from the focus at which an orbiting member takes its tactic slot |
| `KiteRotationSpeed` | 30 | Degrees per second the Kite ring turns |

**`PackRole`, on each NPC entry:** Tank (picks by threat; its target becomes the focus; holds a Flank's front), DPS and Support (take the focus when choosing a target), Healer (heals through a healer archetype, as it would anyway), None (keeps its own targeting but still answers alerts).

**Authoring a pack with an exact composition** — say one orc warrior tank, one orc mage and two orcs:

1. One `ObjectSpawner`, bounding box around the camp.
2. Four `NPCSpawnableSettings` entries: `an orc warrior` (`PackRole` Tank), `an orc mage` (DPS), `an orc` (DPS), `an orc` (DPS).
3. `SpawnType` Linear, `UniqueSpawnables` on, `InitialSpawnCount` and `MaxSpawnCount` 4. With unique entries a respawn refills the entry that died, so the pack comes back as authored rather than as whatever the selection rolled.
4. `Pack`: `Enabled` on, a `Tactic` (Flank suits a tank and damage dealers), `FocusTargeting` on.
5. Save the scene; the dashboard's Spawn Tables page shows the pack and each entry's role.

For a looser pack — "four wolves, one in five an alpha" — leave `UniqueSpawnables` off and use Weighted: every respawn rolls its own entry and joins the pack with that entry's role.

A tactic only moves members whose archetype has an Orbit variety state in its attacking state (the melee and archer archetypes do); the rest still share the focus and the alerts.

### ItemSpawnableSettings

| Field | Type | Default | Description |
|---|---|---|---|
| `ItemTemplate` | `BaseItemTemplate` | — | Item spawned when `RollTable` is empty |
| `MinimumAmount` / `MaximumAmount` | `int` | 1 / 1 | Stack size range |
| `RollTable` | `List<ItemRoll>` | — | Optional weighted table; when non-empty, one entry is rolled per spawn |
| `AchievementTemplateID` | `int` | 0 | Achievement granted on pickup; 0 = none |

Each `ItemRoll` carries its own template, stack range and weight. `OnValidate` repairs inverted stack ranges and negative weights, which would otherwise be a spawn-time exception on a live server rather than a bad item.

`OnSpawned` writes the rolled template and stack amount onto the spawned `WorldItem`, plus a
non-zero **generation seed** onto the `WorldItem` (two 16-bit draws from
`DeterministicRNG.Shared`, retried while zero). Zero is `Item.Initialize`'s sentinel for "derive a
seed from the database id", and a freshly granted item has no id — so a drop spawned without one
rolled its attributes from `RNG(0)`, identically for every drop of that template, and re-rolled
them differently after the next relog once a real row id existed.

### ObjectSpawnType Enum

```
ObjectSpawnType : byte
├── Linear   = 0   # Sequential cycling through the list
├── Random   = 1   # Uniform random selection
└── Weighted = 2   # Weighted random using SpawnChance values
```

## Usage Examples

### ISpawnable and ISpawnOwner

`ISpawnable` (Shared Core) is implemented by any entity a spawner produces. It knows its owner only as an `ISpawnOwner` — the spawner's settings for it are the spawner's bookkeeping.

| Member             | Type                | Description                                         |
|--------------------|---------------------|-----------------------------------------------------|
| `Spawner`          | `ISpawnOwner`       | The spawner that created this entity, or null       |
| `NetworkObject`    | `NetworkObject`     | FishNet network object for synchronization          |
| `ID`               | `long`              | Unique identifier for the spawned entity            |
| `Despawn()`        | `void`              | Hands the entity back to its spawner, or despawns it directly when it has none |

### Spawn Selection Logic (`GetSpawnIndex`)

| SpawnType  | Selection Logic                                                    |
|------------|--------------------------------------------------------------------|
| `Linear`   | Increments an index, wrapping at list end                          |
| `Random`   | Uniform random                                                     |
| `Weighted` | Cumulative weight: picks based on `SpawnChance` proportional odds  |

### Respawn Conditions

`RespawnCondition` is a `[Serializable]` class held by `[SerializeReference]`; subclasses implement `OnCheckCondition(SpawnerRuntime)`. A condition that names other scene objects resolves them at bake time through `Bake(authored, resolveSpawnerIndex)`: a baked table is loaded without its scene, so it may only refer to its siblings by index.

#### SpawnersClearedCondition

Allows respawn only when nothing the listed spawners produced is still alive:

- Authored as a list of `ObjectSpawner`s in the same scene; baked into `SpawnerIndices`.
- Walks each named sibling's live objects; any character whose damage controller is alive blocks the respawn.
- Non-character spawns never block, and an index outside the table is ignored.

Typical use: a camp that does not return while its guard stands.

### External Integration Points

- **NPC System** — NPCs implement `ISpawnable`; `NPC.ReturnToPool` hands a decayed corpse back to its spawner, or pools it through `PersistentPool` when it has none.
- **AI System** — `AIBrainHost.Prepare(npc, spawnPosition, archetypeOverride)` runs after the scene move and before the network spawn.
- **Pet System** — pets are spawned by `PetSystem`, not by spawners, and despawn directly.
- **Scene Server** — `SpawnerHost` follows FishNet's server-side `OnLoadEnd` / `OnUnloadEnd`, and starts a loaded scene one update later so the scene server's own handler (difficulty rules, orphan unloads) has always run first.
- **Build** — `CustomBuildTool` bakes the tables before the addressables step of both `RunBuild` (*Build Game*) and the addressables-only build, and refuses to build while a spawner would ship.

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| Nothing ships | `SpawnTableBakeTests.ABuiltWorldScene_ReferencesNoSpawnerDataAndNoServerScript` | Built world scenes reference no prefab only a spawner names, and no server-assembly script |
| The check is live | `SpawnTableBakeTests.TheDependencyCheck_WouldSeeSpawnerDataThatReachedTheBuild` | The same scene with its spawners untagged does report their prefabs |
| Tables up to date | `SpawnTableBakeTests.EverySceneWithSpawners_HasABakedTableThatMatchesIt` | Every scene's table matches its spawners |
| Tag enforced | `SpawnTableBakeTests.AnUntaggedSpawner_IsRetaggedBeforeItsSceneIsSaved` | Saving a scene retags an untagged spawner |
| Server group only | `SpawnTableBakeTests.ServerDataAssets_AreAddressableInTheServerGroupOnly` | Tables and catalogues are addressable only where clients never build |
| Every build bakes | `SpawnTableBakeTests.EveryBuildPath_BakesSpawnTablesBeforeItsAddressables` | Each addressables build in `CustomBuildTool` is preceded by a bake |
| Rebakes don't churn | `SpawnTableBakeTests.ABakedTable_KeepsItsReferenceIdsAcrossRebakes` | An unchanged rebake serializes identically |
| Initial spawn | Run a scene server with `InitialSpawnCount > 0` | Correct number of entities spawned once the scene loads |
| Max spawn cap | Verify `SpawnedCount` never exceeds `MaxSpawnCount` | Count stays at or below configured maximum |
| Weighted selection | Set `SpawnType = Weighted` with varied `SpawnChance` | Higher-chance entries spawn proportionally more |
| Respawn timer | Kill an NPC, wait out corpse decay and respawn delay | New entity spawns after configured delay |
| Conditions | `SpawnerSchedulerTests` | A refused respawn keeps its deadline and its place in the schedule |
| Stacked instances | Load two instances of one world scene | Each instance populates itself; unloading one leaves the other alone |
| Scheduler membership | Read `SpawnerHost.Scheduler.ActiveCount` with every spawner at its cap | Zero — a world at rest costs nothing per frame |
| Packs | `NPCPackTests`, `SpawnTableBakeTests.ToDefinition_CopiesThePackAndEachEntrysRole` | Roles and tactic reach the pack; a respawn rejoins it; a wipe founds a new one; a spawner without a pack joins nobody |

## Flow Diagram

### High-Level Overview

```mermaid
flowchart LR
    Author[ObjectSpawner in scene] -->|save / build| Bake[SceneSpawnTable]
    Bake --> Catalogue[SpawnTableCatalogue]
    Load[Scene load on server] --> Host[SpawnerHost]
    Catalogue --> Host
    Host --> Runtime[SpawnerRuntime per definition]
    Runtime --> Pool[Pool reservation]
    Runtime -->|on timer| Spawn[Pooled spawn + brain prepare]
    Spawn -->|death / pickup| Runtime
```

### Spawn Lifecycle

#### Start (`SpawnerRuntime.Populate`, from one update after the scene loads)

1. Reserves pool instances for every prefab the spawner can select.
2. Spawns `InitialSpawnCount` objects (clamped to `MaxSpawnCount`).
3. Creates respawn timers for the remaining slots, including any an initial spawn failed to fill.
4. Joins the scheduler if it has any timers — once every spawner in its scene has started.

`SpawnerHost` spreads a scene's starts over frames: each start spends what it made (prewarmed instances plus spawns, plus one) from `StartWorkPerFrame` (default 32), a token bucket that can go into debt and recovers every frame, so no scene waits forever. The whole scene joins the schedule only when all of its spawners have started, so a condition that names a sibling never sees it unstarted. `SpawnerHost.StartScene` still starts a scene all at once, for tests and tools.

#### Spawn (`SpawnObject`)

1. Selects a `SpawnableSettings` via `GetSpawnIndex()` (and a free entry when `UniqueSpawnables`).
2. If `RandomSpawnPosition`, sphere-casts down from a random point at the top of the box in the scene instance's physics scene and adds `YOffset`.
3. Retrieves a pooled instance via `GetPooledInstantiated()`.
4. Applies the settings (`OnSpawned`).
5. Moves the object to the scene instance.
6. For an NPC, prepares its brain through `AIBrainHost`.
7. Sets the object's `Spawner` to this runtime, spawns it, and records it with its settings.
8. For an NPC, when the spawner's `Pack` is enabled, adds its brain to the spawner's pack (founding one if none stands) with its entry's `PackRole`.
9. Clears respawn timers if `MaxSpawnCount` is reached.

Steps 4 to 8 complete or are rolled back in a `finally` (owner cleared, instance despawned or returned to the pool, and — for an instance never spawned, which raises no despawn — its prepared brain taken off the tick and out of any pack), so an exception never leaves a half-spawned object in the world untracked. The attempt returns a `SpawnOutcome`; a problem that stops it early (a misconfigured entry, an unloaded scene) is logged once per spawner and entry, not at every check.

#### Despawn

1. Removes the entity (and its settings record); a second despawn of the same object is ignored.
2. Schedules a respawn timer from the settings' cadence, or the NPC's own.
3. Clears the entity's `Spawner`.
4. Returns the object to the pool via `ServerManager.Despawn(DespawnType.Pool)` and keeps it out of the world scene (`PersistentPool.Despawn`). The despawn resets an NPC's brain, which leaves its pack if it had not already left at death.

#### Scene unload

The scene's runtimes leave the schedule and forget their bookkeeping; the objects still alive go with the scene, are taken out of the pool reservation, and any still pointing at a stopped runtime has its `Spawner` cleared. Each runtime's pack is dissolved.

### Respawn Loop (`TryRespawn`)

1. Skips if no spawnables or no pending timers.
2. Clears all timers if `SpawnedCount >= MaxSpawnCount`.
3. Returns immediately if no timer has elapsed.
4. Evaluates the respawn conditions **once** for the pass.
5. Consumes **every** elapsed timer, spawning one object each, stopping at `MaxSpawnCount` or when the frame's spawn budget runs out.

A refused respawn consumes no timer: the spawner keeps its work and is re-tested on its own interval, which is what lets a camp come back once the boss guarding it dies. Neither does an attempt that leaves the object owed (`SpawnerRuntime.ConsumesTimer`: a misconfigured entry, an unloaded scene, a spawn that threw and was rolled back): the deadline goes back and the pass ends, to be tried again at the next check. A timer is spent only when an object entered the world or there is no slot for one (the cap, or every unique entry alive).

### Who gets asked, and how often (`SpawnerScheduler`)

- **How often a spawner polls** is its own `RespawnCheckIntervalMinimum` / `Maximum`, re-rolled each pass. Respawn deadlines are absolute times on the scheduler's monotonic clock (`SpawnerScheduler.Now`, which reads `MonotonicClock`), so a longer interval does not shift them, and neither does the host's wall clock being set: `DateTime.UtcNow` deadlines used to fire every respawn at once on a forward step.
- **How much is spawned per frame** is `SpawnerScheduler.SpawnsPerFrame` (default 8), separate from the start budget so neither can hold the other up. A spawner the budget cuts short keeps the sweep's cursor and its open gate, so it finishes first next frame; the first spawner of every frame always has the whole budget, so none is starved.
- **A pass that throws** is caught per spawner and reported to that spawner's `RepeatingFaultLog`; the sweep carries on.
- **Which spawners are asked at all** is the scheduler's membership list. A spawner joins when it has something to respawn and leaves the moment it does not, so a spawner at its cap costs nothing.

`SpawnerSystem.OnUpdate` ticks the host once per frame. The list is *swept* rather than walked — each frame covers a `FramesPerSweep`-th (1/60) of it. The scheduler is an instance per host, so a simulation running beside a server does not walk the server's spawners.

## Project Structure

```
Spawner/                                   # Server/Implementation/World/SceneServer/Spawner
├── ObjectSpawner.cs              # Authoring component (EditorOnly); inert at runtime
├── SpawnerDefinition.cs          # One baked spawner
├── SceneSpawnTable.cs            # One scene's baked spawners
├── SpawnTableCatalogue.cs        # Every table, by scene name
├── SpawnerSystem.cs              # Scene server behaviour; ticks the host
├── SpawnerHost.cs                # Starts/stops runtimes per scene instance
├── SpawnerRuntime.cs             # The running spawner; ISpawnOwner
├── SpawnerScheduler.cs           # Active list of spawners with work
├── SpawnerPool.cs                # Pool pre-warming
├── ObjectSpawnType.cs
├── Settings/
│   ├── SpawnableSettings.cs
│   ├── ItemSpawnableSettings.cs
│   └── NPCSpawnableSettings.cs
└── Condition/
    ├── RespawnCondition.cs
    └── Types/
        └── SpawnersClearedCondition.cs
```

Related: `Shared/Core/Entity/Spawner/ISpawnable.cs`, `ISpawnOwner.cs`; the pack a spawner owns and its authored settings, `AI/Group/NPCGroup.cs` and `NPCPackSettings.cs`; editor tooling in `Shared/Implementation/Tools/Extensions/Unity/Editor/Spawner/` (`SpawnTableBaker`, `SpawnerSceneMigration`, `SpawnerBuildStripper`).

## License

This project is subject to the FishMMO project license.
