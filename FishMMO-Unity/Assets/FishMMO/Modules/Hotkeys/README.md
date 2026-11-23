# Hotkeys module

**Short description:** The hotkey bar: a player's bound abilities and items in numbered slots, the server's authority over
the bindings and their persistence, and the bar on the HUD with its keys, stacked or paged bars, and drag-and-drop.
[Server/README.md](Server/README.md) documents the hotkey system in detail.

| | |
|---|---|
| **Module id** | `hotkeys` (`HotkeyModule`) |
| **Assemblies** | `FishMMO.Hotkeys`, `.Server`, `.Client`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core (items the bar names, the character persistence participants, the player controls); Abilities (abilities the bar names,) |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Out of the core (the core is shrunk to login, world and scene) |

## Folders

```
Assets/FishMMO/Modules/Hotkeys/
├── Runtime/              FishMMO.Hotkeys          HotkeyModule and the HotkeyBar component (the server's bar on the player's
│                                                  entity), HotkeyData, HotkeySetBroadcast / HotkeySetMultipleBroadcast
├── Server/               FishMMO.Hotkeys.Server   HotkeySystem (+ asset): set requests, the login prune and send, the forget
│                                                  cleanup, the write pump; HotkeySystemRuntimeData; HotkeyPersistenceParticipant
├── Client/               FishMMO.Hotkeys.Client   UITKHotkeyBar (+ UIHotkeyBar.prefab/uxml/uss), HotkeyBarLayout
│                                                  (stacked/paged bars, the press router), HotkeyKeyMap (the bar actions)
├── Editor/RenderScratch/ FishMMO.Hotkeys.RenderScratch   The bar in the HUD renders and the HUD overlap probe
├── Tests/                FishMMO.Hotkeys.Tests    The bar, the handlers, persistence, wiring; bar layout, stacking, lift
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: GameModule]` | `HotkeyModule` ("hotkeys") adds the `HotkeyBar` component to the server's player archetype. |
| `[assembly: CharacterPersistence]` | `HotkeyPersistenceParticipant` loads the bar with the character, and at a departure takes the live bar (`CaptureDeparture`) so it is written inside the save-and-release, on each character's lane (`SavesPerCharacter`). A resident's changes are written by the hotkey system's own pump. |
| `IAbilitySystem.OnAbilityForgotten` (server core) | `HotkeySystem` clears the bindings that name a forgotten ability and echoes the bar. |
| `SceneServer_Systems` label | `HotkeySystem.asset` is adopted by the scene server. |
| `ClientWorldGUI_Panels` label | `UIHotkeyBar.prefab` is placed into the world GUI. |
| `ModuleInputActions` (client core) | `HotkeyKeyMap` adds the bar's key actions to the player's controls and names them in the key-binding options. |

## What stays in the core

The hotkey constants (`Constants.Configuration`: bar count, slots per bar), the `character_hotkeys` table and
`ICharacterHotkeyService`, the `Hotkey` value of the drag object's `ReferenceButtonType`, the HUD layout helpers the bar
insets the HUD with (`UITKHudLayout`), and the `PlayerControls` input asset.

## Removing it

Delete `Assets/FishMMO/Modules/Hotkeys` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
Players then have no hotkey bar, and the bar is the only place abilities are cast and items used from: the abilities
window and the inventory have no use action of their own. A game without the module needs another way to cast (its own
module). The bar's key bindings disappear from the options. The `character_hotkeys` table and its rows stay in place,
unused.

When abilities and items leave the core (they are planned as modules), this module will depend on them.

## License

This project is subject to the FishMMO project license.
