# Archetype System

## Overview

The Archetype system is a data-driven, template-based framework for defining character archetypes in FishMMO. An archetype represents a character specialization or class, determining which abilities, items, buffs, titles, and attribute bonuses a character has access to. The system provides conditional requirement gating via `BaseCondition`, NPC guild association, FishNet network payload synchronization, and an event-driven change notification for dependent systems.

## Directory Structure

```
Archetype/
├── ArchetypeController.cs     # Per-entity controller (CharacterBehaviour)
├── IArchetypeController.cs    # Archetype controller interface
└── Template/
    └── ArchetypeTemplate.cs   # ScriptableObject blueprint for archetypes
```

### Related Files (Outside This Directory)

```
Shared/Network/Character/ArchetypeBroadcasts.cs   # FishNet broadcast structs for owner + observer archetype updates
Shared/Entity/BaseCharacter.cs                     # Client-side character cache (ClientCharacters) used for observer routing
```

## Inheritance Hierarchies

### Templates (ScriptableObjects)

```
CachedScriptableObject<ArchetypeTemplate>
└── ArchetypeTemplate : ICachedObject
```

### Controllers (NetworkBehaviour)

```
CharacterBehaviour
└── ArchetypeController : IArchetypeController
```

## ArchetypeTemplate

The `ArchetypeTemplate` is a `CachedScriptableObject` that defines a character archetype. Each template has a deterministic `ID` for network serialization and cache lookups via `ArchetypeTemplate.Get<ArchetypeTemplate>(id)`.

### Fields

| Field                   | Type                                | Description                                                  |
|-------------------------|-------------------------------------|--------------------------------------------------------------|
| `NPCGuild`              | `NPCGuildTemplate`                  | The NPC guild associated with this archetype, if any         |
| `Icon`                  | `Sprite`                            | Icon representing this archetype in the UI                   |
| `Description`           | `string`                            | Player-facing description of the archetype                   |
| `AttributeRewards`      | `List<CharacterAttributeTemplate>`  | Attribute templates rewarded for unlocking this archetype    |
| `AbilityRewards`        | `List<BaseAbilityTemplate>`         | Ability templates rewarded for unlocking this archetype      |
| `ItemRewards`           | `List<BaseItemTemplate>`            | Item templates rewarded for unlocking this archetype         |
| `BuffRewards`           | `List<BaseBuffTemplate>`            | Buff templates rewarded for unlocking this archetype         |
| `TitleRewards`          | `List<string>`                      | Title strings rewarded for unlocking this archetype          |
| `ArchetypeRequirements` | `BaseCondition`                     | Condition that must be met to unlock this archetype          |

### Properties

| Property | Type     | Description                                            |
|----------|----------|--------------------------------------------------------|
| `Name`   | `string` | The archetype name, derived from the ScriptableObject's asset name |

### Methods

| Method                                          | Returns | Description                                                     |
|-------------------------------------------------|---------|-----------------------------------------------------------------|
| `MeetsRequirements(IPlayerCharacter character)` | `bool`  | Evaluates `ArchetypeRequirements` for the given player. Returns `true` if requirements are met or if none are set. |

## ArchetypeController

The `ArchetypeController` is a `CharacterBehaviour` that manages the currently assigned archetype for a character. It handles network payload serialization, archetype assignment with change detection, and event invocation.

### Interface (IArchetypeController)

| Member                | Type / Signature                                    | Description                                                  |
|-----------------------|-----------------------------------------------------|--------------------------------------------------------------|
| `Template`            | `ArchetypeTemplate`                                 | The currently assigned archetype template (read-only)        |
| `OnArchetypeChanged`  | `event Action<ArchetypeTemplate, ArchetypeTemplate>` | Fired when archetype changes (old template, new template)    |
| `SetArchetype(int)`   | `void`                                              | Sets archetype by cached template ID                         |
| `SetArchetype(ArchetypeTemplate)` | `void`                                 | Sets archetype by direct template reference                  |

