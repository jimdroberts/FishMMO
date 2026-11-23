# Mounts module

**Short description:** Mounts (issue #109): the Mount Saddle equipment socket's saddles, the Mount ability that puts a player
on the mount its saddle names, how each mount moves (faster, leaping, climbing, flying), the mount drawn under its rider, the
Dismount effect, and the Stablemaster who sells them at a town's stables.

| | |
|---|---|
| **Module id** | `mounts` (`MountsModule`) |
| **Assemblies** | `FishMMO.Mounts`, `.Editor`, `.Tests` |
| **Depends on** | Buffs (a mount is a buff), Abilities (the Mount ability, the press override), Wildlife (the procedural bodies); the editor assembly also on Commerce, NPCs and World Design |
| **Depended on by** | Nothing |
| **Remove together with** | Nothing: without it the saddle socket stays empty and nobody sells mounts |
| **Added** | Issue #109 |

## How it works

- **A mount is a buff.** `MountBuffTemplate` (one asset per mount) carries the motion (`MountMotion`: ground speed, jump
  height, leap, steepest slope, flight) and the body. Riding = holding the buff; removing it dismounts. It lasts until
  removed (`BaseBuffTemplate.LastsUntilRemoved`: no expiry, saved with the character, so a rider logs back in mounted) and
  ends when its rider acts (`BreaksOnAction`).
- **Nothing on the wire.** The motor (`KCCController`) asks the core seam `ICharacterMount` (this module's `MountController`, a
  part on every player) for the active mount's motion at the start of every input and motor step. It reads the buff set, which
  is reconciled to the owner, so the server and the owner move the rider identically. A flying mount flies whenever it is
  off stable ground, which is motor state too. Observers see the mount through the buff list they already receive.
- **The saddle** (`SaddleTemplate`, socket `ItemSlot.MountSaddle = 10`) names the mount. The **Mount ability**'s
  `MountAction` applies the saddle's mount buff to its caster (predicted, through `ApplyBuffAction`); `CanMountCondition`
  refuses it with no saddle, in combat, swimming or climbing, dead, or indoors/underground. Pressed while mounted it
  dismounts at once instead of casting (`MountPressOverride`, through the Abilities module's `AbilityPressOverrides`).
- **Getting off**: casting anything (BreaksOnAction), pressing Mount again, clicking the buff off, swimming
  or taking hold of a ladder (the motor), the saddle coming off (equipment events), a damaging hit (`ICharacterDamageController.OnDamaged`),
  a roof or a cave overhead (`MountShelter`, checked by the server every 8 ticks), and death (buffs are cleared).
- **Flight costs stamina**: `FlightStaminaPerSecond` each second aloft, twice that sprinting; with none left the
  mount glides (`MountMotion.GlideSinkSpeed`, a share of its speed, no climbing) until it lands.
- **Drawing.** `MountController` builds the mount's procedural body (`WildlifeBodyMesh`, the wildlife material) under the
  rider's mesh root on every client, poses its legs (and a dragon's wings) from how fast the rider really moves, sits the rider
  astride (`CharacterAnimationController.SetRiding`: the `Ride` clip, hips raised to `MountRules.SaddleHeight`) and raises the
  name plate with it.

## The roster (MountContentBuilder: distinct traits, tune freely)

| Mount | Speed | Trait | Saddle price |
|---|---|---|---|
| Cow | +30% | the cheap starter | 50 |
| Llama | +40% | walks slopes up to 75° (60° on foot) | 120 |
| Horse | +70% | jumps 1.3× high | 250 |
| Giant Frog | +20% | jumps 2.5× high and leaps 6 m/s forward | 300 |
| Dragon | +40% | flies at 15 m/s (sprint ×2, capped at 18), 3 stamina/s, glides when spent | 2000 |

## Folders

```
Assets/FishMMO/Modules/Mounts/
├── Runtime/    FishMMO.Mounts          MountsModule (+ the part declaration and the static hooks), MountBuffTemplate,
│                                       SaddleTemplate, MountController, MountRules, MountShelter, MountPressOverride,
│                                       ECA/ (MountAction, DismountAction, CanMountCondition)
├── Editor/     FishMMO.Mounts.Editor   MountContentBuilder (Dashboard > Mounts > Build mounts; batch
│                                       -executeMethod FishMMO.Shared.MountContentBuilder.BuildBatch),
│                                       StablemasterSiteBuilder (a POI site builder)
├── Prefabs/    a stablemaster.prefab (the general merchant's recipe, the Stablemaster's stock)
├── Templates/  Mounts/, Saddles/, Abilities/Mount, Events/ (Mount Event, Dismount), Premade/Premade Mount, NPCs/Stablemaster
└── Tests/      FishMMO.Mounts.Tests    MountsModuleTests, MountContentTests
```

Core tests: `Tests/UnitTests/Prediction/MountMotionTests.cs` (flight and glide), `KCCGroundSpeedTests` (mount speed cases).

## Core seams it uses

| Seam | What the module does |
|---|---|
| `ICharacterMount` / `MountMotion` (core, `Shared/Core/Entity/Mount`) | `MountController` answers it; the motor reads it. |
| `ItemSlot.MountSaddle` (core) | The saddle socket; the character sheet draws it (`slot-saddle`). |
| `CharacterAnimationController.SetRiding` / `CharacterAnimationSet.Ride` (core) | The rider astride. |
| `AbilityPressOverrides` (Abilities) | Mount pressed while mounted dismounts. |
| `BaseBuffTemplate.LastsUntilRemoved` (Buffs) | A mount never runs out and is saved. |
| `PointOfInterestSiteBuilders` (World Design editor) | `StablemasterSiteBuilder` stands a Stablemaster outside the first stable door of every friendly site. |

## The Stablemaster

Every site that lays a **Stables** building (towns, villages, farmsteads, castles; not hostile sites) gets one Stablemaster
spawner 2.5 m out from its first stable door, taking the site's race. It sells the five saddles, the **Mount** premade and the
**Dismount** effect (crafted onto any attack at an Ability Crafter). Cut scenes get their Stablemaster at the next re-cut.
