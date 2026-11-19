using System;

using FishMMO.Shared.Weather;

namespace FishMMO.Shared.Biomes
{
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
		/// <summary>
		/// The habitat of the ground at a point from the scene's biome map, as the core's <see cref="BiomeHabitatQuery"/> asks
		/// (the spawners): false off the map or with none.
		/// </summary>
		public static bool At(UnityEngine.SceneManagement.Scene scene, UnityEngine.Vector3 position, out bool hasMap, out BiomeHabitat habitat)
		{
			habitat = BiomeHabitat.None;
			SceneBiomeMap map = WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings settings) ? settings.BiomeMap() : null;
			hasMap = map != null;
			if (map == null || !map.Contains(position))
			{
				return false;
			}
			habitat = BiomeRegistry.TryGetByID(map.IDAt(position), out BiomeTemplate biome) && biome != null ? biome.LiveHabitat : BiomeHabitat.None;
			return true;
		}

		/// <summary>The bits a template stores: every member but <see cref="BiomeHabitat.Alien"/>.</summary>
		public const BiomeHabitat Stored = ~BiomeHabitat.Alien;

		/// <summary>The bits that say a biome has dry ground to stand on (every land country but the wetland).</summary>
		public const BiomeHabitat Land = BiomeHabitat.Forest | BiomeHabitat.Woodland | BiomeHabitat.Jungle | BiomeHabitat.Grassland |
			BiomeHabitat.Farmland | BiomeHabitat.Scrub | BiomeHabitat.Desert | BiomeHabitat.Coast | BiomeHabitat.Mountain |
			BiomeHabitat.Tundra | BiomeHabitat.Ice | BiomeHabitat.Settlement | BiomeHabitat.Volcanic | BiomeHabitat.Barren;

		/// <summary>
		/// True for a water biome: sea or fresh water and no land beside it. The water bits alone do not say it, since they
		/// also name the creatures a land biome holds (a Forest is Forest | FreshWater for its streams' fish), and testing
		/// them alone refused every forest (housing lots and Moron 2's gate, 2026-11-16).
		/// </summary>
		public static bool IsWater(BiomeHabitat habitat)
		{
			return (habitat & (BiomeHabitat.Sea | BiomeHabitat.FreshWater)) != 0 && (habitat & Land) == 0;
		}

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
