# Staff and Support module

**Short description:** The staff tools and player support: the `/gm` and `/admin` command sets, the staff console, game
master presence (fly, invisibility), and `/report`, `/bug`, `/helpme`, `/tickets` and the report window.

| | |
|---|---|
| **Module id** | `staff` (`StaffModule`) |
| **Assemblies** | `FishMMO.Staff`, `.Server`, `.Client`, `.Tests` |
| **Depends on** | The core; Abilities and NPCs & AI (the simulated players' brains and kits); Buffs (test regions' buff volumes) |
| **Depended on by** | None. Modules that add commands (Housing, World Simulation) do it through the core's `OperatorCommandProvider`, so they compile without this module; their commands are simply not reachable. |
| **Remove together with** | Nothing |
| **Moved** | Out of the core. These were partials of `SceneServerSystem` and `ChatSystem`; they are one system now. |

## Folders

```
Assets/FishMMO/Modules/Staff/
├── Runtime/   FishMMO.Staff          StaffConsoleBroadcasts, ReportPlayerBroadcasts, StaffModule,
│                                     TestRegions/ (StaffTestRegion + its network prefab, for /admin region)
├── Server/    FishMMO.Staff.Server   StaffSystem (+ asset): the command sets and their dispatcher (OperatorCommands),
│                                     every /gm and /admin command (character, economy, simulation, regions, time, moderation,
│                                     presence, support), the console's requests, support tickets, reports
├── Client/    FishMMO.Staff.Client   UITKStaffConsole, UITKReportPlayer (+ prefabs, uxml, uss), StaffCommandLine,
│                                     StaffConsoleFormat, StaffClient ("Report" on character menus)
├── Tests/     FishMMO.Staff.Tests    Console, command line, security, boundary, reports, support tiers, rescue and
│                                     wiring tests
└── README.md
```

## Test regions (`/admin region`)

Puts a real `Region` down around the caller to test what regions do on a live server, one kind at a time or
`all` of them in a ring. Every kind shows the "You have entered" toast with the region's name; then:

| Kind | Built from | Where it runs |
|---|---|---|
| `name [label]` | `DisplayRegionNameAction` on enter | owning client |
| `buff <buff>` | a `BuffVolume` (on while inside, off on leaving) | predicted, both peers |
| `attribute <attribute> <bonus>` | `ApplyRegionAttributeAction` on enter (released by the region on exit) | server |
| `resource <resource> <per second>` | `ApplyRegionAttributeAction` on stay, paced by `TickIntervalCondition` | server |
| `damage <per second> [damage type]` | `ApplyRegionDamageAction` on stay, paced likewise; negative heals | server |
| `fog [density]` | `ChangeFogAction` on enter, and one clearing it on exit | owning client |

The box is `WxHxD` metres anywhere in the line (`WxD` keeps the default 8 m height; default 12x8x12, at most 200 a
side), standing on the caller's feet. `list` numbers them, `clear [number]` despawns one or all. The settings ride the
spawn payload, so each client builds the same region (it needs its own: the toast and fog are client-side, and a buff
volume is tested inside the prediction replicate on both peers). Clients draw the box's edges. Administrator only: a
region that grants a buff, a bonus or healing is the same power as `/admin setattr` and `/admin heal`.

## Core seams it uses

| Seam | What the module does |
|---|---|
| `SceneServer_Systems` label | `StaffSystem.asset` is adopted by the scene server. |
| `ClientWorldGUI_Panels` label | `UIStaffConsole` and `UIReportPlayer` prefabs are placed into the world GUI. |
| `OperatorCommandProvider` | Hosts every provider's commands, its own and other modules'. |
| `ChatSystem.OnSendSystemMessage` | The support commands answer through it. |
| `CharacterSystem.OnDespawnCharacter` | A game master's presence state is dropped when the character despawns. |
| `CharacterMenuEntries` | "Report" on character menus. |
| `[assembly: GameModule]` | `StaffModule` ("staff"). |

## What stays in the core

- The operator command contracts other modules use: `OperatorCommand`, `IOperatorCommandHost`,
  `OperatorCommandProvider`, `OperatorCommandParsing` and `StaffArgumentKind`.
- `GameMasterInvisibility` (observer streaming honours it).
- What the tools act on, exposed read-only by `SceneServerSystem`: `WorldClock`, `WorldSceneDetailsCache`,
  `ScheduledPlayerTicks`, `IdleIntervalPlayerTicks`, and `DescribeServerControl()` (the lock and shutdown state as
  report lines). Locking and shutting down still run through the server-control rows and the Control Panel.

## Removing it

Delete `Assets/FishMMO/Modules/Staff` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
Nothing then hosts the command sets, so no `/gm` or `/admin` command from any module is reachable, and there is no
console or report window. The `/admin gold` currency setting lives on `StaffSystem.asset`. The support ticket, report and
audit tables stay in place, unused.

## License

This project is subject to the FishMMO project license.
