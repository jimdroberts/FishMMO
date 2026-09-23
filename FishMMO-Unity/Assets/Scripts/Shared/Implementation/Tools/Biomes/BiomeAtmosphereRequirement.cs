using System;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Which atmospheres a biome can exist under. A mask, so a biome can name several.
	/// </summary>
	/// <remarks>
	/// The bits line up with <c>AtmosphereKind</c> so the test is one AND. A biome that leaves this
	/// at <see cref="Any"/> is saying it does not care, which is right for rock: a scree slope is a
	/// scree slope on an airless moon.
	/// </remarks>
	[Flags]
	public enum BiomeAtmosphereRequirement : byte
	{
		None = 0,
		/// <summary>Vacuum. Regolith, impact basins, rilles.</summary>
		Airless = 1 << 0,
		/// <summary>A trace atmosphere. Mars-like: dust, but no rain.</summary>
		Thin = 1 << 1,
		/// <summary>Earth-like. Everything temperate.</summary>
		Standard = 1 << 2,
		/// <summary>Crushing. Runaway greenhouse, sulphuric cloud decks.</summary>
		Thick = 1 << 3,

		/// <summary>Anything at all — the default, and correct for bare rock.</summary>
		Any = Airless | Thin | Standard | Thick,
		/// <summary>Needs air of some sort, however little.</summary>
		Breathing = Thin | Standard | Thick,
		/// <summary>Needs real air: rain, forests, anything that lives.</summary>
		Living = Standard | Thick,
	}
}
