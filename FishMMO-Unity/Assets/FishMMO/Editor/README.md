# Editor

**Short description:** Editor-only content tools that belong to no module. Nothing here ships in a build. Each tool is
a button in the FishMMO Dashboard (`[DashboardTool]`), not a Unity menu item.

| Folder | Assembly | Holds |
|---|---|---|
| `GeneratedFiles/` | `FishMMO.GeneratedFiles.Editor` | `GeneratedFileRestorer` (recreates a missing `*.generated.cs` from its tracked template; never overwrites one) and `GeneratedFileDriftCheck` (reports values a template assigns that its generated file lacks). |
| `AbilityContent/` | `FishMMO.AbilityContent.Editor` | `TestAbilitySuiteGenerator`: the test ability suite (heals, areas, charged and movement abilities, blink, root, dispel; stun, freeze, mesmerize, burn and rejuvenate over time, ricochet, fork, chain, block and deflect). Dashboard → Maintenance → Ability Content. It replaced the old "Mock " ability set and its generator, both deleted 2026-11-22. |
| `RenderScratch/` | `FishMMO.Render.Scratch` | Headless UI renders and probes of the core windows (panels, HUD overlap, chat, equipment, inventory capacity, nameplates, interface scaling) and the shared fixtures modules' renders build on. Dashboard → UI Tests. |

The editor tools the core's systems need (the dashboard itself, the build pipeline, Addressables tools, the asset
audit, the world helpers) are in `Shared/Implementation/Tools/Extensions/Unity/Editor` (`FishMMO.Shared.Tools.Editor`).
A module's own tools are in its `Editor/` folder.

## License

This project is subject to the FishMMO project license.
