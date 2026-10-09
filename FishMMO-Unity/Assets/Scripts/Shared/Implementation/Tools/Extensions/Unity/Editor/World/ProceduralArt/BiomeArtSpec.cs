#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What each biome is painted and planted with, written out biome by biome.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>An explicit table, not a guesser.</b> Every biome asset is named here with its ground
	/// families and scatter, chosen by reading its description, tier and climate envelope — not
	/// derived from keywords in its name. A biome missing from the table gets nothing (and the
	/// tests say so); a biome renamed drops out of the table loudly rather than being matched to
	/// something that merely sounds similar. Small helpers (<see cref="Grass"/>, <see cref="Trees"/>)
	/// keep the entries short, but each entry is still its biome's own decision.
	/// </para>
	/// <para>
	/// <b>Bands.</b> Detail layers carry a blend-noise scale (metres-ish: larger is broader
	/// patches) and optional slope bands in degrees and height bands in planet-normalised height,
	/// the same units <see cref="TerrainTextureLayer"/> stores. Cliff layers start at a slope angle.
	/// Scatter densities are <see cref="PrefabSpawnRule.densityPer100m2"/>: the scatter places one
	/// candidate per one-metre detail cell with chance <c>density / 100</c>, so 50 (the most the
	/// field allows) means half the cells; detail coverage is how much of a chosen cell is filled
	/// (255 all of it). That is how flowers, plants, debris and stones are placed (Scattered).
	/// </para>
	/// <para>
	/// <b>Ground cover is a carpet.</b> Grass and the tufts that stand in for moss and low cover are
	/// <see cref="DetailPlacement.Carpet"/>: continuous coverage rather than a draw per cell, because
	/// at any affordable density a draw leaves most cells bare and the result reads as speckle with a
	/// bald line at every texture blend. <see cref="Grass"/> turns the entry's density into the
	/// carpet's lushness (<c>sqrt(density / 40)</c>, so 40 and up is a full turf and 6 is a sparse
	/// third), scaled by the coverage argument; lusher carpets also thin less between swathes, so a
	/// grassland reads as continuous turf, a forest floor as patchy cover and a desert as scattered
	/// tussocks. The threshold is lower than a scattered rule's (0.25 against 0.35) with a 0.3-wide
	/// soft edge, so turf runs well into a blend and thins out there instead of stopping.
	/// </para>
	/// <para>
	/// <b>Sink.</b> Trees, whose trunks are not bedded, sink 0.05–0.15 m plus 0.3 of the slope's
	/// footprint extra. Boulders, formations, ice boulders and seracs are already bedded by
	/// construction (their pivots are raised into the mesh), so they get only the slope's extra and
	/// a few centimetres at most on flat ground; doubling the flat embed would bury them. Detail
	/// rules carry a sink too, for the vegetation material to apply; small rocks and pebbles, bedded
	/// like the boulders, carry none.
	/// </para>
	/// <para>
	/// <b>Groups.</b> Everything point-sampled gathers (<see cref="Grouped"/>): flowers in patches of
	/// a few metres, shrubs and ferns in clumps, stones and pebbles in scatters round a few larger
	/// ones, boulders in piles, formations in outcrop fields, and trees that never close into a wood
	/// (saguaros, beach palms, snags) in groves. The density in the table stays the average; grouping
	/// only moves it. Grouped helpers also vary size plant by plant and stone by stone (a detail's noise
	/// spread of 1.2–2.5 rather than swathes of one size) and pack closer (spacing is how tight a group
	/// may be).
	/// </para>
	/// <para>
	/// <b>Woods.</b> Every other tree rule is a <see cref="Stand"/>: the biome says how much of its
	/// ground is wood, how closed the canopy is inside, how many lone trees stand in the open and how
	/// stunted the trees are, and spacing and density follow from the species' real crowns. A taiga is
	/// four fifths closed spruce-and-pine forest with clearings; a woodland half; a grassland a tenth, in
	/// copses, with a few parkland oaks between. Every wood cuts one scene-wide stand field, so the
	/// grassland's copses are the hearts of the forest next to it (<see cref="TerrainScatter.StandField"/>).
	/// </para>
	/// <para>
	/// <b>Earlier versions.</b> When a helper's output changes, the old output is kept on the new
	/// rule as <see cref="Scatter.Earlier"/>, so <see cref="BiomeArtAuthoring"/> can recognise a rule
	/// an older run wrote and nobody has touched since, and bring it up to date.
	/// </para>
	/// <para>
	/// <b>Submerged.</b> Each biome names a lakebed and a riverbed family; the palette paints the
	/// lakebed under the sea, else the riverbed. Under the sea the lakebed is all there is — the main
	/// and detail layers give way to it below the shore — so what lives on the sea floor rides on it
	/// (<see cref="Entry.Bed"/>), each rule within its own band of depth (<see cref="Scatter.Depth"/>):
	/// kelp and seaweed where light reaches, sponges and sea fans below, tube worms at a vent.
	/// </para>
	/// </remarks>
	public static class BiomeArtSpec
	{
		/// <summary>One ground layer of a biome: its family, bands and what grows where it dominates.</summary>
		public sealed class Layer
		{
			public string Family;
			public float NoiseScale = 64f;
			public float Sharpness = 2f;
			public Vector2? Slope;
			public Vector2? Height;
			public readonly List<Scatter> Scatter = new List<Scatter>();
		}

		/// <summary>A cliff layer: a family on faces steeper than an angle.</summary>
		public sealed class Cliff
		{
			public string Family;
			public float MinAngle = 42f;
			public float MaxAngle = 90f;
		}

		/// <summary>One spawn rule, in the fields <see cref="PrefabSpawnRule"/> has.</summary>
		public sealed class Scatter
		{
			public string Name;
			public PrefabSpawnChannel Channel;
			public string[] Prefabs;
			public float Density;
			public float Spacing;
			public float MinWeight = 0.4f;
			public Vector2? Slope;
			public Vector2? Height;
			public Vector2 Scale = new Vector2(0.85f, 1.2f);
			public bool NonUniform;
			public Vector2 WidthScale = new Vector2(0.85f, 1.15f);
			public Vector2 HeightScale = new Vector2(0.85f, 1.25f);
			public bool AlignToNormal;
			public int Coverage = 255;
			public int MaxPerChunk;
			public float NoiseSpread = 0.4f;
			public Color Healthy = new Color(0.9f, 0.95f, 0.9f, 1f);
			public Color Dry = new Color(0.75f, 0.7f, 0.55f, 1f);
			/// <summary>Detail rules: point-sampled, or a continuous carpet.</summary>
			public DetailPlacement Placement = DetailPlacement.Scattered;
			/// <summary>Carpets: <see cref="PrefabSpawnRule.carpetCoverage"/>.</summary>
			public float CarpetCoverage = 0.85f;
			/// <summary>Carpets: <see cref="PrefabSpawnRule.carpetClumpMetres"/>.</summary>
			public float ClumpMetres = 16f;
			/// <summary>Carpets: <see cref="PrefabSpawnRule.carpetClumpFloor"/>.</summary>
			public float ClumpFloor = 0.45f;
			/// <summary>Carpets: <see cref="PrefabSpawnRule.carpetWeightRamp"/>.</summary>
			public float WeightRamp = 0.25f;
			/// <summary>Metres into the ground, min..max per instance (<see cref="PrefabSpawnRule.sinkRange"/>).</summary>
			public Vector2 Sink;
			/// <summary><see cref="PrefabSpawnRule.sinkSlopeFactor"/>.</summary>
			public float SinkSlope;
			/// <summary>Group radius, metres; 0 = an even scatter (<see cref="PrefabSpawnRule.clusterMetres"/>).</summary>
			public float ClusterMetres;
			/// <summary><see cref="PrefabSpawnRule.clusterSpacing"/> (only without a <see cref="ClusterSize"/>).</summary>
			public float ClusterSpacing = 4f;
			/// <summary>Instances (detail cells) an average group holds (<see cref="PrefabSpawnRule.clusterSize"/>).</summary>
			public float ClusterSize;
			/// <summary><see cref="PrefabSpawnRule.clusterBackground"/>.</summary>
			public float ClusterBackground = 0.2f;
			/// <summary><see cref="PrefabSpawnRule.clusterScaleBias"/>.</summary>
			public float ClusterScaleBias = 0.5f;
			/// <summary>Stand size, metres; 0 = no stands (<see cref="PrefabSpawnRule.forestMetres"/>).</summary>
			public float ForestMetres;
			/// <summary><see cref="PrefabSpawnRule.forestCover"/>.</summary>
			public float ForestCover = 0.6f;
			/// <summary><see cref="PrefabSpawnRule.forestEdge"/>.</summary>
			public float ForestEdge = 0.3f;
			/// <summary><see cref="PrefabSpawnRule.forestOpen"/>.</summary>
			public float ForestOpen = 0.05f;
			/// <summary><see cref="PrefabSpawnRule.forestMix"/>.</summary>
			public float ForestMix = 0.5f;
			/// <summary><see cref="PrefabSpawnRule.forestScaleBias"/>.</summary>
			public float ForestScaleBias = 0.3f;
			/// <summary>
			/// Sea-floor rules (<see cref="Entry.Bed"/>): metres below mean sea level the rule grows between,
			/// x the shallowest and y the deepest, 0 leaving that end open (<see cref="PrefabSpawnRule.depthRange"/>).
			/// </summary>
			public Vector2 Depth;
			/// <summary>
			/// What earlier versions of the spec wrote for this rule, so the authoring tool can tell a
			/// rule an older run wrote and nobody touched from one somebody tuned.
			/// </summary>
			public List<Scatter> Earlier = new List<Scatter>();

			/// <summary>A copy with its own (empty) history.</summary>
			public Scatter Clone()
			{
				var copy = (Scatter)MemberwiseClone();
				copy.Earlier = new List<Scatter>();
				return copy;
			}
		}

		/// <summary>A biome's whole entry.</summary>
		public sealed class Entry
		{
			public string Biome;
			public Layer Main;
			public Layer[] Details = new Layer[0];
			public Cliff[] Cliffs = new Cliff[0];
			public string Lakebed;
			public string Riverbed;
			/// <summary>
			/// What lives on the sea floor: rules on the biome's lakebed layer (family <see cref="Lakebed"/>),
			/// the only layer painted under the sea. Null for none.
			/// </summary>
			public Layer Bed;
			/// <summary>
			/// Rules an earlier spec wrote on a slot (<c>main</c>, <c>detail/N</c>, <c>lakebed</c>) that it no
			/// longer does — kelp the spec once put on an ocean's main layer, never painted under water, so
			/// it only ever grew on the odd island — for the authoring tool to switch off where untouched.
			/// </summary>
			public readonly List<(string slot, string rule)> Retired = new List<(string slot, string rule)>();
			/// <summary>
			/// The planet-geology rocks this biome's cliffs, river boulders and waterfall ledges may be made of, besides its
			/// own (<see cref="CliffRocks.OwnRock"/>). Where the geology under it is one of these, the rock is the geology's;
			/// anywhere else it is the biome's own. Empty: always its own.
			/// </summary>
			/// <remarks>
			/// Geology alone walled whole scenes in one province's rock whatever grew on them: Flo Monolith's bog and taiga
			/// stood in sandstone cliffs and boulders, Baoakraal's karst and grassland in conglomerate (Jim, 2026-10-08, who
			/// chose this list over either alone). So sandstone and conglomerate are accepted only by the dry biomes.
			/// </remarks>
			public string[] Rocks = Array.Empty<string>();

			/// <summary>Sets the geology rocks the biome accepts (<see cref="Rocks"/>).</summary>
			public Entry Accepts(params RockType[] rocks)
			{
				Rocks = Array.ConvertAll(rocks, r => r.Name);
				return this;
			}

			/// <summary>Sets what lives on the sea floor (<see cref="Bed"/>).</summary>
			public Entry Under(params Scatter[] scatter)
			{
				Bed = new Layer { Family = Lakebed };
				Bed.Scatter.AddRange(scatter);
				return this;
			}

			/// <summary>Records rules the spec moved off a slot (<see cref="Retired"/>).</summary>
			public Entry Moved(string slot, params string[] rules)
			{
				foreach (string rule in rules)
				{
					Retired.Add((slot, rule));
				}
				return this;
			}

			/// <summary>Every family the entry uses.</summary>
			public IEnumerable<string> Families()
			{
				if (Main != null) yield return Main.Family;
				foreach (Layer d in Details) yield return d.Family;
				foreach (Cliff c in Cliffs) yield return c.Family;
				if (Lakebed != null) yield return Lakebed;
				if (Riverbed != null) yield return Riverbed;
			}

			/// <summary>Every scatter rule the entry adds, with the layer it rides on.</summary>
			public IEnumerable<(Layer layer, Scatter rule)> Rules()
			{
				if (Main != null) foreach (Scatter s in Main.Scatter) yield return (Main, s);
				foreach (Layer d in Details) foreach (Scatter s in d.Scatter) yield return (d, s);
				if (Bed != null) foreach (Scatter s in Bed.Scatter) yield return (Bed, s);
			}
		}

		// ── Prefab names ──────────────────────────────────────────────

		private static string Det(string name) => ProceduralArtCatalogue.DetailPrefab(name);
		private static string Tree(string name) => ProceduralArtCatalogue.TreePrefab(name);
		private static string Boulder(string material, string shape) => ProceduralArtCatalogue.BoulderPrefab(material, shape);

		// ── Builders ──────────────────────────────────────────────────

		private static Layer L(string family, params Scatter[] scatter)
		{
			var layer = new Layer { Family = family };
			layer.Scatter.AddRange(scatter);
			return layer;
		}

		/// <summary>A detail layer: noise scale, optional slope band (degrees).</summary>
		private static Layer D(string family, float noise, float sharpness = 2f, float slopeMin = -1f, float slopeMax = -1f, params Scatter[] scatter)
		{
			var layer = new Layer { Family = family, NoiseScale = noise, Sharpness = sharpness };
			if (slopeMin >= 0f)
			{
				layer.Slope = new Vector2(slopeMin, slopeMax);
			}
			layer.Scatter.AddRange(scatter);
			return layer;
		}

		private static Cliff C(string family, float minAngle = 42f) => new Cliff { Family = family, MinAngle = minAngle };

		private static Layer[] Ds(params Layer[] layers) => layers;

		/// <summary>Detail sink for grass and plants, applied by the vegetation material.</summary>
		private static readonly Vector2 DetailSink = new Vector2(0.02f, 0.05f);

		/// <summary>
		/// How far an upright plant is pushed into the ground, by its kind: one grass-sized range for all of
		/// them left a fern's or a reed bed's stems standing on the surface. (Slopes are handled where the plant
		/// is placed, by its foot's reach: TerrainDetailField.Place.)
		/// </summary>
		private static Vector2 PlantSink(string detail)
		{
			if (detail.StartsWith("Debris", StringComparison.Ordinal)) return new Vector2(0f, 0.01f);
			if (detail == "Kelp" || detail == "Coral" || detail == "BrainCoral" || detail == "Sponge" || detail == "TubeWorms") return new Vector2(0.05f, 0.1f);
			if (detail == "TableCoral" || detail == "SeaFan") return new Vector2(0.03f, 0.06f);
			if (detail == "Starfish" || detail == "Shells") return new Vector2(0f, 0.008f);
			if (detail == "Urchin" || detail == "Anemone") return new Vector2(0.01f, 0.025f);
			if (detail.StartsWith("Fern", StringComparison.Ordinal) || detail == "Reeds" || detail.Contains("Cactus")) return new Vector2(0.04f, 0.08f);
			if (detail.StartsWith("Shrub", StringComparison.Ordinal)) return new Vector2(0.03f, 0.06f);
			return DetailSink;
		}

		/// <summary>
		/// Grass and other ground cover: a carpet on the detail channel, following the ground, on
		/// gentle slopes. See the class remarks for how density becomes lushness.
		/// </summary>
		/// <remarks>
		/// The scattered rule this helper wrote before carpets existed is kept as the carpet's
		/// <see cref="Scatter.Earlier"/>, exactly as it was, so a biome authored then is recognised
		/// and brought over to the carpet.
		/// </remarks>
		private static Scatter Grass(string detail, float density, int coverage = 255, float slopeMax = 35f)
		{
			Scatter legacy = GroundCover(detail, density, coverage, slopeMax);
			Scatter s = legacy.Clone();
			s.Placement = DetailPlacement.Carpet;
			float lushness = Mathf.Clamp01(Mathf.Sqrt(Mathf.Max(0f, density) / 40f));
			s.CarpetCoverage = Mathf.Clamp01(lushness * Mathf.Clamp(coverage, 0, 255) / 255f);
			// Lush turf thins to half between swathes; sparse cover breaks up into clumps.
			s.ClumpFloor = Mathf.Lerp(0.15f, 0.5f, lushness);
			// Tussocks are small patches; tall grass stands in broad swathes.
			s.ClumpMetres = detail == "GrassTuft" ? 12f : detail == "GrassTall" ? 22f : 18f;
			s.MinWeight = 0.25f;
			s.WeightRamp = 0.3f;
			s.Sink = DetailSink;
			s.Earlier.Add(legacy);
			return s;
		}

		/// <summary>The scattered ground-cover rule every detail helper starts from (and what <see cref="Grass"/> wrote before carpets).</summary>
		private static Scatter GroundCover(string detail, float density, int coverage, float slopeMax)
		{
			ProceduralArtCatalogue.TryDetail(detail, out DetailSpec spec);
			return new Scatter
			{
				Name = detail,
				Channel = PrefabSpawnChannel.DetailLayer,
				Prefabs = new[] { Det(detail) },
				Density = density,
				MinWeight = 0.35f,
				Slope = new Vector2(0f, slopeMax),
				Scale = new Vector2(0.8f, 1.25f),
				AlignToNormal = true,
				Coverage = coverage,
				Healthy = spec.Healthy == default ? new Color(0.9f, 0.95f, 0.9f, 1f) : spec.Healthy,
				Dry = spec.Dry == default ? new Color(0.75f, 0.7f, 0.55f, 1f) : spec.Dry,
			};
		}

		/// <summary>Taller plants and clutter: detail channel, upright, scattered — counted things, not cover.</summary>
		private static Scatter Plants(string detail, float density, int coverage = 160, float slopeMax = 40f)
		{
			Scatter s = GroundCover(detail, density, coverage, slopeMax);
			s.AlignToNormal = false;
			s.MinWeight = 0.45f;
			s.Sink = PlantSink(detail);
			// Flowers in small patches, shrubs and ferns in clumps, reeds, kelp and coral in beds; on the sea
			// floor corals in heads across a reef, urchins and anemones in little colonies, starfish few and apart.
			float metres = detail.StartsWith("Flowers", StringComparison.Ordinal) ? 3f
				: detail.StartsWith("Shrub", StringComparison.Ordinal) ? 5f
				: detail == "Reeds" || detail == "Kelp" ? 6f
				: detail == "Coral" ? 8f
				: detail == "BrainCoral" || detail == "TableCoral" ? 10f
				: detail == "SeaFan" || detail == "Sponge" || detail == "Starfish" ? 6f
				: detail == "Urchin" || detail == "Anemone" || detail == "Shells" || detail == "TubeWorms" ? 3f
				: 4f;
			float background = detail.StartsWith("Flowers", StringComparison.Ordinal) ? 0.15f : 0.25f;
			// Cells per group; each holds a few plants.
			float size = detail.StartsWith("Flowers", StringComparison.Ordinal) ? 10f
				: detail.StartsWith("Shrub", StringComparison.Ordinal) ? 6f
				: detail == "Reeds" ? 20f
				: detail == "Kelp" || detail == "Coral" || detail == "TubeWorms" ? 12f
				: detail == "Urchin" || detail == "Seaweed" ? 10f
				: detail == "Starfish" ? 3f
				: detail == "BrainCoral" || detail == "TableCoral" || detail == "SeaFan" || detail == "Sponge" || detail == "Anemone" ? 5f
				: 8f;
			return Grouped(s, metres, background, size, regroup: g => g.NoiseSpread = 1.2f);
		}

		/// <summary>
		/// What a cell a shrub rule places is written with (<see cref="Scatter.Coverage"/>): a quarter of a cell's top
		/// value. TerrainScatter sets a shrub prototype's density so a cell at this value holds exactly one bush
		/// (TerrainScatter.CalibrateCountedDetails), so the rule's density is bushes per 100 m², and the heart of a
		/// thicket, asking for more than one in a cell, can hold up to four.
		/// </summary>
		public const int BushCoverage = 64;

		/// <summary>
		/// Shrubs (<see cref="BushMeshes"/>): the detail channel (no collider: players walk in and hide), upright, a few
		/// metres apart by their crowns, gathered into thickets of about five with the odd bush between, each bush its own
		/// size. <paramref name="density"/> is bushes per 100 m² over the rule's ground; <paramref name="scale"/> the
		/// species' size here against its mature size (a dwarf willow on tundra, stunted juniper on a mountain).
		/// </summary>
		private static Scatter Bush(string species, float density, float scale = 1f, float slopeMax = 35f)
		{
			ProceduralArtCatalogue.TryBush(species, out BushSpecies b);
			float crown = Mathf.Max(0.3f, BushMeshes.CrownRadius(in b) * scale);
			var s = new Scatter
			{
				Name = "Bush: " + species,
				Channel = PrefabSpawnChannel.DetailLayer,
				Prefabs = new[] { ProceduralArtCatalogue.BushPrefab(species) },
				Density = density,
				// Crowns meet but do not grow through each other.
				Spacing = Mathf.Round(crown * 1.4f * 10f) / 10f,
				MinWeight = 0.45f,
				Slope = new Vector2(0f, slopeMax),
				NonUniform = true,
				WidthScale = new Vector2(0.8f, 1.2f) * scale,
				HeightScale = new Vector2(0.8f, 1.2f) * scale,
				AlignToNormal = false,
				Coverage = BushCoverage,
				NoiseSpread = 1.5f,
				Healthy = new Color(0.92f, 0.97f, 0.92f, 1f),
				Dry = new Color(0.8f, 0.76f, 0.58f, 1f),
				// A woody root crown, set into the ground.
				Sink = new Vector2(0.04f, 0.1f),
				SinkSlope = 0.3f,
			};
			return Grouped(s, Mathf.Max(6f, crown * 4f), 0.25f, 5f, 0.4f, g => g.Spacing = Mathf.Round(crown * 1.1f * 10f) / 10f);
		}

		/// <summary>
		/// Sea-floor life (<see cref="Entry.Bed"/>): a counted thing, grouped as <see cref="Plants"/> groups it,
		/// within a band of depth below mean sea level (0 for an open end). Things that lie on the bed (starfish,
		/// shells, urchins) lie with its slope; anything that grows stands up.
		/// </summary>
		private static Scatter Sea(string detail, float density, float shallowest, float deepest, int coverage = 180, float slopeMax = 40f)
		{
			Scatter s = Plants(detail, density, coverage, slopeMax);
			bool lying = detail == "Starfish" || detail == "Shells" || detail == "Urchin";
			AndEarlier(s, r =>
			{
				r.Depth = new Vector2(shallowest, deepest);
				r.AlignToNormal = lying;
			});
			return s;
		}

		/// <summary>Small stones on the sea floor: as <see cref="Stones"/>, within a band of depth.</summary>
		private static Scatter SeaStones(string prefab, float density, float shallowest, float deepest)
		{
			Scatter s = Stones(prefab, density);
			AndEarlier(s, r => r.Depth = new Vector2(shallowest, deepest));
			return s;
		}

		/// <summary>Small stones: detail channel, any slope, lying with the ground.</summary>
		private static Scatter Stones(string prefab, float density, int coverage = 96)
		{
			var s = new Scatter
			{
				Name = prefab,
				Channel = PrefabSpawnChannel.DetailLayer,
				Prefabs = new[] { prefab },
				Density = density,
				MinWeight = 0.4f,
				Scale = new Vector2(0.6f, 1.5f),
				AlignToNormal = true,
				Coverage = coverage,
				Healthy = Color.white,
				Dry = Color.white,
				NoiseSpread = 0.1f,
			};
			// Stones lie in scatters a few metres across, every one its own size.
			bool pebbles = prefab.StartsWith(Pebbles(string.Empty), StringComparison.Ordinal);
			return Grouped(s, pebbles ? 2f : 3f, 0.2f, pebbles ? 8f : 6f, regroup: g =>
			{
				g.NoiseSpread = 2.5f;
				g.Scale = new Vector2(0.45f, 1.75f);
			});
		}

		/// <summary>
		/// What <see cref="Trees"/> wrote until 2026-10-04, verbatim (the earlier versions it remembers
		/// included): kept so a rule an older run wrote, and nobody has touched, is still recognised.
		/// Never planted from directly.
		/// </summary>
		private static Scatter TreesBefore(float density, float spacing, float slopeMax, params string[] prefabs)
		{
			var names = new string[prefabs.Length];
			for (int i = 0; i < prefabs.Length; i++)
			{
				names[i] = Tree(prefabs[i]);
			}
			var t = new Scatter
			{
				Name = "Trees: " + string.Join(", ", prefabs),
				Channel = PrefabSpawnChannel.TreeInstance,
				Prefabs = names,
				Density = density,
				Spacing = spacing,
				MinWeight = 0.5f,
				Slope = new Vector2(0f, slopeMax),
				NonUniform = true,
				WidthScale = new Vector2(0.8f, 1.15f),
				HeightScale = new Vector2(0.8f, 1.3f),
				AlignToNormal = false,
				MaxPerChunk = 20000,
				// Trunks are not bedded: a few centimetres, and part of the downhill edge on a slope.
				Sink = new Vector2(0.05f, 0.15f),
				SinkSlope = 0.3f,
			};
			if (density < 0.5f)
			{
				// Sparse trees stand in groves of about six, not one per field: closer inside a grove, few strays.
				return Grouped(t, 15f, 0.2f, 6f, 0.4f, g => g.Spacing = Mathf.Min(spacing, Mathf.Max(5f, spacing * 0.5f)));
			}
			/* A forest gathers into thickets with glades between. Its spacing is what caps a thicket's
			 * heart, and at the forest's own spacing that heart could hold barely more than the average,
			 * so a thicket packs closer (crowns touching) than the forest round it. */
			return Grouped(t, 18f, 0.4f, 25f, 0.3f, g => g.Spacing = Mathf.Min(spacing, Mathf.Max(4f, spacing * 0.6f)));
		}

		/// <summary>Per-instance tree sizes: natural variation of about ±15% across and ±20% in height round the species' mature size.</summary>
		private static readonly Vector2 TreeWidthScale = new Vector2(0.85f, 1.15f);
		private static readonly Vector2 TreeHeightScale = new Vector2(0.8f, 1.2f);

		/// <summary>
		/// Size of the largest stands, metres: woods of several hundred metres to a kilometre or so, with
		/// clearings and copses down to an eighth of it. One size for every wood on purpose — forest rules
		/// cut one shared field, and only rules of one size cut the same one, so a forest's stands and a
		/// neighbouring grassland's copses line up (<see cref="TerrainScatter.StandField"/>).
		/// </summary>
		private const float StandMetres = 520f;

		/// <summary>
		/// A stand's spacing over its species' crown radius. Crowns in a closed stand interlock a little —
		/// trunks a bit under a crown's diameter apart — so 1.4 radii: with the random packing a scatter
		/// reaches, the crowns then cover about two thirds of the ground inside a stand, against a third
		/// at two radii.
		/// </summary>
		private const float CanopySpacing = 1.4f;

		/// <summary>
		/// Candidates per spacing² inside a stand at closure 1. A scatter is random sequential packing: a
		/// candidate too close to a tree is refused, not moved, so asking for more never packs past
		/// ~0.7 / spacing². Measured on the scatter's 2 m cells, 1.25 per spacing² places ~0.6 of that
		/// most (≈0.42 / spacing², half the asked), 0.5 ~0.42 of it, 3 only ~0.77 for over twice the asks.
		/// </summary>
		private const float StandPressure = 1.25f;

		/// <summary>What an older spec wrote for a tree rule — its density and spacing — so the rule is still recognised (<see cref="TreesBefore"/>).</summary>
		private readonly struct Before
		{
			public readonly float Density, Spacing;
			public Before(float density, float spacing)
			{
				Density = density;
				Spacing = spacing;
			}
		}

		private static Before Was(float density, float spacing) => new Before(density, spacing);

		/// <summary>How a biome's trees stand in woods (<see cref="Stand"/>).</summary>
		private readonly struct Wood
		{
			/// <summary>Share of the rule's ground under stands.</summary>
			public readonly float Cover;
			/// <summary>How closed a stand's canopy is, 0–1: 1 crowns touching, 0.5 an open wood.</summary>
			public readonly float Closure;
			/// <summary>Density in the open as a share of the density inside a stand: lone trees.</summary>
			public readonly float Open;
			/// <summary>The species' size here against its mature size (stunted at a tree line, on a bog).</summary>
			public readonly float Scale;
			/// <summary>Size of the largest stands, metres.</summary>
			public readonly float Metres;

			public Wood(float cover, float closure, float open, float scale, float metres)
			{
				Cover = cover;
				Closure = closure;
				Open = open;
				Scale = scale;
				Metres = metres;
			}
		}

		private static Wood W(float cover, float closure, float open = 0.02f, float scale = 1f, float metres = StandMetres) => new Wood(cover, closure, open, scale, metres);

		/// <summary>
		/// Trees in ones and small groves: saguaros, palms along a beach, snags on a wasteland — things that
		/// never close into a wood. Grouped as before (<see cref="Grouped"/>), at the realistic size spread.
		/// </summary>
		private static Scatter Trees(float density, float spacing, float slopeMax, params string[] prefabs)
		{
			Scatter before = TreesBefore(density, spacing, slopeMax, prefabs);
			Scatter s = before.Clone();
			s.WidthScale = TreeWidthScale;
			s.HeightScale = TreeHeightScale;
			s.Earlier.AddRange(before.Earlier);
			s.Earlier.Add(before);
			return s;
		}

		/// <summary>
		/// Trees that stand in woods: a stand field (<see cref="PrefabSpawnRule.forestMetres"/>) cut at the
		/// wood's cover, spacing from the species' crowns, and a density that closes the canopy inside a
		/// stand as far as the wood's closure asks.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Spacing from crowns.</b> <see cref="CanopySpacing"/> times the mean crown radius of the rule's
		/// species (<see cref="ProceduralArtCatalogue.CrownRadius"/>) at the wood's scale: 4.3 m for spruce,
		/// 5.5 for pine, 10 for oak, 12.6 for a rainforest giant. Spacing used to be a number per biome,
		/// 4–6 m for most forests, which with real-sized crowns would put three oaks inside one another.
		/// The scatter scales it again per tree by its own crown and width (the shared canopy).
		/// </para>
		/// <para>
		/// <b>Density from closure.</b> Inside a stand the rule asks for <c>closure × </c><see cref="StandPressure"/>
		/// candidates per spacing²; the rule's density is that times the share the stands and the open
		/// ground take of the whole, <c>open + (1 − open) × cover</c>, because the density is the average
		/// over the rule's ground and the scatter gathers it back into the stands. So the table says what a
		/// biome's woods are like — how much of it is wood, how closed — and the numbers follow from the trees.
		/// </para>
		/// <para>
		/// <b>Lone trees are counted standing.</b> The wood's open share is lone trees against the trees
		/// that actually stand in a stand, but the rule's <see cref="PrefabSpawnRule.forestOpen"/> scales
		/// what is asked for, and inside a stand most asks are refused by the spacing while in the open
		/// almost none are. Measured, a share of the asks kept is about <c>1 / (1 + 1.5 × pressure)</c>
		/// (pressure being asks per spacing², closure × <see cref="StandPressure"/>): 0.35 for a closed
		/// stand. Uncorrected, a taiga asking for 4% stood a lone spruce every 17 m in its clearings
		/// (15% of the stand's density); so the rule's open share is the wood's times that.
		/// </para>
		/// <para>
		/// <b>No groups inside.</b> A stand's thickets and glades come from the field's own small octaves and
		/// from the ground layers the rule is gated on, and its species patches from <see cref="PrefabSpawnRule.forestMix"/>;
		/// the 20–50 m groups the rule had before (<see cref="Grouped"/>) only fought the closed canopy, so
		/// they are off. The rule keeps its name, so the authoring tool updates it in place.
		/// </para>
		/// </remarks>
		private static Scatter Stand(Before was, float slopeMax, Wood wood, params string[] species)
		{
			Scatter before = TreesBefore(was.Density, was.Spacing, slopeMax, species);
			Scatter s = before.Clone();
			float crown = 0f;
			foreach (string name in species)
			{
				if (ProceduralArtCatalogue.TryTree(name, out TreeSpecies t))
				{
					crown += ProceduralArtCatalogue.CrownRadius(in t);
				}
			}
			crown = crown / Mathf.Max(1, species.Length) * wood.Scale;
			s.Spacing = Mathf.Clamp(Mathf.Round(crown * CanopySpacing * 10f) / 10f, 1f, 50f);
			float pressure = wood.Closure * StandPressure;
			float inside = pressure * 100f / (s.Spacing * s.Spacing);
			// The open share is of what stands, not of what is asked: see remarks.
			float open = Mathf.Clamp01(wood.Open / (1f + 1.5f * pressure));
			float share = open + (1f - open) * wood.Cover;
			s.Density = Mathf.Clamp(Mathf.Round(inside * share * 1000f) / 1000f, 0.001f, 50f);
			s.WidthScale = TreeWidthScale * wood.Scale;
			s.HeightScale = TreeHeightScale * wood.Scale;
			// No groups: a stand is the gathering (see remarks). Back to the rule's defaults, which a rule without a radius ignores.
			s.ClusterMetres = 0f;
			s.ClusterSize = 0f;
			s.ClusterSpacing = 4f;
			s.ClusterBackground = 0.2f;
			s.ClusterScaleBias = 0.5f;
			s.ForestMetres = wood.Metres;
			s.ForestCover = Mathf.Clamp(wood.Cover, 0.01f, 1f);
			s.ForestOpen = open;
			s.ForestEdge = 0.3f;
			s.ForestMix = 0.5f;
			s.ForestScaleBias = 0.3f;
			s.Earlier.AddRange(before.Earlier);
			s.Earlier.Add(before);
			return s;
		}

		/// <summary>Boulders: tree channel (capsule colliders), bedded into the slope.</summary>
		private static Scatter Boulders(float density, float spacing, params string[] prefabs)
		{
			var s = new Scatter
			{
				Name = "Boulders",
				Channel = PrefabSpawnChannel.TreeInstance,
				Prefabs = prefabs,
				Density = density,
				Spacing = spacing,
				MinWeight = 0.4f,
				Slope = new Vector2(0f, 50f),
				Scale = new Vector2(0.6f, 1.6f),
				AlignToNormal = true,
				MaxPerChunk = 5000,
				// Already bedded by a raised pivot: only the slope's extra, which a tree-channel
				// instance needs because it cannot tilt with the ground (~15% of its height at 30°).
				Sink = new Vector2(0f, 0.05f),
				SinkSlope = 0.5f,
			};
			// Boulders lie in piles round a big one, close enough to touch.
			return Grouped(s, 8f, 0.25f, 5f, 0.6f, g => g.Spacing = Mathf.Min(spacing, 3.5f));
		}

		private static string[] Grey => new[] { Boulder("Grey", "Round"), Boulder("Grey", "Slab"), Boulder("Grey", "Spire") };
		private static string[] GreyRound => new[] { Boulder("Grey", "Round"), Boulder("Grey", "Slab") };
		private static string[] Sandstone => new[] { Boulder("Sandstone", "Round"), Boulder("Sandstone", "Spire"), Boulder("Sandstone", "Slab") };
		private static string[] BasaltRocks => new[] { Boulder("Basalt", "Round"), Boulder("Basalt", "Slab") };
		private static string[] LimestoneRocks => new[] { Boulder("Limestone", "Round"), Boulder("Limestone", "Spire") };
		private static string SmallRocks(string material) => ProceduralArtCatalogue.SmallRocksPrefab(material);
		private static string Pebbles(string material) => ProceduralArtCatalogue.PebblesPrefab(material);

		// ── Rock formations and ice ───────────────────────────────────
		//
		// Formations (RockTypes × RockFormations) are the rock a biome's geology actually makes:
		// 1.2–4.5 m and up to ~2 000 triangles at LOD0, against the legacy boulders' 1.4–2.2 m and
		// 1 200. So they are added beside the legacy Boulder_* rules, never in place of them
		// (committed terrain data references those), and run 3–10× sparser with 2–3× the spacing.
		// Names come from RockArtNames, never typed here.

		/// <summary>Every variant of the named shapes of a rock type.</summary>
		private static string[] F(in RockType type, params string[] shapes) => RockArtNames.Formations(in type, shapes);

		/// <summary>
		/// Upright formations — columns, tors, pinnacles, pedestals, chimneys, standing slabs and
		/// flat-lying beds: tree channel (capsule collider), held vertical as rock that stands on its
		/// own structure does, so kept off steep ground where they would float on the downhill side.
		/// </summary>
		private static Scatter Outcrops(string label, float density, float spacing, string[] prefabs, float slopeMax = 25f)
		{
			var s = new Scatter
			{
				Name = "Formations: " + label,
				Channel = PrefabSpawnChannel.TreeInstance,
				Prefabs = prefabs,
				Density = density,
				Spacing = spacing,
				MinWeight = 0.5f,
				Slope = new Vector2(0f, slopeMax),
				Scale = new Vector2(0.8f, 1.25f),
				AlignToNormal = false,
				MaxPerChunk = 2000,
				// Bedded by construction; upright and narrower than they are tall, so a larger share of the slope's extra.
				Sink = new Vector2(0f, 0.05f),
				SinkSlope = 0.6f,
			};
			// Standing rock comes in outcrop fields, not one tor per hill.
			return Grouped(s, 14f, 0.3f, 5f, 0.5f, g => g.Spacing = Mathf.Min(spacing, 6f));
		}

		/// <summary>
		/// Lying formations — blocks, lumps, slabs, fallen columns, scree and talus heaps, pavement:
		/// tree channel (capsule collider), bedded into the slope like the legacy boulders.
		/// </summary>
		private static Scatter Blocks(string label, float density, float spacing, string[] prefabs, float slopeMax = 45f)
		{
			var s = new Scatter
			{
				Name = "Formations: " + label,
				Channel = PrefabSpawnChannel.TreeInstance,
				Prefabs = prefabs,
				Density = density,
				Spacing = spacing,
				MinWeight = 0.45f,
				Slope = new Vector2(0f, slopeMax),
				Scale = new Vector2(0.75f, 1.3f),
				AlignToNormal = true,
				MaxPerChunk = 2000,
				// Bedded by construction: only the slope's extra.
				Sink = new Vector2(0f, 0.05f),
				SinkSlope = 0.5f,
			};
			// Fallen rock lies in spreads of talus and roof fall.
			return Grouped(s, 10f, 0.25f, 4f, 0.6f, g => g.Spacing = Mathf.Min(spacing, 4f));
		}

		/// <summary>Ice boulders: tree channel (capsule collider), bedded like rock boulders.</summary>
		private static Scatter IceBlocks(float density, float spacing, params string[] shapes)
		{
			Scatter s = Boulders(density, spacing, RockArtNames.IceBoulders(shapes));
			AndEarlier(s, e => e.Name = "Ice boulders");
			return s;
		}

		/// <summary>Seracs, 8–16 m towers: tree channel (capsule collider), upright, sparse, on gentle ice only.</summary>
		private static Scatter Seracs(float density, float spacing, params string[] shapes)
		{
			Scatter s = Boulders(density, spacing, RockArtNames.Seracs(shapes));
			AndEarlier(s, e =>
			{
				e.Name = "Seracs";
				e.Slope = new Vector2(0f, 20f);
				e.Scale = new Vector2(0.7f, 1.15f);
				e.AlignToNormal = false;
				e.MaxPerChunk = 500;
				// Bedded by construction and wide at the foot: no flat embed, a little of the slope's extra.
				e.Sink = Vector2.zero;
				e.SinkSlope = 0.4f;
			});
			// An icefall's towers stand in ranks, not alone.
			s.ClusterMetres = 30f;
			s.ClusterBackground = 0.3f;
			s.ClusterSize = 6f;
			s.Spacing = Mathf.Min(spacing, 20f);
			return s;
		}

		/// <summary>
		/// Gathers a point-sampled rule into groups of about <paramref name="size"/> instances (detail cells)
		/// at least <paramref name="metres"/> across (<see cref="PrefabSpawnRule.clusterSize"/>) and keeps
		/// what it was before as an <see cref="Scatter.Earlier"/>, so a rule an earlier run wrote is still
		/// recognised. <paramref name="regroup"/> makes the changes that go with grouping (closer spacing,
		/// size varying instance by instance) on the grouped rule only.
		/// </summary>
		private static Scatter Grouped(Scatter s, float metres, float background, float size, float scaleBias = 0.5f, Action<Scatter> regroup = null)
		{
			Scatter before = s.Clone();
			s.ClusterMetres = metres;
			s.ClusterBackground = background;
			s.ClusterSize = size;
			s.ClusterScaleBias = scaleBias;
			regroup?.Invoke(s);
			s.Earlier.Add(before);
			return s;
		}

		/// <summary>Makes a change to a rule and to every earlier version of it, for helpers built on other helpers.</summary>
		private static void AndEarlier(Scatter s, Action<Scatter> change)
		{
			change(s);
			foreach (Scatter earlier in s.Earlier)
			{
				change(earlier);
			}
		}

		private static Entry E(string biome, Layer main, Layer[] details, Cliff cliff, string lakebed, string riverbed)
		{
			return new Entry
			{
				Biome = biome,
				Main = main,
				Details = details ?? new Layer[0],
				Cliffs = cliff != null ? new[] { cliff } : new Cliff[0],
				Lakebed = lakebed,
				Riverbed = riverbed,
			};
		}

		// ── The table ─────────────────────────────────────────────────

		/// <summary>Every biome's entry, keyed by the biome asset's name.</summary>
		public static readonly Entry[] Entries = Build();

		private static Entry[] Build()
		{
			Entry[] entries = new[]
			{
				// ── Under the sea (tiers 0–2) ──
				// What lives on the sea floor rides on the lakebed (Entry.Bed), by depth: kelp and seaweed where
				// the light reaches, corals in warm shallows, sponges and sea fans below, little in the abyss.
				E("Abyssal Plain", L(Ground.Silt), Ds(D(Ground.Mud, 96f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
					.Under(Sea("Sponge", 0.2f, 0f, 0f, 128), Sea("Starfish", 0.3f, 0f, 0f, 96), Sea("Anemone", 0.15f, 0f, 0f, 128)),
				E("Abyss", L(Ground.Silt), Ds(D(Ground.Basalt, 64f, 3f, 20f, 90f)), C(Ground.Basalt, 35f), Ground.Silt, Ground.Silt)
					.Under(Sea("Sponge", 0.15f, 0f, 0f, 128, 60f), Sea("Anemone", 0.15f, 0f, 0f, 128, 60f)),
				E("Deep Ocean", L(Ground.Silt), Ds(D(Ground.Mud, 128f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
					.Under(Sea("Sponge", 0.3f, 0f, 0f, 128), Sea("Starfish", 0.2f, 0f, 0f, 96), Sea("Anemone", 0.2f, 0f, 0f, 128)),
				E("Ocean", L(Ground.Sand), Ds(D(Ground.Silt, 64f), D(Ground.Pebbles, 32f, 3f)), C(Ground.Rock), Ground.Sand, Ground.Sand)
					.Under(Sea("Kelp", 3f, 4f, 30f, 160), Sea("Seaweed", 2f, 1.5f, 20f, 160), Sea("Sponge", 1f, 6f, 0f, 128), Sea("SeaFan", 0.5f, 8f, 120f, 128),
						Sea("Urchin", 1f, 2f, 40f, 128), Sea("Starfish", 0.5f, 1f, 0f, 96), Sea("Shells", 1f, 1f, 30f, 96))
					.Moved("main", "Kelp"),
				E("Seamount", L(Ground.Basalt), Ds(D(Ground.Silt, 48f, 2f, 0f, 20f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
					.Under(Sea("Kelp", 4f, 4f, 30f, 160), Sea("SeaFan", 3f, 4f, 0f, 160, 60f), Sea("Sponge", 2f, 4f, 0f, 160, 60f), Sea("Anemone", 1f, 2f, 0f, 128, 60f))
					.Moved("detail/0", "Kelp"),
				E("Underwater Canyon", L(Ground.Silt), Ds(D(Ground.Rock, 48f, 3f, 25f, 90f), D(Ground.Gravel, 64f)), C(Ground.CliffRock, 38f), Ground.Silt, Ground.Silt)
					.Under(Sea("SeaFan", 1.5f, 4f, 0f, 160, 70f), Sea("Sponge", 1.5f, 4f, 0f, 160, 70f), Sea("Anemone", 0.5f, 2f, 0f, 128, 70f)),
				E("Coastal Water", L(Ground.Sand), Ds(D(Ground.Pebbles, 32f, 3f, -1f, -1f, Stones(SmallRocks("Grey"), 3f)), D(Ground.Silt, 64f)),
					C(Ground.Rock), Ground.Sand, Ground.Pebbles)
					.Under(Sea("Seaweed", 6f, 1f, 20f, 200), Sea("Kelp", 6f, 5f, 30f, 200),
						Sea("Urchin", 3f, 1.5f, 30f, 160), Sea("Starfish", 1f, 0.5f, 40f, 128), Sea("Shells", 4f, 0.5f, 25f, 128),
						Sea("Anemone", 1f, 2f, 40f, 128), SeaStones(SmallRocks("Grey"), 2f, 0.5f, 0f))
					.Moved("detail/0", "Kelp").Moved("lakebed", "Seagrass"),
				E("Coral Reef", L(Ground.Coral), Ds(D(Ground.SandBeach, 32f), D(Ground.Sand, 96f)),
					C(Ground.Limestone), Ground.Sand, Ground.Sand)
					.Under(Sea("Coral", 20f, 1f, 30f, 200), Sea("BrainCoral", 3f, 1f, 30f, 200), Sea("TableCoral", 2f, 1f, 15f, 200),
						Sea("SeaFan", 2f, 3f, 60f, 160), Sea("Sponge", 2f, 2f, 0f, 160), Sea("Anemone", 3f, 1f, 40f, 160),
						Sea("Urchin", 2f, 1f, 30f, 128), Sea("Starfish", 1f, 0.5f, 40f, 128), Sea("Shells", 3f, 0.5f, 25f, 128),
						Sea("Kelp", 1f, 4f, 25f, 96))
					.Moved("main", "Coral", "Kelp").Moved("lakebed", "Seagrass"),
				E("Subsurface Ocean Vent", L(Ground.Basalt), Ds(D(Ground.Silt, 48f), D(Ground.Sulphur, 24f, 4f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
					.Under(Sea("TubeWorms", 6f, 0f, 0f, 200, 50f), Sea("Coral", 3f, 0f, 0f, 128), Sea("Anemone", 1f, 0f, 0f, 128))
					.Moved("main", "Coral"),
				E("Methane Lake", L(Ground.Frost), Ds(D(Ground.Ice, 64f)), C(Ground.Ice), Ground.Tholin, Ground.Tholin),
				E("Lava Tube", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 3f), Blocks("Basalt roof fall", 0.05f, 12f, F(RockTypes.Basalt, "Block", "Fallen"))), Ds(D(Ground.Ash, 48f), D(Ground.Lava, 96f, 5f)), C(Ground.Basalt, 35f), Ground.Basalt, Ground.Basalt),

				// ── Shore (tier 3) ──
				E("Beach", L(Ground.SandBeach, Trees(0.03f, 14f, 20f, "Palm")),
					Ds(D(Ground.Sand, 64f, 2f, -1f, -1f, Grass("GrassTall", 8f, 160)), D(Ground.Pebbles, 24f, 3f, -1f, -1f, Boulders(0.3f, 6f, GreyRound))),
					C(Ground.Rock), Ground.Sand, Ground.Pebbles)
					// The shallows off the beach.
					.Under(Sea("Shells", 2f, 0.5f, 15f, 96), Sea("Starfish", 0.5f, 0.5f, 20f, 96))
					.Moved("lakebed", "Seagrass"),
				E("Dust Sea", L(Ground.Regolith), Ds(D(Ground.Sand, 128f)), C(Ground.Rock), Ground.Regolith, Ground.Regolith),
				E("Estuary", L(Ground.Mud, Plants("Reeds", 30f, 220)), Ds(D(Ground.Silt, 48f), D(Ground.Grass, 48f, 2f, 0f, 10f, Grass("GrassTall", 20f), Bush("Willow", 0.2f))),
					C(Ground.Soil), Ground.Silt, Ground.Silt),
				E("Impact Basin", L(Ground.Regolith, Stones(SmallRocks("Grey"), 4f)), Ds(D(Ground.Rock, 48f, 3f, 15f, 90f, Boulders(0.3f, 8f, GreyRound), Blocks("Mare basalt", 0.04f, 14f, F(RockTypes.Basalt, "Block"))), D(Ground.Gravel, 64f)),
					C(Ground.CliffRock), Ground.Regolith, Ground.Regolith),
				E("Mangrove", L(Ground.Mud, Stand(Was(2f, 4f), 15f, W(0.85f, 1f, scale: 0.35f), "Jungle"), Plants("Reeds", 10f)), Ds(D(Ground.Silt, 32f), D(Ground.JungleFloor, 48f, 2f, -1f, -1f, Plants("Fern", 6f), Bush("Hibiscus", 0.2f))),
					C(Ground.Soil), Ground.Silt, Ground.Mud),
				E("Molten Surface", L(Ground.Lava), Ds(D(Ground.Basalt, 32f, 3f, -1f, -1f, Stones(SmallRocks("Basalt"), 2f), Blocks("Obsidian", 0.03f, 14f, F(RockTypes.Obsidian, "Chunk", "Shard"))), D(Ground.Ash, 64f)), C(Ground.Basalt), Ground.Basalt, Ground.Basalt),
				E("Peat Bog", L(Ground.Peat, Plants("Reeds", 15f), Bush("Willow", 0.3f, 0.7f), Stand(Was(0.05f, 12f), 15f, W(0.1f, 0.12f, open: 0.03f, scale: 0.7f), "Dead", "Birch")), Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Grass("GrassTuft", 20f)), D(Ground.Mud, 64f)),
					C(Ground.Soil), Ground.Mud, Ground.Peat),
				E("Rille", L(Ground.Regolith, Stones(SmallRocks("Basalt"), 3f)), Ds(D(Ground.Basalt, 48f, 3f, 20f, 90f, Boulders(0.2f, 8f, BasaltRocks), Blocks("Basalt blocks", 0.05f, 12f, F(RockTypes.Basalt, "Block", "Fallen"))), D(Ground.Gravel, 64f)),
					C(Ground.Basalt), Ground.Regolith, Ground.Regolith),
				E("Rocky Coast", L(Ground.Rock, Boulders(0.6f, 5f, Grey), Stones(SmallRocks("Grey"), 5f)),
					Ds(D(Ground.Pebbles, 32f, 3f, 0f, 25f, Stones(Pebbles("Grey"), 10f), Blocks("Granite", 0.04f, 12f, F(RockTypes.Granite, "Corestone", "Split"))), D(Ground.GrassDry, 64f, 2f, 0f, 20f, Grass("GrassTuft", 12f), Bush("Gorse", 0.5f))),
					C(Ground.CliffRock, 38f), Ground.Pebbles, Ground.Gravel),
				E("Tidal Fracture", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.03f, 14f, "Calved", "Slab")), Ds(D(Ground.Frost, 64f), D(Ground.Rock, 48f, 3f, 20f, 90f)), C(Ground.Ice, 38f), Ground.Ice, Ground.Ice),

				// Frozen sea of a frozen-through world: flat shelf ice with pressure ridges, frost and drifted snow, a few stranded blocks and seracs.
				E("Ice Shelf", L(Ground.Ice, IceBlocks(0.03f, 14f), Seracs(0.005f, 40f)),
					Ds(D(Ground.Frost, 64f), D(Ground.Snow, 96f)), C(Ground.Ice, 38f), Ground.Ice, Ground.Ice),

				// ── Lowland (tier 4) ──
				E("Cryovolcanic Plain", L(Ground.Frost, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.02f, 16f, "Rounded", "Slab")), Ds(D(Ground.Ice, 64f), D(Ground.Snow, 96f)), C(Ground.Ice), Ground.Ice, Ground.Ice),
				E("Grassland", L(Ground.Grass, Grass("GrassLush", 45f, 220), Stand(Was(0.15f, 12f), 25f, W(0.1f, 0.6f, open: 0.06f), "Oak"), Bush("Bramble", 0.15f), Bush("Gorse", 0.15f), Bush("Hazel", 0.08f)),
					Ds(D(Ground.GrassMeadow, 64f, 2f, -1f, -1f, Plants("FlowersMeadow", 6f, 180), Grass("GrassLush", 30f)),
						D(Ground.Soil, 96f, 2f, 15f, 40f, Boulders(0.05f, 10f, GreyRound))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				E("Oasis", L(Ground.Grass, Stand(Was(1.5f, 6f), 15f, W(0.6f, 0.9f, open: 0.05f, metres: 160f), "Palm"), Grass("GrassTall", 25f), Bush("Hibiscus", 0.4f)), Ds(D(Ground.Sand, 64f), D(Ground.Mud, 32f, 2f, 0f, 8f, Plants("Reeds", 12f))),
					C(Ground.Sandstone), Ground.Mud, Ground.Sand),
				E("Plains", L(Ground.GrassDry, Grass("GrassDry", 40f, 220), Plants("FlowersWarm", 3f), Stand(Was(0.03f, 20f), 20f, W(0.03f, 0.5f, open: 0.08f), "Oak"), Bush("Bramble", 0.05f)),
					Ds(D(Ground.Grass, 96f, 2f, -1f, -1f, Grass("GrassLush", 25f)), D(Ground.Soil, 128f, 2f, 15f, 45f)),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				E("Radiation Plain", L(Ground.Regolith, Stones(SmallRocks("Grey"), 2f)), Ds(D(Ground.CrackedEarth, 64f), D(Ground.Gravel, 48f)), C(Ground.Rock), Ground.Regolith, Ground.Regolith),
				E("Regolith Plain", L(Ground.Regolith, Stones(SmallRocks("Grey"), 3f), Stones(Pebbles("Grey"), 5f)),
					Ds(D(Ground.Gravel, 48f), D(Ground.Rock, 48f, 3f, 20f, 90f, Boulders(0.1f, 10f, GreyRound))),
					C(Ground.Rock), Ground.Regolith, Ground.Regolith),
				E("Runaway Greenhouse Plain", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 2f), Blocks("Basalt blocks", 0.03f, 16f, F(RockTypes.Basalt, "Block"))), Ds(D(Ground.Sulphur, 96f), D(Ground.Ash, 64f)), C(Ground.Basalt), Ground.Basalt, Ground.Basalt),
				E("Salt Flat", L(Ground.SaltCrust), Ds(D(Ground.CrackedEarth, 128f), D(Ground.Sand, 96f)), C(Ground.Rock), Ground.SaltCrust, Ground.SaltCrust),
				E("Scrubland", L(Ground.GrassDry, Plants("ShrubDry", 8f), Plants("ShrubSmall", 4f), Bush("Sagebrush", 1.5f), Bush("Creosote", 0.6f), Grass("GrassTuft", 15f), Stand(Was(0.05f, 15f), 25f, W(0.15f, 0.1f, open: 0.2f), "Acacia")),
					Ds(D(Ground.Soil, 64f), D(Ground.Rock, 48f, 3f, 25f, 90f, Boulders(0.1f, 8f, Sandstone), Blocks("Sandstone beds", 0.03f, 14f, F(RockTypes.Sandstone, "Block", "Tilted")))),
					C(Ground.Rock), Ground.Gravel, Ground.Gravel),
				E("Sulphur Flats", L(Ground.Sulphur, Stones(SmallRocks("Basalt"), 1f)), Ds(D(Ground.CrackedEarth, 64f), D(Ground.Ash, 96f)), C(Ground.Basalt), Ground.Sulphur, Ground.Sulphur),
				E("Tholin Plain", L(Ground.Tholin, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.03f, 14f, "Rounded")), Ds(D(Ground.Frost, 96f), D(Ground.Gravel, 64f)), C(Ground.Rock), Ground.Tholin, Ground.Tholin),
				E("Wasteland", L(Ground.CrackedEarth, Plants("ShrubDry", 3f), Trees(0.05f, 15f, 25f, "Dead"), Stones(SmallRocks("Grey"), 2f)),
					Ds(D(Ground.Ash, 64f), D(Ground.Gravel, 48f)), C(Ground.Rock), Ground.Mud, Ground.Gravel),
				E("Wetlands", L(Ground.Grass, Grass("GrassTall", 30f), Stand(Was(0.08f, 10f), 15f, W(0.15f, 0.4f, open: 0.03f), "Birch"), Bush("Willow", 0.8f)),
					Ds(D(Ground.Mud, 48f, 2f, 0f, 10f, Plants("Reeds", 25f, 220)), D(Ground.Moss, 64f, 2f, -1f, -1f, Plants("Fern", 3f))),
					C(Ground.Soil), Ground.Mud, Ground.Mud),
				E("Farmland", L(Ground.Soil, Stand(Was(0.02f, 20f), 15f, W(0.03f, 0.5f, open: 0.06f), "Oak"), Bush("Bramble", 0.3f), Bush("Hazel", 0.15f)), Ds(D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassLush", 30f)), D(Ground.GrassDry, 64f, 2f, -1f, -1f, Grass("GrassDry", 20f))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				E("Lake", L(Ground.Grass, Grass("GrassLush", 30f), Bush("Willow", 0.3f)), Ds(D(Ground.Mud, 32f, 2f, 0f, 10f, Plants("Reeds", 15f)), D(Ground.Pebbles, 48f)),
					C(Ground.Rock), Ground.Mud, Ground.Pebbles),
				E("River", L(Ground.Grass, Grass("GrassLush", 25f), Bush("Willow", 0.4f)), Ds(D(Ground.Pebbles, 32f), D(Ground.Mud, 48f, 2f, 0f, 10f, Plants("Reeds", 10f))),
					C(Ground.Rock), Ground.Pebbles, Ground.Gravel),
				E("Swamp", L(Ground.Mud, Plants("Reeds", 20f), Stand(Was(0.2f, 8f), 15f, W(0.35f, 0.3f, open: 0.05f), "Dead"), Grass("GrassTall", 15f), Bush("Willow", 0.4f)), Ds(D(Ground.Peat, 48f), D(Ground.Moss, 64f, 2f, -1f, -1f, Plants("Fern", 5f))),
					C(Ground.Soil), Ground.Mud, Ground.Mud),
				E("Swamp Cave", L(Ground.Mud, Plants("Reeds", 6f)), Ds(D(Ground.Moss, 48f), D(Ground.Rock, 48f, 3f, 20f, 90f)), C(Ground.CliffRock, 38f), Ground.Mud, Ground.Mud),
				E("Swamp Ruins", L(Ground.Mud, Plants("Reeds", 15f), Stand(Was(0.15f, 8f), 15f, W(0.3f, 0.3f, open: 0.05f), "Dead"), Bush("Willow", 0.3f)), Ds(D(Ground.Flagstone, 32f, 4f), D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("Fern", 5f))),
					C(Ground.Soil), Ground.Mud, Ground.Mud),
				E("Swamp Temple", L(Ground.Mud, Plants("Reeds", 10f)), Ds(D(Ground.Flagstone, 24f, 4f), D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("Fern", 5f))), C(Ground.CliffRock), Ground.Mud, Ground.Mud),
				E("Jungle Ruins", L(Ground.JungleFloor, Stand(Was(1.2f, 5f), 25f, W(0.7f, 0.8f), "Jungle"), Plants("Fern", 15f), Plants("ShrubSmall", 6f), Bush("Hibiscus", 0.5f)), Ds(D(Ground.Flagstone, 32f, 4f), D(Ground.Moss, 48f)),
					C(Ground.Rock), Ground.Mud, Ground.Mud),
				E("Jungle Temple", L(Ground.JungleFloor, Stand(Was(0.8f, 5f), 25f, W(0.55f, 0.7f), "Jungle"), Plants("Fern", 12f), Bush("Hibiscus", 0.4f), Bush("Rhododendron", 0.2f)), Ds(D(Ground.Flagstone, 24f, 4f), D(Ground.Moss, 48f)),
					C(Ground.Rock), Ground.Mud, Ground.Mud),

				// ── Highland (tier 5) ──
				E("Bamboo Forest", L(Ground.ForestFloor, Stand(Was(3f, 3f), 25f, W(0.7f, 0.5f, metres: 200f), "Bamboo"), Plants("Fern", 8f), Grass("GrassTall", 6f), Bush("Rhododendron", 0.6f)), Ds(D(Ground.Moss, 48f), D(Ground.Soil, 64f, 2f, 20f, 45f)),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				E("Forest", L(Ground.ForestFloor, Stand(Was(2f, 6f), 30f, W(0.8f, 1f), "Oak", "Birch"), Plants("Fern", 12f), Plants("DebrisForest", 10f, 128), Plants("ShrubSmall", 3f),
						Bush("Hazel", 0.5f), Bush("Bramble", 0.6f), Bush("Laurel", 0.2f)),
					Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("Fern", 8f), Bush("Rhododendron", 0.3f)), D(Ground.NeedleLitter, 96f, 2f, -1f, -1f, Stand(Was(1.5f, 6f), 30f, W(0.8f, 1f), "Spruce")),
						D(Ground.Soil, 64f, 2f, 20f, 45f, Boulders(0.05f, 10f, GreyRound))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				E("Geyser Basin", L(Ground.CrackedEarth, Grass("GrassTuft", 4f), Blocks("Tuff", 0.02f, 16f, F(RockTypes.Tuff, "Tafoni")), Blocks("Obsidian", 0.015f, 18f, F(RockTypes.Obsidian, "Chunk"))), Ds(D(Ground.Sulphur, 32f, 4f), D(Ground.Mud, 48f)), C(Ground.Rock), Ground.Mud, Ground.Mud),
				E("Hills", L(Ground.Grass, Grass("GrassLush", 35f, 220), Plants("FlowersMeadow", 4f), Stand(Was(0.1f, 12f), 25f, W(0.12f, 0.6f, open: 0.05f), "Oak"), Bush("Gorse", 0.5f), Bush("Juniper", 0.2f), Bush("Bramble", 0.15f), Outcrops("Granite tors", 0.008f, 40f, F(RockTypes.Granite, "Tor", "Perched"), 15f)),
					Ds(D(Ground.Moss, 64f), D(Ground.Rock, 48f, 3f, 25f, 90f, Boulders(0.1f, 8f, GreyRound), Blocks("Granite corestones", 0.04f, 12f, F(RockTypes.Granite, "Corestone", "Split")))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				E("Ice Geyser Field", L(Ground.Snow, Stones(SmallRocks("Grey"), 0.5f), IceBlocks(0.02f, 16f, "Calved")), Ds(D(Ground.Ice, 64f), D(Ground.Frost, 48f)), C(Ground.Ice), Ground.Ice, Ground.Ice),
				E("Jungle", L(Ground.JungleFloor, Stand(Was(3f, 5f), 30f, W(0.9f, 1f), "Jungle"), Stand(Was(0.3f, 8f), 20f, W(0.9f, 0.25f, open: 0.3f), "Palm"), Plants("Fern", 25f), Plants("ShrubSmall", 10f), Grass("GrassTall", 10f), Bush("Hibiscus", 0.6f)),
					Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Stand(Was(0.4f, 4f), 25f, W(0.4f, 0.25f, open: 0.02f, metres: 200f), "Bamboo")), D(Ground.Mud, 64f, 2f, 0f, 10f)),
					C(Ground.Rock), Ground.Mud, Ground.Mud),
				E("Karst", L(Ground.Grass, Grass("GrassLush", 30f), Stand(Was(0.2f, 10f), 25f, W(0.2f, 0.6f, open: 0.05f), "Oak"), Plants("Fern", 4f), Bush("Box", 0.6f), Bush("Juniper", 0.15f), Blocks("Limestone pavement", 0.03f, 18f, F(RockTypes.Limestone, "Pavement", "Block"), 12f)),
					Ds(D(Ground.Limestone, 48f, 3f, 20f, 90f, Boulders(0.3f, 6f, LimestoneRocks), Outcrops("Limestone pinnacles", 0.06f, 12f, F(RockTypes.Limestone, "Pinnacle"), 30f)), D(Ground.Soil, 64f)),
					C(Ground.Limestone, 35f), Ground.Mud, Ground.Gravel),
				E("Nitrogen Ice Field", L(Ground.Frost), Ds(D(Ground.Snow, 64f), D(Ground.Ice, 96f)), C(Ground.Ice), Ground.Frost, Ground.Frost),
				E("Savanna", L(Ground.GrassDry, Grass("GrassDry", 40f, 220), Stand(Was(0.08f, 20f), 20f, W(0.25f, 0.15f, open: 0.25f), "Acacia"), Plants("ShrubDry", 2f), Outcrops("Granite kopjes", 0.01f, 30f, F(RockTypes.Granite, "Tor", "Perched"), 15f)),
					Ds(D(Ground.Soil, 96f), D(Ground.Clay, 64f, 2f, -1f, -1f, Boulders(0.03f, 12f, Sandstone))),
					C(Ground.Sandstone), Ground.Mud, Ground.Gravel),
				E("Steppe", L(Ground.GrassDry, Grass("GrassDry", 35f), Grass("GrassTuft", 15f), Plants("FlowersWarm", 2f), Bush("Sagebrush", 0.8f), Bush("Juniper", 0.05f, 0.7f)),
					Ds(D(Ground.Soil, 96f), D(Ground.Gravel, 64f, 2f, -1f, -1f, Stones(SmallRocks("Grey"), 1f))),
					C(Ground.Rock), Ground.Gravel, Ground.Gravel),
				E("Taiga", L(Ground.NeedleLitter, Stand(Was(2.5f, 5f), 30f, W(0.8f, 1f), "Spruce"), Stand(Was(0.8f, 6f), 30f, W(0.8f, 0.45f), "Pine"), Plants("Fern", 4f), Grass("GrassTuft", 8f), Bush("Juniper", 0.5f)),
					Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Stand(Was(0.2f, 6f), 25f, W(0.8f, 0.25f, open: 0.05f), "Birch"), Plants("ShrubSmall", 3f), Bush("Willow", 0.3f, 0.6f)), D(Ground.Snow, 96f)),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				E("Valley", L(Ground.Grass, Grass("GrassLush", 45f, 220), Stand(Was(0.3f, 10f), 25f, W(0.3f, 0.7f, open: 0.05f), "Oak", "Birch"), Bush("Hazel", 0.3f), Bush("Bramble", 0.3f), Bush("Willow", 0.15f)),
					Ds(D(Ground.GrassMeadow, 64f, 2f, -1f, -1f, Plants("FlowersMeadow", 8f, 180)), D(Ground.Pebbles, 32f, 3f, 0f, 8f, Blocks("Conglomerate", 0.03f, 14f, F(RockTypes.Conglomerate, "Boulder", "Block"), 10f))),
					C(Ground.Rock), Ground.Mud, Ground.Pebbles),
				E("Woodland", L(Ground.Grass, Stand(Was(0.8f, 8f), 30f, W(0.55f, 0.8f, open: 0.05f), "Oak", "Birch"), Grass("GrassLush", 25f), Plants("FlowersMeadow", 3f), Bush("Hazel", 0.8f), Bush("Bramble", 1f)),
					Ds(D(Ground.ForestFloor, 48f, 2f, -1f, -1f, Plants("Fern", 6f), Plants("DebrisForest", 6f, 128)), D(Ground.Moss, 64f)),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				E("Castle", L(Ground.Flagstone), Ds(D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassTuft", 8f), Bush("Box", 0.4f)), D(Ground.Gravel, 48f)), C(Ground.CliffRock), Ground.Mud, Ground.Gravel),
				E("Marble Palace", L(Ground.Flagstone), Ds(D(Ground.Limestone, 48f, 2f, -1f, -1f, Blocks("Marble", 0.02f, 16f, F(RockTypes.Marble, "Block", "Boulder"))), D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassLush", 15f), Bush("Box", 0.6f))), C(Ground.Limestone), Ground.Mud, Ground.Gravel),
				E("Cave", L(Ground.Rock, Stones(SmallRocks("Grey"), 4f), Stones(Pebbles("Grey"), 6f), Blocks("Limestone", 0.05f, 10f, F(RockTypes.Limestone, "Block")), Outcrops("Limestone pinnacles", 0.03f, 12f, F(RockTypes.Limestone, "Pinnacle"))), Ds(D(Ground.Gravel, 48f), D(Ground.Mud, 64f)), C(Ground.CliffRock, 35f), Ground.Mud, Ground.Gravel),
				E("Forest Ruins", L(Ground.ForestFloor, Stand(Was(1f, 6f), 30f, W(0.6f, 0.9f), "Oak"), Plants("Fern", 10f), Plants("DebrisForest", 6f, 128), Bush("Bramble", 0.8f), Bush("Laurel", 0.3f), Bush("Box", 0.15f)), Ds(D(Ground.Flagstone, 32f, 4f), D(Ground.Moss, 48f)),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),

				// ── Mountain (tier 6) ──
				E("Badlands", L(Ground.Clay, Plants("ShrubDry", 1f), Bush("Sagebrush", 0.3f), Bush("Creosote", 0.15f), Stones(SmallRocks("Sandstone"), 2f), Outcrops("Hoodoos", 0.04f, 14f, F(RockTypes.Sandstone, "Pedestal")),
						Blocks("Shale", 0.04f, 12f, F(RockTypes.Shale, "Stack", "Scree"))),
					Ds(D(Ground.Sandstone, 48f, 3f, 20f, 90f, Boulders(0.15f, 8f, Sandstone), Outcrops("Sandstone ledges", 0.03f, 18f, F(RockTypes.Sandstone, "Ledges"), 35f)), D(Ground.CrackedEarth, 64f)),
					C(Ground.Sandstone, 35f), Ground.Clay, Ground.Clay),
				E("Desert", L(Ground.Sand, Trees(0.04f, 15f, 15f, "Saguaro"), Plants("CactusBarrel", 0.5f, 96), Plants("ShrubDry", 0.5f), Bush("Creosote", 0.5f)),
					Ds(D(Ground.Sandstone, 48f, 3f, 15f, 90f, Boulders(0.03f, 12f, Sandstone), Outcrops("Sandstone pedestals", 0.01f, 30f, F(RockTypes.Sandstone, "Pedestal", "Ledges"), 30f)), D(Ground.Gravel, 64f)),
					C(Ground.Sandstone), Ground.Sand, Ground.Sand),
				E("Mountain Slope", L(Ground.Rock, Boulders(0.3f, 6f, Grey), Stones(SmallRocks("Grey"), 4f), Stand(Was(0.1f, 10f), 30f, W(0.1f, 0.35f, scale: 0.8f), "Pine"), Bush("MountainPine", 0.4f, 1f, 40f), Bush("Juniper", 0.15f, 0.7f, 40f),
						Outcrops("Slate upright", 0.03f, 14f, F(RockTypes.Slate, "Upright"), 30f), Blocks("Slate", 0.05f, 10f, F(RockTypes.Slate, "Stack"))),
					Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Blocks("Slate scree", 0.04f, 12f, F(RockTypes.Slate, "Scree"))), D(Ground.GrassDry, 64f, 2f, 0f, 25f, Grass("GrassTuft", 10f))),
					C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel),
				E("Rocky Terrain", L(Ground.Rock, Boulders(0.6f, 5f, Grey), Stones(SmallRocks("Grey"), 6f), Bush("MountainPine", 0.1f, 0.9f, 40f), Blocks("Quartzite", 0.06f, 10f, F(RockTypes.Quartzite, "Block", "Wedge"))),
					Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Blocks("Quartzite scree", 0.03f, 14f, F(RockTypes.Quartzite, "Scree"))), D(Ground.Pebbles, 32f, 3f, -1f, -1f, Stones(Pebbles("Grey"), 8f))),
					C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel),
				E("Tundra", L(Ground.Lichen, Grass("GrassTuft", 20f), Plants("ShrubSmall", 1f), Bush("Willow", 1f, 0.25f), Bush("Juniper", 0.15f, 0.3f), Stones(SmallRocks("Grey"), 2f)),
					Ds(D(Ground.Moss, 64f), D(Ground.Gravel, 48f, 2f, -1f, -1f, Boulders(0.05f, 10f, GreyRound),
						Blocks("Granite erratics", 0.015f, 20f, F(RockTypes.Granite, "Corestone")), Blocks("Gneiss erratics", 0.015f, 20f, F(RockTypes.Gneiss, "Boulder")))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				E("Crater", L(Ground.Gravel, Stones(SmallRocks("Grey"), 4f)), Ds(D(Ground.Rock, 48f, 3f, 20f, 90f, Boulders(0.3f, 6f, GreyRound), Blocks("Andesite talus", 0.03f, 14f, F(RockTypes.Andesite, "Talus"))),
						D(Ground.Ash, 64f, 2f, -1f, -1f, Blocks("Andesite", 0.05f, 12f, F(RockTypes.Andesite, "Block", "Platy")), Blocks("Pumice", 0.04f, 12f, F(RockTypes.Pumice, "Lump")))),
					C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel),
				E("Fortress", L(Ground.Flagstone), Ds(D(Ground.Gravel, 48f), D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassTuft", 5f))), C(Ground.CliffRock), Ground.Mud, Ground.Gravel),
				E("Volcanic Cave", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 3f), Blocks("Obsidian", 0.06f, 10f, F(RockTypes.Obsidian, "Chunk", "Shard", "Scree"))), Ds(D(Ground.Lava, 96f, 5f), D(Ground.Ash, 48f)), C(Ground.Basalt, 35f), Ground.Basalt, Ground.Basalt),
				E("Volcanic Temple", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 2f), Blocks("Basalt fallen columns", 0.03f, 16f, F(RockTypes.Basalt, "Fallen", "Block"))), Ds(D(Ground.Flagstone, 32f, 4f), D(Ground.Lava, 96f, 5f)), C(Ground.Basalt), Ground.Basalt, Ground.Basalt),

				// ── Alpine (tier 7) ──
				E("Alpine", L(Ground.Rock, Boulders(0.3f, 6f, GreyRound), Stones(SmallRocks("Grey"), 4f),
						Blocks("Schist", 0.06f, 10f, F(RockTypes.Schist, "Lump", "Ridge", "Flags")), Blocks("Gneiss", 0.03f, 12f, F(RockTypes.Gneiss, "Boulder"))), Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Bush("MountainPine", 0.3f, 0.8f, 40f)), D(Ground.Snow, 64f, 2f, -1f, -1f, Grass("GrassTuft", 5f))),
					C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel),
				E("Alpine Meadow", L(Ground.GrassMeadow, Grass("GrassLush", 30f), Plants("FlowersMeadow", 12f, 200), Stand(Was(0.05f, 12f), 25f, W(0.08f, 0.15f, open: 0.02f, scale: 0.65f), "Spruce"), Bush("MountainPine", 0.4f), Bush("Juniper", 0.2f, 0.7f)),
					Ds(D(Ground.Rock, 48f, 3f, 25f, 90f, Boulders(0.08f, 8f, GreyRound), Blocks("Gneiss", 0.03f, 12f, F(RockTypes.Gneiss, "Boulder", "Block"))), D(Ground.Moss, 64f)),
					C(Ground.Rock), Ground.Gravel, Ground.Gravel),
				E("High Desert", L(Ground.Sand, Plants("ShrubDry", 6f), Grass("GrassTuft", 6f), Bush("Sagebrush", 2f), Bush("Juniper", 0.05f, 0.8f)), Ds(D(Ground.Gravel, 64f), D(Ground.Sandstone, 48f, 3f, 20f, 90f, Boulders(0.1f, 8f, Sandstone),
						Outcrops("Sandstone pedestals", 0.03f, 16f, F(RockTypes.Sandstone, "Pedestal", "Ledges"), 35f), Blocks("Sandstone beds", 0.03f, 14f, F(RockTypes.Sandstone, "Tilted", "Block")))),
					C(Ground.Sandstone), Ground.Gravel, Ground.Gravel),
				E("Scree", L(Ground.Gravel, Stones(SmallRocks("Grey"), 10f), Stones(Pebbles("Grey"), 15f), Bush("MountainPine", 0.05f, 0.8f, 40f),
						Blocks("Slate scree", 0.06f, 10f, F(RockTypes.Slate, "Scree")), Blocks("Quartzite scree", 0.04f, 12f, F(RockTypes.Quartzite, "Scree"))), Ds(D(Ground.Rock, 48f, 3f, 30f, 90f, Boulders(0.4f, 5f, Grey)), D(Ground.Pebbles, 32f)),
					C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel),
				E("Sulphuric Cloud Deck", L(Ground.Sulphur, Stones(SmallRocks("Basalt"), 1f)), Ds(D(Ground.Basalt, 64f), D(Ground.Ash, 96f)), C(Ground.Basalt), Ground.Sulphur, Ground.Sulphur),
				E("Volcanic", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 3f), Boulders(0.2f, 8f, BasaltRocks), Trees(0.02f, 20f, 25f, "Dead"),
						Outcrops("Basalt columns", 0.05f, 16f, F(RockTypes.Basalt, "Columns")), Blocks("Basalt causeway", 0.02f, 24f, F(RockTypes.Basalt, "Causeway", "Fallen"), 15f)),
					Ds(D(Ground.Ash, 64f, 2f, -1f, -1f, Blocks("Pumice", 0.06f, 10f, F(RockTypes.Pumice, "Lump", "Raft")), Outcrops("Tuff chimneys", 0.015f, 30f, F(RockTypes.Tuff, "Chimney"), 15f)),
						D(Ground.Lava, 128f, 5f)), C(Ground.Basalt), Ground.Basalt, Ground.Basalt),
				E("Ice Cave", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.04f, 10f, "Calved", "Rounded")), Ds(D(Ground.Snow, 48f), D(Ground.Frost, 64f)), C(Ground.Ice, 35f), Ground.Ice, Ground.Ice),
				E("Ice Ruin", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f)), Ds(D(Ground.Snow, 48f), D(Ground.Flagstone, 32f, 4f)), C(Ground.Ice), Ground.Ice, Ground.Ice),

				// ── Nival (tier 8) ──
				E("Glacier", L(Ground.Ice, Stones(SmallRocks("Grey"), 0.5f), Seracs(0.02f, 25f), IceBlocks(0.05f, 12f, "Calved", "Slab")), Ds(D(Ground.Snow, 48f), D(Ground.Rock, 48f, 3f, 30f, 90f)), C(Ground.Ice, 38f), Ground.Ice, Ground.Ice),
				E("Permanent Ice", L(Ground.Ice, IceBlocks(0.02f, 16f, "Rounded", "Calved")), Ds(D(Ground.Snow, 64f), D(Ground.Frost, 96f)), C(Ground.Ice), Ground.Ice, Ground.Ice),
				E("Snow", L(Ground.Snow, Boulders(0.03f, 12f, GreyRound)), Ds(D(Ground.Ice, 96f), D(Ground.Rock, 48f, 3f, 30f, 90f)), C(Ground.Rock), Ground.Ice, Ground.Ice),
				E("Ice Palace", L(Ground.Ice), Ds(D(Ground.Snow, 48f), D(Ground.Flagstone, 32f, 4f)), C(Ground.Ice), Ground.Ice, Ground.Ice),
			};
			// Built here, not read from a static field: Entries is initialised from this before any field declared below it.
			Dictionary<string, RockType[]> accepted = AcceptedRocks();
			foreach (Entry entry in entries)
			{
				if (accepted.TryGetValue(entry.Biome, out RockType[] rocks))
				{
					entry.Accepts(rocks);
				}
			}
			return entries;
		}

		/// <summary>
		/// The planet-geology rocks each biome accepts for its cliffs, river boulders and fall ledges (<see cref="Entry.Rocks"/>),
		/// besides its own. A biome not named here takes only its own: the sea floors, ice, the airless and alien ground,
		/// whose own rock already says what they are.
		/// </summary>
		/// <remarks>
		/// Chosen by climate, as the ground looks rather than as rock is laid down: a humid green biome takes the grey
		/// crystalline and fine-bedded rocks (granite, gneiss, schist, slate, shale, limestone), a dry one the sandstones
		/// and conglomerate beside them, a volcanic one its lavas and tuff. Sandstone and conglomerate stand only where
		/// the ground is dry or the biome names them, so a grassland, forest, taiga or bog is never walled in them.
		/// </remarks>
		private static Dictionary<string, RockType[]> AcceptedRocks()
		{
			RockType granite = RockTypes.Granite, gneiss = RockTypes.Gneiss, schist = RockTypes.Schist, slate = RockTypes.Slate,
				shale = RockTypes.Shale, limestone = RockTypes.Limestone, chalk = RockTypes.Chalk, marble = RockTypes.Marble,
				quartzite = RockTypes.Quartzite, sandstone = RockTypes.Sandstone, conglomerate = RockTypes.Conglomerate,
				basalt = RockTypes.Basalt, andesite = RockTypes.Andesite, tuff = RockTypes.Tuff;
			RockType[] Of(params RockType[] rocks) => rocks;
			// Humid temperate ground: crystalline rock and the fine-bedded sediments.
			RockType[] temperate = Of(granite, gneiss, schist, slate, shale, limestone);
			// Lowland farm and grass country: the soft sediments it is usually on, and granite.
			RockType[] lowland = Of(granite, shale, limestone, chalk);
			// Cold ground: crystalline shield rock.
			RockType[] boreal = Of(granite, gneiss, schist, slate);
			// Mountains: crystalline and metamorphic rock.
			RockType[] mountain = Of(granite, gneiss, schist, slate, quartzite, marble);
			// Dry ground: the sandstones and what lies beside them.
			RockType[] arid = Of(sandstone, conglomerate, shale, limestone, granite, basalt);
			// The humid tropics.
			RockType[] tropical = Of(granite, gneiss, schist, limestone, basalt);
			// Volcanic ground.
			RockType[] volcanic = Of(basalt, andesite, tuff);
			// Banks and shores of rivers and lakes: what the country round them is, less the sandstones.
			RockType[] waterside = Of(granite, gneiss, schist, slate, shale, limestone, basalt);
			return new Dictionary<string, RockType[]>(StringComparer.Ordinal)
			{
				// Shore
				["Beach"] = Of(granite, gneiss, basalt, slate, limestone, chalk, sandstone),
				["Rocky Coast"] = Of(granite, gneiss, basalt, slate, schist, quartzite),
				["Coastal Water"] = waterside,
				["Estuary"] = waterside,
				["Mangrove"] = tropical,
				["Peat Bog"] = boreal,
				// Lowland
				["Grassland"] = Of(granite, gneiss, schist, limestone, shale),
				["Plains"] = lowland,
				["Farmland"] = lowland,
				["Wetlands"] = waterside,
				["Lake"] = waterside,
				["River"] = waterside,
				["Swamp"] = waterside,
				["Swamp Cave"] = waterside,
				["Swamp Ruins"] = waterside,
				["Swamp Temple"] = waterside,
				["Oasis"] = Of(sandstone, limestone, conglomerate),
				["Scrubland"] = arid,
				["Steppe"] = Of(granite, shale, limestone, sandstone, conglomerate),
				["Savanna"] = Of(sandstone, granite, gneiss, conglomerate, shale),
				["Jungle Ruins"] = tropical,
				["Jungle Temple"] = tropical,
				// Highland
				["Bamboo Forest"] = tropical,
				["Forest"] = temperate,
				["Forest Ruins"] = temperate,
				["Woodland"] = temperate,
				["Hills"] = Of(granite, gneiss, schist, slate, limestone),
				["Valley"] = Of(granite, gneiss, schist, shale, limestone),
				["Karst"] = Of(limestone, chalk, marble),
				["Taiga"] = boreal,
				["Jungle"] = tropical,
				["Geyser Basin"] = volcanic,
				["Castle"] = Of(granite, limestone, slate, gneiss),
				["Marble Palace"] = Of(marble, limestone),
				["Cave"] = Of(limestone, chalk, marble, granite),
				// Mountain
				["Badlands"] = arid,
				["Desert"] = arid,
				["Mountain Slope"] = Of(slate, granite, gneiss, schist, quartzite, andesite),
				["Rocky Terrain"] = Of(quartzite, granite, gneiss, slate, schist),
				["Tundra"] = Of(granite, gneiss, schist, basalt),
				["Crater"] = volcanic,
				["Fortress"] = Of(granite, slate, gneiss, limestone),
				["Volcanic Cave"] = volcanic,
				["Volcanic Temple"] = volcanic,
				// Alpine
				["Alpine"] = mountain,
				["Alpine Meadow"] = mountain,
				["High Desert"] = arid,
				["Scree"] = Of(quartzite, slate, granite, gneiss, schist),
				["Volcanic"] = volcanic,
			};
		}

		/// <summary>The entry for a biome asset name, or null when the table has none.</summary>
		public static Entry For(string biomeName)
		{
			foreach (Entry e in Entries)
			{
				if (e.Biome == biomeName)
				{
					return e;
				}
			}
			return null;
		}
	}
}
#endif
