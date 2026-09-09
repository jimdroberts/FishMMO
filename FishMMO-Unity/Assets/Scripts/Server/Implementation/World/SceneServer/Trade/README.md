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

## Two stages: confirm, then accept

Trading is deliberately two-stage, because a single accept only makes a last-minute switch
*unlikely* (you have to win a race against the version check), while two stages make it
*impossible*:

1. **Confirm** — "this is my final offer". One confirmation changes nothing else; the other
   party may still edit.
2. **Both confirmed → the table is FROZEN.** Every change is refused outright
   (`TradeOfferRefusal.TableLocked` → `TradeRefusalReason.TableLocked`): no item added,
   withdrawn or re-priced, on either side. Only accepting, revoking and cancelling remain.
3. **Accept** — refused entirely until the table is frozen (`NotLocked` → `NotConfirmed`), so
   an acceptance is always a decision about a settled table.

Changing your mind means **revoking**, which unfreezes the table and clears **both**
acceptances — an acceptance was consent to a frozen table, and the table is no longer frozen.
The other party's *confirmation* survives a revoke (they have not changed their mind about
their own offer); any actual change to the table then clears that too, as ever.

Both the confirm and the accept quote the `TradeStateBroadcast.Version` they consent to, so a
click already in flight when the table changed lands as a no-op rather than as consent to
something the player never saw. `TradeSession.Touch()` is the single place the invariant
lives: every mutator bumps the version and clears both confirmations and both acceptances.

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

## Completion (`TradeSystem.Commit.cs`) — the database commit is the point of truth

Nothing about a trade is final until the ONE transaction carrying both characters' halves has
committed, and memory is mutated only while that transaction holds both characters' session
row locks. That is what makes a server crash at any instant safe:

- **before the commit**: memory is lost, the database rolls back — the trade never happened
  for either side;
- **after the commit**: the database holds the whole trade for both, and the next login
  loads it.

There is no interleaving in which one side's half is durable and the other's is not, because
there is only one write and it decides. A crash can lose a trade in flight; it cannot
duplicate one.

Three hops, in this order (`ICharacterInventorySystem.TryRunExchange`):

1. **Main thread, `TryCommit`** — the session moves to `Committing` (no request from either
   party is honoured), the offered slots stay locked, nothing is mutated.
2. **Worker** — open the transaction, take BOTH session row locks in ascending id order. No
   other write for either character can commit until this transaction ends.
3. **Main thread, `ApplyExchange`, under the locks** — re-validate everything as if the
   trade were proposed now (presence, `CanAct`, scene, range, every offered slot, both
   balances, no `int` overflow on receipt); **deduct** both currency offers (an escrow — a
   concurrent spend can only spend what is left, and a refusal refunds exactly); apply the
   item exchange in memory all-or-nothing (`TradeExchange.TryApply`); hand back the rows.
   Sequences and versions are stamped HERE, so every write captured before this instant is
   older than the trade and every write captured after carries it. Every touched slot on
   both sides is locked from this instant until the outcome. **Credits are not applied
   here**: they ride in the written attribute row and land in memory only once the commit
   is known, so a refusal never has to claw back money already spent.
4. **Worker** — both characters' item rows (an item that crossed whole keeps its id and is
   re-owned by the upsert; only an item that merged entirely into a resident stack is
   deleted), both attribute sheets, the `currency_ledger` rows (`PlayerTrade`, `Absorbed`),
   commit.
5. **Main thread, `FinishExchange`** — on commit: credit both, close as `Completed` (the
   close goes BEFORE the inventory updates, because the clients still hold their local slot
   locks and their handlers refuse a locked slot), then the set/remove broadcasts. On
   refusal: `TradeExchange.Applied.Undo()` restores both bags exactly, the deductions are
   refunded, the session closes with the reason; the inventory system **voids** every batch
   captured for either character while the applied state was visible
   (`ItemWriteJournal.VoidCaptures`) — a snapshot or despawn flush captured in that window
   described a trade that never happened — and reconciles both from the restored memory.

A character that disconnects while the commit is in flight is not closed out of the session;
the outcome closes it. Its despawn flush waits on the same row lock, so it lands after the
outcome and is ordered — or voided — by the journal. A committing session whose outcome
never arrives (main thread unreachable for 60 s) is treated as refused.

Room is estimated when an offer is **confirmed** and again when it is **accepted**
(`TradeRules.HasRoomFor`), so a player short of bag space hears it while the table is still
theirs to trim rather than after both have accepted. The exchange's own all-or-nothing
refusal remains the decision.

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
