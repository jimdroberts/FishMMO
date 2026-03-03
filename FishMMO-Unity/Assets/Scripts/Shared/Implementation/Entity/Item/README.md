# Item / Inventory System

## Overview

The Item system is a component-based, template-driven framework for items, inventories, equipment, and banking in FishMMO. Each `Item` instance is composed of optional sub-components (`ItemStackable`, `ItemEquippable`, `ItemGenerator`) determined by its template type. Items live inside slot-based `ItemContainer` controllers (`InventoryController`, `EquipmentController`, `BankController`) attached to characters as `CharacterBehaviour` components. The system supports seed-based deterministic attribute generation, stacking, equip/unequip with character stat modification, cross-container slot swaps, and FishNet network synchronization via broadcasts.

## Directory Structure

```
Item/
├── Item.cs                                    # Runtime item instance (composition root)
├── ItemAttribute.cs                           # Runtime attribute instance on an item
├── ItemEquippable.cs                          # Equip/unequip component (IEquippable<ICharacter>)
├── ItemGenerator.cs                           # Seed-based attribute generation and application
├── ItemSlot.cs                                # Enum: Head, Chest, Legs, Hands, Feet, Primary, Secondary
├── ItemStackable.cs                           # Stack management component (IStackable<Item>)
├── Container/
│   ├── IItemContainer.cs                      # Container interface (slot CRUD, events)
│   ├── ItemContainer.cs                       # Abstract base container (CharacterBehaviour)
│   ├── Bank/
│   │   ├── IBankController.cs                 # Bank interface (currency, swap validation)
│   │   └── BankController.cs                  # Bank container (100 slots, currency)
│   ├── Equipment/
│   │   ├── IEquipmentController.cs            # Equipment interface (equip, unequip, activate)
│   │   └── EquipmentController.cs             # Equipment container (slot-per-ItemSlot enum)
│   └── Inventory/
│       ├── IInventoryController.cs            # Inventory interface (activate, swap validation)
│       ├── InventoryController.cs             # Main inventory container (32 slots)
│       └── InventoryType.cs                   # Enum: Inventory, Equipment, Bank
└── Template/
    ├── ItemTemplateDatabase.cs                # ScriptableObject lookup: name → BaseItemTemplate
    ├── Attribute/
    │   ├── ItemAttributeTemplate.cs           # ScriptableObject: min/max value + CharacterAttribute link
    │   └── ItemAttributeTemplateDatabase.cs   # ScriptableObject lookup: name → ItemAttributeTemplate
    └── Types/
        ├── BaseItemTemplate.cs                # Abstract base template (price, icon, stackability, attributes)
        ├── Consumable/
        │   ├── ConsumableTemplate.cs          # Abstract consumable (charge cost, cooldown)
        │   ├── ConsumableType.cs              # Enum: Potion, Food, Mount, Scroll
        │   └── ScrollConsumableTemplate.cs    # Scroll that teaches abilities on use
        └── Equipment/
            ├── EquippableItemTemplate.cs       # Abstract equippable (slot, random attributes, model data)
            ├── WeaponTemplate.cs               # Weapon: attack power + attack speed
            └── ArmorTemplate.cs                # Armor: armor bonus
```

### Related Files (Outside This Directory)

```
Shared/Network/Character/InventoryBroadcasts.cs          # Inventory broadcast structs
Shared/Network/Character/EquipmentBroadcasts.cs          # Equipment broadcast structs
Shared/Network/Character/BankBroadcasts.cs               # Bank broadcast structs
Server/Implementation/World/SceneServer/Character/CharacterSystem.cs  # Loads items from DB on character connect
```

## Inheritance Hierarchies

### Runtime Instances (Composition)

```
Item
├── ItemStackable    (optional, if MaxStackSize > 1)
├── ItemEquippable   (optional, if template is EquippableItemTemplate)
└── ItemGenerator    (optional, if template.Generate is true)
```

### Templates (ScriptableObjects)

