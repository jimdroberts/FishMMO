using System;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// How a biome's ground burns on the shared clock (<see cref="BiomeTemplate.BurnRegime"/>, 2026-10-25, phase I2t, decisions 75, 82
	/// and 84): never, the occasional spring burn of a steppe, a savanna's annual veld fires at the end of the dry season, or the blue
	/// sulphur fire that walks a brimstone plain at night. Presentation only, drawn by the client on the GPU (FishBurn.hlsl) from
	/// <see cref="FishMMO.Shared.Weather.BurnMosaic"/>: no damage, nothing on the wire.
	/// </summary>
	/// <remarks>APPEND-ONLY: the value is serialised on the templates and packed into the GPU's burn rows.</remarks>
	public enum BiomeBurnRegime : byte
	{
		/// <summary>Never burns: every biome before 2026-10-25.</summary>
		None = 0,
		/// <summary>A cell burns in about one shared year in five, in spring (decision 82, Steppe).</summary>
		Occasional = 1,
		/// <summary>Most cells burn every year, in the late dry season (decision 75, Savanna).</summary>
		Annual = 2,
		/// <summary>Blue sulphur flames over the hot ground from dusk to dawn, on breathable worlds only (decision 84, Sulphur Flats).</summary>
		Brimstone = 3,
	}

	/// <summary>Helpers for <see cref="BiomeBurnRegime"/>.</summary>
	public static class BiomeBurnRegimes
	{
		/// <summary>The highest value the GPU's rows hold (FishBurn.hlsl FISH_BURN_BRIMSTONE).</summary>
		public const BiomeBurnRegime Last = BiomeBurnRegime.Brimstone;

		/// <summary>Whether a regime burns the grass (a veld or steppe fire: <see cref="BiomeBurnRegime.Occasional"/>, <see cref="BiomeBurnRegime.Annual"/>).</summary>
		public static bool IsVeld(BiomeBurnRegime regime) => regime == BiomeBurnRegime.Occasional || regime == BiomeBurnRegime.Annual;

		/// <summary>
		/// The regime a scene draws for a biome: its own, except that sulphur burns blue only in air that holds oxygen (decision 84):
		/// on a world whose air is not breathable (<see cref="BiomeAir.IsBreathable(in BiomeWorldConditions)"/>) a Brimstone biome
		/// draws nothing. Veld fires need a world that grew grass, which the biome's own selection already asks.
		/// </summary>
		public static BiomeBurnRegime Drawn(BiomeBurnRegime regime, bool breathable)
		{
			if (regime > Last)
			{
				return BiomeBurnRegime.None;
			}
			return regime == BiomeBurnRegime.Brimstone && !breathable ? BiomeBurnRegime.None : regime;
		}

		/// <summary>The authoring mark of a regime: its name ("None" for none).</summary>
		public static string Signature(BiomeBurnRegime regime) => regime.ToString();

		/// <summary>Parses a regime by name (case-insensitive); false for anything else.</summary>
		public static bool TryParse(string text, out BiomeBurnRegime regime)
		{
			regime = BiomeBurnRegime.None;
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			foreach (BiomeBurnRegime value in (BiomeBurnRegime[])Enum.GetValues(typeof(BiomeBurnRegime)))
			{
				if (string.Equals(value.ToString(), text.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					regime = value;
					return true;
				}
			}
			return false;
		}
	}
}
