# Tests

**Short description:** The core's tests and the test harness. Each module's own tests are in its `Tests/` folder
(`FishMMO.<Module>.Tests`), so removing a module removes its tests with it.

| Folder | Assembly | Holds |
|---|---|---|
| [UnitTests/](UnitTests/README.md) | `FishMMO.UnitTests` | The core's EditMode tests: prediction and combat, AI, servers, UI, persistence, rendering, the asset reference audit. |
| `UnitTests/PlayMode/` | `FishMMO.UnitTests.PlayMode` | PlayMode tests that drive the simulation scenes under a real player loop and a real FishNet server. |
| `TestHarness/` | `FishMMO.TestHarness` | The simulation harnesses: `Combat/`, `Interactable/` (`FishMMO.TestHarness.Server`), `Platform/`, `Region/`, and `Common/` (`SimServer`). |
| `TestHarness/Editor/` | `FishMMO.TestHarness.Editor` | `TestSceneGenerator` (the four simulation scenes) and `CoreGateSceneGenerator` (the core-only world). Dashboard → Maintenance → Test Scenes. |

## Running them

- **In the editor:** Window → General → Test Runner, EditMode or PlayMode.
- **In batch mode** (with the editor closed):
  `Unity -batchmode -projectPath FishMMO-Unity -runTests -testPlatform EditMode -testResults <file>`. Add
  `-testFilter <regex>` to run a subset. If a dedicated-server build ran last, add
  `-buildTarget Linux64 -standaloneBuildSubtarget Player`, or the run finds no tests.
- **The engine-free Core** is tested outside Unity: `dotnet test FishMMO-Core.Tests` at the repository root.

## Tests and modules

A core test that reads a module's files asks `InstalledModules.Has` / `Require` first, so it skips or is ignored when
that module is removed. A test assembly may reference only one optional module; a test that needs two is split at the
data between them. `AssetReferenceAuditTests` compares every asset reference with `AssetReferenceAuditBaseline.txt`;
during a module gate it accepts world scenes' missing scripts into the removed modules (see
[../README.md](../README.md#removing-a-module-the-gate)).

## License

This project is subject to the FishMMO project license.
