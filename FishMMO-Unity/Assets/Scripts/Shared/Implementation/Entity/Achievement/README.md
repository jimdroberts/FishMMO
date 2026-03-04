# Achievement System

## Overview

The Achievement system is a data-driven, template-based framework for tracking player milestones in FishMMO. It supports multi-tier progression, categorized grouping, tier-based rewards (abilities, ability events, items, buffs, titles), FishNet network synchronization, and fire-and-forget database persistence. Achievements are incremented by gameplay systems (combat, healing, kills) and automatically advance through tiers when value thresholds are met.

## Directory Structure

```
Achievement/
├── Achievement.cs                 # Runtime achievement instance (tier, value, template ref)
├── AchievementCategory.cs         # Enum of achievement categories (Combat, Exploration, etc.)
├── AchievementController.cs       # Per-entity controller (CharacterBehaviour / NetworkBehaviour)
├── IAchievementController.cs      # Achievement controller interface + static events
└── Template/
    ├── AchievementTemplate.cs         # ScriptableObject blueprint (icon, category, tiers)
    ├── AchievementTemplateDatabase.cs # Name-to-template lookup database (ScriptableObject)
    └── AchievementTier.cs             # Serializable tier definition (value threshold, rewards)
```

### Related Files (Outside This Directory)

```
Shared/Implementation/Network/Character/AchievementBroadcasts.cs              # FishNet broadcast structs for achievement updates
Server/Implementation/World/SceneServer/Achievement/AchievementSystem.cs       # Server-side achievement tracking, rewards, and DB persistence
Shared/Implementation/Entity/CharacterAttribute/CharacterDamageController.cs   # Calls Increment() for damage, kill, and heal achievements
Client/Client.cs                                                               # Client-side OnCompleteAchievement handler
Client/UI/Controls/World/Achievement/UIAchievements.cs                         # Achievement UI panel
```

## Inheritance Hierarchies

### Runtime Instances

```
Achievement                        # Standalone class (no inheritance)
```

### Templates (ScriptableObjects)

```
CachedScriptableObject<AchievementTemplate>
└── AchievementTemplate            # Icon, Category, Description, List<AchievementTier>
```

### Controllers (NetworkBehaviour)

```
CharacterBehaviour
└── AchievementController : IAchievementController
```

### Configuration Types

```
AchievementTier                    # [Serializable] class: Value threshold + reward lists
AchievementCategory                # Enum: 19 categories (Ability, Character, Combat, etc.)
```

## Achievement Lifecycle

### 1. Increment

Gameplay systems call `AchievementController.Increment(template, amount)`:

```
Increment(template, amount)
  ├── Achievement exists?
  │   └── No  → Create Achievement(template.ID) with Tier=0, Value=0
  ├── CurrentValue += amount
  ├── For each tier from CurrentTier onward:
  │   └── CurrentValue >= tier.Value?
  │       └── Yes → Fire OnCompleteAchievement(Character, Template, Tier)
  │                → CurrentTier = i + 1
  │       └── No  → break
  └── Fire OnUpdateAchievement(Character, Achievement)
```

**Key behavior**: Multiple tiers can be completed in a single `Increment` call if the amount is large enough to cross multiple thresholds.

### 2. Set (Direct)

`SetAchievement(templateID, tier, value)` directly sets tier and value. Used by:
- Client broadcast handler (receiving server state)
- Network payload deserialization
- Server-side state restoration from database

### 3. Server Processing

`AchievementSystem` (server) subscribes to both static events:

| Event | Server Handler |
|-------|---------------|
| `OnUpdateAchievement` | Broadcasts `AchievementUpdateBroadcast` to the player client |
| `OnCompleteAchievement` | Processes tier rewards (abilities, ability events, items) |

### 4. Reward Processing

When a tier is completed, `AchievementSystem.IAchievementController_HandleAchievementRewards` processes:

| Reward Type | Handler | Persistence |
|-------------|---------|-------------|
| `AbilityRewards` | `HandleAbilityRewards` — Learns ability, broadcasts to client | Fire-and-forget async via `ICharacterKnownAbilityService` |
| `AbilityEventRewards` | `HandleAbilityEventRewards` — Learns ability event, broadcasts to client | Fire-and-forget async via `ICharacterKnownAbilityService` |
| `ItemRewards` | `HandleItemRewards` — Adds to inventory (preferred) or bank (fallback), broadcasts to client | Fire-and-forget async via `ICharacterInventoryService` or `ICharacterBankService` |
| `BuffRewards` | Not yet implemented | — |
| `TitleRewards` | Not yet implemented | — |

Item rewards attempt inventory first. If the inventory lacks sufficient free slots, the system falls back to the bank. If neither has space, items are silently dropped.

## Tier Model

Each `AchievementTemplate` contains a `List<AchievementTier>`. Tiers are ordered by ascending `Value` thresholds and represent progressive milestones:

| Property | Type | Description |
|----------|------|-------------|
| `Value` | `uint` | Cumulative value required to complete this tier |
| `TierCompleteMessage` | `string` | Message shown to the player on completion |
| `CompleteSound` | `AudioClip` | Sound played on completion |
| `AbilityRewards` | `List<BaseAbilityTemplate>` | Abilities granted on completion |
| `AbilityEventRewards` | `List<AbilityEvent>` | Ability events granted on completion |
| `ItemRewards` | `List<BaseItemTemplate>` | Items granted on completion |
| `BuffRewards` | `List<BaseBuffTemplate>` | Buffs applied on completion |
| `TitleRewards` | `List<string>` | Titles granted on completion |

