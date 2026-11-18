# Staff and Support module

**Short description:** The staff tools and player support: the `/gm` and `/admin` command sets, the staff console,
game master presence (fly, invisibility), and `/report`, `/bug`, `/helpme`, `/tickets` and the report window.

Phase 3 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), moved 2026-11-17.

| Folder | Assembly | Holds |
|---|---|---|
| Runtime/ | FishMMO.Staff | Staff console and report broadcasts, `StaffModule` |
| Server/ | FishMMO.Staff.Server | `StaffSystem` (+ asset, `SceneServer_Systems`): the command sets and their dispatcher, every `/gm` and `/admin` command, the console's requests, presence, the simulated-player tools, support tickets, reports, the world and clock reports |
| Client/ | FishMMO.Staff.Client | `UITKStaffConsole`, `StaffCommandLine`, `UITKReportPlayer` (+ prefabs), `StaffClient` ("Report" on character menus) |
| Tests/ | FishMMO.Staff.Tests | Console, command line, security, boundary, reports, support tiers, rescue and wiring tests |

## What stays in the core

- The operator command contracts other modules use: `OperatorCommand`, `IOperatorCommandHost`,
  `OperatorCommandProvider` (Housing adds `/gm plot` and `/admin plot` through it), `OperatorCommandParsing` and
  `StaffArgumentKind`. Without this module nothing hosts the sets, so no module's commands are reachable.
- `GameMasterInvisibility` (observer streaming honours it).
- What the tools act on, exposed read-only by `SceneServerSystem`: `WeatherHost`, `WorldClock`, `WeatherService`,
  `WorldSceneDetailsCache`, `ScheduledPlayerTicks`, `IdleIntervalPlayerTicks`, and `DescribeServerControl()` (its lock and
  shutdown state as report lines). Locking and shutting down still run through the server-control rows and the
  Control Panel.
- `ChatSystem.OnSendSystemMessage`, which the support commands answer through.

These were partials of `SceneServerSystem` and `ChatSystem`; they are one system now. The `/admin gold` currency is on
`StaffSystem.asset` (it was on the scene server's asset). The support ticket, report and audit tables stay in place,
unused, without the module.
