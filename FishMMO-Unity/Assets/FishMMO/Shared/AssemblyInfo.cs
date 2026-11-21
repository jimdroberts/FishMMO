// Exposes internal members of the FishMMO.Shared assembly to the EditMode
// unit test assembly so tests can exercise production-only helpers (for
// example, Buff.DurationToTicks) instead of duplicating their formulas.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FishMMO.UnitTests")]
// The in-editor simulation harness (Assets/FishMMO/Tests/TestHarness) drives the deterministic
// pieces the network stack normally drives — KCCPlatform.Step and friends — so it
// gets the same internals access the unit tests have. Delete alongside that folder.
[assembly: InternalsVisibleTo("FishMMO.TestHarness")]
// The combat sim runs NPC brains, which live in the server assembly, so it builds into the
// server-side harness assembly and needs the same access (LagCompensationTick.ClaimOverride).
[assembly: InternalsVisibleTo("FishMMO.TestHarness.Server")]
// The Ambient Life module's tests moved out of FishMMO.UnitTests (Phase 4, 2026-11-17) and keep the access they had.
[assembly: InternalsVisibleTo("FishMMO.AmbientLife.Tests")]
// The World Design module's tests moved out of FishMMO.UnitTests (Phase 4, 2026-11-18).
[assembly: InternalsVisibleTo("FishMMO.WorldDesign.Tests")]
// The Naming module's tests moved out of FishMMO.UnitTests (Phase 4, 2026-11-18).
[assembly: InternalsVisibleTo("FishMMO.Naming.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Naming")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation")]
[assembly: InternalsVisibleTo("FishMMO.Abilities")]
[assembly: InternalsVisibleTo("FishMMO.Abilities.Client")]
[assembly: InternalsVisibleTo("FishMMO.Abilities.Server")]
[assembly: InternalsVisibleTo("FishMMO.Abilities.Tests")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation.Client")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation.Server")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Client.Weather.Editor")]
[assembly: InternalsVisibleTo("FishMMO.Shared.Biomes.Editor")]
// The Items module (Phase 5, 2026-11-20): the containers and the equipment moved out of this assembly with the item
// internals they were written against (Item's write primitives, the prediction seams); its tests keep the access they had.
[assembly: InternalsVisibleTo("FishMMO.Items")]
[assembly: InternalsVisibleTo("FishMMO.Items.Server")]
[assembly: InternalsVisibleTo("FishMMO.Items.Client")]
[assembly: InternalsVisibleTo("FishMMO.Items.Tests")]
// The Combat module (Phase 5, 2026-11-20): the damage controller moved out of this assembly with the internals it was
// written against; its tests keep the access they had.
[assembly: InternalsVisibleTo("FishMMO.Combat")]
[assembly: InternalsVisibleTo("FishMMO.Combat.Server")]
[assembly: InternalsVisibleTo("FishMMO.Combat.Client")]
[assembly: InternalsVisibleTo("FishMMO.Combat.Tests")]
[assembly: InternalsVisibleTo("FishMMO.NPCs.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Pets.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Wildlife.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Commerce.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Achievements.Tests")]
[assembly: InternalsVisibleTo("FishMMO.AbilityCrafting.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Buffs")]
[assembly: InternalsVisibleTo("FishMMO.Buffs.Client")]
[assembly: InternalsVisibleTo("FishMMO.Buffs.Tests")]
