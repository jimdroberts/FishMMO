# Editor

**Short description:** Editor-only content tools that belong to no module. Nothing here ships in a build. Each tool is
a button in the FishMMO Dashboard (`[DashboardTool]`), not a Unity menu item.

| Folder | Assembly | Holds |
|---|---|---|
| `GeneratedFiles/` | `FishMMO.GeneratedFiles.Editor` | `GeneratedFileRestorer` (recreates a missing `*.generated.cs` from its tracked template; never overwrites one) and `GeneratedFileDriftCheck` (reports values a template assigns that its generated file lacks). |
| `MockContent/` | `FishMMO.MockContent.Editor` | `MockAbilityGenerator`: the "Mock " abilities, ability events and buffs covering every ability shape combat and prediction support. Dashboard → Maintenance → Mock Content: generate, register as Addressables, delete. |
| `AbilityContent/` | `FishMMO.AbilityContent.Editor` | `TestAbilitySuiteGenerator`: the test ability suite (heals, areas, charged and movement abilities, blink, root, dispel). Dashboard → Maintenance → Ability Content. |
| `RenderScratch/` | `FishMMO.Render.Scratch` | Headless UI renders and probes of the core windows (panels, HUD overlap, chat, equipment, inventory capacity, nameplates, interface scaling) and the shared fixtures modules' renders build on. Dashboard → UI Tests. |

The editor tools the core's systems need (the dashboard itself, the build pipeline, Addressables tools, the asset
audit, the world helpers) are in `Shared/Implementation/Tools/Extensions/Unity/Editor` (`FishMMO.Shared.Tools.Editor`).
A module's own tools are in its `Editor/` folder.

## License

This project is subject to the FishMMO project license.
