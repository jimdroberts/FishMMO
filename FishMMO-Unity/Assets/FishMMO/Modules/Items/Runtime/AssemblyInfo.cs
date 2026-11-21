using System.Runtime.CompilerServices;

// The inventory system sets the equipment's server-side request validator and reads its test seams; the windows and the
// module's tests check the containers' internals directly, as they did inside FishMMO.Shared.
[assembly: InternalsVisibleTo("FishMMO.Items.Server")]
[assembly: InternalsVisibleTo("FishMMO.Items.Client")]
[assembly: InternalsVisibleTo("FishMMO.Items.Tests")]
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
