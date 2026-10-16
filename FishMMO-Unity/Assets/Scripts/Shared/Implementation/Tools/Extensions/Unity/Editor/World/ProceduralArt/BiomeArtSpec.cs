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
	/// keep the entries short, but each entry is still its biome's own decision, written in its own
	/// file (BiomeArtSpec/BiomeArtSpec.&lt;Biome&gt;.cs; see the notes at <see cref="Entries"/>).
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
	public static partial class BiomeArtSpec
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
			/// stood in sandstone cliffs and boulders, Baoakraal's karst and grassland in conglomerate (2026-10-08, who
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

			// ── Ways, river bars and point-of-interest growth ─────────
			//
			// Columns the authoring tool writes onto the template (BiomeArtAuthoring), which the scene cut then reads alone.
			// Unset, each is what the cut decided in code before the template carried it (2026-10-15), so authoring them
			// changed no scene: a biome's file sets one (Ways, Bars) to give the biome its own.

			private string path, road, cobbles, barSand, barGravel;

			/// <summary>
			/// The ground the biome is painted with: its main layer's family, else its first detail layer's, else grass.
			/// What its ways wear to by default (<see cref="PathGround"/>, <see cref="RoadGround"/>).
			/// </summary>
			public string PaintedGround
			{
				get
				{
					if (!string.IsNullOrEmpty(Main?.Family))
					{
						return Main.Family;
					}
					foreach (Layer d in Details)
					{
						if (!string.IsNullOrEmpty(d?.Family))
						{
							return d.Family;
						}
					}
					return Ground.Grass;
				}
			}

			/// <summary>
			/// The ground a footpath or trail wears to in this biome (the template's Small Path layer): by default what its
			/// painted ground wears to (<see cref="SurfaceCatalogue.PathGroundFor"/>: loam through grass, a littered track
			/// through forest, packed sand through desert, trampled snow over ice).
			/// </summary>
			public string PathGround => path ?? SurfaceCatalogue.PathGroundFor(PaintedGround);

			/// <summary>The ground a road or cart track wears to (the Road layer): by default <see cref="SurfaceCatalogue.RoadGroundFor"/>.</summary>
			public string RoadGround => road ?? SurfaceCatalogue.RoadGroundFor(PaintedGround);

			/// <summary>The paving of a way's cobbled stretches (the Cobbles layer): by default the flagstones every biome shared.</summary>
			public string CobbleGround => cobbles ?? Ground.Flagstone;

			/// <summary>
			/// The sand a river lays on its slow bars here (the Bar Sand layer): by default the generated sand every biome's
			/// bars were painted with, borrowed from the desert's riverbed until the template carried it.
			/// </summary>
			public string BarSand => barSand ?? Ground.Sand;

			/// <summary>The gravel on a river's quick bars here (the Bar Gravel layer): by default the forest riverbed's gravel every biome borrowed.</summary>
			public string BarGravel => barGravel ?? Ground.Gravel;

			/// <summary>Gives the biome its own way grounds (<see cref="PathGround"/>, <see cref="RoadGround"/>, <see cref="CobbleGround"/>); null keeps a default.</summary>
			public Entry Ways(string path = null, string road = null, string cobbles = null)
			{
				this.path = path ?? this.path;
				this.road = road ?? this.road;
				this.cobbles = cobbles ?? this.cobbles;
				return this;
			}

			/// <summary>Gives the biome its own river bars (<see cref="BarSand"/>, <see cref="BarGravel"/>); null keeps a default.</summary>
			public Entry Bars(string sand = null, string gravel = null)
			{
				barSand = sand ?? barSand;
				barGravel = gravel ?? barGravel;
				return this;
			}

			// ── Water role and cave shell (2026-10-15, 2026-10-16) ────
			//
			// Two more decisions the cut made in code, written onto the template by the authoring: which biomes the scene's
			// water and ice take (found by name before: Lake, Methane Lake, River, Estuary, Salt Flat, Ice Sheet, whose files
			// name their role with Water), and the rock a cave is cut in (by the cave's form alone before, the same for every
			// biome: DefaultCaveShell; a biome's file gives one form its own with CaveShell). A cave's formations are its
			// shell's rock (2026-10-16), so the shell is the one value.

			/// <summary>The part the biome plays for the scene's water and ice (<see cref="BiomeTemplate.WaterRole"/>); None unless its file names one.</summary>
			public BiomeWaterRole WaterRole = BiomeWaterRole.None;

			private readonly List<(CaveForm Form, string Rock)> caveShells = new List<(CaveForm, string)>();

			/// <summary>Names the biome's water role (<see cref="WaterRole"/>).</summary>
			public Entry Water(BiomeWaterRole role)
			{
				WaterRole = role;
				return this;
			}

			/// <summary>
			/// Gives caves of one form carved in this biome their own shell rock (a rock type name, Ice, or
			/// <see cref="BiomeCaveShell.Ground"/>) in place of the form's (<see cref="DefaultCaveShell"/>).
			/// </summary>
			public Entry CaveShell(CaveForm form, string rock)
			{
				caveShells.RemoveAll(c => c.Form == form);
				caveShells.Add((form, rock));
				return this;
			}

			/// <summary>The rock a cave of a form carved in this biome is cut in: the biome's own for the form, else the form's.</summary>
			public string CaveShellFor(CaveForm form)
			{
				foreach ((CaveForm f, string rock) in caveShells)
				{
					if (f == form)
					{
						return rock;
					}
				}
				return DefaultCaveShell(form);
			}

			/// <summary>
			/// Every cave form with its shell here (<see cref="CaveShellFor"/>), in the forms' order: what the authoring writes to
			/// the template's Cave Shells.
			/// </summary>
			public List<BiomeCaveShell> CaveShells()
			{
				var shells = new List<BiomeCaveShell>();
				foreach (CaveForm form in (CaveForm[])Enum.GetValues(typeof(CaveForm)))
				{
					shells.Add(new BiomeCaveShell((BiomeCaveForm)form, CaveShellFor(form)));
				}
				return shells;
			}

			/// <summary>
			/// The flowers a herb grove grows between its beds here (the template's Herb Grove Flowers): the flower details
			/// the biome's own rules plant, in rule order, else a meadow's.
			/// </summary>
			public List<string> GroveFlowers()
			{
				var flowers = new List<string>();
				foreach ((Layer _, Scatter rule) in Rules())
				{
					if (rule.Channel != PrefabSpawnChannel.DetailLayer || rule.Prefabs == null)
					{
						continue;
					}
					foreach (string prefab in rule.Prefabs)
					{
						if (!string.IsNullOrEmpty(prefab) && prefab.StartsWith(GroveFlowerPrefix, StringComparison.Ordinal) && !flowers.Contains(prefab))
						{
							flowers.Add(prefab);
						}
					}
				}
				if (flowers.Count == 0)
				{
					flowers.Add(MeadowFlowers);
				}
				return flowers;
			}

			/// <summary>
			/// What grows and lies round a natural cave's mouth here (the template's Cave Mouth lists): the bushes, ferns,
			/// boulders and deadwood the biome's own rules scatter, each in rule order, a woodland's ferns and bushes where it
			/// has none, and ivy as the creeper.
			/// </summary>
			public CaveMouthGrowth CaveMouth()
			{
				var growth = new CaveMouthGrowth();
				foreach ((Layer _, Scatter rule) in Rules())
				{
					if (rule.Prefabs == null)
					{
						continue;
					}
					foreach (string prefab in rule.Prefabs)
					{
						if (string.IsNullOrEmpty(prefab))
						{
							continue;
						}
						List<string> into = prefab.StartsWith(ProceduralArtCatalogue.BushPrefix, StringComparison.Ordinal) ? growth.Bushes
							: prefab.StartsWith("Detail_Fern", StringComparison.Ordinal) ? growth.Ferns
							: prefab.StartsWith("Boulder_", StringComparison.Ordinal) ? growth.Boulders
							: prefab.StartsWith(ProceduralArtCatalogue.DeadwoodPrefix, StringComparison.Ordinal) ? growth.Logs
							: null;
						if (into != null && !into.Contains(prefab))
						{
							into.Add(prefab);
						}
					}
				}
				if (growth.Ferns.Count == 0)
				{
					growth.Ferns.AddRange(WoodlandFerns);
				}
				if (growth.Bushes.Count == 0)
				{
					growth.Bushes.AddRange(WoodlandBushes);
				}
				growth.Creeper = CaveCreeper;
				return growth;
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

			// ── Living looks and blade grass (2026-10-15) ─────────────
			//
			// Columns the authoring writes onto the template (BiomeTemplate.LookClass, BiomeTemplate.GrassTypes), which the
			// client then reads alone. Every biome's file names its look class: the class BiomeLooks.ClassOf read from its
			// name and climate before, so authoring changed no biome's looks (BiomeContentCoverageTests pins it against the
			// assets). Blade looks default to the one old look of each prefab (BiomeGrassDefaults); a biome's file tunes its
			// own with Blades.

			/// <summary>The theme the biome's living things are coloured from (<see cref="BiomeTemplate.LookClass"/>); Unset until its file names one.</summary>
			public BiomeLookClass LookClass = BiomeLookClass.Unset;

			private readonly List<(string Prefab, Action<BiomeGrassType> Tune)> bladeTunings = new List<(string, Action<BiomeGrassType>)>();

			/// <summary>Names the biome's look class (<see cref="LookClass"/>).</summary>
			public Entry Look(BiomeLookClass lookClass)
			{
				LookClass = lookClass;
				return this;
			}

			/// <summary>
			/// Tunes how one of the biome's blade details looks here (by detail name, e.g. <c>"GrassLush"</c>): applied over the
			/// old look (<see cref="BiomeGrassDefaults"/>) when the authoring writes the biome's <see cref="BiomeTemplate.GrassTypes"/>.
			/// Several calls for one detail apply in order.
			/// </summary>
			public Entry Blades(string detail, Action<BiomeGrassType> tune)
			{
				bladeTunings.Add((Det(detail), tune));
				return this;
			}

			/// <summary>
			/// Every blade prefab the biome's rules grow (prefab names drawn as blades by default, <see cref="BiomeGrassDefaults.IsBlade"/>),
			/// in rule order, each with its look here: the old look of the prefab, then this biome's <see cref="Blades"/> tunings.
			/// Prefab unset (the authoring resolves it).
			/// </summary>
			public List<(string Prefab, BiomeGrassType Look)> GrassTypes()
			{
				var looks = new List<(string Prefab, BiomeGrassType Look)>();
				var seen = new HashSet<string>(StringComparer.Ordinal);
				foreach ((Layer _, Scatter rule) in Rules())
				{
					if (rule.Channel != PrefabSpawnChannel.DetailLayer || rule.Prefabs == null)
					{
						continue;
					}
					foreach (string prefab in rule.Prefabs)
					{
						if (BiomeGrassDefaults.IsBlade(prefab) && seen.Add(prefab))
						{
							looks.Add((prefab, LookOf(prefab)));
						}
					}
				}
				return looks;
			}

			/// <summary>The biome's look of one blade prefab (by name): the old look, then its tunings; null for a prefab that is no blade.</summary>
			public BiomeGrassType LookOf(string prefab)
			{
				BiomeGrassType look = BiomeGrassDefaults.For(prefab);
				if (look == null)
				{
					return null;
				}
				foreach ((string Prefab, Action<BiomeGrassType> Tune) tuning in bladeTunings)
				{
					if (tuning.Prefab == prefab)
					{
						tuning.Tune(look);
					}
				}
				return look;
			}

			// ── Wildlife, vines and harvest (2026-10-16) ──────────────
			//
			// Columns the authoring writes onto the template (BiomeTemplate.Habitat, SeaLifeHomes, Vines, GatheringResources,
			// HerbGroveHerbs), which the client and the cut then read alone. Each is what was decided by the biome's name
			// before, so authoring them changed nothing (BiomeTemplateLivingTests pins every biome against the old rules):
			// the creatures' country every biome's file names with Wildlife (the client's keyword reading of the name, and alien
			// ground under another world's air); the sea creatures a sea biome's file names with HomeTo (the kinds that named
			// it as a favourite); the vines from the vine table's row (VineBiomes, by the biome's name) unless the file sets its
			// own with GrowsVines or NoVines; the gathering resources from the catalogue's rows that name the biome
			// (GatheringResourceCatalogue) unless the file sets its own with Gathers; and a herb grove's herbs from those
			// resources unless the file sets its own with Herbs.

			/// <summary>The country the biome offers ambient creatures (<see cref="BiomeTemplate.Habitat"/>); None until its file names one.</summary>
			public BiomeHabitat Habitat = BiomeHabitat.None;

			private string[] seaLifeHomes, gathers, herbs;
			private BiomeVines vines;

			/// <summary>Names the country the biome offers ambient creatures (<see cref="Habitat"/>).</summary>
			public Entry Wildlife(BiomeHabitat habitat)
			{
				Habitat = habitat;
				return this;
			}

			/// <summary>Names the sea creatures (the sea life's kind names) most at home in the biome's water (<see cref="SeaLifeHomes"/>).</summary>
			public Entry HomeTo(params string[] seaKinds)
			{
				seaLifeHomes = seaKinds ?? Array.Empty<string>();
				return this;
			}

			/// <summary>The sea creatures most at home in the biome's water (<see cref="BiomeTemplate.SeaLifeHomes"/>); none unless its file names some.</summary>
			public List<string> SeaLifeHomes() => new List<string>(seaLifeHomes ?? Array.Empty<string>());

			/// <summary>
			/// Gives the biome its own vines in place of the vine table's row: how thickly they grow at a cave's mouth, on old
			/// walls, on cliff crags and on trees (0..1), and the species with their weights.
			/// </summary>
			public Entry GrowsVines(float caves, float ruins, float cliffs, float trees, params (string Species, float Weight)[] species)
			{
				vines = new BiomeVines { Caves = caves, Ruins = ruins, Cliffs = cliffs, Trees = trees };
				foreach ((string s, float w) in species ?? Array.Empty<(string, float)>())
				{
					vines.Species.Add(new BiomeVineSpecies(s, w));
				}
				return this;
			}

			/// <summary>Grows no vines here, whatever the vine table says.</summary>
			public Entry NoVines()
			{
				vines = new BiomeVines();
				return this;
			}

			/// <summary>
			/// The biome's vines (<see cref="BiomeTemplate.Vines"/>): its file's own (<see cref="GrowsVines"/>), else the vine
			/// table's row for the biome's name (<see cref="VineBiomes"/>), else none. A fresh copy each call.
			/// </summary>
			public BiomeVines VineGrowth()
			{
				if (vines != null)
				{
					return vines.Clone();
				}
				var growth = new BiomeVines();
				VineBiomes.Habitat row = VineBiomes.For(Biome);
				if (row != null)
				{
					growth.Caves = row.Caves;
					growth.Ruins = row.Ruins;
					growth.Cliffs = row.Cliffs;
					growth.Trees = row.Trees;
					foreach ((string species, float weight) in row.Species)
					{
						growth.Species.Add(new BiomeVineSpecies(species, weight));
					}
				}
				return growth;
			}

			/// <summary>Gives the biome its own gathering resources (the catalogue's node names) in place of the catalogue's.</summary>
			public Entry Gathers(params string[] nodes)
			{
				gathers = nodes ?? Array.Empty<string>();
				return this;
			}

			/// <summary>
			/// The gathering resources the biome's sites draw from (<see cref="BiomeTemplate.GatheringResources"/>): its file's
			/// own (<see cref="Gathers"/>), else every catalogue resource that grows here
			/// (<see cref="GatheringResourceCatalogue.GrowsIn"/>: one that names the biome, or names none), in the catalogue's order.
			/// </summary>
			public List<string> GatheringResources()
			{
				if (gathers != null)
				{
					return new List<string>(gathers);
				}
				var nodes = new List<string>();
				foreach (GatheringResource resource in GatheringResourceCatalogue.All)
				{
					if (GatheringResourceCatalogue.GrowsIn(resource, Biome) && !nodes.Contains(resource.Node))
					{
						nodes.Add(resource.Node);
					}
				}
				return nodes;
			}

			/// <summary>Gives a herb grove here its own herbs (detail prefab names) in place of the gathered herbs' kin.</summary>
			public Entry Herbs(params string[] prefabs)
			{
				herbs = prefabs ?? Array.Empty<string>();
				return this;
			}

			/// <summary>
			/// The herbs a herb grove grows its beds of here (<see cref="BiomeTemplate.HerbGroveHerbs"/>): its file's own
			/// (<see cref="Herbs"/>), else the wild kin of the herbs and fungi gathered here (<see cref="HerbsOf"/>).
			/// </summary>
			public List<string> GroveHerbs() => herbs != null ? new List<string>(herbs) : HerbsOf(GatheringResources());
		}

		/// <summary>The herbs a herb grove grows where no herb or fungus is gathered: the commonest wayside herbs.</summary>
		public static string[] CommonHerbs => new[] { "Detail_BroadHerb", "Detail_HerbRuderal" };

		/// <summary>
		/// A herb grove's herbs where these gathering resources (node names) are offered: the detail prefabs the catalogue's
		/// herbs and fungi among them show as, in the catalogue's order, else <see cref="CommonHerbs"/>. What the grove grew
		/// from the biome's name until 2026-10-16 (PointOfInterestHerbGrove.HerbsFor), from the biome's resources now.
		/// </summary>
		public static List<string> HerbsOf(IReadOnlyCollection<string> offered)
		{
			var herbs = new List<string>();
			foreach (GatheringResource resource in GatheringResourceCatalogue.All)
			{
				if ((resource.Family == GatheringFamily.Herb || resource.Family == GatheringFamily.Fungus)
					&& GatheringResourceCatalogue.Offers(offered, resource)
					&& !string.IsNullOrEmpty(resource.Visual)
					&& resource.Visual.StartsWith(ProceduralArtCatalogue.DetailPrefab(string.Empty), StringComparison.Ordinal)
					&& !herbs.Contains(resource.Visual))
				{
					herbs.Add(resource.Visual);
				}
			}
			if (herbs.Count == 0)
			{
				herbs.AddRange(CommonHerbs);
			}
			return herbs;
		}

		/// <summary>What grows and lies round a natural cave's mouth in a biome (<see cref="Entry.CaveMouth"/>), by prefab name.</summary>
		public sealed class CaveMouthGrowth
		{
			public readonly List<string> Bushes = new List<string>();
			public readonly List<string> Ferns = new List<string>();
			public readonly List<string> Boulders = new List<string>();
			public readonly List<string> Logs = new List<string>();
			/// <summary>The creeper grown up to the rock, or null for none.</summary>
			public string Creeper;
		}

		/// <summary>The prefix of the flower details a herb grove takes from a biome's rules (<see cref="Entry.GroveFlowers"/>).</summary>
		public const string GroveFlowerPrefix = "Detail_Flowers";
		/// <summary>A herb grove's flowers in a biome whose rules plant none.</summary>
		public const string MeadowFlowers = "Detail_FlowersMeadow";
		/// <summary>The creeper round every cave mouth.</summary>
		public const string CaveCreeper = "Detail_Ivy";
		/// <summary>A cave mouth's ferns in a biome whose rules plant none.</summary>
		public static string[] WoodlandFerns => new[] { "Detail_FernBracken", "Detail_Fern", "Detail_FernHartstongue" };
		/// <summary>A cave mouth's bushes in a biome whose rules plant none.</summary>
		public static string[] WoodlandBushes => new[] { ProceduralArtCatalogue.BushPrefix + "Hazel", ProceduralArtCatalogue.BushPrefix + "Bramble" };

		/// <summary>
		/// The rock a cave of a form is cut in, whatever the biome: the rule the cave shaper kept by form until 2026-10-16
		/// (CaveShaper.RockOf: basalt for a lava tube, glacier ice for an ice cave, the ground's own rock by the biome's rock
		/// rule for a cave, grotto, sea cave or mine). Every biome's default (<see cref="Entry.CaveShellFor"/>).
		/// </summary>
		public static string DefaultCaveShell(CaveForm form)
		{
			switch (form)
			{
				case CaveForm.LavaTube:
					return "Basalt";
				case CaveForm.IceCave:
					return CliffRocks.Ice;
				default:
					return BiomeCaveShell.Ground;
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

		/// <summary>
		/// A helper's rule (or layer) with a setting or two changed on it alone, not on its Earlier versions: what
		/// the helper's grouping gets wrong for one thing, such as a 14 m crater rim packed at a boulder's 4 m, or a
		/// boulder half-buried in dust. The rule is new, so it has no older form of its own to keep.
		/// </summary>
		private static T With<T>(T value, Action<T> change)
		{
			change(value);
			return value;
		}

		// ── The table ─────────────────────────────────────────────────
		//
		// Each biome's entry is written in its own file, BiomeArtSpec/BiomeArtSpec.<Biome>.cs (the asset's name without
		// its spaces), as one method of this partial class named the same: everything the spec says about one biome in
		// one place, so a biome is read, reviewed and changed without touching any other (2026-10-15). The helpers
		// above and the geology sets below stay here, shared by every biome.
		//
		// Build lists those methods explicitly, in the table's order, never by reflection: the entries' order is the
		// order the authoring tool walks them in and the order a scene's palette meets them in, so it must not depend
		// on a file name or on the compiler. A biome added gets its file and a line here, in its tier.
		//
		// A biome's file declares methods only, never a static field. Entries is initialised before any static field
		// declared after it in this file, and the initialisers of a partial class's other files run in an order the
		// language does not promise, so a field read by an entry could still be unset when Build runs. Shared values
		// are written as properties or methods (the geology sets are), which are evaluated when called.
		//
		// The art generator hashes every file in that folder with this one (ProceduralArtPayload.SourceFolders), in
		// the shared core: an edit to any biome's entry rebuilds the art, as an edit to the table did.

		/// <summary>Every biome's entry, keyed by the biome asset's name.</summary>
		public static readonly Entry[] Entries = Build();

		private static Entry[] Build()
		{
			return new[]
			{
				// ── Under the sea (tiers 0–2) ──
				// What lives on the sea floor rides on the lakebed (Entry.Bed), by depth: kelp and seaweed where
				// the light reaches, corals in warm shallows, sponges and sea fans below, little in the abyss.
				AbyssalPlain(),
				Abyss(),
				DeepOcean(),
				Ocean(),
				Seamount(),
				UnderwaterCanyon(),
				CoastalWater(),
				CoralReef(),
				SubsurfaceOceanVent(),
				MethaneLake(),
				LavaTube(),

				// ── Shore (tier 3) ──
				Beach(),
				DustSea(),
				Estuary(),
				ImpactBasin(),
				Mangrove(),
				MoltenSurface(),
				PeatBog(),
				Rille(),
				RockyCoast(),
				TidalFracture(),

				IceShelf(),

				// ── Lowland (tier 4) ──
				CryovolcanicPlain(),
				Grassland(),
				Oasis(),
				Plains(),
				RadiationPlain(),
				RegolithPlain(),
				RunawayGreenhousePlain(),
				SaltFlat(),
				Scrubland(),
				SulphurFlats(),
				TholinPlain(),
				Wasteland(),
				Wetlands(),
				Farmland(),
				Lake(),
				River(),
				Swamp(),
				SwampCave(),
				SwampRuins(),
				SwampTemple(),
				JungleRuins(),
				JungleTemple(),

				// ── Highland (tier 5) ──
				BambooForest(),
				Forest(),
				GeyserBasin(),
				Hills(),
				IceGeyserField(),
				Jungle(),
				Karst(),
				NitrogenIceField(),
				Savanna(),
				Steppe(),
				Taiga(),
				Valley(),
				Woodland(),
				Castle(),
				MarblePalace(),
				Cave(),
				ForestRuins(),

				// ── Mountain (tier 6) ──
				Badlands(),
				Desert(),
				MountainSlope(),
				RockyTerrain(),
				Tundra(),
				Crater(),
				Fortress(),
				VolcanicCave(),
				VolcanicTemple(),

				// ── Alpine (tier 7) ──
				Alpine(),
				AlpineMeadow(),
				HighDesert(),
				Scree(),
				SulphuricCloudDeck(),
				Volcanic(),
				IceCave(),
				IceRuin(),

				// ── Nival (tier 8) ──
				Glacier(),
				PermanentIce(),
				IceSheet(),
				Snow(),
				IcePalace(),
			};
		}

		// ── Geology sets ──────────────────────────────────────────────
		//
		// The planet-geology rocks a biome accepts for its cliffs, river boulders and fall ledges besides its own
		// (Entry.Accepts, Entry.Rocks), shared by the biomes that take the same. A biome that accepts none takes only its
		// own: the sea floors, ice, the airless and alien ground, whose own rock already says what they are.
		//
		// Chosen by climate, as the ground looks rather than as rock is laid down: a humid green biome takes the grey
		// crystalline and fine-bedded rocks (granite, gneiss, schist, slate, shale, limestone), a dry one the sandstones
		// and conglomerate beside them, a volcanic one its lavas and tuff. Sandstone and conglomerate stand only where
		// the ground is dry or the biome names them, so a grassland, forest, taiga or bog is never walled in them. A biome
		// whose rocks are its own choice names them in its entry instead.
		//
		// Properties, not fields: see the table's notes on static fields.

		/// <summary>Humid temperate ground: crystalline rock and the fine-bedded sediments.</summary>
		private static RockType[] TemperateGeology => new[] { RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist, RockTypes.Slate, RockTypes.Shale, RockTypes.Limestone };

		/// <summary>Lowland farm and grass country: the soft sediments it is usually on, and granite.</summary>
		private static RockType[] LowlandGeology => new[] { RockTypes.Granite, RockTypes.Shale, RockTypes.Limestone, RockTypes.Chalk };

		/// <summary>Cold ground: crystalline shield rock.</summary>
		private static RockType[] BorealGeology => new[] { RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist, RockTypes.Slate };

		/// <summary>Mountains: crystalline and metamorphic rock.</summary>
		private static RockType[] MountainGeology => new[] { RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist, RockTypes.Slate, RockTypes.Quartzite, RockTypes.Marble };

		/// <summary>Dry ground: the sandstones and what lies beside them.</summary>
		private static RockType[] AridGeology => new[] { RockTypes.Sandstone, RockTypes.Conglomerate, RockTypes.Shale, RockTypes.Limestone, RockTypes.Granite, RockTypes.Basalt };

		/// <summary>The humid tropics.</summary>
		private static RockType[] TropicalGeology => new[] { RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist, RockTypes.Limestone, RockTypes.Basalt };

		/// <summary>Volcanic ground.</summary>
		private static RockType[] VolcanicGeology => new[] { RockTypes.Basalt, RockTypes.Andesite, RockTypes.Tuff };

		/// <summary>Banks and shores of rivers and lakes: what the country round them is, less the sandstones.</summary>
		private static RockType[] WatersideGeology => new[] { RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist, RockTypes.Slate, RockTypes.Shale, RockTypes.Limestone, RockTypes.Basalt };

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
