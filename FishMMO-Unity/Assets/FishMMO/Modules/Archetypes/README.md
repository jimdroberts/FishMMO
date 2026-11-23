# Archetypes module

**Short description:** A player's archetype (class or specialisation): set, announced to observers, saved, and checked by
the archetype condition.

| | |
|---|---|
| **Module id** | `archetypes` (`ArchetypesModule`) |
| **Assemblies** | `FishMMO.Archetypes`, `.Server`, `.Tests` |
| **Depends on** | The core; Abilities (an archetype's abilities); Buffs (an archetype's buffs). `ArchetypeTemplate` is the module's |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Out of the core |

## Folders

```
Assets/FishMMO/Modules/Archetypes/
├── Runtime/   FishMMO.Archetypes          ArchetypeController (a CharacterPart), IArchetypeController, broadcasts,
│                                          ArchetypeEventData, IsArchetypeCondition, ArchetypesModule
├── Server/    FishMMO.Archetypes.Server   ArchetypePersistenceParticipant (load, and the versioned save through the
│                                          ownership gate)
├── Tests/     FishMMO.Archetypes.Tests    ArchetypesModuleTests
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: CharacterPart]` | `ArchetypeController` is added to every player at wake; its template ID rides the spawn payload (`archetypes.archetype`). |
| `[assembly: CharacterPersistence]` | `ArchetypePersistenceParticipant` over `character_archetypes`. |
| `[assembly: GameModule]` | `ArchetypesModule` ("archetypes"). |

## What stays in the core

`ArchetypeTemplate` (`Shared/Implementation/Entity/Archetype/Template`), because NPC guilds list archetypes
(`NPCGuildTemplate.Archetypes`). The AI's archetypes (`AIBrainCatalogue`) are a different thing. The template's fields: `NPCGuild`, `IconReference`, `Description`, `ArchetypeRequirements` (a `BaseCondition`; `MeetsRequirements`
evaluates it), and the reward lists `AttributeRewards`, `AbilityRewards`, `ItemRewards`, `BuffRewards`, `TitleRewards`.
**Nothing grants the reward lists yet.** An archetype grants what it grants through its change triggers
(`OnArchetypeChangeTriggers` on the controller).

## Removing it

Delete `Assets/FishMMO/Modules/Archetypes` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
A player then has no archetype and `IsArchetypeCondition` is unavailable to content. The `character_archetypes` table
stays in place, unused.

## How the controller works

**Setting it.** `SetArchetype(int templateID)` / `SetArchetype(ArchetypeTemplate)` assign it (a repeat of the current
ID is a no-op), raise `OnArchetypeChanged(old, new)` and run `Trigger.InvokeAll(onArchetypeChangeTriggers)`. Nothing in
the game calls it yet; it is the entry point for whatever assigns archetypes.

**Restoring it.** The spawn payload and the observer broadcast run on every peer for every character that comes into
range, and `Trigger.InvokeAll` has no authority gate, so those paths call `RestoreArchetype(int)` instead: it installs
the value and raises nothing. The load path calls `Restore(templateID, version)`: no events, no requirement check, not
dirty.

**On the wire.** The payload writes the template ID unpacked (template IDs are full-range 32-bit hashes, so the packed
form would spend five bytes), or `-1` for none. Changes go to the owner as `ArchetypeUpdateBroadcast` and to observers as
`CharacterObserverArchetypeUpdateBroadcast`, which carries the character ID and is routed through
`BaseCharacter.ClientCharacters`.

**Saving it.** `Version` and `PersistenceDirty` advance on change; `MarkPersisted(version)` clears the dirty mark only if
nothing changed since that snapshot, so a change during an in-flight write is not lost. `ResetState` clears the template,
version and dirty mark for pooling.

## License

This project is subject to the FishMMO project license.