```
CachedScriptableObject<BaseItemTemplate>
└── BaseItemTemplate
    ├── ConsumableTemplate (abstract)
    │   └── ScrollConsumableTemplate (abstract)
    └── EquippableItemTemplate (abstract)
        ├── WeaponTemplate
        └── ArmorTemplate

CachedScriptableObject<ItemAttributeTemplate>
└── ItemAttributeTemplate
```

### Containers (NetworkBehaviour)

```
CharacterBehaviour
└── ItemContainer (abstract) : IItemContainer
    ├── InventoryController : IInventoryController
    ├── EquipmentController : IEquipmentController
    └── BankController      : IBankController
```

## Data Model

### Item Instance

Each `Item` holds references to its optional sub-components and template:

| Field | Type | Description |
|-------|------|-------------|
| `ID` | `long` | Unique database instance ID |
| `Version` | `long` | Incremented on state changes that affect client sync |
| `Slot` | `int` | Current slot index in its container (-1 if unslotted) |
| `Template` | `BaseItemTemplate` | The ScriptableObject blueprint |
| `Stackable` | `ItemStackable` | Stack management (null if non-stackable) |
| `Equippable` | `ItemEquippable` | Equip/unequip + owner tracking (null if non-equippable) |
| `Generator` | `ItemGenerator` | Seed-based attribute generation (null if non-generated) |

### ItemStackable

| Field | Type | Description |
|-------|------|-------------|
| `Amount` | `uint` | Current stack count |
| `IsStackFull` | `bool` | True when `Amount == MaxStackSize` |

### ItemEquippable

| Field | Type | Description |
|-------|------|-------------|
| `Character` | `ICharacter` | The character currently equipping this item (null if not equipped) |

### ItemGenerator

| Field | Type | Description |
|-------|------|-------------|
| `Seed` | `int` | Deterministic seed for attribute generation |
| `Attributes` | `Dictionary<string, ItemAttribute>` | Generated attribute instances keyed by name |

### ItemAttribute

| Field | Type | Description |
|-------|------|-------------|
| `Template` | `ItemAttributeTemplate` | The attribute type definition (min/max, linked CharacterAttribute) |
| `value` | `int` | Current attribute value |

## Item Initialization

Items are initialized through constructors that wire up sub-components based on template type:

```
new Item(id, seed, templateID, amount)
  └── Initialize(id, amount, seed)
      ├── If MaxStackSize > 1: create ItemStackable(amount)
      ├── If template is EquippableItemTemplate: create ItemEquippable
      ├── If template.Generate: create ItemGenerator
      │   └── If seed == 0 && ID != 0: derive seed from item ID bytes
      ├── Equippable.Initialize(item)
      ├── Generator.Initialize(item, seed)  → calls Generate(seed)
      │   ├── If template is WeaponTemplate: generate AttackPower + AttackSpeed
      │   ├── If template is ArmorTemplate: generate ArmorBonus
      │   ├── If RandomAttributeDatabases: add random attributes
      │   └── Add additional template Attributes (merged/summed)
      └── Wire events: OnEquip → ApplyAttributes, OnUnequip → RemoveAttributes
```

## Container System

### Slot Management

All containers extend `ItemContainer` which provides:

| Method | Description |
|--------|-------------|
| `AddSlots(items, amount)` | Initializes container capacity |
| `TryAddItem(item, out modifiedItems)` | Adds item with stacking logic, returns modified slots |
| `SetItemSlot(item, slot)` | Direct slot assignment |
| `SwapItemSlots(from, to)` | Swaps two slots, fires `OnSlotUpdated` for both |
| `RemoveItem(slot)` | Removes and returns the item at slot |
| `CanAddItem(item)` | Checks stacking capacity and free slots |
| `HasFreeSlot()` / `FreeSlots()` / `FilledSlots()` | Capacity queries |
| `ContainsItem(template)` / `GetItemCount(template)` | Search by template |
| `CanManipulate()` | Checks character is alive and container is non-empty |

### Container Sizes

| Container | Default Slots | Notes |
|-----------|---------------|-------|
| `InventoryController` | 32 | Main character inventory |
| `EquipmentController` | `ItemSlot` enum count (7) | One slot per equipment type |
| `BankController` | 100 | Persistent bank storage with currency |

## Equip / Unequip Flow

