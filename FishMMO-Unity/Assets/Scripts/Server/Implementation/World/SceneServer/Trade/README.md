# Trade System

Player-to-player trading of items and currency (issue #144). One session between two
characters: each puts inventory items and currency on the table, both confirm it, both accept
the same version of the frozen table, and the exchange is applied as one in-memory step and
one database commit.

## Files

| File | What it holds |
|---|---|
| `TradeSystem.cs` | Lifecycle, configuration, invitations, the range/state tick, session open/close. |
| `TradeSystem.Handlers.cs` | The eight broadcast handlers: `TradeRequestBroadcast`, `TradeRequestResponseBroadcast`, `TradeOfferItemBroadcast`, `TradeWithdrawItemBroadcast`, `TradeSetCurrencyBroadcast`, `TradeConfirmBroadcast` (confirm/revoke), `TradeAcceptBroadcast` (accept/un-accept), `TradeCancelBroadcast`. Every refusal is answered with a `TradeRefusedBroadcast` and the current table. |
| `TradeSystem.Commit.cs` | Completion: re-validation, the currency settlement, in-memory exchange, one persistence unit, client notification. |
| `TradeCurrencySettlement.cs` | Pure attribute arithmetic for the currency half: both payments taken and both credits given (held) at the apply, closed exactly with the outcome. |
| `TradeSession.cs` | Pure state, the `TradePhase`, and the confirm/accept rules (`TryAddOffer`, `TryRemoveOffer`, `TrySetCurrency`, `TryConfirm`, `TryAccept`, `TryBeginCommit`). **Every change clears both confirmations and both acceptances and bumps the version; a confirm and an accept must each quote the current version.** It also carries the `CommitState` the commit hangs its currency settlement, its `TradeExchange.Applied` and its failure reason on. |
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

Five hops, in this order (`ICharacterInventorySystem.TryRunExchange`, passed the tag
`"PlayerTrade"`):

1. **Main thread, `TryCommit`** — the session moves to `Committing` (no request from either
   party is honoured), the offered slots stay locked, nothing is mutated.
2. **Worker** — open the transaction, take BOTH session row locks in ascending id order. No
   other write for either character can commit until this transaction ends.
3. **Main thread, `ApplyExchange`, under the locks** — re-validate everything as if the
   trade were proposed now (presence, `CanAct`, scene, range, every offered slot, both
   balances, no `int` overflow on receipt); open the **currency settlement**
   (`TradeCurrencySettlement.TryOpen`): take both payments AND give both credits in memory,
   the credits **held**; apply the item exchange in memory all-or-nothing
   (`TradeExchange.TryApply`); hand back the rows. Sequences and versions are stamped HERE,
   so every write captured before this instant is older than the trade and every write
   captured after carries it — currency included. Every touched slot on both sides is locked
   from this instant until the outcome.
4. **Worker** — both characters' item rows (an item that crossed whole keeps its id and is
   re-owned by the upsert; only an item that merged entirely into a resident stack is
   deleted), both attribute sheets, the `currency_ledger` rows (`PlayerTrade`, `Absorbed`),
   commit.
5. **Main thread, `FinishExchange`** — on commit: release the holds, close as `Completed` (the
   close goes BEFORE the inventory updates, because the clients still hold their local slot
   locks and their handlers refuse a locked slot), then the set/remove broadcasts. On
   refusal: `TradeExchange.Applied.Undo()` restores both bags exactly, the settlement takes
   back both credits and refunds both payments exactly, the session closes with the reason; the inventory system **voids** every batch
   captured for either character while the applied state was visible
   (`ItemWriteJournal.VoidCaptures`) — a snapshot or despawn flush captured in that window
   described a trade that never happened — and reconciles both from the restored memory.

A character that disconnects while the commit is in flight is not closed out of the session;
the outcome closes it. Its despawn flush waits on the same row lock, so it lands after the
outcome and is ordered — or voided — by the journal. A committing session whose outcome
never arrives (main thread unreachable for `CommitTimeoutSeconds`, 60 s) is treated as refused.

Room is estimated when an offer is **confirmed** and again when it is **accepted**
(`TradeRules.HasRoomFor`), so a player short of bag space hears it while the table is still
theirs to trim rather than after both have accepted. The exchange's own all-or-nothing
refusal remains the decision.

### Currency: credited at the apply, held until the outcome

Credits used to reach memory only in the finish hop, after the commit, while the transaction
wrote the payee's currency row as "memory plus the credit". Every other capture of that
attribute in between — the periodic save, another item batch's full sheet (which queues on the
same row lock and lands straight after the commit), a merchant's write — carried a newer
version with no credit and overwrote the credited row. The items had moved and the seller's
coin was missing from the database until the next save; a crash in that interval lost it.

Now memory holds exactly what the transaction writes from the apply onwards, so no capture
can lack the credit. Exact undo — the reason the credit was deferred — is kept by
`CharacterAttribute.CreditHeld`: the credit is in the balance but `CharacterCurrency.TrySpend`
spends only `Value - HeldValue`, so a refusal can always take it back. Earning is unaffected,
and `CanAfford` still reports the whole balance (a hold means "not now", not "too poor").

While a trade settles, both currency attributes are `IsSettling`, and the character's own
saves (`CharacterSystem.AppendAttributeData`: periodic, despawn, linger, shutdown) leave them
dirty for the pass after the outcome. Without that, a periodic save could land the moved
balances while the transaction was still undecided, and a refusal would leave the database
showing a payment or a credit for a trade that never happened — for a departing character,
with no later save to put it right. The settlement is closed through the attribute references
and tokens the apply took, so a party in combat-logout linger is still restored, and a party
whose pooled object was reset for somebody else is left alone.

Not covered, and reported rather than engineered: a player-initiated currency write by one of
the two parties inside the settlement window (a merchant, mail or loot write captures the whole
sheet, holds included), followed by a refused commit and a crash before the next save.

## Tuning (the `TradeSystem` asset)

`maxTradeDistance` (clamped by `TradeRules.ClampMaxDistance`, floor
`TradeRules.MinimumMaxDistance` = 2 m, default `DefaultMaxDistance` = 15 m),
`rangeCheckIntervalSeconds` (floor 0.05 s), `maxOfferSlots` (`TradeRules.ClampMaxOfferSlots`,
default 8 = the window's 4x2 grid, ceiling `MaximumOfferSlots` = 32; the window grows its grid
to match), `inviteTtlSeconds`, `perTargetInviteCooldownSeconds`, `maxMainThreadActionsPerFrame`,
the ingress guard settings (`ingressDebounceMilliseconds`, `ingressSweepIntervalSeconds`,
`ingressEntryTtlSeconds`, `ingressSweepMaxRemovals`), and `currencyTemplate` (the same `Currency` attribute the merchant and mailbox
use; unset refuses currency offers with `NoCurrency`).

## Tests

`Assets/UnitTests/TradeSessionTests.cs` (the acceptance truth table),
`TradeExchangeTests.cs` (conservation on success and on every refusal, the room estimate,
against real `InventoryController`s), `Currency/TradeCurrencySettlementTests.cs` (the currency
half: credited at the apply, a held credit unspendable, exact reversal, a reset attribute left
alone, and source pins on the save path's settling skip), `TradePanelTests.cs` (the window on a
real UI Toolkit panel). `Assets/ZZRenderScratch/TradePanelRender.cs` renders the window with a
mock two-sided table.
