# Housing

Land ownership, building, tax, and who is allowed through the door.

`HousingSystem` is a `ServerBehaviour` ScriptableObject asset on the scene server
(`FishMMO/Server/SceneServer/Housing System`, instance `Assets/Prefabs/Server/SceneServer/HousingSystem.asset`,
listed in `SceneServer.unity`'s `serverBehaviors`), split across partials by concern. It declares
`[RequiresDataContainer]` for `HousingSystemMainThreadQueueData` and `AsyncWorkerData`, so every
database answer comes back through the main-thread queue:

| File | Concern |
| --- | --- |
| `HousingSystem.cs` | Ownership mode, lifecycle, character-lifecycle hooks |
| `HousingSystem.Plots.cs` | Registration, resolution, claiming and purchase |
| `HousingSystem.Building.cs` | Build sessions, placement, structures |
| `HousingSystem.Tax.cs` | The recurring charge, grace, and reclamation |
| `HousingSystem.Access.cs` | Grants, revocation, and eviction |
| `HousingSystem.Vault.cs` | Where a house goes when its owner loses the land |
| `HousingSystem.Sync.cs` | Keeping plots consistent across channels |
| `HousingSystem.Network.cs` | The client-facing broadcasts and the ingress guard |
| `HousingSystemMainThreadQueueData.cs` | The concrete main-thread action queue container |
| `PlotTaxRouting.cs` | Pure: which path a due plot takes (held here, offline batch, reclaim, defer) |
| `PlotSyncWindow.cs` | Pure: where each `plot_updates` poll starts, and when it may move on |

`PlotTaxBilling` / `PlotTaxBill` (Shared, `PlotTaxBill.cs`, beside `PlotTaxDecision`) is the pure
rule for what a due plot is billed.

`InitializeOnce` does nothing but log when housing is off; when it is on it runs
`RandomiseSweepPhases`, `SubscribeToPlots`, `RegisterHousingBroadcasts` and
`SubscribeToCharacterLifecycle`. That last one hooks `ICharacterSystem.OnDisconnect` and
`OnDespawnCharacter` to `EndBuildingFor`, so a build session cannot outlive the character holding
it -- a disconnect is a player leaving, a despawn is a hand-off to another scene server, and both
would otherwise leave the plot shut until the sweep timed it out. `OnDeinitialize` resets the
sweeps' in-flight flags, sync watermarks and epochs, because the asset's fields outlive a play
session in the editor.

Housing is **off by default**. `HousingOwnershipMode.Neither` means a server that has not asked
for housing carries none of its persistent world state, recurring tax, or destruction of unpaid
plots. The `HousingSystem.asset` listed in `SceneServer.unity` is left at `Neither`; turning
housing on is a change to that asset.

A scene whose plots cannot be resolved yet is retried on a timer, never every frame: 30 s after a
database failure, 5 s after the async worker refused the work, 1 s while the scene still waits for
its instance details.

## The wire

Ten broadcasts, all registered `requireAuthentication: true`:
`HousingBeginBuildingBroadcast`, `HousingEndBuildingBroadcast`, `HousingFinishBuildingBroadcast`,
`HousingPlaceStructureBroadcast`, `HousingRemoveStructureBroadcast`, `HousingGrantAccessBroadcast`,
`HousingRevokeAccessBroadcast`, `HousingVaultRequestBroadcast`, `HousingVaultRetrieveBroadcast`,
`HousingVaultForfeitBroadcast`.

Claiming is **not** among them. A plot is bought by interacting with its foundation, through the
ECA action `ClaimPlotAction` -- so the same interaction pipeline that fronts merchants and
dialogue fronts land purchase, and the client needs no housing-specific request to buy.

## What identifies a plot

A plot is a foundation a designer placed in a scene. Its geometry is part of the scene asset, so
none of it is stored or synchronised — only ownership is.

That leaves the question of what a stored row points at, and the answer is deliberately **not** a
scene object identifier: those are handed out fresh on every scene load and never persisted, so a
row keyed by one would attach to a different foundation after a restart. It is not a scene
*instance* either, because channels are several live copies of one scene and a plot is meant to
look the same in all of them.

What is left is **world server + scene name + an authored key**, which is stable across reloads and
identical on every scene server in the cluster. See `PlotIdentity`.

## Lifecycle

```
[empty] --claim--> [building] --finished--> [occupied]
                                                |
                                       tax unpaid past grace
                                                |
                                                v
[building] <--claim-- [abandoned]  (contents moved to the owner's vault)
```

`PlotState` is persisted, not derived. Ownership alone cannot tell the two unowned states apart —
land nobody has ever claimed and land somebody stopped paying for both read as owner zero, and they
are not the same place. One is a bare lot; the other is a house standing empty.

## The orderings that matter

Several operations here are two steps that can fail between, and in each case the order was chosen
against a specific failure. They are not interchangeable.

- **Claim the plot, then take the money.** The plot is the contended thing — two players on two
  scene servers can want it in the same second — so the atomic step goes first and the common
  failure, losing the race, costs the loser nothing and needs no refund. Charging first would run a
  refund every time two people wanted the same land. The claim clears the last owner's guest list
  and anything left standing **in the same transaction** that takes the plot: as separate writes
  after it, a failure left the claim standing with the old keys under it.
- **Charge the vault fee, then remove the row, then hand over** — once retrieval opens. A vault row
  is contended by nobody but its owner, so the only race is a double click, and the removal settles
  it; a fee taken for a row already gone is refunded. Retrieval is refused for now, because there is
  nowhere yet to hand a stored structure to — it used to charge and delete the row and give nothing.
- **Win the right to bill, then collect.** Advancing the due date is pinned to the date the sweep
  read, so a bill produces one charge however many servers sweep it. No leader, and it survives
  any of them dying. Any earlier missed-payment mark comes off in the **same commit** as the payment
  (offline) or the bill win (online, put back with its original date if they then cannot pay) —
  never in a write after it, because the sweep reclaims on that mark alone.
- **Release the land and vault its contents in one transaction**, with the guest list. Released
  first and vaulted by a later write, a failed vault left a house on claimable land, and the claim
  cleared it — demolished, with nothing in the vault.

## Tuning (the `HousingSystem` asset)

| Field | Default | Concern |
| --- | --- | --- |
| `ownershipMode` | `Neither` | Who may own land; `Neither` disables housing entirely |
| `currencyTemplate` | -- | The `Currency` attribute purchases and fees are charged against |
| `taxPerPeriod` | 0 | The recurring charge |
| `taxPeriodDays` | 7 | How often it falls due |
| `taxGraceDays` | 14 | How long past the **first** missed payment before reclamation |
| `taxSweepIntervalSeconds` | 300 | How often this server looks for due plots |
| `vaultBaseFee` | 100 | The `baseFee` in the retrieval formula |
| `vaultFeePercentPerDay` | 10 | The `rate` in the retrieval formula |
| `plotSyncIntervalSeconds` | 10 | How often `plot_updates` is polled |
| `accessSweepIntervalSeconds` | 0.5 | How often standing occupants are re-checked for eviction |
| `housingDebounceMilliseconds` | 250 | Per-request ingress debounce |
| `housingGlobalRateMilliseconds` | 50 | Per-connection floor between any two housing requests |

## Tax and grace

A plot is billed for **every period that has fallen due, at once** (`PlotTaxBilling`). One that is
several periods behind — its world unhosted for a while, every server down across a billing date —
used to be billed one period per sweep and stayed due until it caught up. The sweep prices each due
plot once and hands the same bill to whichever path charges it, so the in-memory and the batched
charge cannot disagree about how many periods are owed or where the date goes next. A bill is paid
**in full or not at all**: an owner who cannot cover the whole amount is marked unpaid for all of
it, with grace running from the earliest period in it, and the date still moves past every period
it covered. Guild land is deferred past them all the same way.

Grace runs from the **first missed payment**, not from the current due date. The due date has to
advance on every billing *attempt* — that is the pin that stops two servers charging the same period
— so it moves whether or not money was collected. Measuring grace against it would mean a plot never
looked more than one period overdue, and nothing would ever be reclaimed. `TaxDelinquentSinceUtc`
is what the clock actually runs from; `PlotTaxDecision` holds the rule and is tested directly.

An owner who is logged in is charged through their in-memory balance, and one who is not is charged
by writing their stored row. This is not an optimisation. A logged-in character's balance lives in
their attribute controller and is what gets written out on their next save, so deducting from the
row underneath them would be silently overwritten — the money would come back, the plot would show
as paid, and they would never have seen the charge.

That in-memory charge, like a land purchase, is persisted by an ownership-gated write
(`TryPersistCurrency`, `PersistOwnedAsync`): it quotes the session claim this server holds for the
character and lands only while that claim is still held. With no claim nothing is written — the
purchase is refused and refunded, and the tax charge is tried again. The debits from the stored row
(the offline batch, and a won bill whose owner logged out before the charge) carry no claim by
design: they bill a character no server holds, and assert exactly that under the row lock.

Guild-owned land is deferred rather than charged: guilds have no treasury, so collecting would mean
billing some member personally for land they do not own, and letting it run out of grace would
confiscate every guild plot on the server.

### Who bills whom

That rule decides who may bill a plot, and it is not "whoever hosts the scene". Sweeps
(`HousingSystem.Tax`) run one at a time per server: the first at a random point 10-40 s after
startup, so bills that piled up while a world was unhosted are collected at once and servers
started together do not sweep in step (retried every 15 s while no plot scene has resolved and
nobody is held yet), then every `taxSweepIntervalSeconds`. Each sweep has two halves, routed by
`PlotTaxRouting`:

- **The holder's half.** A server first bills the due plots of the characters *it holds*, by owner
  and in any world (`IPlotService.FetchTaxDueForOwnersAsync`), through their in-memory balance. A
  server only sweeps worlds whose plot scenes it has loaded, so an owner playing in a dungeon on a
  server that hosts none of their world's scenes used to be billed by nobody.
- **The world's half.** It then pages through the due plots of each world whose plot scenes it has
  resolved, 256 plots a page by a `(tax_due_utc, id)` keyset and at most 16 pages (4,096 plots) a
  world per sweep, the rest waiting for the next sweep. Every owner on a page it does not hold goes
  to one batched transaction for the page (`IPlotService.ChargeTaxOfflineAsync`). That transaction
  locks the owners it can (`SKIP LOCKED`: a row another transaction holds is skipped, never waited
  on, so it cannot deadlock or convoy behind logins and saves), charges only those no server holds,
  wins each bill by locking the plot that still holds the date the sweep read, and settles the
  missed-payment mark and the ledger row in the same commit. A bill produces one charge however many servers sweep it, and a retry after a lost commit
  reply changes nothing.

A plot whose owner some *other* server holds is not charged by the batch; it is deferred
(`plots.tax_next_attempt_utc`, 15 minutes) so it stops heading every server's page while its
holder bills it. A plot whose owner's character is **deleted** (or gone) cannot pay and is marked
unpaid like any other miss, so it reaches the end of its grace and is vaulted rather than staying
owned forever. A misconfigured `currencyTemplate` stops the sweep outright rather than marking
every owner unpaid.

## Access

Houses are **locked by default**. A plot admits its owner, admits exactly the people the owner
named, and admits nobody else. `PlotAccess` is the single pure function that decides this, so the
server enforcing it and a client greying out a button reach the same answer.

Permissions are flags rather than a rank, because they are not ordered — trusting a friend to
redecorate is not a superset of trusting them to bring other people round. Nobody may grant a
permission they do not hold themselves (`PlotAccess.ClampGrant`); without that the model collapses
to its weakest link, since whoever can invite could invite themselves into everything.

Grants are only honoured on an **occupied** plot, and a grant is honoured whoever issued it. Rows
outlive the ownership that created them, so a change of hands clears them inside the transaction
that makes it — a claim, or a reclamation — rather than trusting a later write: a stale list on a new
owner's plot would open their house to the last owner's friends the moment it was finished.

**Eviction is half of the system.** An access rule enforced at the doorway is a rule a player
defeats by not walking through it: standing still while a friend revokes their key, or logging out
inside a house they are now barred from. Eviction runs on revocation, on a plot being claimed out
from under people, and on a sweep, so "may I be here" is the same question as "may I come in".

The sweep (every `accessSweepIntervalSeconds`) walks scenes, not players. A scene whose plots are
all empty lots is skipped before anyone in it is looked at. Otherwise its occupants are read once
per sweep — who is there from FishNet's connection set for the scene (the same set scene-wide
broadcasts use), mapped to characters through the character system's mapping container, and where
each one stands — and that snapshot is tested against every plot in the scene; an evicted
occupant's position is updated to the exit, so the next plot sees where they are now. A batch of
changed plots from a cross-channel poll reads each scene's occupants once, however many of its
plots changed.

