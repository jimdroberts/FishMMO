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

				// The drafts' kinds (2026-10-25, I2i-b), in BiomeFloatingPlantKind's order from Tasselweed. Colours from the drafts.
				// Ruppia: thread leaves under the surface, the seed tassels on it (accent).
				Afloat("PondTasselweed", DetailKind.Tasselweed, 50, 0.03f, 0.28f, 0.9f, "#4a6a3a", "#5e7a42", 0.006f, H("#8a8a5a")),
				// Nelumbo nucifera: blue-green leaves 0.6 m across, pink-white flowers, a gold heart, a seed pod.
				Afloat("PondSacredLotus", DetailKind.SacredLotus, 6, 1f, 0.3f, 1f, "#4e7a6a", "#6a9080", 0.01f, H("#f0b8c8"), H("#e8c040"), H("#8a7a4a")),
				// Trapa: rhombic toothed leaves, white flowers, autumn red now and then.
				Afloat("PondWaterChestnut", DetailKind.WaterChestnut, 3, 0.012f, 0.07f, 0.6f, "#4a6a3a", "#5a7a40", 0.003f, H("#f6f4ec"), H("#e8d060"), H("#a03a2a")),
				// Nasturtium: floating cress mats, a few shoots up out of them.
				Afloat("PondWatercress", DetailKind.Watercress, 70, 0.06f, 0.1f, 0.8f, "#3e7a2a", "#5a9a3a", 0.01f),
				// Nelumbo lutea: as the sacred lotus, pale yellow cups.
				Afloat("PondAmericanLotus", DetailKind.AmericanLotus, 6, 0.9f, 0.25f, 1f, "#5a7a5a", "#6a8a6a", 0.01f, H("#f0e0a0"), H("#e0c050"), H("#6a5a3a")),
				// Marsilea: four-leaved floating leaves.
				Afloat("PondWaterClover", DetailKind.WaterClover, 12, 0.01f, 0.012f, 0.6f, "#5a8a3a", "#6a9a44", 0.0025f),
				// Pistia: pale velvety ribbed rosettes.
				Afloat("PondWaterLettuce", DetailKind.WaterLettuce, 6, 0.12f, 0.1f, 0.6f, "#8ab07a", "#a0c08a", 0.01f),
				// Nymphoides indica: small round leaves, white fringed flowers.
				Afloat("PondWaterSnowflake", DetailKind.WaterSnowflake, 6, 0.012f, 0.035f, 0.5f, "#3a6a2a", "#4a7a32", 0.003f, H("#f6f4ec"), H("#f0d040")),
				// Ottelia: ribbons under clean water, white flowers on it.
				Afloat("PondOttelia", DetailKind.Ottelia, 12, 0.035f, 0.4f, 0.7f, "#3a5a2a", "#4a6a32", 0.008f, H("#f8f6f0"), H("#f0d050")),
				// Azolla filiculoides: velvet green, rust-red in the sun.
				Afloat("PondAzolla", DetailKind.Azolla, 110, 0.01f, 0.2f, 1.1f, "#5a7a3a", "#a04a2a", 0.0025f),
				// Nymphoides peltata: notched round pads mottled, fringed yellow flowers.
				Afloat("PondFloatingHeart", DetailKind.FloatingHeart, 6, 0.016f, 0.04f, 0.5f, "#4a6a2a", "#6a5a3a", 0.004f, H("#f0c830"), H("#e0a020")),
				// Stratiotes: serrated sword rosettes half sunk, white flowers.
				Afloat("PondWaterSoldier", DetailKind.WaterSoldier, 5, 0.35f, 0.035f, 0.6f, "#4a6a3a", "#5a7a44", 0.01f, H("#f6f4ee"), H("#f0d040")),
				// Salvinia natans: chains of paired felted fronds, a bright carpet.
				Afloat("PondWaterFern", DetailKind.WaterFern, 70, 0.01f, 0.12f, 0.9f, "#5a8a2a", "#7aa03a", 0.0025f),
				// Utricularia: a dark tangle under, yellow snapdragons on stalks over it.
				Afloat("PondBladderwort", DetailKind.Bladderwort, 36, 0.2f, 0.18f, 0.7f, "#2a3a22", "#3a4a2a", 0.01f, H("#f0d020")),
				// Limnobium: round spongy leaves, tiny white flowers.
				Afloat("PondSpongeplant", DetailKind.Spongeplant, 5, 0.008f, 0.03f, 0.5f, "#3e6a2a", "#4e7a34", 0.002f, H("#f6f6ee"), H("#e8e0a0")),
				// Hydrocotyle: runners of round crinkled leaves.
				Afloat("PondPennywort", DetailKind.Pennywort, 8, 0.01f, 0.02f, 0.6f, "#4a7a2a", "#5a8a34", 0.0025f),
				// Bergy bits: glacier ice, white over the water, blue under it.
				Afloat("PondBergyBits", DetailKind.BergyBits, 4, 0.3f, 0.6f, 1.6f, "#e8f2f6", "#8cc0d8", 0.01f),
				// Salt rafts: thin white halite plates, brighter rims.
				Afloat("PondSaltRaft", DetailKind.SaltRaft, 12, 0.008f, 0.2f, 0.9f, "#f4f2ec", "#e8e4d8", 0.002f, H("#ffffff")),
				// Persicaria amphibia: oblong floating leaves, pink spikes.
				Afloat("PondAmphibiousBistort", DetailKind.AmphibiousBistort, 9, 0.12f, 0.07f, 0.6f, "#4a6a3a", "#5a7a42", 0.01f, H("#e07a9a")),
				// Callitriche: pale star rosettes.
				Afloat("PondStarwort", DetailKind.Starwort, 7, 0.006f, 0.012f, 0.4f, "#7aa84a", "#8ab85a", 0.0015f),
				// Ranunculus aquatilis: thread-leaf mats, white buttercups with gold eyes.
				Afloat("PondWaterCrowfoot", DetailKind.WaterCrowfoot, 40, 0.012f, 0.2f, 0.8f, "#3a6a2a", "#4a7a34", 0.003f, H("#f6f4ec"), H("#f0c830")),
				// Potamogeton natans: oval floating leaves, brown spikes.
				Afloat("PondPondweed", DetailKind.Pondweed, 9, 0.1f, 0.06f, 0.6f, "#6a6a2a", "#8a7a3a", 0.01f, H("#7a5a32")),
				// Nymphaea caerulea: notched pads with purple undersides, sky-blue star flowers, a gold heart.
				Afloat("PondBlueLotus", DetailKind.BlueLotus, 6, 0.06f, 0.15f, 0.75f, "#2e5a32", "#3e6a3a", 0.01f, H("#8ab0e8"), H("#e8c040"), H("#5a3a6a")),
				// Froth rafts: porous orange-brown froth, paler bubbles.
				Afloat("PondFrothRaft", DetailKind.FrothRaft, 10, 0.03f, 0.35f, 1.4f, "#a0623a", "#8a5a2a", 0.006f, H("#7a4a22"), H("#c8905a")),
				// Propagules in a creek: green seedlings browning at the tip, yellow fallen leaves.
				Afloat("PondPropaguleRaft", DetailKind.PropaguleRaft, 16, 0.02f, 0.3f, 0.6f, "#4a7a2a", "#6a5a2a", 0.005f, H("#d8c040"), H("#8a6a2a")),
				// Tannin foam: cream-yellow rafts, brown rims, paler froth.
				Afloat("PondTanninFoam", DetailKind.TanninFoam, 10, 0.02f, 0.2f, 0.9f, "#e0cc90", "#d8c488", 0.005f, H("#8a6a3a"), H("#f0e0b0")),
				// Ulva: crinkled bright green sheets.
				Afloat("PondGutweed", DetailKind.Gutweed, 10, 0.01f, 0.15f, 0.8f, "#5aa83a", "#7ac04a", 0.0025f),
			};
		}

		/// <summary>A floating detail's entry (I2i-b): still, unbent, never snow-buried, grouped as the first four, its own sink.</summary>
		private static DetailSpec Afloat(string name, DetailKind kind, int count, float height, float width, float radius, string a, string b, float sink,
			params Color[] accents)
		{
			return D(name, kind, count, height, width, radius, a, b, lean: 0f, segments: 1, snowBury: 0f, accents: accents.Length > 0 ? accents : null,
				groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.5f * sink, sink));
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
				// The drafts' kinds that stand up out of the water or ride half sunk (I2i-b) cast shadows; the rest lie flat.
				case DetailKind.SacredLotus:
				case DetailKind.AmericanLotus:
				case DetailKind.WaterLettuce:
				case DetailKind.WaterSoldier:
				case DetailKind.BergyBits:
				case DetailKind.BlueLotus:
					traits.CastsShadows = true;
					traits.TintPatchMetres = 9f;
					break;
				default:
					if (FloatingPlantMeshes.IsFloating(kind))
					{
						traits.CastsShadows = false;
						traits.TintPatchMetres = 9f;
					}
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
				default:
					// The drafts' kinds (I2i-b): "Pond" and the kind's own name, as FloatingDetails names them.
					return kind >= BiomeFloatingPlantKind.Tasselweed && System.Enum.IsDefined(typeof(BiomeFloatingPlantKind), kind) ? DetailPrefab("Pond" + kind) : null;
			}
		}
	}
}
#endif
