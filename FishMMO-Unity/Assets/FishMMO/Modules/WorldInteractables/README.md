# World Interactables module

**Short description:** The furniture of a world a player uses: containers, NPC dialogue, gathering nodes, lore objects,
shrines, capture points, switches and bindstones, with their ECA actions, their windows, and the server system that
answers dialogue choices and container takes. [Server/README.md](Server/README.md) documents dialogue and containers in
detail.

| | |
|---|---|
| **Module id** | `world-interactables` (`WorldInteractablesModule`) |
| **Assemblies** | `FishMMO.WorldInteractables`, `.Server`, `.Client`, `.Editor`, `.Tests` |
| **Depends on** | The core (the interactable base and its ECA triggers, the interaction request, items and the inventory grant, attributes, factions of the character); Buffs (shrines and other interactables apply buffs); Abilities (the lore panel names ability rewards; the grant itself goes through the core's `AbilityGrants`) |
| **Depended on by** | World Design (it generates gathering node content) |
| **Remove together with** | World Design |
| **Moved** | Out of the core, 2026-11-20 (Phase 5: the core is shrunk to login, world and scene) |

## Folders

```
Assets/FishMMO/Modules/WorldInteractables/
├── Runtime/   FishMMO.WorldInteractables          WorldInteractablesModule; Container, Dialogue, GatheringNode, LoreObject,
│                                                  Shrine, CapturePoint (each with its template), Switch (+ SwitchTargetMover,
│                                                  SwitchTargetObject), Bindstone; their contracts (Contracts/); their ECA
│                                                  actions (Actions/, each [MovedFrom] FishMMO.Shared); DialogueEventData;
│                                                  WorldInteractableBroadcasts
├── Server/    FishMMO.WorldInteractables.Server   WorldInteractableSystem (+ asset): dialogue sessions and choices, container takes
├── Client/    FishMMO.WorldInteractables.Client   UITKContainer, UITKNPCDialogue, UITKLore, UITKShrine, UITKGathering,
│                                                  UITKCapturePoint (each + prefab/uxml/uss); ClientInteractableStateSystem
├── Editor/    FishMMO.WorldInteractables.Editor   Dashboard categories; the Dialogue Tree Editor
├── Templates/                                     Containers (the dungeon chest's), Gathering (World Design's generated node
│                                                  templates), Dialogues, LoreObjects; Interactions (the Door, Bindstone,
│                                                  Gathering Node and Container triggers)
├── Prefabs/                                       Containers (Dungeon Chest), Gathering (generated node prefabs + materials)
├── Tests/     FishMMO.WorldInteractables.Tests    Wiring, the dungeon chest, spawn payload shaping
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: GameModule]` | `WorldInteractablesModule` ("world-interactables"). |
| ECA triggers on `Interactable` (shared core) | Each kind is an `Interactable`; its window opens, or its effect runs, from the actions on its triggers, which the core interaction path runs. |
| `IInteractableSystem` (server core) | Dialogue choices and container takes take the core's one interaction guard (`TryBeginInteraction`/`EndInteraction`), resolve object IDs with `TryResolveSceneObject`, validate scenes against `WorldSceneDetails`, and grant items with `SendNewItemBroadcast`. |
| `SceneServer_Systems` label | `WorldInteractableSystem.asset` is adopted by the scene server. |
| `ClientWorldGUI_Panels` label | The six windows' prefabs are placed into the world GUI. |
| `ClientServices` (client core) | `ClientInteractableStateSystem` applies switch and capture point state the server broadcasts. |
| `IDashboardCategoryProvider` (editor core) | The module's template categories in the Dashboard's Interactables group. |
| `ISwitch`, `ISwitchTarget` (shared core) | A switch drives any switch target; the doors the world builds implement the contract. |

## What stays in the core

The interaction itself: `Interactable`, `IInteractable`, its ECA triggers, `InteractableResolver`, the request and its
guard and scene-object validation (`InteractableSystem`), and `PlayerInteractionEventData`. The switch contract
(`ISwitch`, `ISwitchTarget` and its timed and clearance variants). Teleporters and portals, which are how scenes connect
(the scene details' teleporter cache, `TeleportAction`, portal activation). The interactables other modules own stay with
those modules' content (merchants, mailboxes, ability crafters, bankers, plots, dungeon entrances, arena boards, waypoints,
world items). The kinds' content (templates, prefabs and the triggers that name the module's actions) is the module's, in its
`Templates/` and `Prefabs/` folders; World Design's gathering generator writes there.

## Removing it

Delete `Assets/FishMMO/Modules/WorldInteractables` and `Assets/FishMMO/Modules/WorldDesign` (to check it, follow
[the gate](../../README.md#removing-a-module-the-gate)). Chests, NPC dialogue, gathering nodes, lore, shrines, capture
points, switches and bindstones placed in the world scenes then show as missing scripts and do nothing (the module's
own templates, prefabs and triggers go with its folder). Every other interaction (merchants, banks, mail, portals) works as before.
Persisted dialogue choices stay in their table, unused.

## License

This project is subject to the FishMMO project license.
