# Buffs module

**Short description:** Buffs and debuffs: the buff controller and its predicted ticking, every buff template kind
(attribute, attribute tick, resource tick, state, composite, damage negation, deflect), the buff ECA actions and condition
(apply, dispel, region buff, has buff), buff volumes, what a defender's buffs do to a hit (shields, deflection, guard
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

Further detail: [Buff README](Runtime/Buff/README.md).
