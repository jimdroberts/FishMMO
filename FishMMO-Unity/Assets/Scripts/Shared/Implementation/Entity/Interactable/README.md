# Interactable System

## Overview

The Interactable system is a server-authoritative, template-driven framework for interactive world objects in FishMMO. It provides an abstract `Interactable` base class (NetworkBehaviour + ISpawnable) that handles range checking, rate limiting, network payloads, scene-object registration, and UI title rendering. Fourteen concrete subclasses extend this base to implement specific gameplay interactions: banking, ability crafting, shrines, capture points, containers (chests), dialogue NPCs, dungeon entrances, gathering nodes, lore objects, mailboxes, merchants, switches, teleporters, and world items.

## Directory Structure

```
Interactable/
├── IInteractable.cs                    # Shared interface (ISceneObject + interaction API)
├── Interactable.cs                     # Abstract NetworkBehaviour base class (IInteractable, ISpawnable)
├── AbilityCrafter.cs                   # Ability crafting station interactable
├── Banker.cs                           # Banking access interactable
├── Bindstone.cs                        # Respawn bind-point interactable
├── DungeonEntrance.cs                  # Dungeon portal interactable
├── Teleporter.cs                       # Teleporter interactable (target Transform)
├── WorldItem.cs                        # Dropped item in the world (BaseItemTemplate + custom payload)
├── CapturePoint/
│   ├── CapturePoint.cs                 # PvP/PvE objective capture interactable
│   ├── CapturePointTemplate.cs         # ScriptableObject: PointValue, InteractionsToCapture
│   └── ObjectiveState.cs               # Enum: Neutral, Capturing, Captured, Contested
├── Container/
│   ├── Container.cs                    # Chest/crate interactable (IItemContainer)
│   └── ContainerTemplate.cs            # ScriptableObject: SlotCount, DespawnWhenEmpty
├── Dialogue/
│   ├── DialogueInteractable.cs         # NPC dialogue interactable
│   ├── DialogueNode.cs                 # Single node in a dialogue tree
│   ├── DialogueChoice.cs               # Choice within a dialogue node
│   └── Template/
│       └── DialogueTemplate.cs         # ScriptableObject: full dialogue tree with branching
├── EventData/
│   └── PlayerInteractionEventData.cs   # EventData subclass for interaction events
├── GatheringNode/
│   ├── GatheringNode.cs                # Harvestable resource node interactable
│   ├── GatheringNodeTemplate.cs        # ScriptableObject: Drops, MaxUses, GatherTimeSeconds
│   └── GatheringDrop.cs               # Drop entry: Item, MinAmount, MaxAmount, Weight
├── LoreObject/
│   ├── LoreObject.cs                   # Lore discovery interactable
│   └── LoreObjectTemplate.cs           # ScriptableObject: LoreText, GrantAbilities, GrantItems
├── Mailbox/
│   └── Mailbox.cs                      # Mail access interactable
├── Merchant/
│   ├── Merchant.cs                     # Buy/sell merchant interactable
│   ├── MerchantTabType.cs              # Enum: None, Ability, AbilityEvent, Item
│   └── Template/
│       └── MerchantTemplate.cs         # ScriptableObject: Abilities, AbilityEvents, Items
├── Shrine/
│   ├── Shrine.cs                       # Healing/buff shrine interactable
│   └── ShrineTemplate.cs              # ScriptableObject: heal amounts, buff reference
└── Switch/
    ├── Switch.cs                       # Toggle/trigger switch interactable
    └── ISwitchTarget.cs                # Interface for objects activated by switches
```

## Inheritance Hierarchies

### Interactable Types

```
NetworkBehaviour
└── Interactable (abstract) : IInteractable, ISpawnable
    ├── AbilityCrafter     : IAbilityCrafter
    ├── Banker             : IBanker
    ├── Bindstone          : IBindstone
    ├── CapturePoint       : ICapturePoint
    ├── Container          : IContainer, IItemContainer
    ├── DialogueInteractable : IDialogueInteractable
    ├── DungeonEntrance    : IDungeonEntrance
    ├── GatheringNode      : IGatheringNode
    ├── LoreObject         : ILoreObject
    ├── Mailbox            : IMailbox
    ├── Merchant           : IMerchant
    ├── Shrine             : IShrine
    ├── Switch             : ISwitch
    ├── Teleporter         : ITeleporter
    └── WorldItem          : IWorldItem
```

### Templates (ScriptableObjects)

```
CachedScriptableObject<T>
├── CapturePointTemplate
├── ContainerTemplate
├── DialogueTemplate
├── GatheringNodeTemplate
├── LoreObjectTemplate
├── MerchantTemplate
└── ShrineTemplate
```

