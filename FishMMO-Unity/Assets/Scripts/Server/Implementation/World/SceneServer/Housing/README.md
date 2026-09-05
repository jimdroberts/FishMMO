# Housing

Land ownership, building, tax, and who is allowed through the door.

The system is a `ServerBehaviour` on the scene server, split across partials by concern:

| File | Concern |
| --- | --- |
| `HousingSystem.cs` | Ownership mode, lifecycle, character-lifecycle hooks |
| `HousingSystem.Plots.cs` | Registration, resolution, claiming and purchase |
| `HousingSystem.Building.cs` | Build sessions, placement, structures |
| `HousingSystem.Tax.cs` | The recurring charge, grace, and reclamation |
| `HousingSystem.Access.cs` | Grants, revocation, and eviction |
| `HousingSystem.Vault.cs` | Where a house goes when its owner loses the land |
| `HousingSystem.Sync.cs` | Keeping plots consistent across channels |
| `HousingSystem.Network.cs` | The client-facing broadcasts |

Housing is **off by default**. `HousingOwnershipMode.Neither` means a server that has not asked
for housing carries none of its persistent world state, recurring tax, or destruction of unpaid
plots.

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
  refund every time two people wanted the same land.
- **Charge the vault fee, then remove the row.** The opposite, and for the same underlying reason: a
  vault row is contended by nobody but its owner, so the only race is a double click, and the
  removal settles it. The failure this admits — being charged for a row already gone — is refunded;
  the other order loses the furniture for free, which cannot be undone.
- **Win the right to bill, then collect.** Advancing the due date is pinned to the date the sweep
  read, so a period produces one charge however many servers sweep it. No leader, and it survives
  any of them dying.
- **Release the land, then vault its contents.** Released-then-vaulted leaves a moment where free
  land still has a house on it, which the next owner can see and report. The reverse leaves a moment
  where somebody still owns a plot whose house has silently vanished.

## Tax and grace

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

Guild-owned land is deferred rather than charged: guilds have no treasury, so collecting would mean
billing some member personally for land they do not own, and letting it run out of grace would
confiscate every guild plot on the server.

## Access

Houses are **locked by default**. A plot admits its owner, admits exactly the people the owner
named, and admits nobody else. `PlotAccess` is the single pure function that decides this, so the
server enforcing it and a client greying out a button reach the same answer.

Permissions are flags rather than a rank, because they are not ordered — trusting a friend to
redecorate is not a superset of trusting them to bring other people round. Nobody may grant a
permission they do not hold themselves (`PlotAccess.ClampGrant`); without that the model collapses
to its weakest link, since whoever can invite could invite themselves into everything.

Grants are only honoured on an **occupied** plot. Rows outlive the ownership that created them, and
clearing them is a write that can fail — so ignoring them outside the one state where they apply
makes that cleanup a tidiness matter rather than a security one.

**Eviction is half of the system.** An access rule enforced at the doorway is a rule a player
defeats by not walking through it: standing still while a friend revokes their key, or logging out
inside a house they are now barred from. Eviction runs on revocation, on a plot being claimed out
from under people, and on a sweep, so "may I be here" is the same question as "may I come in".

## The vault

Reclaiming a plot destroys something a player built and paid for. Doing that with no way back would
make one missed payment the most punishing event in the game, and would make going on holiday a
risk. What stood on the plot is moved to the owner's vault instead, where they may buy it back —
`baseFee * (1 + daysStored * rate)`, which is both an incentive to collect promptly and a gold sink
— or give it up.

The fee's base and rate are frozen onto each row rather than read from configuration, so rebalancing
what a structure costs cannot change what a player owes on something already in their vault.

## Channels

Channels are ephemeral copies of a scene and hold no world state of their own. Resolving a scene
stamps ownership and state onto its foundations, which is correct at that moment and wrong the
instant anybody claims, releases or loses a plot from anywhere else. `plot_updates` closes that gap:
every write marks its plot changed and `HousingSystem.Sync` polls for the marks — the same shape
guilds use, for the same reason.

Anything applied locally is applied to **every** loaded copy of the plot (`Registry.ForPlot`), or the
same house would be open in one channel and shut in the next.

## Not yet built

- **No client UI.** The broadcasts and the server handlers exist; nothing draws a housing panel, a
  guest list, or a vault window yet.
- **Structures are not spawned.** Placements are validated, persisted and read back, but no prefab
  is instantiated from `PlotStructureTemplate.Prefab` — a plot's contents exist in the database and
  in the server's placement cache, not yet in the world.
- **Guild land is deferred, not taxed**, and has no vault. Both wait on a guild treasury.
- **No voluntary release.** `IPlotService.ReleaseAsync` supports it; nothing calls it except the
  failed-purchase and reclamation paths.
