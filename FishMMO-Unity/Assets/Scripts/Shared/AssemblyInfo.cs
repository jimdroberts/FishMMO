// Exposes internal members of the FishMMO.Shared assembly to the EditMode
// unit test assembly so tests can exercise production-only helpers (for
// example, Buff.DurationToTicks) instead of duplicating their formulas.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FishMMO.UnitTests")]
