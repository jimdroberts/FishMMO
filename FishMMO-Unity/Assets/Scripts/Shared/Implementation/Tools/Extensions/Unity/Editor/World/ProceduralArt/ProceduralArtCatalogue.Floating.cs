#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The plants floating on still fresh water (2026-10-18, phase I2i, decision 27): their detail entries (built by
	/// <see cref="FloatingPlantMeshes"/>), their kinds' traits, and the prefab each <see cref="BiomeFloatingPlantKind"/> is laid as.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Details by recipe, props by use.</b> Each is a detail entry like any other (mesh, material and prefab named
	/// <c>Detail_Pond…</c>; not Grass/Reeds/Flowers/Debris, whose prefixes the grass and the cut read as kinds), but no scatter
	/// rule plants them: the cut lays them as baked props on the water (FloatingPlants), and the generator holds their material
	/// still and unrooted (BiomeArtGenerator: no wind, <c>_Clinging</c>, no lean, no distance fade of a detail's).
	/// </para>
	/// <para>
	/// <b>No static fields read during initialisation</b>, as in ProceduralArtCatalogue.Flora.cs: <see cref="FloatingDetails"/> is
	/// a method and <see cref="FloatingKindTraits"/> a pure switch.
	/// </para>
	/// <para>
	/// <b>Sizes.</b> Duckweed: Width a card's side (a scatter of fronds), Radius the patch; water-lily: Width a pad's radius,
	/// Height a flower's petal length, Radius the clump; water hyacinth: Width a leaf's width, Height the flower spike; frogbit:
	/// Width a leaf's radius, Height its flower's petals. Each thin thing's sink is a share of its own thickness (it lies a
	/// centimetre over its origin, which the cut sets on the water).
	/// </para>
	/// </remarks>
	public static partial class ProceduralArtCatalogue
	{
		/// <summary>The floating plants' details, one per <see cref="BiomeFloatingPlantKind"/> in its order.</summary>
		private static DetailSpec[] FloatingDetails()
		{
			return new DetailSpec[]
			{
				// Lemna: a fresh green carpet, yellower in the sun, the odd darker frond.
				D("PondDuckweed", DetailKind.Duckweed, 120, 0.012f, 0.22f, 1.1f, "#5e9a2a", "#8cbc3a", lean: 0f, segments: 1, snowBury: 0f,
					groupMetres: 6f, groupSize: 20f, sink: new Vector2(0.001f, 0.003f)),
				// Nymphaea alba: dark glossy pads, reddish rims; white flowers with a golden heart (accents: petals, heart, rim).
				D("PondLily", DetailKind.WaterLily, 6, 0.09f, 0.16f, 0.75f, "#2e5a22", "#46702c", lean: 0f, segments: 1, snowBury: 0f,
					accents: new[] { H("#f6f4ee"), H("#e8c030"), H("#7a2e22") },
					groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.005f, 0.01f)),
				// Eichhornia: bright glossy green rosettes, lilac spikes.
				D("PondHyacinth", DetailKind.WaterHyacinth, 5, 0.32f, 0.1f, 0.6f, "#3e8a2e", "#5aa83a", lean: 0f, segments: 1, snowBury: 0f,
					accents: new[] { H("#9a7ad8") },
					groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.005f, 0.01f)),
				// Hydrocharis: small bronzed-green kidney leaves, white three-petalled flowers with a yellow eye.
				D("PondFrogbit", DetailKind.Frogbit, 6, 0.022f, 0.03f, 0.35f, "#44702a", "#6a8a34", lean: 0f, segments: 1, snowBury: 0f,
					accents: new[] { H("#f8f6ee"), H("#f0d040") },
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.002f, 0.006f)),
			};
		}

		/// <summary>The floating plants' kinds' traits: no shadows (they lie flat on the water), still (the material holds them so).</summary>
		static partial void FloatingKindTraits(DetailKind kind, ref DetailKindTraits traits)
		{
			switch (kind)
			{
				case DetailKind.Duckweed:
				case DetailKind.WaterLily:
				case DetailKind.Frogbit:
					traits.CastsShadows = false;
					traits.TintPatchMetres = 9f;
					break;
				case DetailKind.WaterHyacinth:
					traits.CastsShadows = true;
					traits.TintPatchMetres = 9f;
					break;
			}
		}

		/// <summary>The detail a floating plant is drawn as (<see cref="FloatingDetails"/>), by its prefab name.</summary>
		public static string FloatingPrefab(BiomeFloatingPlantKind kind)
		{
			switch (kind)
			{
				case BiomeFloatingPlantKind.Duckweed: return DetailPrefab("PondDuckweed");
				case BiomeFloatingPlantKind.WaterLily: return DetailPrefab("PondLily");
				case BiomeFloatingPlantKind.WaterHyacinth: return DetailPrefab("PondHyacinth");
				case BiomeFloatingPlantKind.Frogbit: return DetailPrefab("PondFrogbit");
				default: return null;
			}
		}
	}
}
#endif
