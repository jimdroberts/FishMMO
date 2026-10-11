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
			/// Climate band: the mean annual temperature the rule grows between, x the coldest and y the
			/// warmest, on the climate's scale (0 = 0 °C, 1 = 33.1 °C); an end at or past ±1 is open. Null
			/// for no temperature band (<see cref="PrefabSpawnRule.temperatureRange"/>). Set through
			/// <see cref="Band"/>.
			/// </summary>
			public Vector2? Temperature;
			/// <summary>
			/// Climate band: the humidity the rule grows between, −1 driest … 1 wettest; an end at or past
			/// ±1 is open. Null for no humidity band (<see cref="PrefabSpawnRule.humidityRange"/>).
			/// </summary>
			public Vector2? Humidity;
			/// <summary><see cref="PrefabSpawnRule.temperatureFalloff"/>: the soft edge's width at each closed end (0.1 ≈ 3.3 °C).</summary>
			public float TemperatureFalloff = 0.1f;
			/// <summary><see cref="PrefabSpawnRule.humidityFalloff"/>.</summary>
			public float HumidityFalloff = 0.1f;
			/// <summary>
			/// Tree-channel rules: the heading range instances are turned to, degrees about the vertical
			/// (<see cref="PrefabSpawnRule.yRotationRange"/>). Any heading unless set through <see cref="Aligned"/>.
			/// </summary>
			public Vector2 Yaw = new Vector2(0f, 360f);
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
			// A detail added with the vegetation expansion carries its own sink (the legacy ones never set it, so keep theirs below).
			if (NewDetail(detail, out DetailSpec spec) && spec.Sink != Vector2.zero) return spec.Sink;
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
			s.Sink = NewDetail(detail, out DetailSpec spec) && spec.Sink != Vector2.zero ? spec.Sink : DetailSink;
			s.Earlier.Add(legacy);
			return s;
		}

		/// <summary>
		/// A detail of the vegetation expansion (2026-10-10), which carries its own group and sink
		/// (<see cref="DetailSpec.GroupMetres"/>, <see cref="DetailSpec.GroupSize"/>, <see cref="DetailSpec.Sink"/>). The
		/// legacy details never set them, so their name-keyed values below stay exactly what they were.
		/// </summary>
		private static bool NewDetail(string detail, out DetailSpec spec)
		{
			return ProceduralArtCatalogue.TryDetail(detail, out spec) && spec.GroupMetres > 0f;
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
			if (NewDetail(detail, out DetailSpec spec))
			{
				metres = spec.GroupMetres;
				size = spec.GroupSize > 1f ? spec.GroupSize : 8f;
			}
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
			bool lying = detail == "Starfish" || detail == "Shells" || detail == "Urchin"
				// The expansion's bed-dwellers that lie on or crust over the bottom rather than grow up from it.
				|| detail.StartsWith("MusselBed", StringComparison.Ordinal) || detail == "Barnacles" || detail == "SeaLettuce" || detail == "SandDollar"
				|| detail == "SeaCucumber" || detail == "BrittleStar" || detail == "GiantClam" || detail == "BacterialMat";
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
		private readonly struct WoodShape
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

			public WoodShape(float cover, float closure, float open, float scale, float metres)
			{
				Cover = cover;
				Closure = closure;
				Open = open;
				Scale = scale;
				Metres = metres;
			}
		}

		private static WoodShape W(float cover, float closure, float open = 0.02f, float scale = 1f, float metres = StandMetres) => new WoodShape(cover, closure, open, scale, metres);

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
		private static Scatter Stand(Before was, float slopeMax, WoodShape wood, params string[] species)
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

		/// <summary>The prefabs of the named deadwood props (<see cref="ProceduralArtCatalogue.Deadwood"/>): <c>FallenLog_Oak</c>, <c>Stump_Broken</c>, <c>Driftwood</c>, <c>PetrifiedLog</c>….</summary>
		private static string[] Wood(params string[] names) => Array.ConvertAll(names, ProceduralArtCatalogue.DeadwoodPrefab);

		/// <summary>
		/// Deadwood — fallen logs, stumps, driftwood, petrified logs, root plates (<see cref="Wood"/>): tree channel (a baked
		/// mesh collider), lying with the slope and bedded like <see cref="Blocks"/>, a few together where a tree came down
		/// or a tide left them, the odd one between. Named "Deadwood: label", never "Trees: …" (they are not standing trees).
		/// </summary>
		private static Scatter Logs(string label, float density, float spacing, string[] prefabs, float slopeMax = 30f)
		{
			var s = new Scatter
			{
				Name = "Deadwood: " + label,
				Channel = PrefabSpawnChannel.TreeInstance,
				Prefabs = prefabs,
				Density = density,
				Spacing = spacing,
				MinWeight = 0.45f,
				Slope = new Vector2(0f, slopeMax),
				Scale = new Vector2(0.75f, 1.25f),
				AlignToNormal = true,
				MaxPerChunk = 2000,
				// Bedded by construction, as the formations are: only the slope's extra.
				Sink = new Vector2(0f, 0.05f),
				SinkSlope = 0.5f,
			};
			return Grouped(s, 12f, 0.3f, 3f, 0.5f, g => g.Spacing = Mathf.Min(spacing, 5f));
		}

		/// <summary>
		/// Holds a tree-channel rule's instances to one heading (<see cref="Scatter.Yaw"/>, degrees about the vertical, in
		/// scene space) instead of any: for formations with a facing — sastrugi prows into the prevailing wind (−x at yaw 0).
		/// Set on the rule's earlier versions too, as <see cref="Band"/> is.
		/// </summary>
		private static Scatter Aligned(Scatter rule, float yawMin, float yawMax)
		{
			AndEarlier(rule, r => r.Yaw = new Vector2(yawMin, yawMax));
			return rule;
		}

		/// <summary>
		/// Sinks a rule's instances deeper than its helper does (<see cref="Scatter.Sink"/>, metres): boulders half
		/// drowned in a dust sea's fines. Set on the rule's earlier versions too, as <see cref="Band"/> is.
		/// </summary>
		private static Scatter Buried(Scatter rule, float min, float max)
		{
			AndEarlier(rule, r => r.Sink = new Vector2(min, max));
			return rule;
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

		/// <summary>
		/// Keeps a rule to a band of the climate (<see cref="PrefabSpawnRule.useClimateBand"/>): the mean
		/// annual temperature between <paramref name="tMin"/> and <paramref name="tMax"/> and the humidity
		/// between <paramref name="hMin"/> and <paramref name="hMax"/>, read where each instance stands, so
		/// a biome with a wide envelope carries a warm set and a cold set and the scene's climate picks.
		/// Returns the rule, for writing <c>Band(Trees(…), 0.55f, 1f)</c> in a layer's list.
		/// </summary>
		/// <param name="tMin">Coldest, on the climate's scale (0 = 0 °C, 1 = 33.1 °C, −1 = −33.1 °C); −1 or below for no cold limit.</param>
		/// <param name="tMax">Warmest; 1 or above for no warm limit.</param>
		/// <param name="hMin">Driest, −1 … 1; −1 or below for no dry limit.</param>
		/// <param name="hMax">Wettest; 1 or above for no wet limit.</param>
		/// <remarks>
		/// <para>
		/// Set on every <see cref="Scatter.Earlier"/> version too, as <see cref="Sea"/> sets its depth: the
		/// band is a property of where the species grows, not a change of what the helper writes, so the
		/// rule's older forms are the same species in the same place and must still be recognised as the
		/// tool's. The authoring tool hashes the band only when there is one, so banding a rule an earlier
		/// run wrote, untouched, brings it up to date rather than taking it for somebody's tuning.
		/// </para>
		/// <para>
		/// An axis whose both ends are open is no band on that axis (null), and a rule with neither axis
		/// banded is written exactly as it would be without this call. Each closed end fades over the
		/// rule's <see cref="Scatter.TemperatureFalloff"/> or <see cref="Scatter.HumidityFalloff"/>,
		/// centred on the end (<c>TerrainScatter.ClimateBand</c>).
		/// </para>
		/// </remarks>
		private static Scatter Band(Scatter rule, float tMin, float tMax, float hMin = -1f, float hMax = 1f)
		{
			Vector2? temperature = tMin <= -1f && tMax >= 1f ? (Vector2?)null : new Vector2(Mathf.Max(-1f, tMin), Mathf.Min(1f, tMax));
			Vector2? humidity = hMin <= -1f && hMax >= 1f ? (Vector2?)null : new Vector2(Mathf.Max(-1f, hMin), Mathf.Min(1f, hMax));
			AndEarlier(rule, r =>
			{
				r.Temperature = temperature;
				r.Humidity = humidity;
			});
			return rule;
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
					// Clarion-Clipperton: manganese nodules carpeting the ooze in patches, deposit feeders crawling over it,
					// stalked glass sponges, xenophyophores and sea pens standing very sparse.
					.Under(Sea("Sponge", 0.2f, 0f, 0f, 128), Sea("Starfish", 0.3f, 0f, 0f, 96), Sea("Anemone", 0.15f, 0f, 0f, 128),
						SeaStones(Pebbles("Nodule"), 4f, 0f, 0f), SeaStones(SmallRocks("Nodule"), 1f, 0f, 0f),
						Sea("SeaCucumber", 0.3f, 0f, 0f, 128), Sea("BrittleStar", 0.4f, 0f, 0f, 128), Sea("Xenophyophore", 0.3f, 0f, 0f, 128),
						Sea("GlassSpongeStalked", 0.1f, 0f, 0f, 128), Sea("SeaPen", 0.15f, 0f, 0f, 128, 15f)),
				E("Abyss", L(Ground.Silt), Ds(D(Ground.Basalt, 64f, 3f, 20f, 90f)), C(Ground.Basalt, 35f), Ground.Silt, Ground.Silt)
					// A hadal trench: crinoids and glass sponges on the walls, cold-seep microbial mats with tube-worm tufts
					// on the floor, holothurians and xenophyophores on the sediment.
					.Under(Sea("Sponge", 0.15f, 0f, 0f, 128, 60f), Sea("Anemone", 0.15f, 0f, 0f, 128, 60f),
						Sea("BacterialMat", 0.3f, 0f, 0f, 160, 15f), Sea("TubeWorms", 0.3f, 0f, 0f, 160, 20f), Sea("SeaCucumber", 0.2f, 0f, 0f, 128),
						Sea("Xenophyophore", 0.2f, 0f, 0f, 128), Sea("Crinoid", 0.3f, 0f, 0f, 128, 90f)),
				E("Deep Ocean", L(Ground.Silt), Ds(D(Ground.Mud, 128f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
					// The continental slope: Lophelia cold-water coral thickets on the rock, sea pens in the mud, crinoids and
					// glass sponges, brittle-star beds and the odd nodule.
					.Under(Sea("Sponge", 0.3f, 0f, 0f, 128), Sea("Starfish", 0.2f, 0f, 0f, 96), Sea("Anemone", 0.2f, 0f, 0f, 128),
						Sea("ColdCoral", 0.3f, 0f, 0f, 160, 60f), Sea("GlassSponge", 0.3f, 0f, 0f, 128), Sea("SeaPen", 1f, 0f, 0f, 160, 15f),
						Sea("Crinoid", 0.3f, 0f, 0f, 128, 60f), Sea("BrittleStar", 0.5f, 0f, 0f, 128), SeaStones(Pebbles("Nodule"), 0.5f, 0f, 0f)),
				E("Ocean", L(Ground.Sand), Ds(D(Ground.Silt, 64f), D(Ground.Pebbles, 32f, 3f)), C(Ground.Rock), Ground.Sand, Ground.Sand)
					// The shelf: giant kelp only in cold water (no kelp forest on a warm shelf), sand-dollar beds in the
					// shallows, sea pens on the silt, mussel patches, holothurians and brittle stars on the sand.
					.Under(Band(Sea("Kelp", 3f, 4f, 30f, 160), -1f, 0.6f), Sea("Seaweed", 2f, 1.5f, 20f, 160), Sea("Sponge", 1f, 6f, 0f, 128), Sea("SeaFan", 0.5f, 8f, 120f, 128),
						Sea("Urchin", 1f, 2f, 40f, 128), Sea("Starfish", 0.5f, 1f, 0f, 96), Sea("Shells", 1f, 1f, 30f, 96),
						Sea("SandDollar", 2f, 2f, 20f, 160, 10f), Sea("SeaPen", 1f, 20f, 0f, 160, 15f), Sea("MusselBed", 1.5f, 1f, 15f, 180),
						Sea("SeaCucumber", 0.3f, 5f, 0f, 128), Sea("BrittleStar", 0.5f, 10f, 0f, 128))
					.Moved("main", "Kelp"),
				E("Seamount", L(Ground.Basalt), Ds(D(Ground.Silt, 48f, 2f, 0f, 20f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
					// A seamount's flanks: kelp only on a shallow summit in cold water, soft corals there in warm water,
					// cold-water and black coral gardens, crinoids and glass sponges down the current-swept basalt.
					.Under(Band(Sea("Kelp", 4f, 4f, 30f, 160), -1f, 0.6f), Sea("SeaFan", 3f, 4f, 0f, 160, 60f), Sea("Sponge", 2f, 4f, 0f, 160, 60f), Sea("Anemone", 1f, 2f, 0f, 128, 60f),
						Sea("ColdCoral", 1.5f, 150f, 1000f, 180, 70f), Sea("Crinoid", 1f, 40f, 0f, 160, 70f), Sea("GlassSponge", 1f, 100f, 0f, 160, 70f),
						Band(Sea("SoftCoral", 2f, 5f, 40f, 180, 60f), 0.55f, 1f), Sea("BrittleStar", 0.5f, 20f, 0f, 128))
					.Moved("detail/0", "Kelp"),
				E("Underwater Canyon", L(Ground.Silt), Ds(D(Ground.Rock, 48f, 3f, 25f, 90f), D(Ground.Gravel, 64f)), C(Ground.CliffRock, 38f), Ground.Silt, Ground.Silt)
					// Monterey Canyon: cold-water coral and crinoids on the sheer walls, sea pens in the soft floor, dense
					// brittle-star beds, a rare seep mat.
					.Under(Sea("SeaFan", 1.5f, 4f, 0f, 160, 70f), Sea("Sponge", 1.5f, 4f, 0f, 160, 70f), Sea("Anemone", 0.5f, 2f, 0f, 128, 70f),
						Sea("ColdCoral", 1f, 50f, 0f, 160, 90f), Sea("Crinoid", 0.5f, 30f, 0f, 160, 90f), Sea("SeaPen", 1f, 20f, 0f, 160, 15f),
						Sea("BrittleStar", 2f, 20f, 0f, 160, 20f), Sea("BacterialMat", 0.05f, 200f, 0f, 160, 15f)),
				E("Coastal Water", L(Ground.Sand), Ds(D(Ground.Pebbles, 32f, 3f, -1f, -1f, Stones(SmallRocks("Grey"), 3f)), D(Ground.Silt, 64f)),
					C(Ground.Rock), Ground.Sand, Ground.Pebbles)
					.Under(Sea("Seaweed", 6f, 1f, 20f, 200), Sea("Kelp", 6f, 5f, 30f, 200),
						Sea("Urchin", 3f, 1.5f, 30f, 160), Sea("Starfish", 1f, 0.5f, 40f, 128), Sea("Shells", 4f, 0.5f, 25f, 128),
						Sea("Anemone", 1f, 2f, 40f, 128), SeaStones(SmallRocks("Grey"), 2f, 0.5f, 0f),
						// Mussel and barnacle bands from the low tide down, sea lettuce in the shallows, sand dollars on the sand.
						Sea("MusselBed", 4f, 0f, 10f, 200, 60f), Sea("Barnacles", 3f, 0f, 3f, 180, 70f), Sea("SeaLettuce", 3f, 0.5f, 8f, 180),
						Sea("SandDollar", 1f, 3f, 20f, 160, 10f))
					.Moved("detail/0", "Kelp").Moved("lakebed", "Seagrass"),
				E("Coral Reef", L(Ground.Coral), Ds(D(Ground.SandBeach, 32f), D(Ground.Sand, 96f)),
					C(Ground.Limestone), Ground.Sand, Ground.Sand)
					.Under(Sea("Coral", 20f, 1f, 30f, 200), Sea("BrainCoral", 3f, 1f, 30f, 200), Sea("TableCoral", 2f, 1f, 15f, 200),
						Sea("SeaFan", 2f, 3f, 60f, 160), Sea("Sponge", 2f, 2f, 0f, 160), Sea("Anemone", 3f, 1f, 40f, 160),
						Sea("Urchin", 2f, 1f, 30f, 128), Sea("Starfish", 1f, 0.5f, 40f, 128), Sea("Shells", 3f, 0.5f, 25f, 128),
						// Soft corals among the reef builders, giant clams, white coral rubble, holothurians on the sand halos.
						Sea("SoftCoral", 4f, 1f, 30f, 200), Sea("GiantClam", 0.3f, 1f, 20f, 160), SeaStones(Pebbles("Coral"), 3f, 0.5f, 20f),
						Sea("SeaCucumber", 0.5f, 2f, 0f, 128))
					// No giant kelp on a tropical reef.
					.Moved("main", "Coral", "Kelp").Moved("lakebed", "Seagrass", "Kelp"),
				E("Subsurface Ocean Vent", L(Ground.Basalt), Ds(D(Ground.Silt, 48f), D(Ground.Sulphur, 24f, 4f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
					// Black smokers in vent fields and white Lost City carbonate towers; round them tube worms, vent clams and
					// bacterial mats on sulphide rubble. No corals: nothing at a vent builds a reef.
					.Under(Sea("TubeWorms", 6f, 0f, 0f, 200, 50f), Sea("Anemone", 1f, 0f, 0f, 128),
						Sea("MusselBedVentClam", 3f, 0f, 0f, 180, 40f), Sea("BacterialMat", 2f, 0f, 0f, 160, 30f), SeaStones(SmallRocks("Basalt"), 2f, 0f, 0f),
						Outcrops("Sulphide smokers", 0.01f, 16f, F(RockTypes.Sulphide, "Smoker"), 30f), Outcrops("Carbonate towers", 0.004f, 30f, F(RockTypes.Chalk, "Pinnacle"), 30f))
					.Moved("main", "Coral").Moved("lakebed", "Coral"),
				// Titan's shores (Huygens): rounded water-ice cobbles, bright evaporite rings on the flats the lake left, tholin dunes.
				E("Methane Lake", L(Ground.Frost, Stones(SmallRocks("Ice"), 4f), Stones(Pebbles("Ice"), 6f), IceBlocks(0.01f, 18f, "Rounded")),
					Ds(D(Ground.Ice, 64f), D(Ground.SaltCrust, 48f, 2f, 0f, 5f), D(Ground.TholinDune, 96f, 2f, -1f, -1f, Stones(Pebbles("Ice"), 2f))), C(Ground.Ice), Ground.Tholin, Ground.Tholin)
					.Under(SeaStones(Pebbles("Ice"), 4f, 0f, 8f)),
				// A dead world's tube is cold: a ropy pahoehoe floor (no glowing lava), roof fall, lava dribble spires and benches
				// along the walls, gypsum crusts, ice in the cold traps.
				E("Lava Tube", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 3f), Blocks("Basalt roof fall", 0.05f, 12f, F(RockTypes.Basalt, "Block", "Fallen")),
						Outcrops("Lava dribbles", 0.03f, 8f, F(RockTypes.Basalt, "Dribble")), Outcrops("Lava benches", 0.015f, 18f, F(RockTypes.Basalt, "Ledges"), 40f)),
					Ds(D(Ground.Ash, 48f, 2f, -1f, -1f, Outcrops("Gypsum", 0.005f, 20f, F(RockTypes.Gypsum, "Crystal"))),
						D(Ground.Pahoehoe, 96f, 5f, -1f, -1f, Outcrops("Cold-trap ice", 0.004f, 20f, F(RockTypes.WaterIce, "Crystal")))),
					C(Ground.Basalt, 35f), Ground.Basalt, Ground.Basalt),

				// ── Shore (tier 3) ──
				// A sandy coast from 3 to 33 °C: coconut palms, palm litter, pandanus and morning-glory runners only where it is
				// tropical; marram / sea-oats tussocks on the dunes, wrack and driftwood along the strandline everywhere.
				E("Beach", L(Ground.SandBeach, Band(Trees(0.03f, 14f, 20f, "Palm"), 0.55f, 1f),
						Plants("DebrisWrack", 3f, 128, 15f), Logs("Driftwood", 0.02f, 10f, Wood("Driftwood"), 15f), Band(Plants("DebrisPalm", 1f, 128, 20f), 0.55f, 1f)),
					Ds(D(Ground.Sand, 64f, 2f, -1f, -1f, Grass("GrassDune", 10f, 160), Band(Plants("BeachVine", 2f, 160, 25f), 0.5f, 1f), Band(Trees(0.01f, 10f, 25f, "Pandanus"), 0.6f, 1f)),
						D(Ground.Pebbles, 24f, 3f, -1f, -1f, Boulders(0.3f, 6f, GreyRound))),
					C(Ground.Rock), Ground.Sand, Ground.Pebbles)
					// The shallows off the beach.
					.Under(Sea("Shells", 2f, 0.5f, 15f, 96), Sea("Starfish", 0.5f, 0.5f, 20f, 96))
					// Dune grass replaces the generic tall grass on the sand.
					.Moved("lakebed", "Seagrass").Moved("detail/0", "GrassTall"),
				// Lunar dust ponds, Martian dust-mantled basins: fine bright dust (Regolith is now the patches it thins to),
				// boulders sunk half into it, wind-faceted ventifacts where there is air, the odd buried crater rim.
				E("Dust Sea", L(Ground.Dust, Buried(Boulders(0.05f, 20f, GreyRound), 0.3f, 0.6f), Stones(Pebbles("Grey"), 1f)),
					Ds(D(Ground.Sand, 128f, 2f, -1f, -1f, Outcrops("Ventifacts", 0.01f, 16f, F(RockTypes.Basalt, "Ventifact"))),
						D(Ground.Regolith, 96f, 2f, -1f, -1f, With(Blocks("Crater rims", 0.002f, 30f, F(RockTypes.Breccia, "CraterRim"), 15f), r => r.Spacing = 30f))),
					C(Ground.Rock), Ground.Regolith, Ground.Regolith),
				// Salt-marsh zonation: bare mudflat, cordgrass on the silt, samphire and sea lavender, then rushes and
				// Phragmites, willow and alder at the upland edge; oyster banks and driftwood below the tide line.
				E("Estuary", L(Ground.Mud, Plants("Reeds", 30f, 220)),
					Ds(D(Ground.Silt, 48f, 2f, -1f, -1f, Grass("GrassDune", 20f, 200, 3f), Plants("ShrubSamphire", 4f, 160, 5f), Plants("FlowersWet", 3f, 180, 8f)),
						D(Ground.Grass, 48f, 2f, 0f, 10f, Grass("GrassTall", 20f), Bush("Willow", 0.2f),
							Grass("GrassRush", 15f), Plants("ReedsPlume", 10f, 200), Logs("Driftwood", 0.01f, 12f, Wood("Driftwood"), 10f),
							Stand(Was(0.05f, 12f), 15f, W(0.08f, 0.6f, open: 0.03f), "Alder"))),
					C(Ground.Soil), Ground.Silt, Ground.Silt)
					.Under(Sea("MusselBedOyster", 2f, 0f, 3f, 160, 10f)),
				// Mare basin: fresh small craters, breccia and impact glass in the ejecta, bright anorthosite blocks on the rims.
				E("Impact Basin", L(Ground.Regolith, Stones(SmallRocks("Grey"), 4f), Stones(Pebbles("Grey"), 4f), With(Blocks("Crater rims", 0.004f, 30f, F(RockTypes.Breccia, "CraterRim"), 20f), r => r.Spacing = 30f)),
					Ds(D(Ground.Rock, 48f, 3f, 15f, 90f, Boulders(0.3f, 8f, GreyRound), Blocks("Mare basalt", 0.04f, 14f, F(RockTypes.Basalt, "Block")), Blocks("Anorthosite", 0.02f, 14f, F(RockTypes.Anorthosite, "Boulder"))),
						D(Ground.Gravel, 64f, 2f, -1f, -1f, Blocks("Breccia", 0.03f, 12f, F(RockTypes.Breccia, "Block", "Boulder")), Blocks("Impact glass", 0.01f, 16f, F(RockTypes.Obsidian, "Chunk")))),
					C(Ground.CliffRock), Ground.Regolith, Ground.Regolith),
				// Rhizophora/Avicennia forest in tidal mud: a closed low canopy of real mangroves on prop roots, a carpet of
				// pneumatophores, nipa palms along the channels, sea hibiscus and mangrove fern on the landward side, oysters
				// on the roots below the tide. (The jungle tree at a third of its size it stood in for is retired.)
				E("Mangrove", L(Ground.Mud, Plants("Reeds", 10f), Stand(Was(1.5f, 6f), 15f, W(0.85f, 1f), "Mangrove"), Plants("Pneumatophores", 30f, 200, 15f)),
					Ds(D(Ground.Silt, 32f, 2f, -1f, -1f, Trees(0.05f, 6f, 10f, "Nipa")), D(Ground.JungleFloor, 48f, 2f, -1f, -1f, Plants("Fern", 6f), Bush("Hibiscus", 0.2f))),
					C(Ground.Soil), Ground.Silt, Ground.Mud)
					.Under(Sea("MusselBedOyster", 1.5f, 0f, 2f, 160, 20f))
					.Moved("main", "Trees: Jungle"),
				// Io's paterae, Erta Ale: nothing on the lake itself; spatter cones and rafted crust plates on the cooled margins,
				// ropy pahoehoe beside them, scoria bombs on the cinder.
				E("Molten Surface", L(Ground.Lava),
					Ds(D(Ground.Basalt, 32f, 3f, -1f, -1f, Stones(SmallRocks("Basalt"), 2f), Blocks("Obsidian", 0.03f, 14f, F(RockTypes.Obsidian, "Chunk", "Shard")),
							Outcrops("Spatter cones", 0.01f, 20f, F(RockTypes.Scoria, "Cone"), 20f), Blocks("Crust plates", 0.03f, 12f, F(RockTypes.Basalt, "Slabs"), 15f)),
						D(Ground.Ash, 64f), D(Ground.Pahoehoe, 48f),
						D(Ground.Cinder, 64f, 2f, -1f, -1f, Blocks("Scoria bombs", 0.03f, 10f, F(RockTypes.Scoria, "Bomb", "Lump")))),
					C(Ground.Basalt), Ground.Basalt, Ground.Basalt),
				// Raised/blanket bog: a living red-green sphagnum carpet (Peat now only the hollows and cuttings) with
				// cottongrass, heather and sphagnum hummocks, dwarf birch and bilberry on the moss, stunted pine and birch
				// among the snags (one wood: the pine joins the birch and snags rather than standing as a second).
				E("Peat Bog", L(Ground.Sphagnum, Plants("Reeds", 15f), Bush("Willow", 0.3f, 0.7f), Stand(Was(0.05f, 12f), 15f, W(0.1f, 0.12f, open: 0.03f, scale: 0.55f), "Dead", "Birch", "Pine"),
						Grass("GrassCotton", 10f, 200), Bush("Heather", 1.5f), Plants("CushionSphagnum", 3f, 160, 15f)),
					Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Grass("GrassTuft", 20f), Bush("DwarfBirch", 0.3f), Bush("Bilberry", 0.8f)), D(Ground.Mud, 64f),
						D(Ground.Peat, 32f, 3f)),
					C(Ground.Soil), Ground.Mud, Ground.Peat)
					.Moved("main", "Trees: Dead, Birch"),
				// Hadley Rille: layered basalt ledges at the rim, talus down the walls, a dusty boulder-strewn floor, small craters.
				E("Rille", L(Ground.Regolith, Stones(SmallRocks("Basalt"), 3f),
						With(Blocks("Crater rims", 0.002f, 24f, F(RockTypes.Breccia, "CraterRim"), 15f), r => { r.Spacing = 24f; r.Scale = new Vector2(0.35f, 0.8f); })),
					Ds(D(Ground.Basalt, 48f, 3f, 20f, 90f, Boulders(0.2f, 8f, BasaltRocks), Blocks("Basalt blocks", 0.05f, 12f, F(RockTypes.Basalt, "Block", "Fallen")),
							Outcrops("Basalt ledges", 0.03f, 16f, F(RockTypes.Basalt, "Ledges"), 45f), Blocks("Basalt talus", 0.03f, 14f, F(RockTypes.Basalt, "Scree"))),
						D(Ground.Gravel, 64f, 2f, -1f, -1f, Blocks("Breccia", 0.015f, 14f, F(RockTypes.Breccia, "Block", "Boulder"))),
						D(Ground.Dust, 96f, 2f, 0f, 10f, Stones(Pebbles("Basalt"), 2f))),
					C(Ground.Basalt), Ground.Regolith, Ground.Regolith),
				// North Atlantic rocky shore (−33…5 °C): sea stacks, wrack and driftwood on the pebbles, orange and black
				// lichen above the splash zone, sea-thrift cushions and heather on the clifftop, gorse only where it is mild
				// and dwarf birch where it is arctic; mussel and barnacle bands in the intertidal.
				E("Rocky Coast", L(Ground.Rock, Boulders(0.6f, 5f, Grey), Stones(SmallRocks("Grey"), 5f), Outcrops("Sea stacks", 0.002f, 60f, F(RockTypes.Granite, "Tor"), 20f)),
					Ds(D(Ground.Pebbles, 32f, 3f, 0f, 25f, Stones(Pebbles("Grey"), 10f), Blocks("Granite", 0.04f, 12f, F(RockTypes.Granite, "Corestone", "Split")),
							Plants("DebrisWrack", 2f, 128, 20f), Logs("Driftwood", 0.01f, 12f, Wood("Driftwood"), 20f)),
						D(Ground.GrassDry, 64f, 2f, 0f, 20f, Grass("GrassTuft", 12f), Band(Bush("Gorse", 0.5f), 0f, 1f),
							Plants("CushionThrift", 3f, 160, 45f), Bush("Heather", 1f), Band(Bush("DwarfBirch", 0.4f), -1f, -0.3f)),
						D(Ground.Lichen, 48f, 3f, 0f, 60f)),
					C(Ground.CliffRock, 38f), Ground.Pebbles, Ground.Gravel)
					.Under(Sea("MusselBed", 4f, 0f, 3f, 180, 60f), Sea("Barnacles", 4f, 0f, 3f, 180, 70f)),
				// Europa's lineae: double ridges, rust-brown salts in the fractures, chaos rafts of broken plate, fresh frost
				// crystals, the penitentes predicted on the sunlit ice.
				E("Tidal Fracture", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.03f, 14f, "Calved", "Slab"),
						With(Blocks("Double ridges", 0.01f, 14f, F(RockTypes.WaterIce, "Ridge"), 20f), r => r.Spacing = 10f)),
					Ds(D(Ground.Frost, 64f, 2f, -1f, -1f, With(IceBlocks(0.015f, 16f, "Slab"), r => r.Scale = new Vector2(1.8f, 3.5f)), Aligned(Outcrops("Penitentes", 0.004f, 24f, F(RockTypes.Firn, "Penitentes")), -10f, 10f)),
						D(Ground.Rock, 48f, 3f, 20f, 90f),
						D(Ground.Lineae, 48f, 3f, -1f, -1f, Outcrops("Frost crystals", 0.01f, 12f, F(RockTypes.WaterIce, "Crystal")))),
					C(Ground.Ice, 38f), Ground.Ice, Ground.Ice),

				// Frozen sea of a frozen-through world: flat shelf ice with pressure ridges, frost and drifted snow, a few stranded blocks and seracs.
				// Pressure ridges in lines across the ice, frost flowers on the refrozen leads, wind-aligned sastrugi on the snow.
				E("Ice Shelf", L(Ground.Ice, IceBlocks(0.03f, 14f), Seracs(0.005f, 40f),
						Aligned(Blocks("Pressure ridges", 0.006f, 30f, F(RockTypes.WaterIce, "Ridge"), 10f), -15f, 15f), Blocks("Frost flowers", 0.05f, 4f, F(RockTypes.WaterIce, "FrostFlowers"), 10f)),
					Ds(D(Ground.Frost, 64f), D(Ground.Snow, 96f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.06f, 8f, F(RockTypes.Firn, "Sastrugi"), 15f), -12f, 12f))),
					C(Ground.Ice, 38f), Ground.Ice, Ground.Ice),

				// ── Lowland (tier 4) ──
				// Smooth cryolava with lobate flow fronts, ice vent cones in fields, frost blooms, salt stains, a few penitentes.
				E("Cryovolcanic Plain", L(Ground.Frost, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.02f, 16f, "Rounded", "Slab"), Outcrops("Ice vent cones", 0.008f, 20f, F(RockTypes.WaterIce, "Cone"), 20f)),
					Ds(D(Ground.Ice, 64f, 2f, -1f, -1f, Blocks("Cryolava lobes", 0.015f, 16f, F(RockTypes.WaterIce, "Lobe"), 15f)),
						D(Ground.Snow, 96f, 2f, -1f, -1f, Outcrops("Frost blooms", 0.01f, 12f, F(RockTypes.WaterIce, "Crystal")), Aligned(Outcrops("Penitentes", 0.003f, 24f, F(RockTypes.Firn, "Penitentes")), -10f, 10f)),
						D(Ground.Lineae, 64f)),
					C(Ground.Ice), Ground.Ice, Ground.Ice),
				// Lowland pasture mosaic: turf, oak copses, hawthorn scrub among the bramble, gorse and hazel; umbellifers and
				// knapweed among the meadow flowers, bracken on the slopes.
				E("Grassland", L(Ground.Grass, Grass("GrassLush", 45f, 220), Stand(Was(0.15f, 12f), 25f, W(0.1f, 0.6f, open: 0.06f), "Oak"), Bush("Bramble", 0.15f), Bush("Gorse", 0.15f), Bush("Hazel", 0.08f),
						Bush("Hawthorn", 0.1f)),
					Ds(D(Ground.GrassMeadow, 64f, 2f, -1f, -1f, Plants("FlowersMeadow", 6f, 180), Grass("GrassLush", 30f), Plants("FlowersTall", 2f, 180)),
						D(Ground.Soil, 96f, 2f, 15f, 40f, Boulders(0.05f, 10f, GreyRound), Plants("FernBracken", 3f, 160, 40f))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				// A Saharan / Arabian spring oasis: date-palm groves (no coconut palm grows at a desert oasis) with their frond
				// litter, tamarisk and oleander (in place of hibiscus), Phragmites and sedges at the spring.
				E("Oasis", L(Ground.Grass, Grass("GrassTall", 25f), Stand(Was(1.5f, 6f), 15f, W(0.6f, 0.9f, open: 0.05f, metres: 160f), "DatePalm"),
						Bush("Tamarisk", 0.3f), Bush("Oleander", 0.3f), Plants("DebrisPalm", 2f, 128, 20f)),
					Ds(D(Ground.Sand, 64f, 2f, -1f, -1f, Bush("Tamarisk", 0.15f)), D(Ground.Mud, 32f, 2f, 0f, 8f, Plants("Reeds", 12f), Plants("ReedsPlume", 10f, 200), Grass("GrassSedge", 15f))),
					C(Ground.Sandstone), Ground.Mud, Ground.Sand)
					.Moved("main", "Trees: Palm", "Bush: Hibiscus"),
				// Mixed-grass prairie / pampas: golden bunch grass and needle-grass, sunflower, coneflower and goldenrod patches
				// among the prairie forbs, lone bur oaks, rabbitbrush on the dry slopes.
				E("Plains", L(Ground.GrassDry, Grass("GrassDry", 40f, 220), Plants("FlowersWarm", 3f), Stand(Was(0.03f, 20f), 20f, W(0.03f, 0.5f, open: 0.08f), "Oak"), Bush("Bramble", 0.05f),
						Plants("FlowersTall", 2f, 180), Grass("GrassFeather", 15f)),
					Ds(D(Ground.Grass, 96f, 2f, -1f, -1f, Grass("GrassLush", 25f)), D(Ground.Soil, 128f, 2f, 15f, 45f, Bush("Rabbitbrush", 0.3f, 1f, 45f))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				// Callisto's dark lag: frost-capped knobs, radiolysis-browned ice, bright-rayed small craters, frost on shaded
				// slopes, ice grains. Fine dust in place of the cracked earth (mud cracks need water and clay).
				E("Radiation Plain", L(Ground.Regolith, Stones(SmallRocks("Grey"), 2f), Stones(Pebbles("Ice"), 2f),
						With(Outcrops("Frost-capped knobs", 0.01f, 20f, F(RockTypes.DarkLag, "FrostKnob"), 20f), r => r.Spacing = 16f)),
					Ds(D(Ground.Dust, 64f, 2f, -1f, -1f, With(Blocks("Crater rims", 0.003f, 30f, F(RockTypes.Breccia, "CraterRim"), 20f), r => r.Spacing = 30f)), D(Ground.Gravel, 48f),
						D(Ground.Frost, 48f, 2f, 15f, 40f), D(Ground.Lineae, 96f)),
					C(Ground.Rock), Ground.Regolith, Ground.Regolith),
				// Lunar maria and highlands: craters at every scale, breccia ejecta, bright anorthosite blocks, ponded dust.
				E("Regolith Plain", L(Ground.Regolith, Stones(SmallRocks("Grey"), 3f), Stones(Pebbles("Grey"), 5f),
						With(Blocks("Crater rims", 0.003f, 30f, F(RockTypes.Breccia, "CraterRim"), 20f), r => r.Spacing = 30f)),
					Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Blocks("Breccia", 0.02f, 14f, F(RockTypes.Breccia, "Block", "Boulder"))),
						D(Ground.Rock, 48f, 3f, 20f, 90f, Boulders(0.1f, 10f, GreyRound), Blocks("Anorthosite", 0.02f, 14f, F(RockTypes.Anorthosite, "Boulder"))),
						D(Ground.Dust, 96f)),
					C(Ground.Rock), Ground.Regolith, Ground.Regolith),
				// The Venera landing sites: platy layered basalt slabs over dark soil, lobate pahoehoe flows, cinder.
				E("Runaway Greenhouse Plain", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 2f), Blocks("Basalt blocks", 0.03f, 16f, F(RockTypes.Basalt, "Block")),
						Blocks("Platy slabs", 0.08f, 8f, F(RockTypes.Basalt, "Slabs"), 20f)),
					Ds(D(Ground.Sulphur, 96f), D(Ground.Ash, 64f), D(Ground.Pahoehoe, 64f, 2f, -1f, -1f, Stones(Pebbles("Basalt"), 2f)), D(Ground.Cinder, 96f)),
					C(Ground.Basalt), Ground.Basalt, Ground.Basalt),
				// Badwater, Uyuni, the Danakil: salt-polygon plates over the crust, Devil's Golf Course halite pinnacles, sailing
				// stones; selenite, saltbush, samphire and salt-crusted driftwood on the cracked margin; tamarisk by the springs.
				E("Salt Flat", L(Ground.SaltCrust, With(Blocks("Salt polygons", 0.2f, 5f, F(RockTypes.Halite, "Polygons"), 8f), r => r.Spacing = 4.5f),
						Blocks("Halite pinnacles", 0.03f, 8f, F(RockTypes.Halite, "Pinnacles"), 15f), Stones(SmallRocks("Grey"), 0.3f)),
					Ds(D(Ground.CrackedEarth, 128f, 2f, -1f, -1f, Bush("Saltbush", 0.4f), Plants("ShrubSamphire", 3f), Outcrops("Gypsum", 0.005f, 16f, F(RockTypes.Gypsum, "Crystal")),
							Logs("Driftwood", 0.005f, 20f, Wood("Driftwood"))),
						D(Ground.Sand, 96f, 2f, -1f, -1f, Bush("Tamarisk", 0.08f))),
					C(Ground.Rock), Ground.SaltCrust, Ground.SaltCrust),
				// Thorn scrub, maquis and chaparral (15–33 °C): open acacia, mesquite groves and the odd wild olive, creosote,
				// thornbush, cistus at the wet end, prickly pear; sagebrush only where it is cool (it is a cold-desert shrub);
				// saltbush, agave and yucca on the bare soil.
				E("Scrubland", L(Ground.GrassDry, Plants("ShrubDry", 8f), Plants("ShrubSmall", 4f), Band(Bush("Sagebrush", 1.5f), -1f, 0.55f), Bush("Creosote", 0.6f), Grass("GrassTuft", 15f), Stand(Was(0.05f, 15f), 25f, W(0.15f, 0.1f, open: 0.2f), "Acacia"),
						Trees(0.03f, 12f, 25f, "Mesquite"), Band(Trees(0.01f, 14f, 25f, "Olive"), -1f, 1f, -0.25f, 1f), Bush("Thornbush", 0.4f), Band(Bush("Cistus", 0.6f), -1f, 1f, -0.25f, 1f),
						Plants("PadCactus", 1f, 128)),
					Ds(D(Ground.Soil, 64f, 2f, -1f, -1f, Bush("Saltbush", 0.3f), Plants("RosetteAgave", 0.3f, 128), Plants("RosetteYucca", 0.2f, 128)),
						D(Ground.Rock, 48f, 3f, 25f, 90f, Boulders(0.1f, 8f, Sandstone), Blocks("Sandstone beds", 0.03f, 14f, F(RockTypes.Sandstone, "Block", "Tilted")))),
					C(Ground.Rock), Ground.Gravel, Ground.Gravel),
				// Io's sulphur plains and Dallol: sulphur chimneys and fumarole cones in vent fields, needle crystals, salt
				// terraces and polygons on the crust, SO₂ frost.
				E("Sulphur Flats", L(Ground.Sulphur, Stones(SmallRocks("Basalt"), 1f), Outcrops("Sulphur chimneys", 0.02f, 10f, F(RockTypes.Sulphur, "Chimney"), 20f),
						Outcrops("Fumarole cones", 0.008f, 18f, F(RockTypes.Sulphur, "Cone"), 20f), Outcrops("Sulphur crystals", 0.03f, 8f, F(RockTypes.Sulphur, "Crystals"), 30f)),
					Ds(D(Ground.CrackedEarth, 64f, 2f, -1f, -1f, Blocks("Salt terraces", 0.006f, 20f, F(RockTypes.Halite, "Terrace"), 12f), Blocks("Salt polygons", 0.02f, 8f, F(RockTypes.Halite, "Polygons"), 8f)),
						D(Ground.Ash, 96f), D(Ground.Frost, 96f)),
					C(Ground.Basalt), Ground.Sulphur, Ground.Sulphur),
				// Titan's dune seas and Huygens' cobble plain: organic dunes, water-ice cobbles, bladed penitentes on the frosted highs.
				E("Tholin Plain", L(Ground.Tholin, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.03f, 14f, "Rounded"), Stones(SmallRocks("Ice"), 3f), Stones(Pebbles("Ice"), 4f)),
					Ds(D(Ground.Frost, 96f, 2f, -1f, -1f, Aligned(Outcrops("Bladed terrain", 0.006f, 20f, F(RockTypes.Firn, "Penitentes"), 20f), -10f, 10f)), D(Ground.Gravel, 64f), D(Ground.TholinDune, 128f)),
					C(Ground.Rock), Ground.Tholin, Ground.Tholin),
				// Atacama-dead land: bleached snags of a lost wood, tumbleweed, varnished desert pavement, wind-faceted ventifacts.
				E("Wasteland", L(Ground.CrackedEarth, Plants("ShrubDry", 3f), Trees(0.05f, 15f, 25f, "Dead"), Stones(SmallRocks("Grey"), 2f), Plants("ShrubTumbleweed", 0.5f, 128),
						Logs("Bleached logs", 0.01f, 16f, Wood("Driftwood"))),
					Ds(D(Ground.Ash, 64f), D(Ground.Gravel, 48f, 2f, -1f, -1f, Outcrops("Ventifacts", 0.01f, 16f, F(RockTypes.Basalt, "Ventifact"))),
						D(Ground.Reg, 96f, 2f, -1f, -1f, Stones(Pebbles("Grey"), 6f), Logs("Petrified logs", 0.01f, 16f, Wood("PetrifiedLog"), 20f))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				// Freshwater marsh and fen: Phragmites reedbeds and cattail on the mud, sedge tussocks and rushes, flag iris,
				// loosestrife and marigold, birch-and-alder carr (one wood: the alder joins the birch).
				E("Wetlands", L(Ground.Grass, Grass("GrassTall", 30f), Stand(Was(0.08f, 10f), 15f, W(0.15f, 0.4f, open: 0.03f), "Birch", "Alder"), Bush("Willow", 0.8f)),
					Ds(D(Ground.Mud, 48f, 2f, 0f, 10f, Plants("Reeds", 25f, 220), Plants("ReedsPlume", 15f, 220), Grass("GrassSedge", 20f)),
						D(Ground.Moss, 64f, 2f, -1f, -1f, Plants("Fern", 3f), Grass("GrassRush", 12f), Plants("FlowersWet", 3f, 180))),
					C(Ground.Soil), Ground.Mud, Ground.Mud)
					.Moved("main", "Trees: Birch"),
				// Mixed farming: standard oaks and poplar rows, hawthorn hedgerow thickets with hazel and bramble (scatter makes
				// thickets, not lines), cow parsley on the verges, poppies at the field margins. (Wheat fields need hand-made masks.)
				E("Farmland", L(Ground.Soil, Stand(Was(0.02f, 20f), 15f, W(0.03f, 0.5f, open: 0.06f), "Oak"), Bush("Bramble", 0.3f), Bush("Hazel", 0.15f),
						Bush("Hawthorn", 0.3f), Trees(0.01f, 8f, 15f, "LombardyPoplar")),
					Ds(D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassLush", 30f), Plants("FlowersTall", 1f, 160)), D(Ground.GrassDry, 64f, 2f, -1f, -1f, Grass("GrassDry", 20f), Plants("FlowersWarm", 1f, 160))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				// Lakeshore zonation: reeds and cattail, then sedges and rushes on the mud, wet-meadow flowers, alder carr and
				// (where it is mild) weeping willow behind; driftwood on the pebble beaches. (Water lilies need a float placement.)
				E("Lake", L(Ground.Grass, Grass("GrassLush", 30f), Bush("Willow", 0.3f),
						Stand(Was(0.1f, 10f), 15f, W(0.15f, 0.5f, open: 0.05f), "Alder"), Band(Trees(0.02f, 12f, 15f, "WeepingWillow"), 0.3f, 1f)),
					Ds(D(Ground.Mud, 32f, 2f, 0f, 10f, Plants("Reeds", 15f), Plants("ReedsPlume", 10f, 200), Grass("GrassSedge", 15f), Grass("GrassRush", 10f), Plants("FlowersWet", 2f, 180)),
						D(Ground.Pebbles, 48f, 2f, -1f, -1f, Logs("Driftwood", 0.01f, 12f, Wood("Driftwood"), 15f))),
					C(Ground.Rock), Ground.Mud, Ground.Pebbles),
				// Riparian corridor: alder, white willow and black poplar along the banks, sedges, rushes and wet flowers on the
				// mud, driftwood log jams on the gravel bars.
				E("River", L(Ground.Grass, Grass("GrassLush", 25f), Bush("Willow", 0.4f),
						Stand(Was(0.15f, 10f), 15f, W(0.2f, 0.6f, open: 0.05f), "Alder"), Trees(0.02f, 12f, 15f, "WeepingWillow"), Trees(0.02f, 14f, 15f, "Poplar")),
					Ds(D(Ground.Pebbles, 32f, 2f, -1f, -1f, Logs("Log jams", 0.02f, 8f, Wood("Driftwood", "FallenLog_Birch"), 15f)),
						D(Ground.Mud, 48f, 2f, 0f, 10f, Plants("Reeds", 10f), Grass("GrassSedge", 15f), Grass("GrassRush", 10f), Plants("FlowersWet", 2f, 180))),
					C(Ground.Rock), Ground.Pebbles, Ground.Gravel),
				// Forested wetland (−3…20 °C): a bald-cypress swamp with its knees and palmetto where it is warm, an alder carr
				// where it is cool, snags in both (one wood each, the snags joined to it; the two bands meet at about 11.6 °C,
				// so only one wood stands anywhere), mossy logs and fungi, sedges on the peat.
				E("Swamp", L(Ground.Mud, Plants("Reeds", 20f), Grass("GrassTall", 15f), Bush("Willow", 0.4f),
						Band(Stand(Was(0.2f, 8f), 15f, W(0.35f, 0.4f, open: 0.05f), "BaldCypress", "Dead"), 0.35f, 1f),
						Band(Stand(Was(0.2f, 8f), 15f, W(0.35f, 0.4f, open: 0.05f), "Alder", "Dead"), -1f, 0.35f),
						Band(Plants("CypressKnees", 3f, 160, 15f), 0.35f, 1f), Band(Bush("Palmetto", 0.3f), 0.5f, 1f)),
					Ds(D(Ground.Peat, 48f, 2f, -1f, -1f, Grass("GrassSedge", 12f)),
						D(Ground.Moss, 64f, 2f, -1f, -1f, Plants("Fern", 5f), Logs("Fallen logs", 0.05f, 12f, Wood("FallenLog_Oak", "FallenLog_Birch")), Plants("MushroomSwamp", 1f, 128))),
					C(Ground.Soil), Ground.Mud, Ground.Mud)
					// The snags joined the cypress and alder woods.
					.Moved("main", "Trees: Dead"),
				// A flooded cave, dark inside: pale fungi and slime-mould mats on the mud, dripstone and flowstone where it is drier.
				E("Swamp Cave", L(Ground.Mud, Plants("Reeds", 6f), Plants("MushroomCave", 2f, 140), Plants("SlimeMat", 1.5f, 128), Logs("Washed-in timber", 0.02f, 10f, Wood("FallenLog_Oak", "Driftwood"))),
					Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Outcrops("Stalagmites", 0.03f, 8f, F(RockTypes.Limestone, "Stalagmite"))),
						D(Ground.Rock, 48f, 3f, 20f, 90f, Blocks("Flowstone", 0.02f, 12f, F(RockTypes.Limestone, "Flowstone")))),
					C(Ground.CliffRock, 38f), Ground.Mud, Ground.Mud),
				// A drowned settlement in swamp forest: bald cypress (knees round them) over the snags, ivy over the flagstones, fungi.
				E("Swamp Ruins", L(Ground.Mud, Plants("Reeds", 15f), Stand(Was(0.15f, 8f), 15f, W(0.3f, 0.3f, open: 0.05f), "Dead"), Bush("Willow", 0.3f)),
					Ds(D(Ground.Flagstone, 32f, 4f, -1f, -1f, Plants("Ivy", 6f, 180)),
						D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("Fern", 5f), Stand(Was(0.1f, 10f), 15f, W(0.3f, 0.35f, open: 0.05f), "BaldCypress"), Plants("CypressKnees", 1.5f, 128), Plants("MushroomSwamp", 1f, 128),
							Logs("Fallen logs", 0.04f, 10f, Wood("FallenLog_Oak", "Stump_Bracket")))),
					C(Ground.Soil), Ground.Mud, Ground.Mud),
				// A sacred site swallowed by the swamp: a few great cypresses, rushes, ivy dense on the flagstones, knees and fungi.
				E("Swamp Temple", L(Ground.Mud, Plants("Reeds", 10f), Grass("GrassRush", 10f, 200), Trees(0.01f, 20f, 15f, "BaldCypress")),
					Ds(D(Ground.Flagstone, 24f, 4f, -1f, -1f, Plants("Ivy", 8f, 200)),
						D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("Fern", 5f), Plants("CypressKnees", 1f, 128), Plants("MushroomSwamp", 0.8f, 128))),
					C(Ground.CliffRock), Ground.Mud, Ground.Mud),
				// A collapsed jungle city: canopy with the odd kapok emergent, vines over the stone, broad herbs and bromeliads,
				// tree ferns and bananas in the gaps the fallen buildings left.
				E("Jungle Ruins", L(Ground.JungleFloor, Stand(Was(1.2f, 5f), 25f, W(0.7f, 0.8f), "Jungle"), Plants("Fern", 15f), Plants("ShrubSmall", 6f), Bush("Hibiscus", 0.5f),
						With(Trees(0.004f, 28f, 20f, "Kapok"), r => { r.Spacing = 28f; r.ClusterSize = 2f; }), Plants("BroadHerb", 3f), Plants("RosetteBromeliad", 1f, 128)),
					Ds(D(Ground.Flagstone, 32f, 4f, -1f, -1f, Plants("Ivy", 8f, 200)),
						D(Ground.Moss, 48f, 2f, -1f, -1f, Trees(0.15f, 8f, 25f, "TreeFern"), Trees(0.1f, 8f, 25f, "Banana"), Logs("Fallen logs", 0.05f, 10f, Wood("FallenLog_Oak", "Stump_Broken")))),
					C(Ground.Rock), Ground.Mud, Ground.Mud),
				// Ta Prohm: silk-cotton giants standing alone over the stone, vines over the flagstones, broad herbs, bromeliads, bananas.
				E("Jungle Temple", L(Ground.JungleFloor, Stand(Was(0.8f, 5f), 25f, W(0.55f, 0.7f), "Jungle"), Plants("Fern", 12f), Bush("Hibiscus", 0.4f), Bush("Rhododendron", 0.2f),
						With(Trees(0.006f, 28f, 20f, "Kapok"), r => { r.Spacing = 28f; r.ClusterSize = 2f; }), Plants("BroadHerb", 2f)),
					Ds(D(Ground.Flagstone, 24f, 4f, -1f, -1f, Plants("Ivy", 10f, 200)),
						D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("RosetteBromeliad", 1f, 128), Trees(0.1f, 8f, 25f, "Banana"))),
					C(Ground.Rock), Ground.Mud, Ground.Mud),

				// ── Highland (tier 5) ──
				// Moso bamboo over a Sasa understory and culm litter; tree ferns in the warmest groves.
				E("Bamboo Forest", L(Ground.ForestFloor, Stand(Was(3f, 3f), 25f, W(0.7f, 0.5f, metres: 200f), "Bamboo"), Plants("Fern", 8f), Grass("GrassTall", 6f), Bush("Rhododendron", 0.6f),
						Bush("DwarfBamboo", 1.5f), Plants("DebrisBamboo", 6f, 128), Plants("MushroomForest", 0.4f)),
					Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Band(Trees(0.08f, 8f, 30f, "TreeFern"), 0.55f, 1f), Plants("Fern", 6f)), D(Ground.Soil, 64f, 2f, 20f, 45f)),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				// Beech–oak–birch forest: beech joins the oak and birch stands (one rule, so they share the woods); holly under
				// them, spring flowers, fallen branches and fungi; bilberry and needle litter under the spruce.
				E("Forest", L(Ground.ForestFloor, Stand(Was(2f, 6f), 30f, W(0.8f, 1f), "Oak", "Birch", "Beech"), Plants("Fern", 12f), Plants("DebrisForest", 10f, 128), Plants("ShrubSmall", 3f),
						Bush("Hazel", 0.5f), Bush("Bramble", 0.6f), Bush("Laurel", 0.2f),
						Bush("Holly", 0.3f), Plants("FlowersWoodland", 5f, 180), Plants("DebrisBranch", 2f, 128), Plants("MushroomForest", 0.6f), Plants("Ivy", 2f, 128),
						Logs("Fallen logs", 0.2f, 8f, Wood("FallenLog_Oak", "FallenLog_Birch", "Stump_Broken", "Stump_Bracket", "RootPlate"))),
					Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("Fern", 8f), Bush("Rhododendron", 0.3f)),
						D(Ground.NeedleLitter, 96f, 2f, -1f, -1f, Stand(Was(1.5f, 6f), 30f, W(0.8f, 1f), "Spruce"), Bush("Bilberry", 4f), Plants("DebrisNeedle", 8f, 128)),
						D(Ground.Soil, 64f, 2f, 20f, 45f, Boulders(0.05f, 10f, GreyRound))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel)
					.Moved("main", "Trees: Oak, Birch"),
				// Yellowstone / El Tatio: sinter flats with geyser cones and terraces, sulphur crystals at the vents, bleached
				// snags where hot water killed the trees, and a lodgepole edge round the basin.
				E("Geyser Basin", L(Ground.CrackedEarth, Grass("GrassTuft", 4f), Blocks("Tuff", 0.02f, 16f, F(RockTypes.Tuff, "Tafoni")), Blocks("Obsidian", 0.015f, 18f, F(RockTypes.Obsidian, "Chunk")),
						Trees(0.02f, 15f, 20f, "Dead"), Stand(Was(0.1f, 8f), 25f, W(0.15f, 0.6f, open: 0.03f), "Pine")),
					Ds(D(Ground.Sulphur, 32f, 4f, -1f, -1f, Blocks("Sulphur crystals", 0.03f, 10f, F(RockTypes.Sulphur, "Crystals"), 30f)), D(Ground.Mud, 48f),
						D(Ground.Sinter, 64f, 3f, 0f, 20f, Outcrops("Sinter cones", 0.005f, 40f, F(RockTypes.Sinter, "Cone"), 20f), Blocks("Sinter terraces", 0.01f, 24f, F(RockTypes.Sinter, "Terrace"), 20f))),
					C(Ground.Rock), Ground.Mud, Ground.Mud),
				// Rolling hills from −33 to 33 °C: temperate pasture and heather moor in the middle, garrigue (olive, rock-rose)
				// when warm, dwarf birch when cold. Hawthorn, bracken and heather each kept to the climate they grow in.
				E("Hills", L(Ground.Grass, Grass("GrassLush", 35f, 220), Plants("FlowersMeadow", 4f), Stand(Was(0.1f, 12f), 25f, W(0.12f, 0.6f, open: 0.05f), "Oak"), Bush("Gorse", 0.5f), Bush("Juniper", 0.2f), Bush("Bramble", 0.15f), Outcrops("Granite tors", 0.008f, 40f, F(RockTypes.Granite, "Tor", "Perched"), 15f),
						Band(Bush("Hawthorn", 0.15f), -0.15f, 0.6f), Band(Trees(0.02f, 12f, 20f, "Olive"), 0.45f, 1f), Band(Bush("Cistus", 0.4f), 0.45f, 1f), Band(Bush("DwarfBirch", 0.5f), -1f, -0.25f)),
					Ds(D(Ground.Moss, 64f, 2f, -1f, -1f, Band(Plants("FernBracken", 3f), -0.2f, 0.7f)), D(Ground.Rock, 48f, 3f, 25f, 90f, Boulders(0.1f, 8f, GreyRound), Blocks("Granite corestones", 0.04f, 12f, F(RockTypes.Granite, "Corestone", "Split"))),
						D(Ground.Heath, 64f, 2f, 0f, 30f, Band(Bush("Heather", 12f), -0.4f, 0.5f), Grass("GrassTuft", 10f))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				// Enceladus' tiger stripes: flank ridges along the vent fissures, ice vent cones, frost blooms, house-sized ice boulders.
				E("Ice Geyser Field", L(Ground.Snow, Stones(SmallRocks("Grey"), 0.5f), IceBlocks(0.02f, 16f, "Calved"),
						With(Blocks("Fissure ridges", 0.012f, 14f, F(RockTypes.WaterIce, "Ridge"), 20f), r => r.Spacing = 10f)),
					Ds(D(Ground.Ice, 64f, 2f, -1f, -1f, Outcrops("Ice vent cones", 0.008f, 20f, F(RockTypes.WaterIce, "Cone"), 20f),
							With(IceBlocks(0.004f, 30f, "Calved", "Rounded"), r => { r.Scale = new Vector2(3f, 6f); r.Spacing = 20f; })),
						D(Ground.Frost, 48f, 2f, -1f, -1f, Outcrops("Frost blooms", 0.012f, 10f, F(RockTypes.WaterIce, "Crystal")))),
					C(Ground.Ice), Ground.Ice, Ground.Ice),
				// Layered rainforest: kapok emergents over the canopy, tree ferns and bananas in the gaps, monstera and
				// bromeliads on the shaded floor, elephant-ears in the wet hollows.
				E("Jungle", L(Ground.JungleFloor, Stand(Was(3f, 5f), 30f, W(0.9f, 1f), "Jungle"), Stand(Was(0.3f, 8f), 20f, W(0.9f, 0.25f, open: 0.3f), "Palm"), Plants("Fern", 25f), Plants("ShrubSmall", 10f), Grass("GrassTall", 10f), Bush("Hibiscus", 0.6f),
						Trees(0.01f, 40f, 30f, "Kapok"), Plants("BroadHerb", 3f), Plants("RosetteBromeliad", 1f), Plants("Ivy", 1.5f, 128),
						Logs("Fallen logs", 0.1f, 10f, Wood("FallenLog_Oak", "Stump_Bracket"))),
					Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Stand(Was(0.4f, 4f), 25f, W(0.4f, 0.25f, open: 0.02f, metres: 200f), "Bamboo"), Trees(0.15f, 8f, 30f, "TreeFern"), Trees(0.1f, 8f, 25f, "Banana")),
						D(Ground.Mud, 64f, 2f, 0f, 10f, Plants("BroadHerb", 4f))),
					C(Ground.Rock), Ground.Mud, Ground.Mud),
				// Limestone country from Dinaric pavement to Guilin towers: holm oak, and olive and rock-rose where it is warm
				// and drier, tree ferns in the warmest; hart's-tongue fern in the grikes and ivy over the rock.
				E("Karst", L(Ground.Grass, Grass("GrassLush", 30f), Stand(Was(0.2f, 10f), 25f, W(0.2f, 0.6f, open: 0.05f), "Oak"), Plants("Fern", 4f), Bush("Box", 0.6f), Bush("Juniper", 0.15f), Blocks("Limestone pavement", 0.03f, 18f, F(RockTypes.Limestone, "Pavement", "Block"), 12f),
						Band(Trees(0.03f, 12f, 25f, "HolmOak"), 0.35f, 1f), Band(Trees(0.02f, 12f, 20f, "Olive"), 0.45f, 1f, -1f, 0.3f), Band(Bush("Cistus", 0.3f), 0.4f, 1f, -1f, 0.5f), Band(Trees(0.05f, 8f, 25f, "TreeFern"), 0.6f, 1f)),
					Ds(D(Ground.Limestone, 48f, 3f, 20f, 90f, Boulders(0.3f, 6f, LimestoneRocks), Outcrops("Limestone pinnacles", 0.06f, 12f, F(RockTypes.Limestone, "Pinnacle"), 30f),
							Plants("FernHartstongue", 3f, 160, 45f), Plants("Ivy", 2f, 128, 60f)),
						D(Ground.Soil, 64f)),
					C(Ground.Limestone, 35f), Ground.Mud, Ground.Gravel),
				// Sputnik Planitia: convecting nitrogen ice in sublimation ripples, drifting water-ice rafts, sublimation pits,
				// bladed terrain on the highs, dark tholin streaks.
				E("Nitrogen Ice Field", L(Ground.Frost, Aligned(Blocks("Sublimation ripples", 0.05f, 6f, F(RockTypes.Firn, "Sastrugi"), 15f), -20f, 20f), With(IceBlocks(0.01f, 20f, "Slab", "Rounded"), r => r.Scale = new Vector2(2f, 4f))),
					Ds(D(Ground.Snow, 64f, 2f, -1f, -1f, With(Blocks("Sublimation pits", 0.003f, 24f, F(RockTypes.Firn, "PitRim"), 15f), r => r.Spacing = 22f)),
						D(Ground.Ice, 96f, 2f, -1f, -1f, Aligned(Outcrops("Bladed terrain", 0.004f, 24f, F(RockTypes.Firn, "Penitentes"), 25f), -10f, 10f)),
						D(Ground.Tholin, 128f)),
					C(Ground.Ice), Ground.Frost, Ground.Frost),
				// East African savanna: tall golden grass swathes, umbrella acacias and the odd baobab in the hottest, thornbush
				// thickets, termite cathedrals, candelabra euphorbia round the kopjes.
				E("Savanna", L(Ground.GrassDry, Grass("GrassDry", 40f, 220), Stand(Was(0.08f, 20f), 20f, W(0.25f, 0.15f, open: 0.25f), "Acacia"), Plants("ShrubDry", 2f), Outcrops("Granite kopjes", 0.01f, 30f, F(RockTypes.Granite, "Tor", "Perched"), 15f),
						Grass("GrassSavanna", 25f, 200), Band(Trees(0.01f, 30f, 15f, "Baobab"), 0.65f, 1f), Bush("Thornbush", 0.3f), Outcrops("Termite mounds", 0.03f, 20f, F(RockTypes.Termitaria, "Cathedral"), 20f), Plants("Euphorbia", 0.2f)),
					Ds(D(Ground.Soil, 96f, 2f, -1f, -1f, Bush("Thornbush", 0.6f)), D(Ground.Clay, 64f, 2f, -1f, -1f, Boulders(0.03f, 12f, Sandstone))),
					C(Ground.Sandstone), Ground.Mud, Ground.Gravel),
				// Kazakh / Great Basin steppe: feather grass over fescue, sagebrush and rabbitbrush, tumbleweed, spiny cushions on the gravel.
				E("Steppe", L(Ground.GrassDry, Grass("GrassDry", 35f), Grass("GrassTuft", 15f), Plants("FlowersWarm", 2f), Bush("Sagebrush", 0.8f), Bush("Juniper", 0.05f, 0.7f),
						Grass("GrassFeather", 20f), Plants("ShrubTumbleweed", 0.3f, 128), Bush("Rabbitbrush", 0.4f)),
					Ds(D(Ground.Soil, 96f), D(Ground.Gravel, 64f, 2f, -1f, -1f, Stones(SmallRocks("Grey"), 1f), Plants("CushionSpiny", 1f))),
					C(Ground.Rock), Ground.Gravel, Ground.Gravel),
				// Boreal forest: larch joins the pine and aspen the birch (one rule each, so they share their woods); bilberry
				// carpets and reindeer lichen under the conifers, needle litter and fungi, fireweed in the clearings.
				E("Taiga", L(Ground.NeedleLitter, Stand(Was(2.5f, 5f), 30f, W(0.8f, 1f), "Spruce"), Stand(Was(0.8f, 6f), 30f, W(0.8f, 0.45f), "Pine", "Larch"), Plants("Fern", 4f), Grass("GrassTuft", 8f), Bush("Juniper", 0.5f),
						Bush("Bilberry", 5f), Plants("LichenClump", 3f), Plants("DebrisNeedle", 8f, 128), Plants("MushroomForest", 0.4f),
						Logs("Fallen logs", 0.15f, 8f, Wood("FallenLog_Conifer", "FallenLog_Birch", "Stump_Broken", "RootPlate"))),
					Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Stand(Was(0.2f, 6f), 25f, W(0.8f, 0.25f, open: 0.05f), "Birch", "Aspen"), Plants("ShrubSmall", 3f), Bush("Willow", 0.3f, 0.6f),
							Plants("FlowersTall", 2f, 180)),
						D(Ground.Snow, 96f)),
					C(Ground.Rock), Ground.Mud, Ground.Gravel)
					.Moved("main", "Trees: Pine").Moved("detail/0", "Trees: Birch"),
				// Montane valley: meadow floor with tall herbs, alder, sedge and wet flowers along the wet bottoms, broadleaf
				// woods on the lower ground and larch on the colder upper slopes.
				E("Valley", L(Ground.Grass, Grass("GrassLush", 45f, 220), Stand(Was(0.3f, 10f), 25f, W(0.3f, 0.7f, open: 0.05f), "Oak", "Birch"), Bush("Hazel", 0.3f), Bush("Bramble", 0.3f), Bush("Willow", 0.15f),
						Plants("FlowersTall", 2f, 180)),
					Ds(D(Ground.GrassMeadow, 64f, 2f, -1f, -1f, Plants("FlowersMeadow", 8f, 180)), D(Ground.Pebbles, 32f, 3f, 0f, 8f, Blocks("Conglomerate", 0.03f, 14f, F(RockTypes.Conglomerate, "Boulder", "Block"), 10f)),
						D(Ground.Mud, 48f, 2f, 0f, 10f, Stand(Was(0.3f, 8f), 15f, W(0.4f, 0.7f, open: 0.05f), "Alder"), Grass("GrassSedge", 15f), Plants("FlowersWet", 4f, 180)),
						D(Ground.NeedleLitter, 96f, 2f, 15f, 40f, Band(Stand(Was(0.3f, 8f), 35f, W(0.5f, 0.6f, open: 0.05f), "Larch"), -1f, 0.1f))),
					C(Ground.Rock), Ground.Mud, Ground.Pebbles),
				// Oak–birch wood pasture: hawthorn and holly among the hazel, bracken and foxgloves in the glades, bluebells and
				// fungi under the trees.
				E("Woodland", L(Ground.Grass, Stand(Was(0.8f, 8f), 30f, W(0.55f, 0.8f, open: 0.05f), "Oak", "Birch"), Grass("GrassLush", 25f), Plants("FlowersMeadow", 3f), Bush("Hazel", 0.8f), Bush("Bramble", 1f),
						Bush("Hawthorn", 0.2f), Bush("Holly", 0.15f), Plants("FernBracken", 3f), Plants("FlowersTall", 1f, 180),
						Logs("Fallen logs", 0.08f, 10f, Wood("FallenLog_Oak", "FallenLog_Birch", "Stump_Broken", "Stump_Bracket"))),
					Ds(D(Ground.ForestFloor, 48f, 2f, -1f, -1f, Plants("Fern", 6f), Plants("DebrisForest", 6f, 128), Plants("FlowersWoodland", 5f, 180), Plants("MushroomForest", 0.5f)), D(Ground.Moss, 64f)),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				// Castle grounds: ivy and moss on the stone, nettles and docks at the wall feet, a few meadow flowers, rubble.
				E("Castle", L(Ground.Flagstone, Plants("Ivy", 4f, 180), Plants("CushionMoss", 3f, 128)),
					Ds(D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassTuft", 8f), Bush("Box", 0.4f), Plants("HerbRuderal", 2f), Plants("FlowersMeadow", 1.5f, 160)),
						D(Ground.Gravel, 48f, 2f, -1f, -1f, Stones(SmallRocks("Grey"), 2f))),
					C(Ground.CliffRock), Ground.Mud, Ground.Gravel),
				// Classical palace gardens: Italian cypress in rows, lone olives, oleander and laurel, a little ivy on the stone.
				E("Marble Palace", L(Ground.Flagstone, Plants("Ivy", 1.5f, 128)),
					Ds(D(Ground.Limestone, 48f, 2f, -1f, -1f, Blocks("Marble", 0.02f, 16f, F(RockTypes.Marble, "Block", "Boulder"))),
						D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassLush", 15f), Bush("Box", 0.6f), Trees(0.08f, 6f, 15f, "Cypress"), Trees(0.02f, 14f, 20f, "Olive"), Bush("Oleander", 0.3f), Bush("Laurel", 0.15f))),
					C(Ground.Limestone), Ground.Mud, Ground.Gravel),
				// A limestone show cave, floor only (no ceiling to hang stalactites from): stalagmites and columns, flowstone,
				// rimstone gours in the wet hollows, breakdown, pale fungi, rare gypsum. The pinnacles were a surface karst spire.
				E("Cave", L(Ground.Rock, Stones(SmallRocks("Grey"), 4f), Stones(Pebbles("Grey"), 6f), Blocks("Limestone", 0.05f, 10f, F(RockTypes.Limestone, "Block")),
						Outcrops("Stalagmites", 0.05f, 6f, F(RockTypes.Limestone, "Stalagmite")), Blocks("Flowstone", 0.02f, 12f, F(RockTypes.Limestone, "Flowstone"), 35f)),
					Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Outcrops("Gypsum", 0.003f, 20f, F(RockTypes.Gypsum, "Crystal"))),
						D(Ground.Mud, 64f, 2f, -1f, -1f, Blocks("Rimstone gours", 0.015f, 14f, F(RockTypes.Limestone, "Gours"), 12f), Plants("MushroomCave", 1.5f, 128))),
					C(Ground.CliffRock, 35f), Ground.Mud, Ground.Gravel)
					.Moved("main", "Formations: Limestone pinnacles"),
				// Ruins consumed by temperate forest: oak with beech joining it, holly under them, ivy over the stone, woodland
				// flowers and fungi.
				E("Forest Ruins", L(Ground.ForestFloor, Stand(Was(1f, 6f), 30f, W(0.6f, 0.9f), "Oak", "Beech"), Plants("Fern", 10f), Plants("DebrisForest", 6f, 128), Bush("Bramble", 0.8f), Bush("Laurel", 0.3f), Bush("Box", 0.15f),
						Bush("Holly", 0.2f), Plants("FlowersWoodland", 3f, 180), Plants("MushroomForest", 1f, 128)),
					Ds(D(Ground.Flagstone, 32f, 4f, -1f, -1f, Plants("Ivy", 8f, 200)),
						D(Ground.Moss, 48f, 2f, -1f, -1f, Logs("Fallen logs", 0.03f, 10f, Wood("FallenLog_Oak", "Stump_Broken", "Stump_Bracket")))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel)
					.Moved("main", "Trees: Oak"),

				// ── Mountain (tier 6) ──
				// Bisti / Badlands NP: prickly pear and rabbitbrush in the draws, a lone pinyon on the caprock, cannonball
				// concretions weathering out of the cracked flats.
				E("Badlands", L(Ground.Clay, Plants("ShrubDry", 1f), Bush("Sagebrush", 0.3f), Bush("Creosote", 0.15f), Stones(SmallRocks("Sandstone"), 2f), Outcrops("Hoodoos", 0.04f, 14f, F(RockTypes.Sandstone, "Pedestal")),
						Blocks("Shale", 0.04f, 12f, F(RockTypes.Shale, "Stack", "Scree")),
						Plants("PadCactus", 0.3f, 128), Bush("Rabbitbrush", 0.2f), Trees(0.01f, 15f, 25f, "Pinyon"), Logs("Petrified logs", 0.01f, 16f, Wood("PetrifiedLog"), 20f)),
					Ds(D(Ground.Sandstone, 48f, 3f, 20f, 90f, Boulders(0.15f, 8f, Sandstone), Outcrops("Sandstone ledges", 0.03f, 18f, F(RockTypes.Sandstone, "Ledges"), 35f)),
						D(Ground.CrackedEarth, 64f, 2f, -1f, -1f, Blocks("Sandstone concretions", 0.02f, 16f, F(RockTypes.Sandstone, "Concretion"), 20f))),
					C(Ground.Sandstone, 35f), Ground.Clay, Ground.Clay),
				// Sonoran / Mojave / erg: Joshua trees in the cooler desert, cholla and ocotillo in the warm, dune tufts on the
				// sand, the odd bloom after rain, and varnished desert pavement with wind-cut ventifacts.
				E("Desert", L(Ground.Sand, Trees(0.04f, 15f, 15f, "Saguaro"), Plants("CactusBarrel", 0.5f, 96), Plants("ShrubDry", 0.5f), Bush("Creosote", 0.5f),
						Band(Trees(0.02f, 15f, 15f, "JoshuaTree"), 0.25f, 0.6f), Band(Plants("Cholla", 0.4f, 96), 0.4f, 1f), Band(Bush("Ocotillo", 0.15f), 0.4f, 1f), Grass("GrassDune", 4f, 200), Plants("FlowersDesert", 0.5f, 160)),
					Ds(D(Ground.Sandstone, 48f, 3f, 15f, 90f, Boulders(0.03f, 12f, Sandstone), Outcrops("Sandstone pedestals", 0.01f, 30f, F(RockTypes.Sandstone, "Pedestal", "Ledges"), 30f)), D(Ground.Gravel, 64f),
						D(Ground.Reg, 96f, 2f, 0f, 12f, Stones(SmallRocks("Basalt"), 2f), Blocks("Ventifacts", 0.02f, 14f, F(RockTypes.Basalt, "Ventifact"), 15f))),
					C(Ground.Sandstone), Ground.Sand, Ground.Sand),
				// Montane to subalpine slopes from −15 to 33 °C: larch woods on the cold grassy slopes, bristlecone on the dry
				// rock, alpenrose where it is wet; alpine flowers in the turf, cones and needles under the pines.
				E("Mountain Slope", L(Ground.Rock, Boulders(0.3f, 6f, Grey), Stones(SmallRocks("Grey"), 4f), Stand(Was(0.1f, 10f), 30f, W(0.1f, 0.35f, scale: 0.8f), "Pine"), Bush("MountainPine", 0.4f, 1f, 40f), Bush("Juniper", 0.15f, 0.7f, 40f),
						Outcrops("Slate upright", 0.03f, 14f, F(RockTypes.Slate, "Upright"), 30f), Blocks("Slate", 0.05f, 10f, F(RockTypes.Slate, "Stack")),
						Band(Trees(0.02f, 14f, 35f, "Bristlecone"), -1f, 0.45f, -1f, -0.1f), Band(Bush("Rhododendron", 0.3f, 0.35f, 40f), -1f, 1f, -0.1f, 1f), Plants("DebrisNeedle", 3f, 128)),
					Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Blocks("Slate scree", 0.04f, 12f, F(RockTypes.Slate, "Scree"))),
						D(Ground.GrassDry, 64f, 2f, 0f, 25f, Grass("GrassTuft", 10f), Band(Stand(Was(0.1f, 10f), 25f, W(0.25f, 0.35f, scale: 0.8f), "Larch"), -1f, 0.15f), Plants("FlowersAlpine", 3f, 180))),
					C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel),
				// Dry ranges (Great Basin, Anti-Atlas): lone bristlecones where it is cold, pinyon where it is mild, spiny
				// cushions and bunch-grass tufts between the quartzite blocks, lichen on the rock.
				E("Rocky Terrain", L(Ground.Rock, Boulders(0.6f, 5f, Grey), Stones(SmallRocks("Grey"), 6f), Bush("MountainPine", 0.1f, 0.9f, 40f), Blocks("Quartzite", 0.06f, 10f, F(RockTypes.Quartzite, "Block", "Wedge")),
						Band(Trees(0.01f, 14f, 35f, "Bristlecone"), -1f, 0.35f), Band(Trees(0.02f, 12f, 30f, "Pinyon"), 0.15f, 0.7f), Plants("CushionSpiny", 1.5f), Grass("GrassTuft", 5f, 200), Plants("LichenClump", 1f)),
					Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Blocks("Quartzite scree", 0.03f, 14f, F(RockTypes.Quartzite, "Scree"))), D(Ground.Pebbles, 32f, 3f, -1f, -1f, Stones(Pebbles("Grey"), 8f))),
					C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel),
				// Arctic tundra: dwarf birch and bilberry, reindeer lichen and arctic flowers; cottongrass and sedge tussocks on
				// the wet moss, moss campion on the frost-shattered gravel.
				E("Tundra", L(Ground.Lichen, Grass("GrassTuft", 20f), Plants("ShrubSmall", 1f), Bush("Willow", 1f, 0.25f), Bush("Juniper", 0.15f, 0.3f), Stones(SmallRocks("Grey"), 2f),
						Bush("DwarfBirch", 1f, 0.7f), Bush("Bilberry", 2f), Plants("LichenClump", 4f), Plants("FlowersAlpine", 3f, 180)),
					Ds(D(Ground.Moss, 64f, 2f, -1f, -1f, Grass("GrassCotton", 10f), Grass("GrassSedge", 12f)), D(Ground.Gravel, 48f, 2f, -1f, -1f, Boulders(0.05f, 10f, GreyRound),
						Blocks("Granite erratics", 0.015f, 20f, F(RockTypes.Granite, "Corestone")), Blocks("Gneiss erratics", 0.015f, 20f, F(RockTypes.Gneiss, "Boulder")), Plants("CushionMossCampion", 2f))),
					C(Ground.Rock), Ground.Mud, Ground.Gravel),
				// A volcanic crater: fumarole cones with sulphur rims on the floor, sulphur crystals and scoria bombs on the cinder,
				// pioneer tufts, lava lichen on the walls.
				E("Crater", L(Ground.Gravel, Stones(SmallRocks("Grey"), 4f), Outcrops("Fumarole cones", 0.006f, 20f, F(RockTypes.Sulphur, "Cone"), 20f), Grass("GrassTuft", 4f, 160)),
					Ds(D(Ground.Rock, 48f, 3f, 20f, 90f, Boulders(0.3f, 6f, GreyRound), Blocks("Andesite talus", 0.03f, 14f, F(RockTypes.Andesite, "Talus")), Plants("LichenLava", 3f, 128, 60f)),
						D(Ground.Ash, 64f, 2f, -1f, -1f, Blocks("Andesite", 0.05f, 12f, F(RockTypes.Andesite, "Block", "Platy")), Blocks("Pumice", 0.04f, 12f, F(RockTypes.Pumice, "Lump"))),
						D(Ground.Cinder, 64f, 2f, -1f, -1f, Outcrops("Sulphur crystals", 0.015f, 10f, F(RockTypes.Sulphur, "Crystals")), Blocks("Scoria bombs", 0.02f, 10f, F(RockTypes.Scoria, "Bomb", "Lump")))),
					C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel),
				// A mountain stronghold: moss cushions and alpine flowers in the stone's cracks, rubble, nettles at the middens,
				// stunted juniper and mountain pine outside the walls.
				E("Fortress", L(Ground.Flagstone, Plants("CushionMoss", 3f, 128), Plants("FlowersAlpine", 1f, 160)),
					Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Stones(SmallRocks("Grey"), 2f)),
						D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassTuft", 5f), Plants("HerbRuderal", 1.5f), Bush("Juniper", 0.1f, 0.7f), Bush("MountainPine", 0.08f))),
					C(Ground.CliffRock), Ground.Mud, Ground.Gravel),
				// An active lava tube with crystal-lined chambers: dribble spires, sulphur needles, Naica-like gypsum blades,
				// obsidian, glowing lava, ropy pahoehoe with scoria.
				E("Volcanic Cave", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 3f), Blocks("Obsidian", 0.06f, 10f, F(RockTypes.Obsidian, "Chunk", "Shard", "Scree")),
						Outcrops("Lava dribbles", 0.04f, 8f, F(RockTypes.Basalt, "Dribble"))),
					Ds(D(Ground.Lava, 96f, 5f),
						D(Ground.Ash, 48f, 2f, -1f, -1f, Outcrops("Sulphur crystals", 0.02f, 10f, F(RockTypes.Sulphur, "Crystals")), Outcrops("Gypsum", 0.006f, 16f, F(RockTypes.Gypsum, "Crystal"))),
						D(Ground.Pahoehoe, 64f, 2f, -1f, -1f, Blocks("Scoria", 0.02f, 10f, F(RockTypes.Scoria, "Lump")))),
					C(Ground.Basalt, 35f), Ground.Basalt, Ground.Basalt),
				// A temple inside a volcano: fumarole cones, standing basalt columns, obsidian, sulphur crystals and sparse lava
				// lichen on the cinder.
				E("Volcanic Temple", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 2f), Blocks("Basalt fallen columns", 0.03f, 16f, F(RockTypes.Basalt, "Fallen", "Block")),
						Outcrops("Fumarole cones", 0.006f, 20f, F(RockTypes.Sulphur, "Cone"), 20f), Outcrops("Basalt columns", 0.02f, 16f, F(RockTypes.Basalt, "Columns")), Blocks("Obsidian", 0.02f, 14f, F(RockTypes.Obsidian, "Chunk", "Shard"))),
					Ds(D(Ground.Flagstone, 32f, 4f), D(Ground.Lava, 96f, 5f),
						D(Ground.Cinder, 64f, 2f, -1f, -1f, Outcrops("Sulphur crystals", 0.015f, 10f, F(RockTypes.Sulphur, "Crystals")), Plants("LichenLava", 1f, 128))),
					C(Ground.Basalt), Ground.Basalt, Ground.Basalt),

				// ── Alpine (tier 7) ──
				// Fellfield above the trees: map lichen on the rock, moss campion cushions and alpine flowers in the gravel,
				// krummholz mountain pine, sastrugi on the snow patches.
				E("Alpine", L(Ground.Rock, Boulders(0.3f, 6f, GreyRound), Stones(SmallRocks("Grey"), 4f),
						Blocks("Schist", 0.06f, 10f, F(RockTypes.Schist, "Lump", "Ridge", "Flags")), Blocks("Gneiss", 0.03f, 12f, F(RockTypes.Gneiss, "Boulder")), Plants("LichenClump", 2f)),
					Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Bush("MountainPine", 0.3f, 0.8f, 40f), Plants("CushionMossCampion", 3f), Plants("FlowersAlpine", 2f, 180)),
						D(Ground.Snow, 64f, 2f, -1f, -1f, Grass("GrassTuft", 5f), Aligned(Blocks("Sastrugi", 0.03f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f))),
					C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel),
				// Subalpine meadow at the treeline: gentians and edelweiss, lupin and globeflower, alpenrose; larch joins the
				// stunted spruce (one rule, so they share the treeline woods); sedge in the wet hollows.
				E("Alpine Meadow", L(Ground.GrassMeadow, Grass("GrassLush", 30f), Plants("FlowersMeadow", 12f, 200), Stand(Was(0.05f, 12f), 25f, W(0.08f, 0.15f, open: 0.02f, scale: 0.65f), "Spruce", "Larch"), Bush("MountainPine", 0.4f), Bush("Juniper", 0.2f, 0.7f),
						Plants("FlowersAlpine", 8f, 200), Bush("Rhododendron", 0.4f, 0.35f), Plants("FlowersTall", 2f, 180)),
					Ds(D(Ground.Rock, 48f, 3f, 25f, 90f, Boulders(0.08f, 8f, GreyRound), Blocks("Gneiss", 0.03f, 12f, F(RockTypes.Gneiss, "Boulder", "Block"))), D(Ground.Moss, 64f, 2f, -1f, -1f, Grass("GrassSedge", 15f))),
					C(Ground.Rock), Ground.Gravel, Ground.Gravel)
					.Moved("main", "Trees: Spruce"),
				// Great Basin / Colorado Plateau: open pinyon woodland where it is mild, sagebrush and rabbitbrush steppe with
				// feather grass, yucca in the warmer, spiny cushions on the gravel.
				E("High Desert", L(Ground.Sand, Plants("ShrubDry", 6f), Grass("GrassTuft", 6f), Bush("Sagebrush", 2f), Bush("Juniper", 0.05f, 0.8f),
						Band(Stand(Was(0.1f, 10f), 30f, W(0.15f, 0.3f, open: 0.1f), "Pinyon"), 0.05f, 0.65f), Bush("Rabbitbrush", 1f), Grass("GrassFeather", 10f), Band(Plants("RosetteYucca", 0.5f), 0.25f, 1f)),
					Ds(D(Ground.Gravel, 64f, 2f, -1f, -1f, Plants("CushionSpiny", 1.5f)), D(Ground.Sandstone, 48f, 3f, 20f, 90f, Boulders(0.1f, 8f, Sandstone),
						Outcrops("Sandstone pedestals", 0.03f, 16f, F(RockTypes.Sandstone, "Pedestal", "Ledges"), 35f), Blocks("Sandstone beds", 0.03f, 14f, F(RockTypes.Sandstone, "Tilted", "Block")))),
					C(Ground.Sandstone), Ground.Gravel, Ground.Gravel),
				// Talus: scree specialists very sparse (alpine poppy, purple saxifrage, moss campion), lichen-crusted blocks.
				E("Scree", L(Ground.Gravel, Stones(SmallRocks("Grey"), 10f), Stones(Pebbles("Grey"), 15f), Bush("MountainPine", 0.05f, 0.8f, 40f),
						Blocks("Slate scree", 0.06f, 10f, F(RockTypes.Slate, "Scree")), Blocks("Quartzite scree", 0.04f, 12f, F(RockTypes.Quartzite, "Scree")),
						Plants("FlowersAlpine", 0.3f, 160), Plants("CushionMossCampion", 0.4f)),
					Ds(D(Ground.Rock, 48f, 3f, 30f, 90f, Boulders(0.4f, 5f, Grey), Plants("LichenClump", 0.5f, 160, 60f)), D(Ground.Pebbles, 32f)),
					C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel),
				// Maxwell Montes: radar-bright metal frost with pyrite crystals, tessera fins in ridged fields, platy basalt slabs.
				E("Sulphuric Cloud Deck", L(Ground.Sulphur, Stones(SmallRocks("Basalt"), 1f)),
					Ds(D(Ground.Basalt, 64f, 2f, -1f, -1f, Outcrops("Tessera fins", 0.02f, 16f, F(RockTypes.Basalt, "Fin"), 30f), Blocks("Platy slabs", 0.04f, 10f, F(RockTypes.Basalt, "Slabs"), 20f)),
						D(Ground.Ash, 96f),
						D(Ground.MetalFrost, 96f, 2f, -1f, -1f, Outcrops("Pyrite", 0.012f, 14f, F(RockTypes.Pyrite, "Cubes")))),
					C(Ground.Basalt), Ground.Sulphur, Ground.Sulphur),
				// Warm dry volcanic ground (Canaries, Hawaiʻi): lava lichen on the flows, euphorbia and dragon trees where it is
				// warm, ropy pahoehoe, cinder fields with scoria cones and bombs, sulphur fumaroles in the ash.
				E("Volcanic", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 3f), Boulders(0.2f, 8f, BasaltRocks), Trees(0.02f, 20f, 25f, "Dead"),
						Outcrops("Basalt columns", 0.05f, 16f, F(RockTypes.Basalt, "Columns")), Blocks("Basalt causeway", 0.02f, 24f, F(RockTypes.Basalt, "Causeway", "Fallen"), 15f),
						Plants("LichenLava", 3f), Band(Plants("Euphorbia", 0.3f), 0.4f, 1f), Band(Trees(0.01f, 15f, 20f, "DragonTree"), 0.45f, 1f)),
					Ds(D(Ground.Ash, 64f, 2f, -1f, -1f, Blocks("Pumice", 0.06f, 10f, F(RockTypes.Pumice, "Lump", "Raft")), Outcrops("Tuff chimneys", 0.015f, 30f, F(RockTypes.Tuff, "Chimney"), 15f),
							Outcrops("Sulphur cones", 0.006f, 30f, F(RockTypes.Sulphur, "Cone"), 20f)),
						D(Ground.Lava, 128f, 5f),
						D(Ground.Cinder, 64f, 2f, -1f, -1f, Outcrops("Scoria cones", 0.01f, 40f, F(RockTypes.Scoria, "Cone"), 20f), Blocks("Scoria bombs", 0.03f, 10f, F(RockTypes.Scoria, "Bomb", "Lump"))),
						D(Ground.Pahoehoe, 96f, 3f, 0f, 15f, Plants("LichenLava", 2f))),
					C(Ground.Basalt), Ground.Basalt, Ground.Basalt),
				// Eisriesenwelt, floor only: ice stalagmites and columns, giant hoar-frost crystals, ice pebbles.
				E("Ice Cave", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.04f, 10f, "Calved", "Rounded"), Outcrops("Ice stalagmites", 0.05f, 6f, F(RockTypes.WaterIce, "Stalagmite")), Stones(Pebbles("Ice"), 3f)),
					Ds(D(Ground.Snow, 48f), D(Ground.Frost, 64f, 2f, -1f, -1f, Outcrops("Hoar crystals", 0.03f, 6f, F(RockTypes.WaterIce, "Crystal")))),
					C(Ground.Ice, 35f), Ground.Ice, Ground.Ice),
				// A collapsed ice city: ice rubble, broken ice pillars, drifted snow in sastrugi, rime on the old flagstones.
				E("Ice Ruin", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.03f, 10f, "Calved", "Slab"), Outcrops("Ice pillars", 0.02f, 8f, F(RockTypes.WaterIce, "Stalagmite"))),
					Ds(D(Ground.Snow, 48f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.04f, 6f, F(RockTypes.Firn, "Sastrugi"), 15f), -20f, 20f)),
						D(Ground.Flagstone, 32f, 4f, -1f, -1f, Outcrops("Rime crystals", 0.015f, 8f, F(RockTypes.WaterIce, "Crystal")))),
					C(Ground.Ice), Ground.Ice, Ground.Ice),

				// ── Nival (tier 8) ──
				// Valley glacier: icefall seracs, glacier tables and dirt cones on the ablation ice, sastrugi on the snow and
				// penitentes where the air is dry, moraine stones and boulders in stripes of gravel.
				E("Glacier", L(Ground.Ice, Stones(SmallRocks("Grey"), 0.5f), Seracs(0.02f, 25f), IceBlocks(0.05f, 12f, "Calved", "Slab"),
						Outcrops("Glacier tables", 0.005f, 30f, F(RockTypes.WaterIce, "GlacierTable"), 15f), Outcrops("Dirt cones", 0.005f, 30f, F(RockTypes.WaterIce, "DirtCone"), 15f)),
					Ds(D(Ground.Snow, 48f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.04f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f),
							Band(Aligned(Outcrops("Penitentes", 0.01f, 20f, F(RockTypes.Firn, "Penitentes"), 20f), -10f, 10f), -1f, 1f, -1f, -0.2f)),
						D(Ground.Rock, 48f, 3f, 30f, 90f),
						D(Ground.Gravel, 32f, 4f, 0f, 25f, Stones(SmallRocks("Grey"), 4f), Boulders(0.1f, 8f, GreyRound))),
					C(Ground.Ice, 38f), Ground.Ice, Ground.Ice),
				// Ice cap and summit firn: dense sastrugi and penitente fields on the snow, rime frost-flowers, nunatak rock on the steeps.
				E("Permanent Ice", L(Ground.Ice, IceBlocks(0.02f, 16f, "Rounded", "Calved")),
					Ds(D(Ground.Snow, 64f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.05f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f),
							Aligned(Outcrops("Penitentes", 0.008f, 20f, F(RockTypes.Firn, "Penitentes"), 20f), -10f, 10f)),
						D(Ground.Frost, 96f, 2f, -1f, -1f, Blocks("Rime", 0.02f, 10f, F(RockTypes.WaterIce, "FrostFlowers"), 30f)),
						D(Ground.Rock, 48f, 3f, 30f, 90f, Stones(SmallRocks("Grey"), 1f))),
					C(Ground.Ice), Ground.Ice, Ground.Ice),
				// Ice Sheet (any tier, 2026-10-10): land buried under ice that never thaws. Wind-packed snow over glacier ice, blue ice where the wind scours it bare, nunatak rock breaking through on steep ground, ice cliffs at the calving front, a few calved blocks below them. Nothing grows
				// but crustose lichen on the nunataks; sastrugi ridge the snow, and the wind-scoured blue ice keeps the odd meteorite.
				E("Ice Sheet", L(Ground.Snow, IceBlocks(0.01f, 24f, "Calved", "Slab"), Aligned(Blocks("Sastrugi", 0.05f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f)),
					Ds(D(Ground.Ice, 72f, 2f, -1f, -1f, Stones(SmallRocks("Basalt"), 0.05f)), D(Ground.Frost, 96f), D(Ground.Rock, 48f, 3f, 30f, 90f, Plants("LichenClump", 0.3f, 160, 60f))),
					C(Ground.Ice, 38f), Ground.Ice, Ground.Ice),
				// Polar-desert lowland: sastrugi, granite and gneiss erratics, the odd rounded ice block, frost-shattered stones
				// on the rock. (Granite outnumbers gneiss in the formations, so the cliffs stay granite: CliffRocks.BedrockOf.)
				E("Snow", L(Ground.Snow, Boulders(0.03f, 12f, GreyRound), Aligned(Blocks("Sastrugi", 0.04f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f), IceBlocks(0.005f, 24f, "Rounded"),
						Blocks("Granite erratics", 0.01f, 24f, F(RockTypes.Granite, "Corestone", "Split")), Blocks("Gneiss erratics", 0.008f, 24f, F(RockTypes.Gneiss, "Boulder"))),
					Ds(D(Ground.Ice, 96f), D(Ground.Rock, 48f, 3f, 30f, 90f, Stones(SmallRocks("Grey"), 3f))),
					C(Ground.Rock), Ground.Ice, Ground.Ice),
				// Frozen architecture: large prismatic ice crystals and ice columns round the structures, drifts in sastrugi, a few blocks.
				E("Ice Palace", L(Ground.Ice, With(Outcrops("Ice crystals", 0.02f, 10f, F(RockTypes.WaterIce, "Crystal")), r => r.Scale = new Vector2(1.5f, 3f)),
						Outcrops("Ice columns", 0.015f, 10f, F(RockTypes.WaterIce, "Stalagmite"))),
					Ds(D(Ground.Snow, 48f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.03f, 6f, F(RockTypes.Firn, "Sastrugi"), 15f), -20f, 20f), IceBlocks(0.01f, 14f, "Rounded")),
						D(Ground.Flagstone, 32f, 4f)),
					C(Ground.Ice), Ground.Ice, Ground.Ice),
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

			// A helper's rule (or layer) with a setting or two changed on it alone, not on its Earlier versions: what
			// the helper's grouping gets wrong for one thing, such as a 14 m crater rim packed at a boulder's 4 m, or a
			// boulder half-buried in dust. The rule is new, so it has no older form of its own to keep.
			static T With<T>(T value, Action<T> change)
			{
				change(value);
				return value;
			}
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
