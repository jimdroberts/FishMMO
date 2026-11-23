# Items module

**Short description:** What a player does with items: the inventory, bank and equipment containers on the player (the
equipment predicted on the character's replicate), the scene server's inventory system (every move, split, merge,
equip, grant and exchange, and the item writes), and the inventory, bank and equipment windows.
[Server/README.md](Server/README.md) documents the inventory system in detail; the core's
[item README](../../Shared/Implementation/Entity/Item/README.md) documents the items themselves.

| | |
|---|---|
| **Module id** | `items` (`ItemsModule`) |
| **Assemblies** | `FishMMO.Items`, `.Server`, `.Client`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core (the item data, the container contracts, the item broadcasts, the equipment replicate fields, the character persistence and prediction); Abilities (the inventory system hears a consumable cast cancel on the ability controller) |
| **Depended on by** | None (Commerce, Housing, Quests, Achievements, Staff and World Interactables reach the inventory through the core's `ICharacterInventorySystem`; their tests that need a real inventory find it by name and skip without it) |
| **Remove together with** | Nothing |
| **Moved** | Out of the core (the core is shrunk to login, world and scene) |

## Folders

```
Assets/FishMMO/Modules/Items/
├── Runtime/              FishMMO.Items            ItemsModule and the three containers, declared as player parts:
│                                                  ItemContainer (the base: slots, locks, stacking), InventoryController,
│                                                  BankController, EquipmentController (predicted, Order 93);
│                                                  ItemStackTransfer (split and merge)
├── Server/               FishMMO.Items.Server     CharacterInventorySystem (+ asset): the requests, the item write batches
│                                                  and journal, the exchange, the periodic snapshot, the identity
│                                                  write-back; its runtime data and main-thread queue; ItemContainerMapping,
│                                                  ItemSnapshotContent
├── Client/               FishMMO.Items.Client     UITKInventory (+ UIInventory.prefab/uxml/uss), UITKBank (+ UIBank),
│                                                  UITKEquipment (+ UIEquipment.prefab, on the core's character sheet),
│                                                  UITKItemGridPanel (the inventory's and bank's shared grid)
├── Editor/RenderScratch/ FishMMO.Items.RenderScratch   The three windows in the panel renders; the equipment, preview,
│                                                  drop and capacity probes
├── Tests/                FishMMO.Items.Tests      The containers, split and merge, grants, unequip destinations, predicted
│                                                  equipment and its audit pins, the item writes and their gate, the
│                                                  windows, wiring
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: GameModule]` | `ItemsModule` ("items"). |
| `[assembly: CharacterPart]` | Every player gets an `InventoryController`, a `BankController` and an `EquipmentController` as it wakes (no race prefab authors them). The equipment's spawn state rides the player's spawn payload in a block of its own (`items.equipment`). |
| `IPredictableController` | The equipment takes part in the character's prediction: requests ride `CharacterReplicateData.EquipmentRequest`, state reconciles in `CharacterReconcileData.Equipment` (both core fields). The prediction controller finds its controllers again at network start, after the parts are attached. |
| Container contracts (`IItemContainer`, `IInventoryController`, `IBankController`, `IEquipmentController`) | Everything outside the module, the core's item load and login sends included, reaches the containers through these with `ICharacter.TryGet`. |
| `ICharacterInventorySystem` (server core) | The system implements it; the character save and despawn flush items through it, and the modules that grant, take or exchange items call it. |
| `ServerItemHooks` (shared core) | The system installs the grant and inventory-changed hooks the shared item actions call. |
| `SceneServer_Systems` label | `CharacterInventorySystem.asset` is adopted by the scene server. |
| `ClientWorldGUI_Panels` label | `UIInventory`, `UIBank` and `UIEquipment` are placed into the world GUI under those names; the core opens them by name (the Inventory and Equipment keys, the interact panels, the bank interaction). |
| `InventorySlotClaims`, `BankWindowLinks` (client core) | The inventory lets another window claim a right-click (Commerce's trade); the bank lists modules' links (Housing's vault). |

## What stays in the core

The item data: `Item` and its components (`ItemStackable`, `ItemEquippable`, `ItemGenerator`), `ItemSlot`,
`InventoryType`, the item templates and loot tables, `ServerItemHooks`, and the container contracts. The wire: the
inventory, bank and equipment broadcasts, the equipment replicate input and reconcile entries. The item ECA actions and
conditions (they work through the contracts and the hooks). The `character_item` table and `ICharacterItemService`, the
item fetch and apply in the character load, and the login sends of the inventory and bank (each a no-op without the
containers). The client's shared slot-panel pieces (`UITKSlotPanelBase`, `ItemOperationTracker`, `ItemSlotPendingSet`)
used by the hotkey bar, trade, loot and container windows, and the character sheet with its preview
(`CharacterSheetView`, `EquipmentPreviewRenderer`), which the inspect window shares.

## Removing it

Delete `Assets/FishMMO/Modules/Items` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
Players then have no inventory, bank or equipment: their item rows stay in the database and are fetched at login, but
there is no container to put them in and nothing is sent. Every feature that grants, takes or exchanges items (merchants,
trade, loot, mail, quests' rewards, achievements' rewards, gathering, the house vault, the staff's item commands) finds no
`ICharacterInventorySystem` and refuses, as it already does when the system is missing. The Inventory and Equipment keys
do nothing.

## License

This project is subject to the FishMMO project license.
