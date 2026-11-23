# Travel module

**Short description:** Discovering waypoints and fast travel between them, and the travel spells (issue #241): Portal to
Bind, Mark, Recall, Gate (to Gate Stones) and Call to Arms.

| | |
|---|---|
| **Module id** | `travel` (`TravelModule`) |
| **Assemblies** | `FishMMO.Travel`, `.Server`, `.Client`, `.Tests` |
| **Depends on** | The core (the `Waypoint` scene objects and their baked details, `CharacterTransfer`, `GateStoneChoice`, `PartySignals`). Call to Arms reaches party members on other scene servers only with the Chat module (it posts the summons); without it, only members on the caster's server are asked. |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Out of the core |

## Folders

```
Assets/FishMMO/Modules/Travel/
├── Runtime/   FishMMO.Travel          WaypointController (a CharacterPart), WaypointTravel (the rule), WaypointBroadcasts,
│                                      WaypointEventData, UnlockWaypointAction, TeleportToWaypointAction, TravelModule
│              TravelSpells/           PortalToBindAction, MarkLocationAction, RecallAction, TravelMarkController
│                                      (a CharacterPart), TravelSpellRules (the refusal order), NoTravelZone,
│                                      TravelSpellFeedback
│              Gate/                   OpenGateAction, CallToArmsAction, GateRules (access, placement, choice order),
│                                      TravelTicket (the signed ticket), GateBroadcasts, GateHost/IGateHost/GateStoneUse
│                                      (the server seam), GateController (a CharacterPart, owner client only),
│                                      TravelTicketWallet, GateVisuals (portals and lit stones, built from code)
├── Server/    FishMMO.Travel.Server   WaypointTravelSystem (+ asset), WaypointPersistence and its save participant,
│                                      MarkPersistenceParticipant (character_marks), GateSystem (declared in code)
├── Client/    FishMMO.Travel.Client   CallToArmsPrompt (the accept dialog)
├── Templates/                         Waypoint Interact (the discovery trigger); Abilities/ = the travel spells
│                                      (Portal to Bind, Mark, Recall, Gate, Call to Arms + their effects), written by
│                                      "Generate Travel Spells"
├── Tests/     FishMMO.Travel.Tests    TravelWaypointTests, TravelModuleTests, TravelSpellTests, GateTests
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: CharacterPart]` | `WaypointController` is added to every player at wake; its spawn state rides the character's payload. No prefab carries it. `TravelMarkController` (the Mark) likewise, server state only. |
| `IWaypointController` (core contract) | The controller implements it, including `RequestTravel`; the map reads it. |
| `InteractableTriggers` | `WaypointTravelSystem` attaches `Waypoint Interact` to every `Waypoint`; the core waypoint prefab names no trigger. |
| `SceneServer_Systems` label | `WaypointTravelSystem.asset` is adopted by the scene server. |
| `[assembly: CharacterPersistence]` | `WaypointPersistenceParticipant` over `character_waypoints`; `MarkPersistenceParticipant` over `character_marks` (one row per character, replaced when the mark changes; deleted with the character). |
| `[assembly: GameModule]` | `TravelModule` ("travel"). |
| `CharacterTransfer` (core) | Travel spells move the caster through the scene server's one transfer (`ICharacterTransferHost`, installed by the character system): a local move in the same scene instance, a hand-off through the world server otherwise. |
| `[assembly: ModuleServerSystem]` | `GateSystem` is created by the scene server at start; it has nothing to author, so the module ships no asset for it. |
| `GateStoneChoice` (core) | `GateController` begins and ends the Gate's wait for a stone and sends the choice; the Map module shows it. |
| `PartySignals` (server core) | Call to Arms posts its signed summons to the caster's party; the Chat module carries it to the other scene servers and hands it back. |
| `WaypointKind` (core) | A Gate Stone is a `Waypoint` of kind `GateStone`: same index space, same discovery record (`character_waypoints`), never fast travel. |

## Travel spells (issue #241)

| Spell | Effect | Notes |
|---|---|---|
| **Portal to Bind** | `PortalToBindAction` on a self-cast ability: the caster goes to its bind (home) point, the move a respawn at the bind point makes. | The escape spell: castable in combat, never checks whether the bind point is occupied; 5 s cast with `InterruptedByDamage`, so attacking the caster stops it. |
| **Mark** | `MarkLocationAction`: records where the caster stands as its one marked location (scene, channel, position, heading). | Refused in an instance, an arena, a `NoTravelZone`, and anywhere the caster could not safely be put back down (`StandingSpot`, the bindstone's rule, now in the core). 3 s cast. |
| **Recall** | `RecallAction`: sends the caster to its mark through `CharacterTransfer`. | A mark in the open-world scene the caster stands in is a local move in the caster's own channel (`RecallAction.RecallChannel`); from another scene or an instance it asks for the mark's channel, and the world server falls back to another channel of the scene when that one is gone or full. 5 s cast, `InterruptedByDamage`. |
| **Gate** | `OpenGateAction`: after a 2 s cast the map opens in gate mode; the caster chooses a discovered **Gate Stone** in any scene it has entered (the dropdown), and a portal opens in front of it to that stone for the template's `LifetimeSeconds` (default 30). | The wait lasts up to `ChooseSeconds` (60) and ends on a choice, closing the map, damage or the time running out. Anyone the template's `Access` lets in (default Anyone) walks into the portal and lands at the stone, each carrying a signed return ticket. The stone lights while a gate to it is known there and takes a ticket holder (E) back to the casting spot until the gate expires. Refused in an instance. |
| **Call to Arms** | `CallToArmsAction`: every member of the caster's party, on any scene server, is asked to come; each who accepts within `AcceptSeconds` (60) is brought to the caster's side. | Members on other servers get it through the party chat pump. Refused outside a party and in an instance. |

Every travel spell applies `TravelSpellRules.Decide` first: can act → not in an arena → not in a `NoTravelZone` → has
a destination (Mark: `DecideMark`, then not in an instance, then a safe spot; Gate and Call to Arms: `DecideOpening`, then not in an instance). Combat is not a rule. A spell may take its caster out of an instance, never into one. Housing needs no rule: its trespasser sweep moves
anyone off a plot they may not enter, an arrival included. A `NoTravelZone`
(trigger collider, or `CoversWholeScene`) blocks travel spells only; teleporters and waypoints ignore it.

The spell assets are generated by the dashboard's **Maintenance → Ability Content → Generate Travel Spells**
(`TestAbilitySuiteGenerator.Travel.cs`, core editor content that finds the Travel actions by name so it compiles without
this module) and added to the General Goods merchant. They are `AbilityTemplate`s, so without the Abilities module they
are inert assets with a missing script.

## Gate in detail

- **Gate Stones** are waypoints of kind `GateStone` (`Waypoint.kind`, baked into `SceneWaypointDetails.Kind` with the
  arrival point). Discovered by using one, like a waypoint; drawn on the map in their own tint; refused by fast travel
  (`NotATravelPoint`) and never a travel origin. The site cut puts one at the old stone sites (`GateStoneFeature`: stone
  circles, ley nexuses, henges, elf waystones, rune stones); it takes the site's unlock index, so a site has a waypoint or
  a stone. They use the waypoint prefab for now; a stone of their own look is still to be made.
- **The wait for a stone** is held by the server (`GateSystem.BeginChoice`), not the ability system: the cast is a normal
  short cast, its effect starts the wait, and the owner's map shows it (`GateChooseBroadcast` → `GateStoneChoice`). The
  ability's mana and cooldown are spent when the cast ends, so giving the choice up still costs the cast.
- **The portal** is not a networked object: the server keeps it (position, radius, access, expiry), tells the clients in
  the scene (`GatePortalBroadcast`, and again to each arrival), and checks walk-ins every 0.1 s. The client draws it
  (`GateVisuals`: the scene portals' `FishMMO/Portal` surface in glow-only mode, and a light).
- **The return ticket** (`TravelTicket`, kind `GateReturn`) names the casting spot and the stone and expires with the gate.
  It is HMAC-signed with a key every server derives from the deployment's signing-key KEK, bound to the traveller, and
  carried by the client (`TravelTicketWallet`, memory only) to every scene server it loads into. A server that verifies one
  leading to a stone in its scene lights that stone for the ticket's time. Scene servers never talk to each other and
  nothing is stored, so a stone only knows the gates its arrivals told it of: using it is for ticket holders only.
- **Within one scene instance** the stone lights at once and returns are kept by the server; this works even without the
  key. Without the key, gates to other scenes and Call to Arms are refused (logged at start).
- **Call to Arms** sends a signed summons (`TravelTicketKind.CallToArms`, bound to the party) to members on this server
  and, through `PartySignals`, to the rest. An acceptance is checked again (still in the party, can act, not in an arena
  or a no-travel zone) and moved to a spot around the caster.

## What stays in the core

The world uses waypoints without fast travel, so the core keeps the `Waypoint` scene object and its baked details
(rescue points, cloud landings, the map's landmarks), `WaypointUnlockMask` (portals), `WaypointRegistry`,
`WaypointTravelPolicy`, `WaypointSceneAudit` and the "Waypoint Travel" arrival trigger.

## Removing it

Delete `Assets/FishMMO/Modules/Travel` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
The map then finds no controller: nothing is unlocked and there is no fast travel. The `character_waypoints` table and
its rows stay as they are.

## License

This project is subject to the FishMMO project license.
