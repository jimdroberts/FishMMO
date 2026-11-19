# Core

**Short description:** `FishMMO.Core`, the engine-free runtime under the game: entities, component stores, systems run
on the network tick, stats and modifiers, effects, tags, signals, and the catalogue of installed modules.

The assembly has `noEngineReferences` set and an empty reference list: it uses nothing but the .NET base library, so it
compiles outside Unity. `FishMMO-Core/FishMMO.Core.csproj` (repository root) builds the same sources as a
netstandard2.1 library, and `FishMMO-Core.Tests` tests them (`dotnet test FishMMO-Core.Tests`). `CoreBoundaryTests`
fails if the assembly gains a reference or a Unity, FishNet or feature `using`.

## Folders

```
Core/Runtime/                FishMMO.Core
├── Entities/   Entity (index + generation), World (stores and resources), ComponentStore (packed sparse sets),
│               CommandBuffer (adds, removes, destroys after the tick), EventStream, EntityLink, Signals
├── Systems/    GameSystem, SystemPhase, TickContext, SystemScheduler
├── Data/       Stats (StatRegistry, StatPool, StatTable, modifiers), StatSystems (stat, lifetime, regeneration),
│               Tags, Effects (EffectKind, EffectQueue, EffectSystem, CoreEffects)
└── Modules/    GameModule, GameModuleAttribute, ModuleCatalog, ModuleBuilder, CoreModule ("core"), Archetypes,
                PersistenceRegistry, SystemTimings, LiveWorlds
```

## How the game uses it

- **Hosts.** `Shared/Implementation/CoreAdapter/CoreWorldHost.cs` makes one Core `World` per FishNet `NetworkManager`
  and side (server or client) on first use, installs every module the catalogue finds, and steps the scheduler on
  `TimeManager.OnTick`. `CharacterLinks` ties a character to its entity.
- **Modules.** `ModuleCatalog.Discover` reads `[assembly: GameModule]` from the loaded assemblies. `CoreModule` is
  always first, dependencies come before dependents, and a module with a missing dependency, a cycle or a duplicate id
  is left out with the reason logged. See [../README.md](../README.md#how-a-module-plugs-in) for the other seams.
- **Inspector.** The FishMMO Dashboard's Entity Inspector page shows each live world's modules, system timings,
  component counts, archetypes and entities.

Characters still run most of their simulation in their FishNet controllers (prediction replays them per character).
What runs as Core systems today: NPC corpse decay, achievements' accomplishment counting, and the components of the
chat, hotkeys and interaction modules.

## License

This project is subject to the FishMMO project license.
