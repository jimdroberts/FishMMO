// Exposes internal members of the FishMMO.Shared assembly to the EditMode
// unit test assembly so tests can exercise production-only helpers (for
// example, Buff.DurationToTicks) instead of duplicating their formulas.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FishMMO.UnitTests")]
// The in-editor simulation harness (Assets/TestHarness) drives the deterministic
// pieces the network stack normally drives — KCCPlatform.Step and friends — so it
// gets the same internals access the unit tests have. Delete alongside that folder.
[assembly: InternalsVisibleTo("FishMMO.TestHarness")]
