# CharacterAttribute System

**Short description:** A data-driven, template-based attribute framework for FishMMO characters providing hierarchical attribute relationships with formula-driven modifiers, depletable resource management, damage/resistance calculations, tick-based regeneration, and FishNet network synchronization.

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Installation / Build](#installation--build)
- [Quick Start Guide](#quick-start-guide)
- [Configuration](#configuration)
- [Usage Examples](#usage-examples)
- [Operational Checks](#operational-checks)
- [Flow Diagram](#flow-diagram)
- [Project Structure](#project-structure)
- [License](#license)

## Overview

The CharacterAttribute system is a data-driven, template-based attribute framework for FishMMO characters. It provides hierarchical attribute relationships with formula-driven modifiers, depletable resource management (Health, Mana, Stamina), damage/resistance calculations, tick-based regeneration, and FishNet network synchronization.

## Supported Platforms

| Platform | Status | Notes |
|----------|--------|-------|
| Windows  | ✅ Supported | Primary development platform |
| Linux    | ✅ Supported | Server and client builds |
| WebGL    | ✅ Supported | Via Unity WebGL export |

Built with **Unity 6.3 LTS** using **IL2CPP** scripting backend.

## Features

- Hierarchical attribute relationships with parent, child, and dependency links
- Formula-driven modifiers (flat bonus, percentage bonus) via ScriptableObject templates
- Depletable resource attributes (Health, Mana, Stamina) with current/max tracking
- Damage and resistance calculations with linked damage/resistance template pairs
- Tick-based regeneration (5-second intervals) for resource attributes
- External modifier accumulation from items, buffs, and region effects — preserved across formula recalculations
- FishNet network synchronization with owner and observer broadcast paths
- Replicate/Reconcile prediction support via `CharacterAttributeResourceState` snapshots
- Static events for damage, kill, and heal for cross-system integration
- Immortality flag to prevent all damage and kill processing

## Prerequisites

- Unity 6.3 LTS
- FishNetworking (FishNet)
- FishMMO Shared Core

## Installation / Build

This system is an integrated module of the FishMMO Unity project. No separate installation is required.

## Quick Start Guide

1. **Define templates** — Create `CharacterAttributeTemplate` ScriptableObjects for each attribute (e.g., Strength, Health). Configure base values, min/max, clamp settings, and parent/child/dependency relationships.
2. **Add damage/resistance pairs** — Use `DamageAttributeTemplate` for damage types and link each to a `ResistanceAttributeTemplate`.
3. **Assign formulas** — In each template's `Formulas` dictionary, map child templates to `CharacterAttributeFormulaTemplate` assets (`FlatBonusFormulaTemplate` or `PercentageBonusFormulaTemplate`).
4. **Attach controllers** — Add `CharacterAttributeController` and `CharacterDamageController` NetworkBehaviour components to your character prefab.
5. **Runtime wiring** — On initialization the controller calls `AddDependents()` which resolves all parent, child, and dependency links between attribute instances.
6. **External modifiers** — Call `AddModifier(amount)` on any attribute to apply item, buff, or region bonuses. These persist across formula recalculations.

## Configuration

### Template Properties

Each `CharacterAttributeTemplate` ScriptableObject exposes:

| Property | Description |
|----------|-------------|
| `Value` | Base value set from template or database |
| `MinValue` / `MaxValue` | Clamp bounds for `FinalValue` |
| `ClampFinalValue` | Whether to enforce `[MinValue, MaxValue]` clamping |
| `ParentTypes` | Templates this attribute feeds into as a child input |
| `ChildTypes` | Templates that feed into this attribute's formulas |
| `DependantTypes` | Soft-reference templates for lookups (e.g., regen rate) |
| `Formulas` | `Dictionary<CharacterAttributeTemplate, CharacterAttributeFormulaTemplate>` mapping each child to a formula |

### Formula System

Formulas are ScriptableObjects assigned per-child in `CharacterAttributeTemplate.Formulas`.

| Formula | Calculation |
|---------|-------------|
| `FlatBonusFormulaTemplate` | `bonusAttribute.FinalValue * 2` |
| `PercentageBonusFormulaTemplate` | `bonusAttribute.FinalValue * Percentage` |

### Relationship Model

Templates declare three types of relationships, resolved at runtime into bidirectional links between `CharacterAttribute` instances:

| Relationship | Purpose | Propagation |
|--------------|---------|-------------|
| **Parent** | This attribute feeds into the parent's formulas as a child input. | Automatic upward |
| **Child** | The child feeds into this attribute's formulas as a formula input. | Automatic upward |
| **Dependency** | Soft reference for lookups (e.g., regen rate). No formula involvement. | None |

During `AddDependents()`, the wiring works as follows:
- **ParentTypes**: The parent attribute calls `AddChild(instance)` — this attribute becomes a formula input for the parent.
- **ChildTypes**: This attribute calls `AddChild(childInstance)` — the child becomes a formula input for this attribute.
- **DependantTypes**: This attribute calls `AddDependant(dependantInstance)` — a soft reference for lookups.

## Usage Examples

### Value Calculation API

Each `CharacterAttribute` has four value layers:

| Layer | Description |
|-------|-------------|
| `Value` | Base value, set from template or database. |
| `FormulaModifier` | Derived from child attribute formulas. Reset and recalculated each `ApplyChildren()`. |
| `ExternalModifier` | Accumulated from external sources (items, buffs, regions). Persistent across recalculations. |
| `FinalValue` | `Value + FormulaModifier + ExternalModifier`, optionally clamped to `[MinValue, MaxValue]`. |

```
FinalValue = Clamp(Value + FormulaModifier + ExternalModifier, MinValue, MaxValue)
```

Clamping is only applied when `CharacterAttributeTemplate.ClampFinalValue` is `true`.

The `Modifier` property returns the total: `FormulaModifier + ExternalModifier`.

`CharacterResourceAttribute` adds a fifth layer:

| Layer | Description |
|-------|-------------|
| `CurrentValue` | Depletable float representing current HP/MP/Stamina. Clamped between `0` and `FinalValue`. |

### Network Synchronization

#### Payload Serialization (FishNet Reader/Writer)

- **WritePayload**: Writes `(templateID, value)` for regular attributes; `(templateID, value, currentValue)` for resource attributes.
- **ReadPayload**: Reads and applies via `SetAttribute()` / `SetResourceAttribute()`.

#### Client Broadcast Receivers

| Broadcast | Purpose |
|-----------|---------|
| `CharacterAttributeUpdateBroadcast` | Owner-targeted single attribute update |
| `CharacterAttributeUpdateMultipleBroadcast` | Owner-targeted batch attribute update |
| `CharacterResourceAttributeUpdateBroadcast` | Legacy owner resource update path (owner currently reconcile-driven) |
| `CharacterResourceAttributeUpdateMultipleBroadcast` | Legacy owner resource batch path |
| `CharacterObserverAttributeUpdateBroadcast` | Observer-targeted attribute updates with `CharacterID` routing |
| `CharacterObserverResourceAttributeUpdateBroadcast` | Observer-targeted resource updates with `CharacterID` routing |

Observer-targeted updates are routed through the client character cache (`BaseCharacter.ClientCharacters`) to resolve the target `ICharacterAttributeController` by `CharacterID`.

#### Reconciliation

`CharacterAttributeResourceState` is a snapshot struct containing `RegenDelta`, `Health`, `Mana`, `Stamina`, and max/final caps (`MaxHealth`, `MaxMana`, `MaxStamina`). Used by FishNet's Replicate/Reconcile prediction system via `GetResourceState()` / `ApplyResourceState()`.

### Static Events

| Event (on `ICharacterDamageController`) | Signature |
|-----------------------------------------|-----------|
| `OnDamaged` | `Action<ICharacter, ICharacter, int, DamageAttributeTemplate>` |
| `OnKilled` | `Action<ICharacter, ICharacter>` |
| `OnHealed` | `Action<ICharacter, ICharacter, int>` |

### External Integration Points

The attribute system is consumed by many other systems:

- **Ability System** — Checks/consumes mana, applies damage via attributes.
- **Buff System** — Applies/removes external modifiers via `AddModifier()` / `AddModifier(-amount)`.
- **Item System** — Applies item attribute bonuses via `AddModifier()` on equip, reversed on unequip.
- **Quest System** — Checks attribute prerequisites.
- **KCC (Movement)** — Reads stamina for sprint/jump.
- **Party System** — Gets health percentages (`CurrentValue / FinalValue`).
- **UI** — Resource bars, target frames, pet controls.
- **Region Effects** — Zone-based attribute modification via `AddModifier()`.
- **Pet System** — Manages pet attributes.
- **Achievement System** — Tracks damage/heal/kill milestones.
- **Faction System** — Adjusted on kill events.
- **Database Layer** — Persists/loads via `CharacterAttributeData` DTO and `ICharacterAttributeService`.

All external systems use `AddModifier()` / `SetModifier()` which operate on `ExternalModifier`, ensuring their contributions are never overwritten by the formula recalculation system.

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| Attribute templates loaded | Open `CharacterAttributeTemplateDatabase` in Inspector | All attribute templates listed |
| Parent/child wiring | Enter Play mode, inspect `CharacterAttributeController` | Attributes show correct parent/child links |
| Formula propagation | Modify a child attribute's base value | Parent `FinalValue` updates automatically |
| External modifier persistence | Apply a buff, then trigger formula recalculation | `ExternalModifier` value unchanged |
| Damage/resistance calculation | Deal damage with a `DamageAttributeTemplate` | Health reduced by `RawDamage - Resistance.FinalValue` (clamped ≥ 0) |
| Regeneration tick | Wait 5 seconds in Play mode with regen attributes set | Resource `CurrentValue` increases by regen amount |
| Network sync (owner) | Modify attribute server-side | Owner receives `CharacterAttributeUpdateBroadcast` |
| Network sync (observer) | Modify another character's attribute | Observer receives `CharacterObserverAttributeUpdateBroadcast` |
| Reconciliation | Simulate prediction mismatch | `ApplyResourceState()` corrects client resource values |
| Immortality flag | Set `Immortal = true`, apply damage | No health change, no `OnDamaged` event |

## Flow Diagram

### Value Calculation Flow

```
┌─────────────────┐
│  Value (Base)    │
└────────┬────────┘
         │
         ▼
┌──────────────────────┐     ┌──────────────────────────┐
│  ApplyChildren()     │◄────│  Child Attributes        │
│  Reset FormulaModifier│     │  (via Template.Formulas) │
│  Sum formula results │     └──────────────────────────┘
└────────┬─────────────┘
         │
         ▼
┌──────────────────────┐
│  FormulaModifier     │
└────────┬─────────────┘
         │
         ▼
┌──────────────────────────────────────────────────────┐
│  FinalValue = Value + FormulaModifier + ExternalModifier │
│  (Clamped to [MinValue, MaxValue] if enabled)        │
└────────┬─────────────────────────────────────────────┘
         │
         ▼
┌───────────────────────────────┐
│  If FinalValue changed →      │
│  Propagate upward to parents  │
│  Fire OnAttributeUpdated      │
└───────────────────────────────┘
```

### Propagation Flow

1. An attribute's base `Value` or `ExternalModifier` changes.
2. `UpdateValues()` calls `ApplyChildren()` which resets `FormulaModifier` to `0`.
3. For each entry in `Template.Formulas`, the matching child attribute is found and `CalculateBonus()` is called.
4. All formula results are summed into `FormulaModifier`.
5. `FinalValue` is recalculated as `Value + FormulaModifier + ExternalModifier`.
6. If `FinalValue` changed, propagation continues upward to all parent attributes.
7. `OnAttributeUpdated` fires after each recalculation.

**Key design**: `ApplyChildren()` only resets `FormulaModifier`. The `ExternalModifier` (from items, buffs, regions) is preserved across recalculations, ensuring equipment and buff bonuses are never lost during formula propagation.

### Damage and Resistance Flow

```
┌────────────────────┐
│  Raw Damage Input   │
└────────┬───────────┘
         │
         ▼
┌────────────────────────────────────────────────────────┐
│  EffectiveDamage = Clamp(RawDamage - Resistance, 0, 999999) │
└────────┬───────────────────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────┐
│  CharacterDamageController           │
│  ├─ Apply resistance modifiers       │
│  ├─ Consume health                   │
│  ├─ Fire OnDamaged                   │
│  ├─ Track achievements               │
│  └─ Kill if health reaches zero      │
└──────────────────────────────────────┘
```

The `CharacterDamageController` handles:
- **Damage**: Applies resistance modifiers, consumes health, fires `OnDamaged`, tracks achievements, triggers `Kill` if health reaches zero.
- **Kill**: Adjusts faction, awards achievements, removes all buffs, kills pet, fires `OnKilled`.
- **Heal**: Gains health, fires `OnHealed`, tracks achievements.
- **CompleteHeal**: Restores health to `FinalValue`.
- **Immortal**: Flag that prevents all damage and kill processing.

### Regeneration Flow

`CharacterAttributeController.Regenerate()` uses a 5-second tick rate:

1. Accumulates `deltaTime` until >= 5 seconds.
2. Calculates number of intervals elapsed.
3. For each resource (Health, Mana, Stamina), looks up the regeneration attribute via dependency and calls `Gain(regenAmount * intervals)`.

Regeneration attributes (HealthRegeneration, ManaRegeneration, StaminaRegeneration) are **dependencies** of their corresponding resource attribute.

## Project Structure

### Directory Structure

```
CharacterAttribute/
├── CharacterAttribute.cs                  # Runtime attribute instance
├── CharacterResourceAttribute.cs          # Depletable resource (HP/MP/Stamina)
├── CharacterAttributeController.cs        # Per-entity controller (NetworkBehaviour)
├── CharacterDamageController.cs           # Damage, heal, and kill logic
├── CharacterAttributeResourceState.cs     # Snapshot struct for reconciliation
├── ICharacterAttributeController.cs       # Attribute controller interface
├── ICharacterAttributeControllerExtensions.cs # Extension methods (health/mana/stamina percentage queries)
├── ICharacterDamageController.cs          # Damage controller interface
└── Template/
    ├── CharacterAttributeTemplate.cs          # ScriptableObject blueprint
    ├── CharacterAttributeTemplateDatabase.cs  # Master list of all templates
    ├── CharacterAttributeFormulaTemplate.cs   # Abstract formula base
    ├── DamageAttributeTemplate.cs             # Damage type with linked resistance
    ├── ResistanceAttributeTemplate.cs         # Resistance type marker
    └── Formulas/
        ├── FlatBonusFormulaTemplate.cs            # Flat bonus formula
        └── PercentageBonusFormulaTemplate.cs      # Percentage bonus formula
```

### Inheritance Hierarchies

#### Runtime Instances

```
CharacterAttribute
└── CharacterResourceAttribute
```

#### Templates (ScriptableObjects)

```
CachedScriptableObject<CharacterAttributeTemplate>
└── CharacterAttributeTemplate
    ├── DamageAttributeTemplate
    └── ResistanceAttributeTemplate

FormulaTemplate<CharacterAttribute>
└── CharacterAttributeFormulaTemplate
    ├── FlatBonusFormulaTemplate
    └── PercentageBonusFormulaTemplate
```

#### Controllers (NetworkBehaviour)

```
CharacterBehaviour
├── CharacterAttributeController : ICharacterAttributeController
└── CharacterDamageController    : ICharacterDamageController, IDamageable, IHealable
```

## License

This project is subject to the FishMMO project license.
