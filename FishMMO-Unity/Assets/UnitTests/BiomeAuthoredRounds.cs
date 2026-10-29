using System.Collections.Generic;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The biomes whose biome-suite rounds have been AUTHORED (their spec file rewritten from the approved draft, 2026-10-26 on).
	/// </summary>
	/// <remarks>
	/// Several fixtures pin, for every biome, that its spec file gives exactly what an old code table or the biome's name decided
	/// (phase CODE-TABLE CLEARANCE: the migration onto the files changed nothing), or that no biome names a new column yet. An
	/// authored round changes those values on purpose, so those pins skip the biomes listed here and BiomeAuthoredRoundTests
	/// pins what each authored file now gives instead. A round appends its biomes' asset names (append only; one line a round).
	/// </remarks>
	public static class BiomeAuthoredRounds
	{
		private static readonly HashSet<string> names = new HashSet<string>
		{
			// Coastal Water + Ocean (round G2, 2026-10-26).
			"Coastal Water", "Ocean",
			// Tundra + Ice Sheet + Glacier (round G3, 2026-10-26).
			"Tundra", "Ice Sheet", "Glacier",
			// Forest + Woodland (round G1, 2026-10-26).
			"Forest", "Woodland",
			// Alpine Meadow + Mountain Slope + Scree (round G4, 2026-10-26).
			"Alpine Meadow", "Mountain Slope", "Scree",
			// Scrubland + Savanna (round G5, 2026-10-26).
			"Scrubland", "Savanna",
			// Grassland + Plains + Steppe + Hills (round G6, 2026-10-26).
			"Grassland", "Plains", "Steppe", "Hills",
			// Desert + Badlands + Salt Flat (round G7, 2026-10-26).
			"Desert", "Badlands", "Salt Flat",
			// Coral Reef + Beach + Abyss (round G9, 2026-10-26).
			"Coral Reef", "Beach", "Abyss",
			// Estuary + Wetlands + Mangrove (round G8, 2026-10-26).
			"Estuary", "Wetlands", "Mangrove",
			// Lake + River (round G10, 2026-10-26).
			"Lake", "River",
			// Jungle + Bamboo Forest (round G11, 2026-10-26).
			"Jungle", "Bamboo Forest",
			// Rocky Terrain + Geyser Basin (round G12, 2026-10-26).
			"Rocky Terrain", "Geyser Basin",
		};

		/// <summary>True when the biome (its asset's name) has been authored by its round.</summary>
		public static bool Contains(string biome) => biome != null && names.Contains(biome);

		/// <summary>Every authored biome's asset name.</summary>
		public static IReadOnlyCollection<string> All => names;
	}
}
