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
