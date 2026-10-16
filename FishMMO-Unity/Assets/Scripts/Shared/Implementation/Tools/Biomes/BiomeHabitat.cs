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
	/// never renumber a member. <see cref="Alien"/> alone is never stored: it is read live from the template's air
	/// (<see cref="BiomeHabitats.Live"/>).
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
		/// <summary>
		/// Other worlds' open ground: lifeless to the home world's animals, home to the creatures of thin and frozen air. Never
		/// stored on a template: read live from its air (<see cref="BiomeHabitats.Live"/>).
		/// </summary>
		Alien = 1 << 19,
	}

	/// <summary>
	/// The one rule for the <see cref="BiomeHabitat.Alien"/> bit (2026-10-16): ground that only forms under another world's air
	/// is alien ground, read live from the template's <see cref="BiomeTemplate.Atmosphere"/> and
	/// <see cref="BiomeTemplate.Requires"/> rather than stored, so editing a biome's air moves its creatures with it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The rule the biome's name gave before (AmbientGating.HabitatOfBiome) called ground alien when its Atmosphere left out
	/// the home world's Standard air, or when its name said so ("dust sea", "regolith", "methane", "tholin"…). Every biome the
	/// name alone made alien that still allows Standard air (Methane Lake, Tholin Plain) asks of its world a methane cycle or
	/// cryogenic air, which no Standard-air world of the home kind has; so the template's own world requirements stand in for
	/// the name: <see cref="OtherAirWorlds"/>. Lifeless ground (<see cref="BiomeHabitat.Barren"/>) is never alien, as before.
	/// A test pins the rule against the old one for every biome asset.
	/// </para>
	/// <para>
	/// The biome art authoring of 2026-10-16 (phase I4) wrote the bit onto the templates it authored; it is masked on every
	/// read (<see cref="BiomeTemplate.Habitat"/>), dropped by the next authoring and by OnValidate, and never trusted.
	/// </para>
	/// </remarks>
	public static class BiomeHabitats
	{
		/// <summary>The bits a template stores: every member but <see cref="BiomeHabitat.Alien"/>.</summary>
		public const BiomeHabitat Stored = ~BiomeHabitat.Alien;

		/// <summary>World requirements that only another world's air meets: methane weather, air cold enough to condense, frozen nitrogen.</summary>
		public const BiomeWorldRequirement OtherAirWorlds = BiomeWorldRequirement.MethaneCycle | BiomeWorldRequirement.CryogenicAir | BiomeWorldRequirement.FrozenNitrogen;

		/// <summary>
		/// Whether ground of this air and these world requirements only forms under another world's air: its atmosphere leaves
		/// out Standard (an atmosphere of none asks nothing), or it needs a world <see cref="OtherAirWorlds"/> describes.
		/// </summary>
		public static bool UnderOtherAir(BiomeAtmosphereRequirement atmosphere, BiomeWorldRequirement requires)
		{
			bool otherAir = atmosphere != BiomeAtmosphereRequirement.None && (atmosphere & BiomeAtmosphereRequirement.Standard) == 0;
			return otherAir || (requires & OtherAirWorlds) != 0;
		}

		/// <summary>
		/// The country a template offers, read live: its stored bits (any <see cref="BiomeHabitat.Alien"/> among them dropped)
		/// and Alien where the ground forms under another world's air (<see cref="UnderOtherAir"/>) and is not lifeless.
		/// </summary>
		public static BiomeHabitat Live(BiomeHabitat stored, BiomeAtmosphereRequirement atmosphere, BiomeWorldRequirement requires)
		{
			BiomeHabitat habitat = stored & Stored;
			if ((habitat & BiomeHabitat.Barren) == 0 && UnderOtherAir(atmosphere, requires))
			{
				habitat |= BiomeHabitat.Alien;
			}
			return habitat;
		}
	}
}
