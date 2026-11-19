# Commerce module

**Short description:** Buying from and selling to merchants, mail (with attachments and postage), looting corpses, and
player-to-player trade, with their windows.

| | |
|---|---|
| **Module id** | `commerce` (`CommerceModule`) |
| **Assemblies** | `FishMMO.Commerce`, `.Server`, `.Client`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core (items, inventory, currency) |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Phase 3 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), 2026-11-17 |

## Folders

```
Assets/FishMMO/Modules/Commerce/
├── Runtime/              FishMMO.Commerce          CommerceBroadcasts (merchant, mail, corpse loot), TradeBroadcasts,
│                                                   TradeRules, CommerceModule
├── Server/               FishMMO.Commerce.Server   CommerceSystem (+ asset): merchant, mail and corpse-loot partials
│                                                   with their own request guard and main-thread queue;
│                                                   Trade/: TradeSystem (+ asset), sessions, the exchange, the currency
│                                                   settlement (see Trade/README.md)
├── Client/               FishMMO.Commerce.Client   UITKMerchant, UITKMail, UITKLoot, UITKTrade (+ prefabs, uxml, uss),
│                                                   TradeClient
├── Editor/RenderScratch/ FishMMO.Commerce.RenderScratch   The merchant and trade renders
├── Tests/                FishMMO.Commerce.Tests    Merchant, trade, settlement and wiring tests
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `SceneServer_Systems` label | `CommerceSystem.asset` and `TradeSystem.asset` are adopted by the scene server. |
| `ClientWorldGUI_Panels` label | `UIMerchant`, `UIMail`, `UILoot` and `UITrade` prefabs are placed into the world GUI. |
| `IInteractableSystem.OnPlayerInteracting` | Opens a corpse's loot window on any accepted interaction with it. |
| `InventorySlotClaims` | A right-click on an inventory slot while a trade is open puts the item on the table. |
| `CharacterMenuEntries` | "Trade" on the world and party menus (order 120). |
| `ServerBehaviour.TryPersistCharacterAttributes` / `RecordCurrencyMovement` | The currency save and ledger row every spend shares. |
| `Accomplishments.Record` | A merchant's achievement is credited on a purchase or sale. |
| `[assembly: GameModule]` | `CommerceModule` ("commerce"). |

Merchant, mail and loot requests have their own debounce; they used to share the interactable system's single "one
interaction at a time" key.

## What stays in the core

Merchant templates, premade abilities and merchant tabs (NPC recipes and content use them), the merchant and mailbox
interactables placed in the world, their ECA actions and the two messages those send to open a window
(`MerchantBroadcast`, `MailboxBroadcast`), and the loot an NPC carries (`ILootableCorpse`, built on death; corpse decay
is the core's).

## Removing it

Delete `Assets/FishMMO/Modules/Commerce` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
Merchants and mailboxes then open nothing, corpses cannot be opened, and there is no trade. The mail tables and the
currency ledger stay in place, unused.

## License

This project is subject to the FishMMO project license.
