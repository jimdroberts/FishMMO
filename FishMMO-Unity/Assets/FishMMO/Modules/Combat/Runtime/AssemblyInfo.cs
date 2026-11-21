using System.Runtime.CompilerServices;

// The combat system, the client's combat text and death dialog and the module's tests reach the controller's internals,
// as they did inside FishMMO.Shared.
[assembly: InternalsVisibleTo("FishMMO.Combat.Server")]
[assembly: InternalsVisibleTo("FishMMO.Combat.Client")]
[assembly: InternalsVisibleTo("FishMMO.Combat.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Abilities.Tests")]
[assembly: InternalsVisibleTo("FishMMO.NPCs.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Pets.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Wildlife.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Commerce.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Achievements.Tests")]
[assembly: InternalsVisibleTo("FishMMO.AbilityCrafting.Tests")]
[assembly: InternalsVisibleTo("FishMMO.TestHarness")]
[assembly: InternalsVisibleTo("FishMMO.TestHarness.Server")]
[assembly: InternalsVisibleTo("FishMMO.TestHarness.Editor")]
[assembly: InternalsVisibleTo("FishMMO.UnitTests")]
[assembly: InternalsVisibleTo("FishMMO.Buffs.Tests")]
