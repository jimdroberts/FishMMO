using System;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The kinds of country a biome offers the ambient creatures (birds, bats, small animals, insects) that live on it, as
	/// its template names them (<see cref="BiomeTemplate.Habitat"/>). The same members, by the same bits, as the client's own
	/// AmbientHabitat, which a test keeps equal: the client reads the template's value as its own.
	/// </summary>
	/// <remarks>
	/// Until 2026-10-16 the client read a biome's country from keywords in its name (AmbientGating.HabitatOf: "forest",
	/// "marsh", "coast"…), and called ground under another world's air alien as well. The biome art authoring now writes that
	/// reading onto every template from the spec table, so a biome's animals are its template's to decide. Stored by number:
	/// never renumber a member.
	/// </remarks>
	[Flags]
	public enum BiomeHabitat
	{
		None = 0,
		Forest = 1 << 0,
		Woodland = 1 << 1,
		Jungle = 1 << 2,
		Grassland = 1 << 3,
		Farmland = 1 << 4,
		Scrub = 1 << 5,
		Wetland = 1 << 6,
		Desert = 1 << 7,
		/// <summary>Beaches, rocky shores, estuaries: where land meets the sea.</summary>
		Coast = 1 << 8,
		/// <summary>Open sea.</summary>
		Sea = 1 << 9,
		/// <summary>Lakes and rivers.</summary>
		FreshWater = 1 << 10,
		Mountain = 1 << 11,
		Tundra = 1 << 12,
		/// <summary>Snow fields, glaciers, ice sheets: nothing lives on them but a passing seabird.</summary>
		Ice = 1 << 13,
		Settlement = 1 << 14,
		Cave = 1 << 15,
		Volcanic = 1 << 16,
		/// <summary>No life at all: molten rock, poison, radiation, the wasteland.</summary>
		Barren = 1 << 17,
		/// <summary>Ruins, graveyards and abandoned farms (a point of interest adds it where it stands; no biome is one).</summary>
		Ruins = 1 << 18,
		/// <summary>Other worlds' open ground: lifeless to the home world's animals, home to the creatures of thin and frozen air.</summary>
		Alien = 1 << 19,
	}
}
