# Commerce module

**Short description:** Buying from and selling to merchants, mail (with attachments and postage), looting corpses, and
player-to-player trade, with their windows.

Phase 3 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), moved 2026-11-17. Depends on
the core's items, inventory and currency.

| Folder | Assembly | Holds |
|---|---|---|
| Runtime/ | FishMMO.Commerce | Merchant, mail and corpse-loot requests and replies (`CommerceBroadcasts`), trade broadcasts, `TradeRules`, `CommerceModule` |
| Server/ | FishMMO.Commerce.Server | `CommerceSystem` (+ asset, `SceneServer_Systems`): merchant, mail and corpse-loot partials with their own request guard and main-thread queue; `Trade/`: `TradeSystem` (+ asset), sessions, the exchange and its currency settlement |
| Client/ | FishMMO.Commerce.Client | Merchant, mail, loot and trade windows (+ prefabs, `ClientWorldGUI_Panels`); `TradeClient` (the "Trade" menu entry and the inventory right-click claim) |
| Editor/RenderScratch/ | | The merchant and trade renders; the trade window in the all-panels set and the grid fit probe |
| Tests/ | FishMMO.Commerce.Tests | Merchant, trade and settlement tests, and the wiring tests |

## What stays in the core

Merchant templates, premade abilities and merchant tabs (NPC recipes and content use them), the merchant and mailbox
interactables placed in the world, their ECA actions and the two messages those send to open a window
(`MerchantBroadcast`, `MailboxBroadcast`), and the loot an NPC carries (`ILootableCorpse`, built on death; corpse decay is
the core's). Without the module the merchant and mailbox open nothing and corpses cannot be opened. The mail tables and
the currency ledger stay in place, unused.

## Seams it uses

- `IInteractableSystem.OnPlayerInteracting`: opens a corpse's loot window on any accepted interaction with it.
- `InventorySlotClaims`: a right-click on an inventory slot while a trade is open puts the item on the table.
- `CharacterMenuEntries`: "Trade" on the world and party menus (order 120).
- `ServerBehaviour.TryPersistCharacterAttributes` / `RecordCurrencyMovement`: the currency save and ledger row every
  spend shares (ability crafting in the core uses the same pair).

Merchant, mail and loot requests have their own debounce now; they used to share the interactable system's single
"one interaction at a time" key.
