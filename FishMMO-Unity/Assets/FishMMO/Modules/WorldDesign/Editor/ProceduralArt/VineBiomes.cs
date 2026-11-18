#if UNITY_EDITOR
using System;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Which vines grow in which biome, and how thickly at each kind of place they grow on (2026-10-12: "Cave mouths,
	/// Ruins and old walls, Cliffs and outcrops, Trees … none in deserts/ice"). Kept beside <see cref="BiomeArtSpec"/> and
	/// keyed the same way, by the biome asset's name.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Only the cut's fallback since 2026-10-26</b> (phase CODE-TABLE CLEARANCE, decision 1): every row was written into its
	/// biome's spec file (<see cref="BiomeArtSpec.Entry.GrowsVines"/>), which the authoring writes onto the template, and only
	/// BiomeTemplateArt.Vines reads this table, for a template never authored (warning once). Change a biome's vines in its file.
	/// </para>
	/// </remarks>
	/// <remarks>
	/// <para>
	/// <b>An explicit table.</b> As the art spec: every biome a vine grows in is named with its own decision, read from what
	/// the biome is (its climate and its water), not guessed from its name. A biome not in the table grows none: every sea
	/// floor, desert, salt and sulphur flat, ice and snow field, volcanic waste, the airless and alien plains, tundra and the
	/// high mountains. Ivy is a plant of mild, damp country (it is killed back by hard frost and drought); Virginia creeper
	/// takes the colder continental woods ivy does not; lianas are the jungle's and the warm swamps'.
	/// </para>
	/// <para>
	/// <b>Densities</b> are 0..1 by place: <see cref="Habitat.Caves"/> how often a cave's mouth is overgrown and how thickly,
	/// <see cref="Habitat.Ruins"/> the chance a wall face of a ruin or an old building carries ivy, <see cref="Habitat.Cliffs"/>
	/// the share of cliff crags that have a curtain hanging off them, <see cref="Habitat.Trees"/> the share of the trees a vine
	/// climbs that carry one. The damp, shaded woods are densest; open and dry country sparse.
	/// </para>
	/// </remarks>
	public static class VineBiomes
	{
		/// <summary>One biome's vines.</summary>
		public sealed class Habitat
		{
			public string Biome;
			/// <summary>The species (<see cref="ProceduralArtCatalogue.Vines"/>) and their weights.</summary>
			public (string species, float weight)[] Species;
			public float Caves, Ruins, Cliffs, Trees;

			/// <summary>A species by weight, from a 0..1 draw.</summary>
			public string Pick(float draw)
			{
				float total = 0f;
				foreach ((string _, float w) in Species)
				{
					total += w;
				}
				float at = draw * total;
				foreach ((string s, float w) in Species)
				{
					if (at < w)
					{
						return s;
					}
					at -= w;
				}
				return Species.Length > 0 ? Species[Species.Length - 1].species : null;
			}

			/// <summary>Whether this biome grows that species at all.</summary>
			public bool Grows(string species) => Array.Exists(Species, s => s.species == species && s.weight > 0f);
		}

		private static Habitat H(string biome, float caves, float ruins, float cliffs, float trees, params (string, float)[] species)
			=> new Habitat { Biome = biome, Caves = caves, Ruins = ruins, Cliffs = cliffs, Trees = trees, Species = species };

		private static readonly (string, float)[] Temperate = { ("Ivy", 0.75f), ("Creeper", 0.25f) };
		private static readonly (string, float)[] Ivy = { ("Ivy", 1f) };
		private static readonly (string, float)[] Continental = { ("Creeper", 0.8f), ("Ivy", 0.2f) };
		private static readonly (string, float)[] Tropical = { ("Liana", 1f) };
		private static readonly (string, float)[] WarmWet = { ("Liana", 0.6f), ("Ivy", 0.4f) };

		/// <summary>Every biome that grows vines (caves, ruins, cliffs, trees; species).</summary>
		public static readonly Habitat[] All =
		{
			// ── The woods: the heart of it ──
			H("Forest", 1f, 0.9f, 0.4f, 0.16f, Temperate),
			H("Woodland", 0.95f, 0.8f, 0.35f, 0.12f, Temperate),
			H("Forest Ruins", 1f, 1f, 0.45f, 0.22f, ("Ivy", 0.85f), ("Creeper", 0.15f)),
			H("Taiga", 0.35f, 0.25f, 0.06f, 0.02f, Continental),
			// ── The jungle and the warm wet ──
			H("Jungle", 1f, 1f, 0.6f, 0.3f, Tropical),
			H("Jungle Ruins", 1f, 1f, 0.6f, 0.35f, Tropical),
			H("Jungle Temple", 1f, 1f, 0.55f, 0.3f, Tropical),
			H("Bamboo Forest", 0.8f, 0.8f, 0.4f, 0.1f, WarmWet),
			H("Mangrove", 0.6f, 0.7f, 0.2f, 0.12f, Tropical),
			H("Swamp", 0.8f, 0.8f, 0.25f, 0.1f, WarmWet),
			H("Swamp Cave", 1f, 0.8f, 0.3f, 0.1f, WarmWet),
			H("Swamp Ruins", 0.9f, 1f, 0.3f, 0.14f, WarmWet),
			H("Swamp Temple", 0.9f, 1f, 0.3f, 0.12f, WarmWet),
			H("Oasis", 0.4f, 0.4f, 0.1f, 0.04f, Tropical),
			// ── Wet ground and water ──
			H("Wetlands", 0.5f, 0.45f, 0.15f, 0.05f, Ivy),
			H("Peat Bog", 0.4f, 0.35f, 0.1f, 0.03f, Ivy),
			H("Lake", 0.7f, 0.65f, 0.3f, 0.1f, Temperate),
			H("River", 0.75f, 0.7f, 0.35f, 0.1f, Temperate),
			H("Estuary", 0.5f, 0.5f, 0.2f, 0.06f, Ivy),
			// ── Coasts: ivy on the sea cliffs and the old walls ──
			H("Rocky Coast", 0.6f, 0.55f, 0.25f, 0.04f, Ivy),
			H("Beach", 0.35f, 0.35f, 0.1f, 0.02f, Ivy),
			// ── Open, settled and hill country ──
			H("Valley", 0.85f, 0.7f, 0.3f, 0.08f, Temperate),
			H("Hills", 0.75f, 0.6f, 0.25f, 0.06f, Temperate),
			H("Karst", 1f, 0.6f, 0.4f, 0.08f, ("Ivy", 0.8f), ("Creeper", 0.2f)),
			H("Cave", 1f, 0.55f, 0.35f, 0.06f, Ivy),
			H("Farmland", 0.6f, 0.65f, 0.15f, 0.06f, Temperate),
			H("Grassland", 0.5f, 0.45f, 0.12f, 0.04f, Temperate),
			H("Plains", 0.4f, 0.4f, 0.1f, 0.03f, Temperate),
			H("Scrubland", 0.45f, 0.4f, 0.12f, 0.03f, Ivy),
			H("Steppe", 0.25f, 0.2f, 0.04f, 0f, Continental),
			H("Savanna", 0.3f, 0.25f, 0.06f, 0.03f, Tropical),
			H("Mountain Slope", 0.35f, 0.25f, 0.1f, 0.02f, Continental),
			H("Alpine Meadow", 0.2f, 0.15f, 0.05f, 0f, Continental),
			// ── Built biomes: old stone, ivied ──
			H("Castle", 0.6f, 0.7f, 0.2f, 0.08f, Temperate),
			H("Fortress", 0.5f, 0.6f, 0.15f, 0.06f, Temperate),
			H("Marble Palace", 0.5f, 0.55f, 0.15f, 0.06f, ("Ivy", 0.6f), ("Creeper", 0.4f)),
		};

		/// <summary>The habitat for a biome asset name, or null where no vine grows.</summary>
		public static Habitat For(string biome)
		{
			if (string.IsNullOrEmpty(biome))
			{
				return null;
			}
			foreach (Habitat h in All)
			{
				if (h.Biome == biome)
				{
					return h;
				}
			}
			return null;
		}
	}
}
#endif