### Enums

```
ObjectiveState : byte
├── Neutral    = 0
├── Capturing  = 1
├── Captured   = 2
└── Contested  = 3

MerchantTabType : byte
├── None         = 0
├── Ability      = 1
├── AbilityEvent = 2
└── Item         = 3
```

## Interactable Base Class

The abstract `Interactable` class (`NetworkBehaviour`) provides the shared foundation for all interactive world objects.

### Constants

| Constant              | Value | Description                                           |
|-----------------------|-------|-------------------------------------------------------|
| `INTERACT_RATE_LIMIT` | 60 ms | Default minimum time between consecutive interactions |

### Configuration

| Field              | Type    | Default | Description                                       |
|--------------------|---------|---------|---------------------------------------------------|
| `InteractionRange` | `float` | 3.5     | Maximum distance a player can interact from       |

### Virtual Properties

| Property         | Type     | Default                        | Description                           |
|------------------|----------|--------------------------------|---------------------------------------|
| `Name`           | `string` | `GameObject.name`              | Object name                           |
| `Title`          | `string` | `"Interactable"`               | UI display title                      |
| `TitleColor`     | `Color`  | `TinyColor.forestGreen`        | Title label color                     |
| `InteractRateLimit`| `double`| `INTERACT_RATE_LIMIT`          | Override per-type rate limit          |

### Interaction Flow

```
Player requests interaction
        │
        ▼
  CanInteract(IPlayerCharacter)
        │
        ├── NextInteractTime < UtcNow?  ──No──▶  Rejected
        │       │
        │      Yes
        │       ▼
        ├── InRange(transform)?  ──No──▶  Rejected
        │       │
        │      Yes
        │       ▼
        └── Set NextInteractTime = UtcNow + InteractRateLimit
                │
                ▼
           return true → Subclass handles interaction
```

Range checking uses `sqrMagnitude` for efficiency (no square root).

### Network Lifecycle

| Method            | Description                                                              |
|-------------------|--------------------------------------------------------------------------|
| `Awake()`         | Caches `Transform`, computes `interactionRangeSqr`. Client: strips "(Clone)" from name, renders title label. Server: calls `SceneObject.Register()`. |
| `OnDestroy()`     | Calls `SceneObject.Unregister()`.                                        |
| `ReadPayload()`   | Reads `ID` (Int64) from network reader, registers in scene.             |
| `WritePayload()`  | Writes `ID` (Int64) to network writer.                                  |
| `ResetState()`    | Clears `OnDespawn` event and `SpawnableSettings` (object pooling reset).|
| `Despawn()`       | Delegates to `ObjectSpawner.Despawn(this)`.                             |

### ISpawnable Members

| Member              | Type                | Description                                         |
|---------------------|---------------------|-----------------------------------------------------|
| `ObjectSpawner`     | `ObjectSpawner`     | The spawner managing this object                    |
| `SpawnableSettings` | `SpawnableSettings` | Spawn configuration from the spawner                |
| `ID`                | `long`              | Unique network identifier                           |
| `OnDespawn`         | `event Action<ISpawnable>` | Fired when the object is despawned            |

## Interactable Types

### AbilityCrafter

Opens the ability crafting UI. `[RequireComponent(typeof(SceneObjectNamer))]`.

### Banker

Opens the bank storage UI. `[RequireComponent(typeof(SceneObjectNamer))]`.

### Bindstone

Sets the player's respawn point to this location. Fires `IBindstone.OnBind`.

### CapturePoint

PvP/PvE objective that tracks ownership and capture progress.

| Field / Property       | Type               | Description                                  |
|------------------------|--------------------|----------------------------------------------|
| `Template`             | `CapturePointTemplate` | ScriptableObject with capture parameters |
| `AchievementTemplate`  | `AchievementTemplate`  | Achievement to increment on capture      |
| `OwnerCharacterID`     | `long`             | Current owner (0 = neutral)                  |
| `CaptureProgress`      | `int`              | Interactions toward capture                  |
| `CapturingCharacterID` | `long`             | Player currently capturing                   |
| `State`                | `ObjectiveState`   | Current objective state                      |

**Static Events** on `ICapturePoint`:
- `OnCaptured(ICapturePoint, IPlayerCharacter)` — fired when capture completes.
- `OnStateChanged(ICapturePoint, ObjectiveState)` — fired on state transitions.

### Container

Chest/crate that stores items. Implements `IItemContainer` for full slot management (add, remove, set, swap, clear).