## The vault

Reclaiming a plot destroys something a player built and paid for. Doing that with no way back would
make one missed payment the most punishing event in the game, and would make going on holiday a
risk. What stood on the plot is moved to the owner's vault instead, where they will be able to buy it
back — `baseFee * (1 + daysStored * rate)`, which is both an incentive to collect promptly and a gold
sink — or give it up. The fee is quoted and forfeiting works; buying back waits on a destination for
a stored structure (see below).

The fee's base and rate are frozen onto each row rather than read from configuration, so rebalancing
what a structure costs cannot change what a player owes on something already in their vault.

## Channels

Channels are ephemeral copies of a scene and hold no world state of their own. Resolving a scene
stamps ownership and state onto its foundations, which is correct at that moment and wrong the
instant anybody claims, releases or loses a plot from anywhere else. `plot_updates` closes that gap:
every write that changes what a channel shows — the owner, the state, the structures, the guest
list — marks its plot changed, and `HousingSystem.Sync` polls for the marks — the same shape guilds
use, for the same reason. The mark is the *only* way another channel hears of a change, so a mark
that fails is logged as an error, and each world's poll window moves on only once a poll has read
and applied everything it found.

Tax does **not** mark a plot. A payment, a miss, a deferral or a moved due date changes nothing a
channel draws (`ApplyChangedPlots` reads only the owner, the state and the grants; no foundation or
client message carries tax state), so a mark only made every server showing the plot re-read it and
its guest list to change nothing. Reclamation changes the owner and marks the plot where it
happens.

