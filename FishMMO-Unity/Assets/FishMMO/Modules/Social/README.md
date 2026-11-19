# Social module

**Short description:** Friends, party and guild: the player-to-player relationships, as one opt-in module. Chat is its
own core module, so a game can have chat without Social.

| | |
|---|---|
| **Module id** | `social` (`SocialModule`) |
| **Assemblies** | `FishMMO.Social`, `.Server`, `.Client`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core |
| **Depended on by** | None. Matchmaking, Factions, Housing and chat read parties and guilds through core contracts. |
| **Remove together with** | Nothing |
| **Moved** | Phase 3 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), 2026-11-16 |

## Folders

```
Assets/FishMMO/Modules/Social/
├── Runtime/   FishMMO.Social          FriendController, PartyController, GuildController (CharacterParts); IFriendController,
│                                      IPartyMembership, IGuildMembership; broadcasts; PartyVitalsSerializer; guild ranks
│                                      and permissions; Party/GuildEventData; SocialModule
├── Server/    FishMMO.Social.Server   FriendSystem, PartySystem, GuildSystem (+ their assets, runtime data, contracts);
│                                      Friend/Party/GuildPersistence; UpdatePumpWatermark; the guild roster delta
├── Client/    FishMMO.Social.Client   UITKFriendList, UITKParty, UITKGuild (+ prefabs, uxml, uss); SocialClient
├── Editor/RenderScratch/              Fakes, fixtures and the three windows for the panel renders and HUD probes
├── Tests/     FishMMO.Social.Tests    Module, party, pump and guild tests
└── README.md
```

Feature documents: [Runtime/README.md](Runtime/README.md) and [Server/README.md](Server/README.md) (friends),
[Runtime/GUILD.md](Runtime/GUILD.md) and [Server/GUILD.md](Server/GUILD.md) (guilds), [Server/PARTY.md](Server/PARTY.md)
(parties).

## Core seams it uses

| Seam | Where | What the module does |
|---|---|---|
| Character parts | `[assembly: CharacterPart]` (Shared) | Every player gets Friend, Party and Guild controllers at wake; no prefab carries them. The guild ID rides the spawn payload to observers, and `GuildIdentityBroadcast` carries a change (it was a SyncVar). |
| Character load | `CharacterPersistenceParticipant` (Server) | Friends, party membership (dropping one from another world server, as before) and guild membership load through the module. All three are written when they change, not by the character save. |
| Server systems | `Server.AdoptModuleSystems` | `FriendSystem`, `PartySystem`, `GuildSystem` assets carry `SceneServer_Systems`. Each sends a spawned player its list or roster. |
| Windows | `ModulePanelHost` (Client) | `UIFriendList`, `UIParty`, `UIGuild` prefabs carry `ClientWorldGUI_Panels`. The Friends, Party and Guild keys still toggle them by name. |
| Character menus | `CharacterMenuEntries` (Client) | "Add Friend" everywhere, "Invite to Party" on a player in the world. |
| Achievements | `Accomplishments.Record` | Adding a friend, creating or joining a party or guild. |
| Module list | `[assembly: GameModule]` (Core) | `SocialModule` ("social"). |

## What stays in the core

Other features ask about parties and guilds, so the core keeps read-only contracts that the module implements:

| Contract | Read by | Module side |
|---|---|---|
| `IPartyController` (ID, Rank, roster events) | group and dungeon finder, arenas, instances, chat, map, harm rule, observer streaming, Factions | `IPartyMembership` (setters, invites, vitals, triggers) |
| `IPartySystem` (join, create, remove, `ShowMembership`, `ClearMembership`) | group and dungeon finder | `PartySystem` |
| `IPartyCharacterMappingData` | chat, arenas | `PartyCharacterMappingData` |
| `IGuildController` (ID, roster events by id, guild info and message of the day, static `OnReadID`) | chat, map, nameplates, housing access, harm rule, observer streaming, Factions | `IGuildMembership` (setters, standing, panel events, triggers) |
| `IGuildCharacterMappingData` (online members) | chat | `IGuildRosterMappingData` (+ the delivered roster) |
| `PartyRank`, `HasPartyCondition`, `HasGuildCondition` | ECA, arenas | |

## Removing it

Delete `Assets/FishMMO/Modules/Social` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
No character then has a friend list, a party or a guild, and every read of those contracts finds none. The friend,
party and guild tables and their services stay in place, unused.

## License

This project is subject to the FishMMO project license.
