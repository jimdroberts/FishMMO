# Combat module

**Short description:** Damage, healing, death and revive. Every character's damage controller (resistances, immortality,
the combat timer, refusals, the combat reports), the scene server's combat system (what a death does, respawning at the
bind point, resurrect offers and their acceptance), and the client's combat text and death dialog.

| | |
|---|---|
| **Module id** | `combat` (`CombatModule`) |
| **Assemblies** | `FishMMO.Combat`, `.Server`, `.Client`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core (the damage contract and its static events, the damage and resistance templates, the combat and death messages, the predicted combat events, the character parts, the character system's `ICharacterDeathHost`) |
| **Depended on by** | Abilities, NPCs & AI, Pets and Wildlife by reference (since they left the core), and through Abilities every module that depends on it. Buffs, items and the world still reach damage through the core's `ICharacterDamageController`; without the module they find no controller and nothing is hurt |
| **Remove together with** | Abilities, and every module that depends on it |
| **Moved** | Out of the core (the core is shrunk to login, world and scene) |

## Folders

```
Assets/FishMMO/Modules/Combat/
├── Runtime/              FishMMO.Combat           CombatModule; CharacterDamageController (a part every character gets: players
│                                                  and NPCs), CombatEventCoalescer (a tick's combat reports merged),
│                                                  PlayerCombatTriggers (the players' combat triggers)
├── Server/               FishMMO.Combat.Server    CombatSystem (+ asset): the kill handler, respawn at the bind point,
│                                                  resurrect offers and acceptance, the death notice at spawn
├── Client/               FishMMO.Combat.Client    CombatTextDisplay (floating numbers, refusals), DeathDialogClient (the death
│                                                  notice, the dialog's fallback respawn), UITKDeathDialog
│                                                  (+ UIDeathDialog.prefab/uxml/uss)
├── Templates/            PlayerCombatTriggers.asset (the achievement counters every player runs: damage dealt,
│                         healing done, monsters killed)
├── Editor/RenderScratch/ FishMMO.Combat.RenderScratch   The death dialog in the panel renders
├── Tests/                FishMMO.Combat.Tests     The combat timer, the coalescer and refusals, the missing-health report, wiring
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: GameModule]` | `CombatModule` ("combat"). |
| `[assembly: CharacterPart]` | Every character (`ICharacter`) gets a `CharacterDamageController` as it wakes; no prefab authors one. The part hears the network start and stop (`CharacterPart.OnStartNetwork`/`OnStopNetwork`, forwarded by `BaseCharacter`), where it registers its client messages and its tick. |
| `ICharacterDamageController` and its static events | The controller implements the contract; `OnKilled`, `OnResurrected`, `OnResurrectOffered`, `OnDamaged`, `OnHealed` are raised for every listener (the module's combat system and text, and the core's AI, pets, cast bar and cloud world). |
| `ICharacterDeathHost` (server core) | The combat system asks the character system for the world's part of a death: an instance's lives and resurrection ban, ending a combat-logout linger, the pets' owners' notice, the move to the bind point (a scene-server transfer when it is in another scene), the system line. |
| `SceneServer_Systems` label | `CombatSystem.asset` is adopted by the scene server. |
| `ClientServices` | `CombatTextDisplay` ("combat.text") and `DeathDialogClient` ("combat.death"). |
| `ClientWorldGUI_Panels` label | `UIDeathDialog.prefab` is placed into the world GUI. |
| `Shared_Static_Permanent` label | `PlayerCombatTriggers.asset` is loaded at boot on every peer, so a player's controller finds it as it wakes. |

## What stays in the core

The contract (`ICharacterDamageController` and its events), `IDamageable`/`IHealable`, `CharacterEvade`, the damage and
resistance attribute templates, `PredictedCombatEvents` (the caster's own numbers, which abilities raise), the combat event
and death messages (`CombatEventBroadcast`, `DeathBroadcast`, `RespawnAtBindPointBroadcast`, `ResurrectOfferBroadcast`,
`ResurrectAcceptBroadcast`), the damage, heal, revive and knockback ECA actions and the immortal condition (they act through
the contract), the `IsDead` flag and its persistence, the combat-logout linger, instance lives and resurrection bans, the
cloud world's lift on a revive, and `ClientMilestoneDisplay` (the achievements' label, split out of the old combat
display).

## The death

### What a kill does

`CombatSystem.CharacterDamageController_OnKilled(killer, defender)`:

1. Flags a player `IsDead` (first; see below) and removes all the defender's buffs.
2. **Player:** spends an instance life where the difficulty limits them; a combat-logged body's linger ends (it was left
   exposed for exactly this); otherwise `DeathBroadcast` goes to the owner, whose client opens the death dialog. The player
   chooses: respawn at the bind point (revived at full health and moved there) or wait for a resurrect (revived where it
   lies). A player who spawns dead (reconnect-while-dead, a scene transfer) is sent `DeathBroadcast` again on
   `OnSpawnCharacter`.