The window is measured on the **database** clock (`PlotSyncWindow`): marks are stamped by it, and
each poll reports the database time it was taken at. A window on the poller's own clock lost every
change stamped within however far that clock ran ahead. Each poll starts 10 s before the last
complete poll's database time, for a mark stamped before that poll and committed after it; applying
a change twice is harmless. A poll reads exactly the plots whose marks it found, by ID, and a world
has at most one poll in flight. Resolving a scene restarts its world's window (and bumps its epoch,
so a poll already out cannot put the old window back), so the first poll after it reads every mark
and nothing between the resolve's read and the window is lost. Polls start at a random point in
`plotSyncIntervalSeconds`, so the cluster's scene servers do not poll in the same second.

Anything applied locally is applied to **every** loaded copy of the plot (`Registry.ForPlot`), or the
same house would be open in one channel and shut in the next.

## Not yet built

- **No client UI.** The broadcasts and the server handlers exist; nothing draws a housing panel, a
  guest list, or a vault window yet.
- **Structures are not spawned.** Placements are validated, persisted and read back, but no prefab
  is instantiated from `PlotStructureTemplate.Prefab` — a plot's contents exist in the database and
  in the server's placement cache, not yet in the world.
- **Vault retrieval.** A vault row is a structure template and a count, and structures are neither
  items nor carried, so there is nowhere to hand one back to. `RetrieveFromVault` answers `Failed`
  without charging until there is.
- **Guild land is deferred, not taxed**, and has no vault. Both wait on a guild treasury.
- **No voluntary release.** `IPlotService.ReleaseAsync` supports it; nothing calls it except the
  failed-purchase and reclamation paths.
