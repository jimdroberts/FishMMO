# Buffs module

**Short description:** Buffs and debuffs: the buff controller and its predicted ticking, every buff template kind
(attribute, attribute tick, resource tick, state, composite, damage negation, deflect, charm), the buff ECA actions and
conditions (apply, dispel, region buff, has buff, can be charmed), buff volumes, what a defender's buffs do to a hit (shields, deflection, guard
blocks), and the client's buff and debuff strips.

| | |
|---|---|
| **Module id** | `buffs` (`BuffsModule`) |
| **Assemblies** | `FishMMO.Buffs`, `.Client`, `.Editor`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core only |
| **Depended on by** | Abilities, Achievements, Archetypes, NPCs & AI, Pets, Social (the party frame's buffs), World Interactables (shrines); and through them every module that depends on those |
| **Remove together with** | Every module above |
| **Moved** | Out of the core, 2026-11-20 (Phase 5) |

## Folders

```
Assets/FishMMO/Modules/Buffs/
├── Runtime/              FishMMO.Buffs          BuffsModule; Contracts/IBuffController (the full controller contract and its
│                                                static events); Buff/ (BuffController and BuffController.Core, Buff,
│                                                BuffTooltip, BuffDamageMitigation, Template/ (BaseBuffTemplate and the
│                                                seven kinds, BuffTickEvent)); ECA/ (ApplyBuffAction, ApplyDispelAction,
│                                                ApplyRegionBuffAction, HasBuffCondition, BuffEventData); Region/
│                                                (BuffVolume, BuffVolumeController)
├── Client/               FishMMO.Buffs.Client   UITKBuffContainer, UITKBuff, UITKDebuff (+ UIBuff/UIDebuff prefabs, uxml, uss)
├── Editor/               FishMMO.Buffs.Editor   The dashboard's Buffs category; RenderScratch/ (the strips in the HUD probes,
│                                                the buff tooltip page, the buff-strip probe, the render fixtures)
├── Templates/            The buff assets (Weather/, Mock/, and the shipped buffs)
└── Tests/                FishMMO.Buffs.Tests
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: GameModule]` | `BuffsModule` ("buffs"). |
| `ICharacterBuffs` (core) | The controller implements what the core does with buffs: weather exposure applies, removes and asks after its buffs; the target frame reads the visible strip (`CaptureObserved`, `OnObservedBuffsChanged`); the server honours a player's dismiss; Combat clears buffs on a death and asks whether this peer spends shield charges. `IBuffController` (the module's) extends it. |
| `IBuffPersistence` (core) | The character save writes and reads buffs (a player's and each pet's) through it, under the character's claims and version; permanent, environmental buffs are left out. |
| `IBuffTemplateInfo` (core) | `BaseBuffTemplate` implements it: duration, debuff, dismissable, permanent, icon. Weather exposure holds its buffs as `CachedScriptableObject` with `[TemplateOf("BaseBuffTemplate")]`. |
| `DamageMitigation` (core) | `BuffDamageMitigation` installs the negation, deflection, guard-volume and shield-volume hooks that Combat and Abilities call. Without the module nothing is mitigated. |
| `ClientWorldGUI_Panels` label | `UIBuff.prefab` and `UIDebuff.prefab` are placed into the world GUI. |
| Dashboard providers | `BuffDashboardCategories` adds Buffs to the Character group. |

## What stays in the core

The wire shapes: `BuffReconcileEntry` (in the owner's reconcile payload), `ObservedBuffEntry` and `CharacterBuffsBroadcast`
(what observers see), `DismissBuffBroadcast` and its handler; `ShieldVolume`; `ModifierSource.Buff` (an attribute
modifier's source kind); the contracts above; and `CharacterEvade.RefusesBuff`.

## Crowd control and charm (2026-11-22)

- **Stun and freeze** are `StateBuffTemplate`s (`IsStunned`, `IsFrozen`): `CharacterIncapacitation` stops a player's input,
  casting and item use, and since 2026-11-22 an NPC's walking and turning too (`AIController.StepAgent`). A freeze's effect
  carries `FXFreezePose`, which holds the body's animation still on every client that shows the ice.
- **Charm** (`CharmBuffTemplate`, the Mesmerize ability) hands a charmable NPC's server brain to the buff's caster through the
  core seam `INPCCharm` (the NPCs module's `AIController.Charm`): `NPC.CharmedBy` makes the harm rule, kill credit and threat
  see it as the caster's; it follows, fights what the caster fights and what strikes either, and is itself again when the
  buff ends. `CanBeCharmedCondition` keeps players, pets and wildlife out. It sets no state flag: `IsMesmerized` still means
  "cannot act". `BuffController.Apply` now sets a new buff's caster before `OnApply` runs, so a template can read it there.

- **Invisibility** (`CharacterFlags.IsInvisible`, a `StateBuffTemplate` with `BreaksOnAction`): NPC enemy sweeps skip
  the character unless the NPC has `SeesInvisible`; the buff's effect carries `FXConcealment`, which draws the model at
  20% and hides the nameplate (`Nameplate.ConcealCount`) on every client. **See Invisible** (`SeesInvisible`, effect
  `FXSeeInvisible`) makes the local client (`ConcealmentView`) draw other invisible characters at 55% with their
  nameplates. `/gm invis` is observer culling plus `SetFaded`, and nothing here reveals it.
- **Permanent pet effects**: a permanent buff is never saved, so `PetSystem` re-runs the owner's learned summon spell's
  summon effects when it restores a pet at login (Pet Regeneration: +10 Health Regeneration = 2/s).

Further detail: [Buff README](Runtime/Buff/README.md).
