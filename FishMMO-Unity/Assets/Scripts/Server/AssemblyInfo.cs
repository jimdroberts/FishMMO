// Exposes internal members of the FishMMO.Server assembly to the EditMode unit tests and to
// the server-side simulation harness, which drive the NPC brain and spawner hooks
// (AIController.Tick, SpawnerRuntime bookkeeping) that a running scene server normally drives.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FishMMO.UnitTests")]
// Delete alongside Assets/TestHarness.
[assembly: InternalsVisibleTo("FishMMO.TestHarness.Server")]
