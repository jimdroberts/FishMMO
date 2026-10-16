namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The part a biome plays for a scene's water and ice (<see cref="BiomeTemplate.WaterRole"/>): the biome a scene cut paints
	/// under its lakes, rivers, estuaries and dry lake beds, and the one whose ground the ice sheet is. At most one registered
	/// biome is meant to hold each role; the cut takes the first in key order (<see cref="BiomeRegistry.WithWaterRole"/>).
	/// </summary>
	/// <remarks>
	/// Until 2026-10-15 the cut found these biomes by name ("Lake", "Methane Lake", "River", "Estuary", "Salt Flat", "Ice
	/// Sheet"). The role is that decision as data: the biome art authoring writes each of those biomes its role, and a world's
	/// own lake biome is made by giving its template the role, whatever it is called. Values are stored by number: append only.
	/// </remarks>
	public enum BiomeWaterRole : byte
	{
		/// <summary>No role: the biome is chosen by climate alone.</summary>
		None = 0,
		/// <summary>The bed and shore of a lake of water, where most of a cell lies under one.</summary>
		Lake = 1,
		/// <summary>A lake of what a cryogenic world's air condenses (methane, nitrogen): taken over the water lake there.</summary>
		MethaneLake = 2,
		/// <summary>A river's channel, where most of a cell lies in one.</summary>
		River = 3,
		/// <summary>A river's channel where it meets the sea.</summary>
		Estuary = 4,
		/// <summary>A dry lake bed (a playa): salt and clay where water stood and dried.</summary>
		SaltFlat = 5,
		/// <summary>The ground of the ice that buries land whose summer never thaws (IceSheetSurface); no sheet is laid without one.</summary>
		IceSheet = 6,
	}
}