| Field / Property       | Type                | Description                              |
|------------------------|---------------------|------------------------------------------|
| `Template`             | `ContainerTemplate` | ScriptableObject: `SlotCount`, `DespawnWhenEmpty` |
| `AchievementTemplate`  | `AchievementTemplate`  | Achievement to increment on open      |
| `Items`                | `List<Item>`        | Current item slots                       |

### DialogueInteractable

Starts a dialogue tree with the player. Fires `IDialogueInteractable.OnDialogueStarted`.

**DialogueTemplate** (ScriptableObject):
- `StartNodeId` — entry node in the tree.
- `CacheDialogueChoices` — server-side choice persistence to prevent replay abuse.
- `Nodes` — list of `DialogueNode` entries, each with `Text`, `Conditions`, `OnEnterActions`, `OnExitActions`, and `Choices`.
- `DialogueChoice` — each choice has `Text`, `NextNodeId`, `Conditions`, and `OnSelectActions`.

### DungeonEntrance

Portal to a dungeon scene. Achievement-integrated.

### GatheringNode

Harvestable resource (mining, herbalism, etc.) with limited uses and weighted drops.

| Field / Property       | Type                    | Description                            |
|------------------------|-------------------------|----------------------------------------|
| `Template`             | `GatheringNodeTemplate` | Drops list, MaxUses, GatherTimeSeconds |
| `RemainingUses`        | `int`                   | Remaining harvests before respawn      |

**GatheringDrop**: `Item` (BaseItemTemplate), `MinAmount`, `MaxAmount`, `Weight`.

### LoreObject

Discoverable lore that can grant abilities, ability events, or items on interaction.

| Field             | Type                  | Description                           |
|-------------------|-----------------------|---------------------------------------|
| `Template`        | `LoreObjectTemplate`  | LoreText, GrantAbilities, GrantAbilityEvents, GrantItems |

### Mailbox

Opens the mail UI. No template required.

### Merchant

Buy/sell interactable with tabbed inventory. `[RequireComponent(typeof(SceneObjectNamer))]`.

**MerchantTemplate** (ScriptableObject): lists of `AbilityTemplate`, `AbilityEvent`, and `BaseItemTemplate` references, organized by `MerchantTabType`.

### Shrine

Healing/buff station.

**ShrineTemplate** (ScriptableObject):

| Field              | Type                  | Description                        |
|--------------------|-----------------------|------------------------------------|
| `HealHealth`       | `bool`                | Whether to heal health             |
| `HealthHealPercent`| `float`               | Percentage of max HP to restore    |
| `HealMana`         | `bool`                | Whether to heal mana               |
| `ManaHealPercent`  | `float`               | Percentage of max MP to restore    |
| `Buff`             | `BuffTemplate`        | Optional buff to apply             |
| `BuffStackCount`   | `int`                 | Number of buff stacks to apply     |

### Switch

Toggle/trigger that activates an `ISwitchTarget`.

| Field       | Type             | Description                          |
|-------------|------------------|--------------------------------------|
| `Target`    | `ISwitchTarget`  | Object to activate/deactivate        |
| `IsToggle`  | `bool`           | If true, toggles; otherwise one-shot |

**ISwitchTarget** interface: `IsActivated` (bool), `Activate()`, `Deactivate()`.

### Teleporter

Moves the player to a target location. Has a `Target` Transform reference.

### WorldItem

Dropped item in the world with a `BaseItemTemplate` reference. Writes/reads custom item data via network payload.

## Common Patterns

- **SceneObjectNamer**: Required component on `AbilityCrafter`, `Banker`, `CapturePoint`, `Container`, `Merchant`, and others. Generates deterministic scene-unique names for network-safe identification.
- **AchievementTemplate**: Most interactable types expose an `AchievementTemplate` field to increment progress on interaction.
- **Title / TitleColor**: Every subclass overrides `Title` and `TitleColor` to customize the floating name label rendered via the `ICharacter.CharacterGuildLabel` on the client.
- **SceneObject Registration**: Server-side registration happens in `Awake()`; client-side registration happens in `ReadPayload()` after receiving the object's `ID` from the server.

## Related Files

```
Shared/Core/Entity/Interactable/                # 15 core interfaces (IAbilityCrafter, IBanker, etc.)
Shared/Implementation/Entity/Naming/             # SceneObjectNamer used by interactables
Shared/Implementation/Entity/Spawner/            # ObjectSpawner that spawns/despawns interactables
Server/Implementation/World/SceneServer/          # Server-side interaction handling systems
Client/UI/Controls/World/                         # Client-side UI panels for each interaction type
```