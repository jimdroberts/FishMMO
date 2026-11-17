# Housing module

**Short description:** Player housing: plots bought and built on, rent and eviction, guest access, the house vault, and
the staff plot commands.

Phase 3 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), moved 2026-11-17.
Server/README.md documents the housing system itself.

## Project Structure

```
Assets/Modules/Housing/
├── Runtime/   FishMMO.Housing          HousingBroadcasts; rent, tax, vault fee, eviction, placement, structures; HousingModule
├── Server/    FishMMO.Housing.Server   HousingSystem (+ asset, partials, queue data, contracts), PlotSyncWindow, PlotTaxRouting,
│                                       HousingOperatorCommands
├── Client/    FishMMO.Housing.Client   UITKHousingPlot, UITKHousingVault (+ prefabs, uxml, uss), HousingClient
├── Tests/     FishMMO.Housing.Tests    Rent, tax, eviction, placement, vault fee and wiring tests
└── README.md
```

## What stays in the core

The plots are part of the world: the world generator lays them into the scenes, and the scenes keep them whether or
not housing is installed. So the core keeps `PlotFoundation` (and its visual and mesh), `IPlotFoundation`, the plot's
state, identity, owner, access and permission types, `ScenePlotManifest`, the plot tools, and the ECA actions and the
`Plot Interact` trigger on every deed post. The actions only raise `PlotFoundation.Registry` events; this module's
housing system answers them. Without the module a deed post does nothing when used.

## How it plugs in

| Seam | Where | What the module does |
|---|---|---|
| Server system | `Server.AdoptModuleSystems` | `HousingSystem.asset` carries `SceneServer_Systems`. |
| Windows | `ModulePanelHost` (Client) | `UIHousingPlot` and `UIHousingVault` prefabs carry `ClientWorldGUI_Panels`. |
| Staff commands | `OperatorCommandProvider` (Server, new) | `HousingOperatorCommands` adds `/gm plot` and `/admin plot` (they were a `SceneServerSystem` partial). |
| Bank | `BankWindowLinks` (Client, new) | `HousingClient` adds "House vault", shown only while the vault holds something. |
| Module list | `[assembly: GameModule]` (Core) | `HousingModule` ("housing"). |

The database keeps its plot, structure, access and vault tables and services. With the module removed they stay in
place, unused.

## License

This project is subject to the FishMMO project license.
