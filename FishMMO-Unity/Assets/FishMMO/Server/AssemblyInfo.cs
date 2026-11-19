// Exposes internal members of the FishMMO.Server assembly to the EditMode unit tests and to
// the server-side simulation harness, which drive the NPC brain and spawner hooks
// (AIController.Tick, SpawnerRuntime bookkeeping) that a running scene server normally drives.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FishMMO.UnitTests")]
// Delete alongside Assets/FishMMO/Tests/TestHarness.
[assembly: InternalsVisibleTo("FishMMO.TestHarness.Server")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation.Client")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation.Server")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Client.Weather.Editor")]
[assembly: InternalsVisibleTo("FishMMO.Shared.Biomes.Editor")]
