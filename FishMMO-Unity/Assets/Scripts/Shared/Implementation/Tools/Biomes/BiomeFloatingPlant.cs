using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The plants a biome's still fresh water carries on its surface (<see cref="BiomeTemplate.FloatingPlants"/>, 2026-10-18, phase
	/// I2i, decision 27): duckweed carpets, water-lily pads and their flowers, water hyacinth mats, frogbit rosettes, laid on the
	/// lakes, ponds, moats and slack river pools whose water lies in the biome. Append-only (stored by value); each kind is drawn
	/// by the biome art generator as a detail mesh of its own (FloatingPlantMeshes) and laid by the cut as a baked prop at the
	/// water's surface (FloatingPlants).
	/// </summary>
	/// <remarks>
	/// Until 2026-10-18 no water carried anything on its surface; an empty list (every biome's default, and what the authoring
	/// writes until a biome's file names its own with <c>.Floats(...)</c>) keeps it so.
	/// </remarks>
	public enum BiomeFloatingPlantKind : byte
	{
		/// <summary>Duckweed: a green carpet of tiny round fronds over sheltered water, thickest against the lee shore.</summary>
		Duckweed = 0,
		/// <summary>Water-lilies: round notched pads on long stalks from the mud, a white or pink flower among them, in 0.3–2.5 m of water.</summary>
		WaterLily = 1,
		/// <summary>Water hyacinth: free-floating rosettes of glossy rounded leaves on swollen stalks, lilac flower spikes, in mats along a sheltered edge.</summary>
		WaterHyacinth = 2,
		/// <summary>Frogbit: small kidney-shaped floating leaves in rosettes, three-petalled white flowers, in the shallow sheltered margins.</summary>
		Frogbit = 3,

		// The kinds the biome drafts name (2026-10-25, phase I2i-b), appended in the order the infrastructure map lists them.
		/// <summary>Tasselweed (Ruppia): fine thread leaves just under the surface of brackish pannes, its seed tassels floating on it.</summary>
		Tasselweed = 4,
		/// <summary>Sacred lotus (Nelumbo nucifera): round blue-green leaves lying on the water and raised over it, pink-white cup flowers, seed pods.</summary>
		SacredLotus = 5,
		/// <summary>Water chestnut (Trapa): rosettes of rhombic toothed leaves, red in autumn, small white flowers at their hearts.</summary>
		WaterChestnut = 6,
		/// <summary>Watercress: floating mats of small rounded leaflets in clear shallow water, a few shoots standing up out of them.</summary>
		Watercress = 7,
		/// <summary>American lotus (Nelumbo lutea): as the sacred lotus, pale yellow cup flowers.</summary>
		AmericanLotus = 8,
		/// <summary>Water clover (Marsilea): mats of four-leaved floating leaves on shallow pools and wet playas.</summary>
		WaterClover = 9,
		/// <summary>Water lettuce (Pistia): free-floating rosettes of pale, velvety, ribbed leaves, in mats along a sheltered edge.</summary>
		WaterLettuce = 10,
		/// <summary>Water snowflake (Nymphoides indica): small round floating leaves, white fringed star flowers.</summary>
		WaterSnowflake = 11,
		/// <summary>Ottelia: ribbon leaves under very clean water, white three-petalled flowers on its surface.</summary>
		Ottelia = 12,
		/// <summary>Azolla: a velvet fern carpet, green in shade, rust-red in the sun.</summary>
		Azolla = 13,
		/// <summary>Floating-heart (Nymphoides peltata): small notched round leaves, fringed yellow five-petalled flowers.</summary>
		FloatingHeart = 14,
		/// <summary>Water soldier (Stratiotes): half-sunk rosettes of serrated sword leaves, white three-petalled flowers.</summary>
		WaterSoldier = 15,
		/// <summary>Floating fern (Salvinia natans): chains of paired oval felted fronds, a bright carpet in sheltered water.</summary>
		WaterFern = 16,
		/// <summary>Bladderwort (Utricularia): a dark tangle just under the surface, yellow snapdragon flowers on stalks over it.</summary>
		Bladderwort = 17,
		/// <summary>Spongeplant (Limnobium): rosettes of round spongy leaves, tiny white flowers.</summary>
		Spongeplant = 18,
		/// <summary>Pennywort (Hydrocotyle): runners of round crinkled leaves lying out on the water from the bank.</summary>
		Pennywort = 19,
		/// <summary>Bergy bits: lumps of glacier ice calved into a proglacial lake, most of each under the water. Not a plant.</summary>
		BergyBits = 20,
		/// <summary>Salt rafts: thin white halite plates drifting at a brine lake's shores. Not a plant.</summary>
		SaltRaft = 21,
		/// <summary>Amphibious bistort: oblong floating leaves, pink flower spikes.</summary>
		AmphibiousBistort = 22,
		/// <summary>Starwort (Callitriche): pale star rosettes of small leaves at the surface.</summary>
		Starwort = 23,
		/// <summary>Water crowfoot (Ranunculus aquatilis): thread-leaf mats starred with white, gold-eyed buttercup flowers.</summary>
		WaterCrowfoot = 24,
		/// <summary>Broad-leaved pondweed (Potamogeton natans): oval floating leaves, brown flower spikes.</summary>
		Pondweed = 25,
		/// <summary>Blue lotus (Nymphaea caerulea): notched pads with purple undersides, star-pointed sky-blue flowers held over the water.</summary>
		BlueLotus = 26,
		/// <summary>Froth rafts: porous orange-brown froth windrowed into lee coves. Not a plant.</summary>
		FrothRaft = 27,
		/// <summary>Propagule rafts: mangrove seedlings lying flat among yellow fallen leaves in a creek or moat (the open sea's own: BiomeSeaFloatKind).</summary>
		PropaguleRaft = 28,
		/// <summary>Tannin foam: cream-yellow foam rafts with brown rims, gathered in the eddies of tea-dark water. Not a plant.</summary>
		TanninFoam = 29,
		/// <summary>Gutweed (Ulva): crinkled bright green sheets floating in brackish pannes and creeks.</summary>
		Gutweed = 30,
	}

	/// <summary>One plant a biome's still water carries and how often among its others (<see cref="BiomeTemplate.FloatingPlants"/>).</summary>
	[Serializable]
	public struct BiomeFloatingPlant
	{
		[Tooltip("The plant (or the ice, salt, froth or foam) on the water: duckweed, water-lilies, water hyacinth, frogbit, and the kinds the later biomes added (lotuses, water chestnut, cress, clover, lettuce, floating-hearts, Ottelia, Azolla, water soldier, floating fern, bladderwort, spongeplant, pennywort, bistort, starwort, crowfoot, pondweed, gutweed, propagules; bergy bits, salt rafts, froth rafts, tannin foam).")]
		public BiomeFloatingPlantKind Kind;
		[Tooltip("How often a patch of the water's surface is this plant among the biome's others (0 never).")]
		[Min(0f)] public float Weight;

		public BiomeFloatingPlant(BiomeFloatingPlantKind kind, float weight)
		{
			Kind = kind;
			Weight = weight;
		}

		/// <summary>The entry as the authoring marks and reports it: "Kind×weight".</summary>
		public override string ToString() => Kind + "×" + Weight.ToString("R", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// The floating-plant rules that do not depend on a template asset (pure): which plant a roll picks, how thickly each grows
	/// by the water's depth, its distance from the shore and how sheltered it lies, and how sheltered a body of still water is.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Still water only.</b> Free-floating plants (duckweed, frogbit, water hyacinth) drift with the wind and the current and
	/// pile up where neither reaches: in small ponds, moats, the lee shore of a lake and the slack water of a river's pools.
	/// Rooted floating-leaved plants (water-lilies) hold where the water is between about 0.3 m and 2.5 m deep, best between
	/// 0.5 m and 1.8 m, wherever the bed is soft and the water calm enough not to tear their pads. A fast river carries none.
	/// </para>
	/// <para>
	/// <b>Shelter.</b> A body's own shelter by how much open water the wind has to raise a chop over (its area: a pond under
	/// 0.2 ha is wholly sheltered, a lake of 20 ha and more little more than its margins), and near any shore more again (the
	/// shelter rises to full over the last ten metres or so). A moat is a narrow ditch: wholly sheltered.
	/// </para>
	/// </remarks>
	public static class BiomeFloatingPlants
	{
		/// <summary>An empty list: no water carries anything (every biome's default). A fresh list each call.</summary>
		public static List<BiomeFloatingPlant> None() => new List<BiomeFloatingPlant>();

		/// <summary>The least water a floating plant is laid on, metres: thinner is a wet bank, not water.</summary>
		public const float MinDepthMetres = 0.08f;

		/// <summary>The area under which a pond is wholly sheltered, square metres (0.2 ha).</summary>
		public const float PondSquareMetres = 2000f;
		/// <summary>The area over which a lake is sheltered only by its shores, square metres (20 ha).</summary>
		public const float OpenLakeSquareMetres = 200000f;
		/// <summary>The shelter of a lake's open water at <see cref="OpenLakeSquareMetres"/> and over.</summary>
		public const float OpenLakeShelter = 0.3f;
		/// <summary>The shelter of a river's slack pool (a reach the current hardly moves).</summary>
		public const float PoolShelter = 0.75f;
		/// <summary>How far out from a shore its shelter reaches, metres (the e-folding distance).</summary>
		public const float ShoreShelterMetres = 8f;

		/// <summary>The sum of a list's positive weights.</summary>
		public static float Total(IReadOnlyList<BiomeFloatingPlant> plants)
		{
			float total = 0f;
			if (plants != null)
			{
				foreach (BiomeFloatingPlant p in plants)
				{
					if (p.Weight > 0f)
					{
						total += p.Weight;
					}
				}
			}
			return total;
		}

		/// <summary>True when a list carries anything (an entry of weight).</summary>
		public static bool Any(IReadOnlyList<BiomeFloatingPlant> plants) => Total(plants) > 0f;

		/// <summary>
		/// The kind a roll in [0, 1) picks from a list by weight (entries of no weight never); false when nothing can be picked.
		/// Pure and deterministic.
		/// </summary>
		public static bool TryPick(IReadOnlyList<BiomeFloatingPlant> plants, float roll, out BiomeFloatingPlantKind kind)
		{
			kind = BiomeFloatingPlantKind.Duckweed;
			float total = Total(plants);
			if (total <= 0f)
			{
				return false;
			}
			float at = Mathf.Clamp01(roll) * total, run = 0f;
			bool found = false;
			foreach (BiomeFloatingPlant p in plants)
			{
				if (p.Weight <= 0f)
				{
					continue;
				}
				kind = p.Kind;
				found = true;
				run += p.Weight;
				if (at < run)
				{
					return true;
				}
			}
			return found;
		}

		/// <summary>
		/// How sheltered a lake of <paramref name="squareMetres"/> is over its open water, 0 … 1: wholly under
		/// <see cref="PondSquareMetres"/>, falling with the logarithm of its area to <see cref="OpenLakeShelter"/> at
		/// <see cref="OpenLakeSquareMetres"/>.
		/// </summary>
		public static float LakeShelter(float squareMetres)
		{
			if (!(squareMetres > PondSquareMetres))
			{
				return 1f;
			}
			float t = Mathf.Clamp01(Mathf.Log(squareMetres / PondSquareMetres) / Mathf.Log(OpenLakeSquareMetres / PondSquareMetres));
			return Mathf.Lerp(1f, OpenLakeShelter, t);
		}

		/// <summary>The shelter at <paramref name="shore"/> metres out from the nearest shore of a body whose open water has <paramref name="body"/>, 0 … 1.</summary>
		public static float Shelter(float body, float shore)
		{
			float near = Mathf.Exp(-Mathf.Max(0f, shore) / ShoreShelterMetres);
			return Mathf.Clamp01(Mathf.Lerp(Mathf.Clamp01(body), 1f, near));
		}

		/// <summary>
		/// How much of a patch of water's surface a plant covers, 0 … 1 (pure): by the water's <paramref name="depth"/> (metres),
		/// how far the patch lies from the nearest shore (<paramref name="shore"/>, metres) and its <paramref name="shelter"/>
		/// (<see cref="Shelter"/>). Nothing on water under <see cref="MinDepthMetres"/>.
		/// </summary>
		public static float Cover(BiomeFloatingPlantKind kind, float depth, float shore, float shelter)
		{
			if (!(depth >= MinDepthMetres) || float.IsNaN(shore))
			{
				return 0f;
			}
			shelter = Mathf.Clamp01(shelter);
			shore = Mathf.Max(0f, shore);
			switch (kind)
			{
				case BiomeFloatingPlantKind.Duckweed:
					// A carpet wherever it lies still: thickest in the shelter of the margins, gone over open water.
					return 0.9f * shelter * shelter * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 40f, shore)));
				case BiomeFloatingPlantKind.WaterLily:
					// Rooted: by the depth of water over its rhizome, a little shy of the very edge.
					return 0.75f * shelter * DepthBand(depth, 0.3f, 0.5f, 1.8f, 2.5f) * Mathf.Clamp01(shore / 1.5f);
				case BiomeFloatingPlantKind.WaterHyacinth:
					return 0.8f * Mathf.Pow(shelter, 1.5f) * (depth >= 0.15f ? 1f : 0f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 18f, shore)));
				case BiomeFloatingPlantKind.Frogbit:
					return 0.7f * Mathf.Pow(shelter, 1.5f) * DepthBand(depth, MinDepthMetres, 0.15f, 1f, 1.5f)
						* (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 10f, shore)));
				default:
					if (!TryHabit(kind, out Habit h))
					{
						return 0f;
					}
					float cover = h.Peak * Mathf.Pow(shelter, h.ShelterPower) * DepthBand(depth, h.Depth.x, h.Depth.y, h.Depth.z, h.Depth.w);
					if (h.ShoreFall.y > 0f)
					{
						cover *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(h.ShoreFall.x, h.ShoreFall.y, shore));
					}
					if (h.EdgeMetres > 0f)
					{
						cover *= Mathf.Clamp01(shore / h.EdgeMetres);
					}
					return cover;
			}
		}

		/// <summary>No upper depth: a band's last two corners for a kind that floats free over any depth.</summary>
		private const float Deep = 10000f;

		/// <summary>
		/// How a kind appended since I2i grows (2026-10-25, I2i-b): <see cref="Cover"/> is <see cref="Peak"/> × shelter ^
		/// <see cref="ShelterPower"/> × <see cref="DepthBand"/>(depth, <see cref="Depth"/>), thinning to nothing from
		/// <see cref="ShoreFall"/>.x to .y metres out from the shore (none when .y is 0), and a little shy of the very edge over
		/// <see cref="EdgeMetres"/> (a rooted plant's stalk needs water under it).
		/// </summary>
		public struct Habit
		{
			public float Peak;
			public float ShelterPower;
			public Vector4 Depth;
			public Vector2 ShoreFall;
			public float EdgeMetres;

			public Habit(float peak, float shelterPower, Vector4 depth, Vector2 shoreFall, float edgeMetres)
			{
				Peak = peak;
				ShelterPower = shelterPower;
				Depth = depth;
				ShoreFall = shoreFall;
				EdgeMetres = edgeMetres;
			}
		}

		/// <summary>
		/// The habit of a kind appended since I2i (pure); false for the first four (their rules are written out in <see cref="Cover"/>)
		/// and for a value no kind has. Free-floating kinds pile up in shelter near a shore; rooted ones hold a band of depth.
		/// </summary>
		public static bool TryHabit(BiomeFloatingPlantKind kind, out Habit habit)
		{
			switch (kind)
			{
				// Ruppia: rooted in shallow brackish water, its threads lying along the surface.
				case BiomeFloatingPlantKind.Tasselweed: habit = new Habit(0.7f, 1f, new Vector4(MinDepthMetres, 0.15f, 0.8f, 1.5f), new Vector2(4f, 30f), 0f); return true;
				// The lotuses: rooted in the mud of quiet water up to about 2.5 m, a little off the very edge.
				case BiomeFloatingPlantKind.SacredLotus:
				case BiomeFloatingPlantKind.AmericanLotus: habit = new Habit(0.8f, 1f, new Vector4(0.2f, 0.4f, 1.5f, 2.5f), Vector2.zero, 1f); return true;
				// Trapa: rooted, its rosettes floating on long stems in still water up to 3.5 m.
				case BiomeFloatingPlantKind.WaterChestnut: habit = new Habit(0.75f, 1f, new Vector4(0.3f, 0.6f, 2.5f, 3.5f), Vector2.zero, 1.5f); return true;
				// Watercress: shallow clear margins; it likes moving water, so shelter matters little.
				case BiomeFloatingPlantKind.Watercress: habit = new Habit(0.75f, 0.5f, new Vector4(MinDepthMetres, 0.1f, 0.4f, 0.8f), new Vector2(1f, 5f), 0f); return true;
				// Marsilea: shallow pools and the wet edges of playas.
				case BiomeFloatingPlantKind.WaterClover: habit = new Habit(0.7f, 1f, new Vector4(MinDepthMetres, 0.1f, 0.4f, 0.8f), new Vector2(2f, 12f), 0f); return true;
				// Pistia: free-floating, in mats along a sheltered edge, as water hyacinth.
				case BiomeFloatingPlantKind.WaterLettuce: habit = new Habit(0.8f, 1.5f, new Vector4(0.12f, 0.15f, Deep, Deep + 1f), new Vector2(3f, 18f), 0f); return true;
				// The floating-hearts: rooted, small leaves on long stalks in shallow to middling water.
				case BiomeFloatingPlantKind.WaterSnowflake: habit = new Habit(0.7f, 1f, new Vector4(0.15f, 0.3f, 1.2f, 1.8f), Vector2.zero, 1f); return true;
				case BiomeFloatingPlantKind.FloatingHeart: habit = new Habit(0.75f, 1f, new Vector4(0.2f, 0.4f, 1.5f, 2.2f), Vector2.zero, 1f); return true;
				// Ottelia: rooted in clean water, its ribbons under, its flowers at the surface.
				case BiomeFloatingPlantKind.Ottelia: habit = new Habit(0.6f, 1f, new Vector4(0.3f, 0.5f, 2f, 3f), Vector2.zero, 1f); return true;
				// Azolla: a carpet as duckweed's, thickest in shelter.
				case BiomeFloatingPlantKind.Azolla: habit = new Habit(0.9f, 2f, new Vector4(MinDepthMetres, 0.1f, Deep, Deep + 1f), new Vector2(6f, 40f), 0f); return true;
				// Stratiotes: free-floating, rising and sinking with the season, in sheltered water deep enough to hold it.
				case BiomeFloatingPlantKind.WaterSoldier: habit = new Habit(0.7f, 1.5f, new Vector4(0.3f, 0.5f, 2f, 3f), new Vector2(3f, 25f), 0f); return true;
				// Salvinia: a bright carpet in sheltered water within about 30 m of the shore.
				case BiomeFloatingPlantKind.WaterFern: habit = new Habit(0.85f, 2f, new Vector4(MinDepthMetres, 0.1f, Deep, Deep + 1f), new Vector2(5f, 30f), 0f); return true;
				// Utricularia: free-floating under the surface in 0.3–1.5 m of water.
				case BiomeFloatingPlantKind.Bladderwort: habit = new Habit(0.55f, 1f, new Vector4(0.3f, 0.4f, 1.2f, 1.5f), Vector2.zero, 0f); return true;
				// Limnobium: free-floating rosettes in the sheltered margins.
				case BiomeFloatingPlantKind.Spongeplant: habit = new Habit(0.75f, 1.5f, new Vector4(MinDepthMetres, 0.15f, Deep, Deep + 1f), new Vector2(2f, 12f), 0f); return true;
				// Hydrocotyle: runners out from the bank, a few metres at most.
				case BiomeFloatingPlantKind.Pennywort: habit = new Habit(0.7f, 1f, new Vector4(MinDepthMetres, 0.1f, Deep, Deep + 1f), new Vector2(1f, 6f), 0f); return true;
				// Bergy bits: sparse over a proglacial lake's open water, grounding where it shoals.
				case BiomeFloatingPlantKind.BergyBits: habit = new Habit(0.35f, 0.5f, new Vector4(0.5f, 1.2f, Deep, Deep + 1f), new Vector2(10f, 120f), 2f); return true;
				// Salt plates: at a brine lake's shores, where the brine is shallowest.
				case BiomeFloatingPlantKind.SaltRaft: habit = new Habit(0.6f, 1f, new Vector4(MinDepthMetres, 0.1f, Deep, Deep + 1f), new Vector2(0.5f, 15f), 0f); return true;
				// Persicaria amphibia and Potamogeton natans: rooted, oval leaves on long stalks.
				case BiomeFloatingPlantKind.AmphibiousBistort: habit = new Habit(0.7f, 1f, new Vector4(0.2f, 0.4f, 1.5f, 2.5f), Vector2.zero, 1f); return true;
				case BiomeFloatingPlantKind.Pondweed: habit = new Habit(0.7f, 1f, new Vector4(0.3f, 0.5f, 1.8f, 3f), Vector2.zero, 1f); return true;
				// Callitriche: shallow water, its rosettes at the surface.
				case BiomeFloatingPlantKind.Starwort: habit = new Habit(0.65f, 1f, new Vector4(MinDepthMetres, 0.1f, 0.6f, 1f), new Vector2(2f, 15f), 0f); return true;
				// Ranunculus aquatilis: shallow clear water, slow streams' side channels too.
				case BiomeFloatingPlantKind.WaterCrowfoot: habit = new Habit(0.75f, 0.5f, new Vector4(MinDepthMetres, 0.15f, 0.8f, 1.3f), Vector2.zero, 0f); return true;
				// Nymphaea caerulea: as a water-lily.
				case BiomeFloatingPlantKind.BlueLotus: habit = new Habit(0.75f, 1f, new Vector4(0.3f, 0.5f, 1.5f, 2.2f), Vector2.zero, 1.5f); return true;
				// Froth and foam: blown into the lee coves and eddies, against the shore.
				case BiomeFloatingPlantKind.FrothRaft: habit = new Habit(0.6f, 2f, new Vector4(0.1f, 0.2f, Deep, Deep + 1f), new Vector2(3f, 25f), 0f); return true;
				case BiomeFloatingPlantKind.TanninFoam: habit = new Habit(0.6f, 2f, new Vector4(MinDepthMetres, 0.1f, Deep, Deep + 1f), new Vector2(1f, 10f), 0f); return true;
				// Propagules: drifted into the sheltered edges of creeks and moats.
				case BiomeFloatingPlantKind.PropaguleRaft: habit = new Habit(0.55f, 1.5f, new Vector4(0.1f, 0.2f, Deep, Deep + 1f), new Vector2(2f, 15f), 0f); return true;
				// Ulva: loose sheets in shallow brackish water.
				case BiomeFloatingPlantKind.Gutweed: habit = new Habit(0.6f, 1f, new Vector4(MinDepthMetres, 0.1f, 1.2f, 1.8f), new Vector2(3f, 20f), 0f); return true;
				default:
					habit = default;
					return false;
			}
		}

		/// <summary>1 between <paramref name="b"/> and <paramref name="c"/>, rising from <paramref name="a"/> and falling to <paramref name="d"/>, 0 outside.</summary>
		public static float DepthBand(float depth, float a, float b, float c, float d)
		{
			if (depth <= a || depth >= d)
			{
				return 0f;
			}
			if (depth < b)
			{
				return Mathf.InverseLerp(a, b, depth);
			}
			if (depth > c)
			{
				return Mathf.InverseLerp(d, c, depth);
			}
			return 1f;
		}

		/// <summary>The size a patch of each kind is drawn at, as a share of its mesh, the least and the most.</summary>
		public static Vector2 ScaleRange(BiomeFloatingPlantKind kind)
		{
			switch (kind)
			{
				case BiomeFloatingPlantKind.Duckweed: return new Vector2(0.8f, 1.35f);
				case BiomeFloatingPlantKind.WaterLily: return new Vector2(0.75f, 1.25f);
				case BiomeFloatingPlantKind.WaterHyacinth: return new Vector2(0.8f, 1.25f);
				case BiomeFloatingPlantKind.SacredLotus:
				case BiomeFloatingPlantKind.AmericanLotus: return new Vector2(0.8f, 1.3f);
				// Ice, salt, froth and foam come in every size.
				case BiomeFloatingPlantKind.BergyBits: return new Vector2(0.5f, 1.6f);
				case BiomeFloatingPlantKind.SaltRaft:
				case BiomeFloatingPlantKind.FrothRaft:
				case BiomeFloatingPlantKind.TanninFoam: return new Vector2(0.6f, 1.5f);
				default: return new Vector2(0.8f, 1.2f);
			}
		}
	}
}
