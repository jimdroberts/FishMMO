# Auction House module

**Short description:** One market for the whole world (issue #299). Players list a stack from their bags at a price per
unit for 12, 24 or 48 hours at any Auctioneer; anyone at any Auctioneer can search, sort and buy all of a listing or part of
it. What is bought, what a sale earned, and what was cancelled or did not sell arrives by mail from the "Auction House".

| | |
|---|---|
| **Module id** | `auctionhouse` (`AuctionHouseModule`) |
| **Assemblies** | `FishMMO.AuctionHouse`, `.Server`, `.Client`, `.Editor`, `.Tests` |
| **Depends on** | The core (items, inventory, currency, mail tables); the editor assembly also on NPCs (the NPC factory) and World Design (site builders) |
| **Depended on by** | Nothing |
| **Remove together with** | Nothing |
| **Added** | Issue #299 |

## How it works

- **One market, in the database.** Every scene server reads and writes the `auction_listings` table
  (`IAuctionHouseService`, `FishMMO-DB`), so a listing posted in one town is for sale in all of them. A listing row *is*
  the item while it is for sale: the stack is out of its seller's bags.
- **Buyout only, at a price per unit.** A buyer may take part of a stack. Nothing bids.
- **Posting** (`AuctionHouseSystem.OnPost`): the request names an inventory slot, a quantity, a price per unit and a
  duration. The server takes the **deposit** (`AuctionRules.Deposit`: 5% of the item's *merchant* price × quantity per 12
  hours, at least 1; never returned) and the stack out of the seller first, then writes the row. A seller may hold
  **10** listings at once (the seller's character row is locked for the count, so two servers cannot both see room).
- **Buying** (`OnBuy`): the request names the listing, a quantity and the price per unit the buyer was shown. The server
  takes that price × quantity out of the buyer first; then one transaction takes the stack off the listing (only while it
  is still at that price, not the buyer's own and not expired), deletes it when the last is taken, and writes two mails:
  the item to the buyer and the proceeds (less the house's **5% cut**, rounded down) to the seller.
- **Cancelling** (`OnCancel`) deletes the listing and mails the stack back in one transaction. The deposit is kept.
- **Expiry**: every scene server sweeps once a minute (`ExpireAsync`, `FOR UPDATE SKIP LOCKED`, so sweeps on different
  servers never take the same row); each expired listing is deleted and mailed back in one transaction.
- **Failures give back what they took.** A post or purchase that fails after taking the deposit, stack or price returns
  it: into the purse and bags when the player is still here and has room, by mail when not (`AuctionHouseSystem.Refund`).
- **Retries find their own work.** The service layer retries a lost connection, which can come after the commit. A post
  carries a request key; a purchase and a cancellation carry the instant they were made, which their mail is written at,
  and a retry that finds that mail answers with it.
- **Limits**: a listing's total must fit in a mail's 32-bit currency (`AuctionRules.MaxListingTotal`).

The fees, the listing cap and the sweep are on `Server/AuctionHouseSystem.asset`.

## The window

`UIAuctionHouse` (`UITKAuctionHouse`), opened by the Auctioneer's interaction:

- **Browse**: name search, category (Weapons, Armour, Consumables, Materials, Structure kits, Other gear, Other — decided by
  the server from the item's template, `AuctionRules.Categorize`), sort (price each, total, time left, name; either
  direction), 50 per page. Buy takes two presses; a stack has a quantity box.
- **Sell**: right-click an item in the bags while the window is open (`AuctionHouseClient`, the core's
  `InventorySlotClaims`). Shows the cheapest current price of the same item, the deposit, and what the seller receives.
- **My Listings**: the player's listings, with Cancel (two presses).

## The Auctioneer

`Auctioneer` (an `Interactable`) with the **Auctioneer Interact** trigger (`NPCLookAtInteractorAction`,
`SendAuctioneerBroadcastAction`). The NPC `Prefabs/an auctioneer.prefab` is made by **Dashboard > Maintenance > Auction
House > Build auctioneer** (batch: `-executeMethod FishMMO.Shared.AuctioneerBuilder.BuildBatch`) from the human banker.
`AuctioneerService` registers it with `PointOfInterestServiceNpcs`: every town or stronghold that lays a Market Hall or
a Counting House keeps one standing at it (the market hall first), and every capital keeps one (on the market when it laid
neither). Cut scenes get theirs at the next re-cut.

## Folders

```
Assets/FishMMO/Modules/AuctionHouse/
├── Runtime/    FishMMO.AuctionHouse          AuctionHouseModule, AuctionRules (+ the enums), AuctionHouseBroadcasts,
│                                             Auctioneer / IAuctioneer, ECA/SendAuctioneerBroadcastAction
├── Server/     FishMMO.AuctionHouse.Server   AuctionHouseSystem (+ asset; Browse, Trade and Escrow partials),
│                                             AuctionMailComposer, the system's data containers
├── Client/     FishMMO.AuctionHouse.Client   UITKAuctionHouse (+ prefab, uxml, uss), AuctionHouseClient
├── Editor/     FishMMO.AuctionHouse.Editor   AuctioneerBuilder, AuctioneerService
├── Templates/  Interactions/Auctioneer Interact
├── Prefabs/    an auctioneer (made by the builder)
├── Tests/      FishMMO.AuctionHouse.Tests    Rules, server pieces and wiring tests
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `SceneServer_Systems` label | `AuctionHouseSystem.asset` is adopted by the scene server. |
| `ClientWorldGUI_Panels` label | The `UIAuctionHouse` prefab is placed into the world GUI. |
| `InventorySlotClaims` | A right-click on an inventory slot while the window is open puts the item on the Sell tab. |
| `ServerBehaviour.TryPersistCharacterAttributes` / `RecordCurrencyMovement` | The currency save and ledger rows (`AuctionDeposit`, `AuctionPurchase`). |
| `ICharacterInventorySystem` | The stack taken out of the bags (journalled) and a refund granted back. |
| `ICharacterMailService` / `character_mail` | Every delivery and refund. |
| `PointOfInterestServiceNpcs` (World Design editor) | Where Auctioneers stand. |
| `[assembly: GameModule]` | `AuctionHouseModule` ("auctionhouse"). |

## Removing it

Delete `Assets/FishMMO/Modules/AuctionHouse` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
Auctioneers already placed in cut scenes then have no component behind them and should be cleared by a re-cut. The
`auction_listings` table and its service stay in place, unused; listings in it stay until the module returns.

## License

This project is subject to the FishMMO project license.
