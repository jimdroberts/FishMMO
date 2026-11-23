# Appearance module

**Short description:** How a player's body looks: race proportions and blend shapes, the body regions armour hides, and
the equipment drawn on the body.

| | |
|---|---|
| **Module id** | `appearance` (`AppearanceModule`) |
| **Assemblies** | `FishMMO.Appearance`, `.Tests` |
| **Depends on** | The core (the character and its model load, race templates, items and the equipment controller, the mesh-binding helpers) |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Out of the core (the core is shrunk to login, world and scene) |

## Folders

```
Assets/FishMMO/Modules/Appearance/
├── Runtime/   FishMMO.Appearance        AppearanceModule; CharacterAppearanceManager (+ ICharacterAppearanceManager,
│                                        CharacterAppearanceData), BodyVisibilityManager (+ IBodyVisibilityManager),
│                                        EquipmentVisualController (+ IEquipmentVisualController)
├── Tests/     FishMMO.Appearance.Tests  Body region discovery, equipment visual lifetime
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: GameModule]` | `AppearanceModule` ("appearance"). |
| `[assembly: CharacterPart]` | The three components are added to every player when it wakes, on every peer; the race prefabs no longer carry them. They draw on the client only. |
| `IModelReadyHandler` (shared core) | Each re-reads the skeleton and the body when the character's model has loaded. |
| `IEquipmentController` events (shared core) | `EquipmentVisualController` draws an item on the body when it is equipped and removes it when it is not. |
| `BodyRegion`, `BlendShapeProfile` (shared core) | Read from the equipped item templates: the regions an item hides and the blend shapes it sets. |

## What stays in the core

Loading the race model and the model-ready signal (`BaseCharacter`, `IModelReadyHandler`), the data item templates carry
(`BodyRegion`, `BlendShapeProfile`), the mesh-binding helpers (`EquipmentVisualAttach`, `SkeletonBinder`,
`SkeletonBones`; the character select preview dresses its model with them), and the carried light (torch and lantern).

## Removing it

Delete `Assets/FishMMO/Modules/Appearance` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
Players are then drawn as their race model is authored: no race proportions or blend shapes, no body regions hidden, and
no equipment on the body (equipping still works; it is not drawn). The character select preview still dresses its model.

## License

This project is subject to the FishMMO project license.