### Lifecycle Methods

| Method                                  | Description                                                              |
|-----------------------------------------|--------------------------------------------------------------------------|
| `ResetState(bool asServer)`             | Clears the `Template` reference to `null`                                |
| `ReadPayload(connection, reader)`       | Reads template ID (`int`), calls `SetArchetype(id)` if valid (>= 0)     |
| `WritePayload(connection, writer)`      | Writes template ID (`int`), or `-1` if no archetype is assigned          |

### Assignment Flow

1. `SetArchetype(int templateID)` is called with a cached template ID.
2. Looks up the template via `ArchetypeTemplate.Get<ArchetypeTemplate>(templateID)`.
3. If not found, logs a warning and returns.
4. Delegates to `SetArchetype(ArchetypeTemplate template)`.
5. Validates the template is non-null and different from the current assignment.
6. Stores the old template reference.
7. Assigns the new template.
8. Invokes `OnArchetypeChanged` with the old and new templates.

```
SetArchetype(templateID)
    │
    ├── Lookup via CachedScriptableObject → ArchetypeTemplate
    │
    └── SetArchetype(template)
            │
            ├── Null check → Log warning if null
            │
            ├── Same ID check → Skip if already assigned
            │
            └── Assign + Invoke OnArchetypeChanged(oldTemplate, newTemplate)
```

## Network Synchronization

### Payload Serialization (FishNet Reader/Writer)

| Direction | Data Written/Read     | Description                                           |
|-----------|-----------------------|-------------------------------------------------------|
| Write     | `int` (template ID)   | Writes the template's cached `ID`, or `-1` if null    |
| Read      | `int` (template ID)   | Reads the ID and calls `SetArchetype(id)` if >= 0     |

The archetype is synchronized as part of the character's initial payload when a client receives the character's networked state. FishNet calls `WritePayload` on the server and `ReadPayload` on the client automatically for each `NetworkBehaviour` on the character.

### Runtime Broadcast Synchronization

At runtime, archetype changes are synchronized with two broadcast paths:

| Broadcast                                  | Target            | Description |
|--------------------------------------------|-------------------|-------------|
| `ArchetypeUpdateBroadcast`                  | Owner connection  | Updates the local owner's archetype controller directly. |
| `CharacterObserverArchetypeUpdateBroadcast` | Observer clients  | Includes `CharacterID` so observers can route updates through `BaseCharacter.ClientCharacters` to the correct remote character controller. |

Observer routing avoids per-controller global fan-out by using the client-side character cache keyed by `ICharacter.ID`.

## Static Events

| Event (on `IArchetypeController`)  | Signature                                          | Description                                      |
|------------------------------------|----------------------------------------------------|--------------------------------------------------|
| `OnArchetypeChanged`               | `Action<ArchetypeTemplate, ArchetypeTemplate>`     | Fired when archetype changes (old, new)          |

The old template parameter may be `null` if no archetype was previously assigned (e.g., on initial assignment during character load).

## External Integration Points

The Archetype system is consumed by many other systems:

- **Ability System** — `BaseAbilityTemplate.RequiredArchetype` gates ability access by archetype. `AbilityEvent.RequiredArchetype` gates individual ability events. `IsArchetypeCondition` evaluates whether a character matches a required archetype.
- **NPC Guild System** — `NPCGuildTemplate.Archetypes` links guilds to their associated archetypes. Guild interactions may be filtered by archetype membership.
- **Condition System** — `BaseCondition` is used for `ArchetypeRequirements` gating, enabling data-driven unlock prerequisites.
- **Reward Systems** — `AttributeRewards`, `AbilityRewards`, `ItemRewards`, `BuffRewards`, and `TitleRewards` are granted when an archetype is unlocked.
- **UI System** — `Icon` and `Description` are displayed in archetype selection and information panels.
- **Constants** — `ArchetypeTemplate` is registered in the `CachedScriptableObject` type list for deterministic ID assignment.