3. **NPC:** `npc.Despawn()`, which enters the corpse state for `CorpseDecayDuration` before returning to the pool.
4. **Pet:** the character system's `OnPetKilled` is raised for its owner (the pet system listens), then `pet.Despawn()`.

When the bind point is in a **different scene**, respawning is a scene-server transfer the character system performs in
the teleporter's order (see its README): announce the departure, rebind, save, release, drop the connection.

### The kill handler

`CharacterDamageController_OnKilled` flags `IsDead` **first**, before any side-effecting call.
`CharacterDamageController.Kill` guards re-entry on that flag and sets it before it raises `OnKilled`; the handler sets
it again as a harmless backstop. Anything side-effecting that ran before the
flag was set executed inside a window where a second `Kill` would sail past the guard and
recurse; buff removal, which invokes each buff's removal effects, is exactly such a call.

The handler never revives, teleports, or moves the character. Bind-point movement happens only
in `OnClientRespawnAtBindPointBroadcastReceived`, in response to an explicit client request, through the character
system's `ICharacterDeathHost.RespawnAtBindPoint`.

#### Resurrect offers

`ApplyReviveAction` **offers** a resurrect to a player rather than applying one — the revive
happens only if they accept. It previously called `Revive` and *then* sent the offer, which
made the prompt meaningless and left the target with health restored but `IsDead` still set,
because only the accept handler cleared it. A player who ignored the prompt stayed in that
contradictory state indefinitely.

Offers are recorded server-side (`pendingResurrectOffers`: who offered, how much, 30 s expiry)
and raised through `ICharacterDamageController.OnResurrectOffered`, so the shared ECA action
does not need to reference server types.

`OnClientResurrectAcceptBroadcastReceived` requires a matching, unexpired recorded offer.
Previously it only checked that the named resurrector existed and shared the scene — **not that
they had offered anything** — so a dead player could revive themselves at full health at will
by naming any nearby character. The accept now applies the offer's configured amount, credited
to the actual resurrector so their ECA resurrect triggers fire, instead of a blanket full heal.
Choosing bind-point respawn declines any outstanding offer; unanswered offers expire on the
existing respawn/resurrect sweep.

### The client

`DeathDialogClient` opens `UIDeathDialog` on `DeathBroadcast`, and for a local character that starts the world dead. If
the dialog is not registered yet (the world GUI's panels are placed late), it retries for five seconds; if it never
appears, the character is respawned at its bind point rather than left stranded, with an error naming the wiring fault.
`CombatTextDisplay` draws the server's combat reports and this client's own predicted numbers, and closes the dialog on a
resurrect.

## Removing it

Delete `Assets/FishMMO/Modules/Combat` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
Characters then have no damage controller: nothing can be damaged, healed or killed, abilities and attacks land without
effect on health, and nobody dies. A character saved dead before the removal stays dead (nothing can respawn it). Combat
numbers and the death dialog are gone. The achievement counters for damage, healing and kills stop counting.

## License

This project is subject to the FishMMO project license.
