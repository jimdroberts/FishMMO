# Housing module

**Short description:** Player housing: plots bought and built on, rent and eviction, guest access, the house vault, and
the staff plot commands. [Server/README.md](Server/README.md) documents the housing system itself.

| | |
|---|---|
| **Module id** | `housing` (`HousingModule`) |
| **Assemblies** | `FishMMO.Housing`, `.Server`, `.Client`, `.Tests` |
| **Depends on** | The core (plots, items, currency) |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Out of the core |

## Folders

```
Assets/FishMMO/Modules/Housing/
├── Runtime/   FishMMO.Housing          HousingBroadcasts; rent prepay, tax bill and decision, vault fee, eviction,
│                                       placement; HousingModule
├── Server/    FishMMO.Housing.Server   HousingSystem (+ asset; access, building, panel, plots, staff, sync, tax and
│                                       vault partials), PlotSyncWindow, PlotTaxRouting, HousingOperatorCommands
├── Client/    FishMMO.Housing.Client   UITKHousingPlot, UITKHousingVault (+ prefabs, uxml, uss), HousingClient
├── Tests/     FishMMO.Housing.Tests    Rent, tax, eviction, placement, vault fee, sync and wiring tests
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `PlotFoundation.Registry` (core) | The deed posts' ECA actions raise its events; `HousingSystem` answers them. |
| `SceneServer_Systems` label | `HousingSystem.asset` is adopted by the scene server. |
| `ClientWorldGUI_Panels` label | `UIHousingPlot` and `UIHousingVault` prefabs are placed into the world GUI. |
| `[assembly: OperatorCommandProvider]` | `HousingOperatorCommands` adds `/gm plot` and `/admin plot` (hosted by the Staff module). |
| `BankWindowLinks` (core client) | "House vault" in the bank window, shown only while the vault holds something. |
| `[assembly: GameModule]` | `HousingModule` ("housing"). |

## What stays in the core

The plots are part of the world: the world generator lays them into the scenes, and the scenes keep them whether or not
housing is installed. So the core keeps `PlotFoundation` (and its visual and mesh), `IPlotFoundation`, the plot's state,
identity, owner, access and permission types, `ScenePlotManifest`, the plot tools, and the ECA actions
and the `Plot Interact` trigger on every deed post.

## Removing it

Delete `Assets/FishMMO/Modules/Housing` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
A deed post then does nothing when used. The plot, structure, access and vault tables and their services stay in place,
unused.

## License

This project is subject to the FishMMO project license.
