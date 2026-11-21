using System.Runtime.CompilerServices;

// Module test assemblies that were split out of these fixtures reuse their helpers (the biome asset scans, the YAML field
// reader, the hall planner) rather than copying them.
[assembly: InternalsVisibleTo("FishMMO.AmbientLife.Tests")]
[assembly: InternalsVisibleTo("FishMMO.WorldDesign.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Naming.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Terrain.Tests")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Abilities.Tests")]
[assembly: InternalsVisibleTo("FishMMO.NPCs.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Pets.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Wildlife.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Commerce.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Achievements.Tests")]
[assembly: InternalsVisibleTo("FishMMO.AbilityCrafting.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Buffs.Tests")]
