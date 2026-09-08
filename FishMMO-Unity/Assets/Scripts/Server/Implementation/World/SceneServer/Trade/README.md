# Trade System

Player-to-player trading of items and currency (issue #144). One session between two
characters: each puts inventory items and currency on the table, both must accept the same
version of it, and the exchange is applied as one in-memory step and one database commit.

## Files

| File | What it holds |
|---|---|
| `TradeSystem.cs` | Lifecycle, configuration, invitations, the range/state tick, session open/close. |
| `TradeSystem.Handlers.cs` | The broadcast handlers: request, respond, offer, withdraw, currency, accept, cancel. Every refusal is answered with a `TradeRefusedBroadcast` and the current table. |
| `TradeSystem.Commit.cs` | Completion: re-validation, currency deduction, in-memory exchange, credit, one persistence unit, client notification. |
| `TradeSession.cs` | Pure state and the acceptance rules. **Every change clears both acceptances and bumps the version; an accept must quote the current version.** |
| `TradeExchange.cs` | Pure container arithmetic: TAKE every offer out of both bags, GIVE each to the other, or undo everything. Emits the rows each write must carry. |
| `TradeSystemRuntimeData.cs` / `TradeSystemMainThreadQueueData.cs` | The ingress guard and the main-thread queue. |

The wire contract is `Shared/Implementation/Network/Character/TradeBroadcasts.cs`; the rules
both peers share (range, scene, quantity resolution, the bag-room estimate) are
`Shared/Implementation/Entity/Trade/TradeRules.cs`. The client window is
`Client/GUI/World/Trade/UITKTrade.cs`.

## What the client may say

A slot and a quantity, a currency amount, a character id, an accept that quotes a state
version, a cancel. Never an item, a template, a seed, a price or a balance. The server
resolves everything from its own state, and `CharacterStateValidation.CanAct` fronts every
handler except cancel — a dead or stunned player must still be able to close their window.

## Reservation: the inventory slot lock

An offered item's inventory slot is **locked** on the server the moment it goes on the
table. Every path that could move, split, merge, sell, mail, equip or consume an item refuses
a locked slot — including equip, which runs inside the replicate tick and has no broadcast
handler, and the consumable finder, which skips locked slots for the same reason. Withdrawing
the offer, or the session ending for any reason, unlocks it. The owning client locks the same
slots from the authoritative `TradeStateBroadcast`, so its bag greys them out and prediction
stays deterministic.

Completion re-reads every slot regardless (`TradeRules.OfferStillHolds`: same item id, same
template, at least the offered quantity). The lock is the reservation; the re-check is the
second lock on the same door.

## Range and state

`maxTradeDistance` (asset, default 15 m, floor 2 m) is enforced by the server: a tick every
`rangeCheckIntervalSeconds` closes any open session whose parties are no longer both present,
able to act, in the same scene **instance** (handle, not name) and within range. The same
rule is applied once more at completion. The client runs the same check against the partner's
observed position so its window closes immediately; the server's check is the one that
decides. Disconnect and despawn both close the session (the character system raises the
disconnect event before combat-linger begins).

## One character, one trade

`sessionsByCharacter` holds one session per party. A character in a session, or with an
invitation out or pending, refuses new invitations from either end (`SelfBusy` /
`TargetBusy`). Invitations lapse after `inviteTtlSeconds`; a decline or lapse arms a
per-pair cooldown so a refused request cannot be re-sent every debounce interval.

## Completion (`TradeSystem.Commit.cs`)

1. `TryBeginCommit` — no request from either party is honoured from here.
2. Re-validate everything: presence, `CanAct`, scene, range, every offered slot, both
   currency balances, no receiving-balance overflow (currency is an `int` attribute).
3. Deduct both currency offers (refunded on any later refusal).
4. `TradeExchange.TryApply`: all-or-nothing in memory. A refusal for room closes with
   `NoRoom`; both bags are untouched.
5. Credit both currency offers.
6. `ICharacterInventorySystem.TryPersistExchange` — **one unit of work**: both characters'
   item rows (an item that crossed whole keeps its id and is re-owned by the upsert; only an
   item that merged entirely into a resident stack is deleted), both attribute sheets when
   currency moved, and the `currency_ledger` rows (`PlayerTrade`, `Absorbed`) — under both
   characters' session row locks in ascending id order, with both journal sequences claimed
   under those locks. Any refusal rolls the whole transaction back and reconciles both
   characters from memory.
7. `NotifyInventorySlots` for both (removes for still-empty slots first, then sets), then
   `TradeClosedBroadcast(Completed)`.

Room is also estimated when consent is given (`TradeRules.HasRoomFor`), so a player short
of bag space is told at Accept with the table still open, not after both have accepted.

## Tuning (the `TradeSystem` asset)

`maxTradeDistance`, `rangeCheckIntervalSeconds`, `maxOfferSlots` (1–32; the window grows its
grid to match), `inviteTtlSeconds`, `perTargetInviteCooldownSeconds`, the ingress guard
settings, and `currencyTemplate` (the same `Currency` attribute the merchant and mailbox
use; unset refuses currency offers with `NoCurrency`).

## Tests

`Assets/UnitTests/TradeSessionTests.cs` (the acceptance truth table),
`TradeExchangeTests.cs` (conservation on success and on every refusal, the room estimate,
against real `InventoryController`s), `TradePanelTests.cs` (the window on a real UI
Toolkit panel). `Assets/ZZRenderScratch/TradePanelRender.cs` renders the window with a
mock two-sided table.
