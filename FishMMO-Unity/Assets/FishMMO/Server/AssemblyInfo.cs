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
// The Items module's inventory system moved out of this assembly (Phase 5, 2026-11-20); it and its tests keep the access.
[assembly: InternalsVisibleTo("FishMMO.Items.Server")]
[assembly: InternalsVisibleTo("FishMMO.Items.Tests")]
// The Combat module's system took the death, respawn and resurrect handlers out of this assembly (Phase 5, 2026-11-20).
[assembly: InternalsVisibleTo("FishMMO.Combat.Server")]
[assembly: InternalsVisibleTo("FishMMO.Combat.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Abilities.Tests")]
[assembly: InternalsVisibleTo("FishMMO.NPCs.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Pets.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Wildlife.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Commerce.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Achievements.Tests")]
[assembly: InternalsVisibleTo("FishMMO.AbilityCrafting.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Buffs.Tests")]
