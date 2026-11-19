using System.Runtime.CompilerServices;

// The World Simulation module (Phase 4 of the core and modules plan, 2026-11-18) left the client with internals it still uses.
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation.Client")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation.Server")]
[assembly: InternalsVisibleTo("FishMMO.WorldSimulation.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Client.Weather.Editor")]
[assembly: InternalsVisibleTo("FishMMO.Shared.Biomes.Editor")]
[assembly: InternalsVisibleTo("FishMMO.UnitTests")]
