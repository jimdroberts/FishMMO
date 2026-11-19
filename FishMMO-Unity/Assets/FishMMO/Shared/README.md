# Shared

**Short description:** `FishMMO.Shared`, the code both peers run: characters and their controllers, client-side
prediction, abilities, buffs, items, NPCs and AI data, interactables and the ECA (event, condition, action) layer,
templates, broadcasts, world scene data, and the editor's FishMMO Dashboard.

Client-only and server-only code is kept out of it: the client's in `FishMMO.Client`, the servers' in `FishMMO.Server`.
`FishMMO.Shared` references `FishMMO.Core` and no module.

## Folders

| Folder | Holds |
|---|---|
| `Core/` | The contracts: `ICharacter`, `IPlayerCharacter`, `ICharacterBehaviour`, the controller interfaces (achievements, factions, guild, party, items, prediction, targets, waypoints, spawners), `IDamageable`/`IHealable`/`IKillable`, chat contracts, UI contracts. Modules implement several of them (`IFactionController`, `IPartyController`, `IGuildController`, `IWaypointController`). |
| `Implementation/Entity/` | `BaseCharacter`, `PlayerCharacter`, `CharacterPart` (the base of module controllers), and the features: `Prediction/` (movement, abilities, buffs, cooldowns, attributes, damage), `Item/`, `NPC/`, `Interactable/`, `ECA/`, `Achievement/`, `Relations/` (the harm rule), `Wildlife/`, `Map/`, `WorldSceneDetails/` (the baked scene data), `PointsOfInterest/`, `Waypoint/`, `Housing/` (plots, which belong to the world), and others. |
| `Implementation/Network/` | Broadcasts and serializers by feature, packet compression, the send schedule. |
| `Implementation/World/` | The world contracts every module reads: `Clock/` (`WorldClock`), `WorldTick`, `Weather/` (`WeatherQuery`, exposure), `Water/` (`WaterQuery`, `SurfaceWater`), `Biomes/` (habitat and affinity), `Climbing/`. |
| `Implementation/Bootstrap/` | Boot systems, Addressables load paths, `ServerPreloadLabels`, the web frame loop, logging. |
| `Implementation/CoreAdapter/` | `CoreWorldHost` and `CharacterLinks`: the bridge to `FishMMO.Core`. |
| `Implementation/Tools/` | Deterministic RNG, pools, serializable dictionaries, extension methods. `Tools/Extensions/Unity/Editor/` is the `FishMMO.Shared.Tools.Editor` assembly: the FishMMO Dashboard, the build pipeline (`CustomBuildTool/`), Addressables tools, the asset audit, and the world editor helpers the core keeps. |
| `Implementation/Constants.cs`, `HostConfig.cs` | Addressables labels, paths, and the generated host settings. |

## Editor assemblies

`FishMMO.Shared.Tools.Editor` (the dashboard and build), `FishMMO.Shared.Map.Editor`, `FishMMO.Shared.Housing.Editor`,
`FishMMO.Shared.WorldSceneDetails.Editor`, `FishMMO.Shared.SerializableDictionary.Editor`.

## Feature READMEs

- [Prediction](Implementation/Entity/Prediction/README.md): the predicted character, with
  [Abilities](Implementation/Entity/Prediction/Ability/README.md),
  [Cooldowns](Implementation/Entity/Prediction/Ability/Cooldown/README.md),
  [Buffs](Implementation/Entity/Prediction/Buff/README.md) and
  [Character attributes](Implementation/Entity/Prediction/CharacterAttribute/README.md).
- [Items](Implementation/Entity/Item/README.md), [Interactables](Implementation/Entity/Interactable/README.md),
  [Achievements](Implementation/Entity/Achievement/README.md), [Party contracts](Implementation/Entity/Party/README.md),
  [Targeting](Implementation/Entity/Target/README.md), [ECA targets](Implementation/Entity/ECA/Target/README.md),
  [Map](Implementation/Entity/Map/README.md).
- [Bootstrap](Implementation/Bootstrap/README.md), [Logging](Implementation/Bootstrap/Logging/README.md),
  [Addressable loading](Implementation/Tools/Extensions/Unity/AddressableLoadProcessor/README.md),
  [Serializable dictionary](Implementation/Tools/SerializableDictionary/README.md).

## License

This project is subject to the FishMMO project license.