### Equipping an Item

```
EquipmentController.Equip(item, inventoryIndex, sourceContainer, toSlot)
  ├── Validate: item != null, item.IsEquippable, CanManipulate()
  ├── Validate: template slot matches target slot
  ├── If slot occupied:
  │   ├── previousItem.Equippable.Unequip()  → removes attribute modifiers
  │   └── Swap previousItem back to sourceContainer[inventoryIndex]
  ├── Else: sourceContainer.RemoveItem(inventoryIndex)
  ├── SetItemSlot(item, slotIndex)
  └── item.Equippable.Equip(Character)
      └── fires OnEquip
          └── Item.ItemEquippable_OnEquip(character)
              └── Generator.ApplyAttributes(character)
                  └── For each generated attribute:
                      └── characterAttribute.AddModifier(value)
```

### Unequipping an Item

```
EquipmentController.Unequip(targetContainer, slot, out modifiedItems)
  ├── Validate: CanManipulate(), item exists, container.CanAddItem(item)
  ├── targetContainer.TryAddItem(item, out modifiedItems)
  ├── item.Equippable.Unequip()
  │   └── fires OnUnequip
  │       └── Item.ItemEquippable_OnUnequip(character)
  │           └── Generator.RemoveAttributes(character)
  │               └── For each generated attribute:
  │                   └── characterAttribute.AddModifier(-value)
  └── SetItemSlot(null, slot)
```

## Stacking Logic

Items stack when they have the same template ID and matching generation seed (via `IsMatch()`).

```
ItemStackable.AddToStack(other)
  ├── Validate: same template, matching seed, neither stack full
  ├── Calculate remainingCapacity = MaxStackSize - Amount
  ├── Transfer: Amount += min(remainingCapacity, other.Amount)
  └── other.Amount = remainder

ItemStackable.TryUnstack(amount, out instance)
  ├── If amount >= Amount: return entire item
  └── Else: reduce Amount, return null (new instance creation unfinished)
```

## Consumable System

Consumables use a charge-based model with cooldowns:

```
ConsumableTemplate.Invoke(character, item)
  ├── CanConsume: character alive, item stackable, Amount >= ChargeCost, not on cooldown
  ├── Apply cooldown (if Cooldown > 0)
  ├── item.Stackable.Remove(ChargeCost)
  └── If Amount < 1: item.Destroy()

ScrollConsumableTemplate extends this to grant abilities:
  └── abilityController.LearnBaseAbilities(AbilityTemplates)
```

## Attribute Generation

The `ItemGenerator` uses deterministic seed-based generation:

1. **Seed derivation**: If no seed provided, derived from item ID bytes for reproducibility.
2. **Base attributes**: `WeaponTemplate` generates AttackPower + AttackSpeed; `ArmorTemplate` generates ArmorBonus — values randomized within template min/max ranges.
3. **Random attributes**: Up to `MaxItemAttributes` drawn from `RandomAttributeDatabases`, each with randomized values.
4. **Additional attributes**: `BaseItemTemplate.Attributes` list merged/summed into generated attributes.

### Live Attribute Updates

`SetAttribute(name, newValue)` updates a generated attribute and, if the item is equipped, immediately adjusts the character's attribute modifiers (removes old value, applies new value).

## Network Synchronization

### Equipment Payload (FishNet Reader/Writer)

The `EquipmentController` implements `ReadPayload` / `WritePayload` for initial character synchronization:

| Direction | Data per item |
|-----------|---------------|
| Write | `ID, TemplateID, Slot, Seed, StackSize` |
| Read | Creates `Item`, calls `SetItemSlot`, calls `Equippable.Equip(Character)` |

### Broadcast Types

#### Inventory

| Broadcast | Direction | Purpose |
|-----------|-----------|---------|
| `InventorySetItemBroadcast` | Server → Client | Set a single inventory slot |
| `InventorySetMultipleItemsBroadcast` | Server → Client | Batch set multiple slots |
| `InventoryRemoveItemBroadcast` | Server → Client | Remove item from slot |
| `InventorySwapItemSlotsBroadcast` | Server → Client | Swap slots (within inventory or cross-container) |