### Example Progression

```
Template: "Monster Slayer"
  Tier 0: Value=10   → "Novice Slayer"     (reward: 5 gold)
  Tier 1: Value=100  → "Veteran Slayer"    (reward: sword + title)
  Tier 2: Value=1000 → "Master Slayer"     (reward: rare mount + buff)
```

A player with `CurrentTier=1, CurrentValue=85` needs 15 more to reach Tier 1's threshold of 100. `NextTierValue` returns `100`.

## Achievement Categories

The `AchievementCategory` enum provides 19 categories for organizing achievements:

| Category | Description |
|----------|-------------|
| `Ability` | Learning or using abilities |
| `Character` | Character progression (leveling, stats) |
| `Combat` | Defeating enemies or bosses |
| `Crafting` | Crafting items, equipment, or consumables |
| `Dungeon` | Completing dungeons or dungeon objectives |
| `Environment` | Weather, biomes, or world events |
| `Events` | Special or limited-time events |
| `Exploration` | Discovering locations or uncovering secrets |
| `Gathering` | Mining, fishing, or harvesting |
| `Guild` | Guild creation, joining, or contributions |
| `Housing` | Building, decorating, or owning housing |
| `Lore` | Discovering or interacting with game lore |
| `Mastery` | Mastering skills, professions, or systems |
| `Miscellaneous` | Achievements that don't fit other categories |
| `Pets` | Collecting, training, or battling with pets |
| `PvP` | Player-vs-player activities |
| `Seasonal` | Seasonal or holiday events |
| `Social` | Social interactions (friends, parties) |
| `Survival` | Staying alive or overcoming hazards |
| `Trading` | Trading, bartering, or marketplace |
| `World` | Server-wide goals or global events |

## Template Properties

`AchievementTemplate` exposes the following configurable fields:

| Property | Type | Description |
|----------|------|-------------|
| `Icon` | `Sprite` | UI icon for the achievement |
| `Category` | `AchievementCategory` | Category for grouping and filtering |
| `Description` | `string` | Player-facing description text |
| `Tiers` | `List<AchievementTier>` | Ordered list of tier milestones and rewards |
| `Name` | `string` | Read-only, from ScriptableObject name |

## Network Synchronization

### Client Broadcast Receivers

| Broadcast | Purpose |
|-----------|---------|
| `AchievementUpdateBroadcast` | Server tells client to update a single achievement (templateID, value, tier) |
| `AchievementUpdateMultipleBroadcast` | Server tells client to update multiple achievements at once |

Client broadcast handlers call `SetAchievement(templateID, tier, value)` to apply server-authoritative state.

## Static Events

All events are defined on `IAchievementController`:

| Event | Signature | When Fired |
|-------|-----------|------------|
| `OnCompleteAchievement` | `Action<ICharacter, AchievementTemplate, AchievementTier>` | When a tier threshold is reached during `Increment` |
| `OnUpdateAchievement` | `Action<ICharacter, Achievement>` | After any `Increment` call or `SetAchievement` (unless `skipEvent=true`) |

## Increment Sources

The achievement system is incremented by gameplay systems:

| Source | Achievement | Amount |
|--------|------------|--------|
| `CharacterDamageController.Damage()` | `DamageAchievementTemplate` (attacker) | Damage dealt |
| `CharacterDamageController.Damage()` | `DamagedAchievementTemplate` (defender) | Damage received |
| `CharacterDamageController.Kill()` | `KillAchievementTemplate` (killer) | 1 per kill |
| `CharacterDamageController.Kill()` | `KilledAchievementTemplate` (victim) | 1 per death |
| `CharacterDamageController.Heal()` | `HealAchievementTemplate` (healer) | Amount healed |
| `CharacterDamageController.Heal()` | `HealedAchievementTemplate` (target) | Amount healed |

Additional increment sources can be added by any system with access to `IAchievementController`.

## External Integration Points

The achievement system is consumed by and interacts with:

- **CharacterDamageController** — Increments damage, kill, and heal achievements during combat.
- **Ability System** — Achievement rewards can grant new abilities and ability events.
- **Item System** — Achievement rewards can grant items to inventory or bank.
- **Buff System** — Achievement tiers can define buff rewards (not yet handled server-side).
- **Title System** — Achievement tiers can define title rewards (not yet handled server-side).
- **Database Layer** — Achievements are persisted and restored via `CharacterAchievementData` DTO. Reward persistence (abilities, items) uses fire-and-forget async via `ICharacterKnownAbilityService`, `ICharacterInventoryService`, and `ICharacterBankService`.
- **UI** — `UIAchievements` panel subscribes to `OnUpdateAchievement` for real-time progress display.
- **Client** — `Client.cs` subscribes to `OnCompleteAchievement` for client-side completion effects (messages, sounds).

## Notes

- **Buff and Title rewards** are defined on `AchievementTier` but not yet processed by `AchievementSystem`. These reward types will need dedicated handlers when the title system is implemented and buff reward application is designed.
- **Item overflow** — If neither inventory nor bank has sufficient free slots for item rewards, the items are silently dropped. Consider adding a mail or overflow system.