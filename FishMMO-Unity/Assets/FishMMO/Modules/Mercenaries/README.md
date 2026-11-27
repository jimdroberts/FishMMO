# Mercenaries module

**Short description:** Sellswords for hire (issue #135). A player speaks to a Mercenary in town ("Would you like to hire
this mercenary?") and pays a fee; the mercenary follows, guards and fights for them by the pet rules with a preset kit
(Warrior, Archer, Wizard, Healer), takes a seat in their party, and wears the gear they keep for it.

| | |
|---|---|
| **Module id** | `mercenaries` (`MercenariesModule`) |
| **Assemblies** | `FishMMO.Mercenaries`, `.Server`, `.Client`, `.Editor`, `.Tests` |
| **Depends on** | The core; NPCs & AI (the companion AI, `IAICompanion`); Items (the gear container); a party module (the seat: Social); the editor assembly also on World Design (town placement) |
| **Depended on by** | Nothing |
| **Remove together with** | Nothing |
| **Added** | Issue #135 |

## How it works

- **Hiring.** Every Mercenary (`MercenaryRecruiter`, titled "Mercenary") offers one kit. Speaking to them runs the
  **Mercenary Interact** trigger, which sends the offer (`SendMercenaryOfferAction`); the window asks the question and
  shows the price. The server checks the player is at that Mercenary, has none already (one at a time, out or resting),
  leads their party if they have one, and can pay; then it takes a **party seat**, and only once the seat is held takes
  the fee (`CurrencyMovementReason.MercenaryHire`) and takes the Mercenary spoken to into their service. **The one hired
  is that one:** the mercenary appears where they stood with their name, race and face (`SceneObjectNamer.CaptureName`/
  `AdoptName`, `NPC.SetRaceTemplateOnSpawn`, `NPC.SpawnSeedOverride`; the race matters because a town's spawner may
  make its Mercenaries another race than the kit's, and the name and models are drawn from it), and the Mercenary leaves
  their post through its spawner, which sends a new sellsword there in its respawn time. One player hires a given
  Mercenary: a second hire of them while the first is being answered is refused (`MercenaryResult.RecruiterTaken`).
- **The party seat.** A mercenary takes a seat as a member does, and a party holds one. Hiring with no party forms one
  (the player leads it). The seat is on the owner's `character_party` row (`mercenary_kit_id`, `mercenary_name`), counted
  in every capacity check in the same locked statement as a join (`ICharacterPartyService.SeatMercenaryAsync`), so a
  mercenary and an invited player can never both take the last seat. The party frame lists it as its own row under the
  key `PartySeatCompanions.RosterKey(owner)` with its live vitals and buffs. **Only the leader** hires or brings one back.
- **Rest and return.** *Send to Rest* dismisses it for now: it leaves the world and frees its seat (so the party can take a
  player), keeping its health and buffs. *Return* brings it back for free, into a seat (the leader's to give). An owner
  who **leaves the party**, is removed, or joins another takes the seat with their membership row; the server's
  once-a-second check then sends the mercenary to rest.
- **Release and death.** *Release* (two presses) lets it go for good. If it **dies** it is released the same way. Either
  way its record is deleted and its seat freed; the **gear stays** with the player and goes onto the next mercenary.
- **The AI** is the pet AI (`PetIdleState`, `PetAttackingState`, `PetSupport`), driven through `IAICompanion`: it heels,
  defends its owner, and a Healer heals its owner first and then the party. **Stance** (Passive, Defensive, Aggressive)
  is set in the window; a mercenary always follows (there is no Stay or Attack order).
- **Kits are fixed** (`MercenaryKitTemplate`): the abilities, the price and the NPC. A mercenary knows exactly its kit's
  abilities. Stats are fixed per kit (the guard's numbers) and only the gear raises them.
- **Gear** is the player's own items in the `MercenaryEquipment` container (one slot per equipment socket), saved with
  their other items in one transaction (`ModuleItemContainers`). A mercenary that is out wears them
  (`ItemEquippable.Equip`), so their attributes apply to it. **Everything but a saddle** (`MercenaryRules.Wears`): a
  mercenary does not ride, so a saddle is refused and never worn (its container slot stays, unused, so the indexes stay
  the sockets'). Drag gear from the bags onto its socket, or right-click it in the bags while the window is open, to give
  it; click a socket to take it back. Gear can be given with no mercenary hired.
- **Persistence.** `character_mercenaries` (one row per character: kit, name, look and race, resting, stance, resource
  values, buffs), written by the character save (`MercenaryPersistenceParticipant`, per character) and by each hire, rest,
  return, release and death on the same lane, so it comes back as it left after a logout or a scene change.

## The window

`UIMercenary` (`UITKMercenary`), from the game menu (**Mercenary**) or opened on a hire, laid out as the player's own
character sheet (it loads `UICharacterSheet.uss` and uses its socket names and places): name, kit, out or resting; the
gear sockets around a preview of the mercenary (photographed by `EquipmentPreviewRenderer` through a camera the window
makes on the mercenary, since an NPC carries none), with no saddle socket; its attributes while it is out and in sight;
stance; *Send to Rest* / *Return*; *Release*.

## Assets

Made by **Dashboard > Maintenance > Mercenaries > Build mercenaries** (batch:
`-executeMethod FishMMO.Shared.MercenaryBuilder.BuildBatch`), idempotent:

- `Templates/Kits/<Kit> Mercenary Kit.asset` (price, abilities, NPC), `Templates/Interactions/Mercenary Interact.asset`;
- `Prefabs/a mercenary <kit>.prefab` (hired: the banker made a fighter with the guard's attributes, the pet archetype,
  the kit's abilities, the `Mercenary` script) and `Prefabs/a <kit> for hire.prefab` (the recruiter);
- `Server/MercenarySystem.asset` (`SceneServer_Systems`), `Client/UIMercenary.prefab` (`ClientWorldGUI_Panels`).

`MercenaryService` registers the recruiters with `PointOfInterestServiceNpcs`: every site that keeps a Barracks,
Guard House, Tavern or Inn keeps one of each kit there, and every capital keeps them. Cut scenes get theirs at the next
re-cut.

## Folders

```
Assets/FishMMO/Modules/Mercenaries/
├── Runtime/    FishMMO.Mercenaries          MercenariesModule (+ the two seams), MercenaryKitTemplate, Mercenary (the NPC),
│                                            MercenaryRecruiter, MercenaryController, MercenaryGearController, MercenaryRules,
│                                            MercenaryBroadcasts, ECA/SendMercenaryOfferAction
├── Server/     FishMMO.Mercenaries.Server   MercenarySystem (+ asset; Requests and Gear partials), MercenaryPersistence,
│                                            MercenaryRecords, the system's data containers
├── Client/     FishMMO.Mercenaries.Client   UITKMercenary (+ prefab, uxml, uss), MercenaryClient (menu entry, right-click)
├── Editor/     FishMMO.Mercenaries.Editor   MercenaryBuilder, MercenaryService
├── Templates/  Kits/, Interactions/Mercenary Interact
├── Prefabs/    the hired NPCs and the recruiters (made by the builder)
├── Tests/      FishMMO.Mercenaries.Tests    rules, records, wiring
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `PartySeatCompanions` (core) | Resolves a member's seated mercenary by owner ID, for the party's vitals pump and the party frame's pin. |
| `IPartySystem.TrySeatMercenaryAsync` / `UnseatMercenaryAsync` (core) | Takes and frees the seat; the Social module implements them. |
| `ModuleItemContainers` (Shared) | The gear container is loaded by the character load and saved by the item snapshot with the bags. |
| `ICharacterInventorySystem.TryMoveBetweenContainers` (core) | Moves gear between the bags and the gear container in one journalled batch. |
| `IAICompanion` (NPCs module) | The pet AI drives a `Mercenary`. |
| `NPC.CompanionOwner` (core) | Harm rule, loot rights, kill credit and party meters see through it to its owner. |
| `NPC.SpawnSeedOverride`, `SceneObjectNamer.AdoptName` (core) | The hired one keeps the face and name of the one hired. |
| `CharacterPersistenceParticipant` (Server) | `character_mercenaries` loads and saves with the character. |
| `SceneServer_Systems`, `ClientWorldGUI_Panels` labels | The system and the window load where the game looks for them. |

## Removing it

Delete `Assets/FishMMO/Modules/Mercenaries`. Nobody can hire a mercenary; parties are unchanged (no seat is ever
taken). The `character_mercenaries` table, the seat columns and any `MercenaryEquipment` item rows stay in the database,
unused (the core's item load leaves rows of a container it does not know untouched).

## License

This project is subject to the FishMMO project license.