#### Equipment

| Broadcast | Direction | Purpose |
|-----------|-----------|---------|
| `EquipmentSetItemBroadcast` | Server → Client | Set a single equipment slot |
| `EquipmentSetMultipleItemsBroadcast` | Server → Client | Batch set multiple slots |
| `EquipmentEquipItemBroadcast` | Server → Client | Equip from inventory/bank |
| `EquipmentUnequipItemBroadcast` | Server → Client | Unequip to inventory/bank |

#### Bank

| Broadcast | Direction | Purpose |
|-----------|-----------|---------|
| `BankSetItemBroadcast` | Server → Client | Set a single bank slot |
| `BankSetMultipleItemsBroadcast` | Server → Client | Batch set multiple slots |
| `BankRemoveItemBroadcast` | Server → Client | Remove item from slot |
| `BankSwapItemSlotsBroadcast` | Server → Client | Swap slots (within bank or cross-container) |

### Cross-Container Swaps

Swap broadcasts include an `InventoryType` field (`Inventory`, `Equipment`, `Bank`) to identify the source container. The receiving controller resolves the other container via `Character.TryGet<T>()` and performs the swap across both containers.

## Events

### Item Events

| Event | Parameters | Description |
|-------|------------|-------------|
| `Item.OnDestroy` | _(none)_ | Fired when the item is destroyed |

### ItemEquippable Events

| Event | Parameters | Description |
|-------|------------|-------------|
| `OnEquip` | `ICharacter owner` | Fired when equipped to a character |
| `OnUnequip` | `ICharacter owner` | Fired when unequipped from a character |

### Container Events

| Event | Parameters | Description |
|-------|------------|-------------|
| `OnSlotUpdated` | `IItemContainer, Item, int slot` | Fired on any slot change (add, remove, swap, set) |

## Template Configuration

### BaseItemTemplate

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `IsIdentifiable` | `bool` | false | Whether the item has hidden stats |
| `Generate` | `bool` | false | Whether to create an `ItemGenerator` |
| `MaxStackSize` | `uint` | 1 | Max stack size (>1 enables stacking) |
| `Price` | `int` | 0 | Buy/sell price |
| `Attributes` | `List<ItemAttributeTemplate>` | — | Base attributes added after generation |

### EquippableItemTemplate

| Field | Type | Description |
|-------|------|-------------|
| `Slot` | `ItemSlot` | Equipment slot (Head, Chest, Legs, etc.) |
| `MaxItemAttributes` | `int` | Max random attributes on generation |
| `RandomAttributeDatabases` | `ItemAttributeTemplateDatabase[]` | Pools for random attribute selection |
| `ModelSeed` | `uint` | Seed for model randomization |
| `ModelPools` | `int[]` | Visual model variation pools |

### ConsumableTemplate

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `ConsumableType` | `ConsumableType` | — | Potion, Food, Mount, or Scroll |
| `ChargeCost` | `uint` | 1 | Charges consumed per use |
| `Cooldown` | `float` | 0 | Seconds of cooldown after use |

## External Integration Points

| System | Integration |
|--------|-------------|
| **CharacterAttribute** | `ItemGenerator.ApplyAttributes` / `RemoveAttributes` calls `AddModifier()` on character attributes via `ExternalModifier` |
| **Ability System** | `ScrollConsumableTemplate` teaches abilities via `IAbilityController.LearnBaseAbilities` |
| **Cooldown System** | `ConsumableTemplate` adds cooldowns via `ICooldownController.AddCooldown` |
| **Damage System** | `ItemContainer.CanManipulate()` checks `ICharacterDamageController.IsAlive` |
| **Achievement System** | Item rewards delivered via `InventoryController.TryAddItem` |
| **Quest System** | Checks item prerequisites via `ContainsItem` / `GetItemCount` |
| **Trade / Merchant** | Uses `Price` field and container add/remove operations |
| **Database Layer** | Persists/loads via item DTOs and services (`ICharacterInventoryService`, `ICharacterEquipmentService`, `ICharacterBankService`) |
| **UI** | Inventory, equipment, and bank panels subscribe to `OnSlotUpdated` events |