#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Bakes a generated scene's grass and trees into its terrain tiles from the biome rules: detail
	/// layers and tree instances, committed with the scene like its heights.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where a rule comes from.</b> Rules live on a biome's texture layers
	/// (<see cref="TerrainTextureLayer.prefabSpawnRules"/>). A rule belongs to the palette entry
	/// that brought its layer in — one biome, one layer — and it is gated on two things at once:
	/// that layer's alphamap channel, which says the ground it grows on is there, and its own
	/// biome's reach from <see cref="SceneBiomeField.WeightsAt"/>, which says it is that biome's
	/// ground. Both matter because palette layers are shared: a grassland and a meadow painting the
	/// same grass get one channel, and without the biome gate the meadow's flowers would bloom
	/// across the whole grassland.
	/// </para>
	/// <para>
	/// <b>Shared ground is shared out, not doubled.</b> Where several biomes use the channel a rule
	/// is gated on, the rule's share of that ground is its biome's weight over the summed weight of
	/// every biome on the channel. Inside one biome that is 1; across a blend between two biomes on
	/// the same grass each one's rules fade with its weight, so the total density carries straight
	/// through the seam rather than doubling in it. A channel only one biome uses gets 1 wherever
	/// that biome reaches at all, because the splat painter has already faded the channel itself.
	/// </para>
	/// <para>
	/// <b>One candidate per cell, visited in a shuffled order.</b> Details sample the detail map's
	/// own cells; trees sample cells of <see cref="TerrainScatterOptions.TreeCellMetres"/> with the
	/// position jittered inside. Cells partition a tile exactly and adjacent tiles' cells continue
	/// one another, so nothing stands on a shared edge for two tiles to both claim. Each tile's cells
	/// are visited along a full-period stride (offset + k·stride mod n, the stride coprime with n),
	/// which is a permutation in O(1) memory: when a budget stops a rule, what it had placed is
	/// spread across the tile rather than packed into its first rows, and spacing conflicts are not
	/// all won by one corner.
	/// </para>
	/// <para>
	/// <b>Chance is density × area × weight.</b> A scattered rule's candidate passes with probability
	/// <c>densityPer100m2 / 100 × cell area × texture weight × multiplier</c>, times the biome share
	/// and the height, slope and climate bands. The cell's area is in it, so the expected count per square
	/// metre is the rule's however fine the cells are.
	/// </para>
	/// <para>
	/// <b>Groups gather, they do not add.</b> A rule with <see cref="PrefabSpawnRule.clusterMetres"/>
	/// set has its chance multiplied by <see cref="ClusterIntensity"/>, a world-space field of group
	/// centres averaging 1: patches of flowers, piles of stones and groves of trees with bare ground
	/// between, at the rule's own average density. At a detail group's heart the chance can pass 1;
	/// the excess goes into the cell's cover instead (up to a full cell), so the count still follows
	/// the field. A tree's size draw leans toward the large end at a group's heart and the small at
	/// its fringe, by <see cref="PrefabSpawnRule.clusterScaleBias"/>. Spacing still applies, so a
	/// clustered rule's spacing is how tightly its group may pack.
	/// </para>
	/// <para>
	/// <b>Forests stand in woods.</b> A tree rule with <see cref="PrefabSpawnRule.forestMetres"/> set
	/// has its chance multiplied by its <see cref="StandField"/> instead: one scene-wide field of
	/// stands hundreds of metres across, which every forest rule cuts at its own cover, with clearings,
	/// feathered edges and lone trees in the open, averaging 1 like the groups. Its biome share and
	/// the ground's steepness shrink its stands rather than thinning them. Forest rules share one
	/// canopy spacing, each tree keeping clear a ring scaled by its own crown (<see cref="ShareCanopy"/>),
	/// so two species gathered into the same wood do not grow through each other.
	/// </para>
	/// <para>
	/// <b>Carpets are coverage, not candidates.</b> A detail rule placed as a
	/// <see cref="DetailPlacement.Carpet"/> skips the draw entirely: every detail cell of its ground
	/// is written with a share of the cell, <c>carpetCoverage × ramp(texture weight) × biome share ×
	/// height, slope and climate bands × clump</c>. The ramp is a smoothstep across
	/// <see cref="PrefabSpawnRule.carpetWeightRamp"/> centred on <see cref="PrefabSpawnRule.minTextureWeight"/>,
	/// so turf thins across a texture blend instead of stopping at a line; the clump is a world-space
	/// value noise of <see cref="PrefabSpawnRule.carpetClumpMetres"/> seeded from the rule, swelling
	/// to full and thinning to <see cref="PrefabSpawnRule.carpetClumpFloor"/>, and because it is a
	/// function of world position alone it runs straight through a tile seam. Where several carpets
	/// reach one cell — lush and dry grass across a texture or biome blend — their coverages are
	/// summed first and, where the sum passes a full cell, each is scaled by it, so they share the
	/// cell in proportion and never stack past it. Carpets run after the scattered rules, all
	/// together, in two passes over the same arithmetic (sum, then write), which keeps the memory
	/// one float per cell however many carpets a tile has.
	/// </para>
	/// <para>
	/// <b>Carpets are outside the detail budgets.</b> <see cref="TerrainScatterOptions.MaxDetailSpawnsPerTile"/>,
	/// a biome's share of it and <see cref="PrefabSpawnRule.maxPerChunk"/> count covered cells, which
	/// for a point-sampled rule tracks how many instances it adds. A carpet covers most of its
	/// ground on purpose, and what it costs to draw is set by the coverage, the prototype's density,
	/// the quality preset's density scale and the detail distance — not by how many cells hold a
	/// non-zero byte, the detail map being a fixed size per prototype whatever is in it. A budget
	/// would only cut the turf off part-way across a tile, which is a hard edge, so carpets are
	/// reported (cells covered, mean coverage) rather than capped.
	/// </para>
	/// <para>
	/// <b>Climate bands follow the world, not the biome.</b> A rule with
	/// <see cref="PrefabSpawnRule.useClimateBand"/> set has its chance (or a carpet's coverage)
	/// multiplied by <see cref="ClimateBand(PrefabSpawnRule, float, float)"/> at the scene's placement
	/// climate where the candidate stands — the mean annual temperature at its true place on the
	/// globe and its true altitude, and the humidity there — so a biome whose envelope spans thirty
	/// degrees grows its palms only where it is warm, and a band climbs a slope with the lapse rate.
	/// The climate is read from a per-tile grid (<see cref="TerrainClimateGrid"/>), built only when a banded
	/// rule reaches the tile. It is applied like the height and slope bands, after the draw's cheap
	/// refusals and before the final comparison, and draws nothing of its own, so a rule without a
	/// band takes exactly the path, and places exactly the instances, it did before bands existed.
	/// A scene without a placement climate lets every band pass (see <see cref="Scatter"/>).
	/// </para>
	/// <para>
	/// <b>Trees are sunk, not snapped.</b> Each tree instance is set at the terrain's interpolated
	/// height less its rule's sink — a per-instance draw from <see cref="PrefabSpawnRule.sinkRange"/>
	/// plus <see cref="PrefabSpawnRule.sinkSlopeFactor"/> × footprint radius × tan(slope), the radius
	/// being the prefab's horizontal bounds (cached per prototype) times the instance's width — and
	/// written with snapping off, because snapping would put every one back on the surface. With no
	/// sink it is exactly the height snapping gave. The sink's draw comes from the cell's hash like
	/// everything else, so it is as deterministic as the position.
	/// </para>
	/// <para>
	/// <b>Deterministic from where, never from what order.</b> A cell's random numbers are a hash of
	/// the scene seed, the rule's <see cref="PrefabSpawnRule.StableGuid"/>, its biome's key and the
	/// cell's integer position in the world. Not the layer index, not the rule's or the biome's
	/// place in any list: re-ordering biomes, adding a layer or re-running on a scene gives every
	/// rule the same trees in the same places. Tiles run in world order and rules in key order, so
	/// even the choices that do depend on order (who wins a spacing conflict at a seam, which rule
	/// a full budget stops) are the same every time.
	/// </para>
	/// <para>
	/// <b>Re-runnable.</b> Every tile's previous details, trees and prototypes are cleared first,
	/// and every tile gets the same prototype lists, so a prototype index means the same prefab
	/// on every tile and a designer's paint tools see one consistent set.
	/// </para>
	/// <para>
	/// <b>No seasonal state is baked.</b> Healthy and dry tints and noise go onto the detail
	/// prototypes as authored, and trees get a fixed per-instance colour variation; season, drought,
	/// snow and wetness are the vegetation shader's, read from globals at run time.
	/// </para>
	/// </remarks>
	public static class TerrainScatter
	{
		/// <summary>Unity's largest detail map.</summary>
		private const int MaximumDetailResolution = 4048;

		/// <summary>
		/// Scatters every enabled spawn rule the palette reaches into the tiles.
		/// </summary>
		/// <param name="tiles">The scene's terrain tiles. Their alphamaps must already hold <paramref name="palette"/>'s layers in order.</param>
		/// <param name="palette">The scene's layers and which biome entry each came from.</param>
		/// <param name="field">The scene's biomes; the palette's entries index its <see cref="SceneBiomeField.Biomes"/>.</param>
		/// <param name="seed">The scene's seed.</param>
		/// <param name="options">Sampling, budgets and draw settings. Null uses the defaults.</param>
		/// <param name="climate">
		/// The scene's placement climate, for rules with a climate band
		/// (<see cref="PrefabSpawnRule.useClimateBand"/>): the same field the biomes were chosen from.
		/// Null, or a scene that is only placed rather than cut from its globe, has no climate to read,
		/// and every band then passes (a factor of 1) rather than refusing everything — a band says
		/// where on a world a rule belongs, and with no world there is nothing to say no with. The
		/// report warns when that happens to a banded rule.
		/// </param>
		public static TerrainScatterReport Scatter(IReadOnlyList<Terrain> tiles, SceneTerrainPalette palette, SceneBiomeField field,
			uint seed, TerrainScatterOptions options, ScenePlacementClimate climate = null)
		{
			if (tiles == null)
			{
				throw new ArgumentNullException(nameof(tiles));
			}
			if (palette == null)
			{
				throw new ArgumentNullException(nameof(palette));
			}
			if (field == null)
			{
				throw new ArgumentNullException(nameof(field));
			}
			options ??= new TerrainScatterOptions();

			Stopwatch clock = Stopwatch.StartNew();
			var report = new TerrainScatterReport { DetailScatterMode = options.DetailScatterMode };

			List<Terrain> ordered = OrderTiles(tiles);
			report.Tiles = ordered.Count;
			if (ordered.Count == 0)
			{
				report.Warnings.Add("No terrain tiles with data were given; nothing was scattered.");
				report.Elapsed = clock.Elapsed;
				return report;
			}

			var attributionEntries = new List<SceneTerrainPalette.Entry>();
			// Only a scene cut from its globe has a climate at every point; see ScenePlacementClimate.SampleAt.
			ScenePlacementClimate placement = climate != null && climate.Generated ? climate : null;
			/* The world the scene stands on, for the rules' world gates (decision 41, 2026-10-18): its body's conditions, placed or
			 * cut, else the home world's (what a scene with no body has always been). */
			BiomeWorldConditions world = climate != null ? climate.Conditions : BiomeWorldConditions.Earthlike;
			List<ScatterRule> rules = GatherRules(palette, field, seed, options, placement, world, attributionEntries, report);

			/* Run order: by biome key, then GUID. Independent of palette and list order on purpose,
			 * and fixed before the prototypes are built so their indices are too. */
			rules.Sort((a, b) =>
			{
				int byKey = string.CompareOrdinal(a.BiomeKey, b.BiomeKey);
				if (byKey != 0)
				{
					return byKey;
				}
				int byGuid = string.CompareOrdinal(a.Guid, b.Guid);
				return byGuid != 0 ? byGuid : a.DuplicateIndex.CompareTo(b.DuplicateIndex);
			});
			report.Rules.Clear();
			foreach (ScatterRule rule in rules)
			{
				report.Rules.Add(rule.Outcome);
			}

			var detailPrototypes = new List<DetailPrototype>();
			var treePrototypes = new List<TreePrototype>();
			var treeFootprints = new List<float>();
			BuildPrototypes(rules, options, detailPrototypes, treePrototypes, treeFootprints, report);
			ShareCanopy(rules, treeFootprints);
			report.DetailPrototypes = detailPrototypes.Count;
			report.TreePrototypes = treePrototypes.Count;

			bool[][] layerBiomes = LayerBiomes(palette, field.Biomes.Count);
			Func<float, float, float, float> normalizedHeight = options.NormalizedHeight ?? SceneRelativeHeight(ordered, report);

			if (palette.Layers.Count == 0)
			{
				report.Warnings.Add("The palette has no layers, so no rule has ground to grow on.");
			}

			var workspace = new Workspace(field.Biomes.Count, attributionEntries.Count) { TreeFootprints = treeFootprints.ToArray(), Placement = placement };
			DetailPrototype[] detailArray = detailPrototypes.ToArray();
			TreePrototype[] treeArray = treePrototypes.ToArray();
			List<TerrainDetailBiomes.Entry> prototypeBiomes = PrototypeBiomes(rules, detailArray);
			bool firstTile = true;
			foreach (Terrain terrain in ordered)
			{
				RecordBiomes(terrain, prototypeBiomes);
				ScatterTile(terrain, rules, detailArray, treeArray, palette, field, layerBiomes, attributionEntries,
					normalizedHeight, options, workspace, firstTile, report);
				firstTile = false;
			}

			report.Elapsed = clock.Elapsed;
			return report;
		}

		// ── Rules and prototypes ─────────────────────────────────────

		/// <summary>One rule as the scatter runs it: where it is gated, how it is seeded, what it places.</summary>
		private sealed class ScatterRule
		{
			public PrefabSpawnRule Rule;
			/// <summary>
			/// What the rule places: its own prefabs, or what <see cref="TerrainScatterOptions.Prefabs"/>
			/// resolved them to (a LOCAL scene's overrides). Read instead of <c>Rule.prefabs</c> everywhere.
			/// </summary>
			public GameObject[] Prefabs;
			public SceneTerrainPalette.Entry Entry;
			public string BiomeKey;
			public string Guid;
			public int DuplicateIndex;
			public uint Seed;
			public int Attribution;
			/// <summary>Detail prototype indices, or tree prototype indices, one per valid prefab.</summary>
			public readonly List<int> Prototypes = new List<int>();
			/// <summary>The value one covered detail cell is written with.</summary>
			public int DetailValue;
			public ScatterSpacing Spacing;
			/// <summary>True for a detail rule placed as a continuous carpet (<see cref="PrefabSpawnRule.IsCarpet"/>).</summary>
			public bool Carpet;
			/// <summary>The seed of a carpet's clump noise.</summary>
			public uint ClumpSeed;
			/// <summary>The seed of a clustered rule's group field (<see cref="ClusterIntensity"/>).</summary>
			public uint ClusterSeed;
			/// <summary>A clustered rule's group radius and centre spacing, metres (<see cref="ClusterShape"/>).</summary>
			public float ClusterMetres, ClusterPitch;
			/// <summary>True for a tree rule that stands in woods and copses (<see cref="PrefabSpawnRule.IsForested"/>).</summary>
			public bool Forest;
			/// <summary>A forest rule's stands: the scene-wide field cut at the rule's cover.</summary>
			public StandField Stands;
			/// <summary>The seeds of a forest rule's own species patches, between rules and between its prefabs.</summary>
			public uint MixSeed, VariantSeed;
			/// <summary>Size of a forest rule's species patches, metres.</summary>
			public float PatchMetres;
			/// <summary>The slope at which a forest rule's stands have thinned the most, degrees.</summary>
			public float SlopeReference;
			/// <summary>
			/// True when the rule's <see cref="Spacing"/> is the canopy every forest rule shares, each tree
			/// carrying its own spacing (<see cref="ShareCanopy"/>).
			/// </summary>
			public bool Canopy;
			/// <summary>Each prototype's crown against the rule's mean crown, by variant; scales a canopy tree's spacing.</summary>
			public float[] CrownScale;
			/// <summary>
			/// Which side of the water line the rule grows on, as a smoothstep over world y from
			/// <see cref="WaterFrom"/> (none) to <see cref="WaterTo"/> (full); unused when <see cref="WaterGated"/> is false.
			/// </summary>
			public bool WaterGated;
			public float WaterFrom, WaterTo;
			/// <summary>
			/// A sea-floor rule's band of depth below mean sea level (<see cref="PrefabSpawnRule.depthRange"/>):
			/// shallowest and deepest, 0 for an open end; both 0 for none.
			/// </summary>
			public Vector2 Depth;
			/// <summary>
			/// A bed rule's own reading of the water over it (decision 35 (a), 2026-10-18): the waters it grows under (its own, else its
			/// slot's, <see cref="BiomeBedBands.RuleWaters"/>), its current band, and where an inland water's surface is
			/// (<see cref="BedInland"/>, null with none) so its depth band is read below THAT surface; the sea's case keeps the sea-level
			/// arithmetic it always had.
			/// </summary>
			public bool Aquatic;
			public BiomeBedWaters Waters;
			public Vector2 Current;
			public Func<float, float, float> BedInland;
			public Func<float, float, Vector3> RiverAt;
			public float BedInlandFrom, BedInlandTo;
			/// <summary>A river's or lake's surface at (x, z), for a land rule kept above it (see <see cref="TerrainScatterOptions.InlandWaterSurface"/>); null for none.</summary>
			public Func<float, float, float> Inland;
			public float InlandFrom, InlandTo;
			/// <summary>True where nothing of this rule may grow (<see cref="TerrainScatterOptions.Excluded"/>); null for nowhere.</summary>
			public Func<float, float, bool> Excluded;
			/// <summary>
			/// True when the rule has a climate band (<see cref="PrefabSpawnRule.useClimateBand"/>) and the
			/// scene a climate to read it against; false skips the band entirely, never sampling the climate.
			/// </summary>
			public bool Climate;
			/// <summary>The ground's heat (<see cref="TerrainScatterOptions.HeatAt"/>) when the rule has a heat band and the scene a heat to read; null skips the band (2026-10-18, I2q).</summary>
			public Func<float, float, float> Heat;
			public TerrainScatterReport.RuleOutcome Outcome;
			public bool Runnable => !Outcome.Skipped;
		}

		private static List<Terrain> OrderTiles(IReadOnlyList<Terrain> tiles)
		{
			var ordered = new List<Terrain>(tiles.Count);
			var seen = new HashSet<Terrain>();
			for (int i = 0; i < tiles.Count; i++)
			{
				Terrain terrain = tiles[i];
				if (terrain != null && terrain.terrainData != null && seen.Add(terrain))
				{
					ordered.Add(terrain);
				}
			}
			// South to north, west to east: the order seams are resolved in, whatever order the caller listed them.
			ordered.Sort((a, b) =>
			{
				Vector3 pa = a.transform.position, pb = b.transform.position;
				int byZ = pa.z.CompareTo(pb.z);
				return byZ != 0 ? byZ : pa.x.CompareTo(pb.x);
			});
			return ordered;
		}

		private static List<ScatterRule> GatherRules(SceneTerrainPalette palette, SceneBiomeField field, uint seed, TerrainScatterOptions options,
			ScenePlacementClimate climate, in BiomeWorldConditions world, List<SceneTerrainPalette.Entry> attributionEntries, TerrainScatterReport report)
		{
			var rules = new List<ScatterRule>();
			var seen = new HashSet<(int, PrefabSpawnRule)>();
			var duplicates = new Dictionary<string, int>();
			var attributionIndex = new Dictionary<SceneTerrainPalette.Entry, int>();
			// One quantile table per stand size: every forest rule reads the same scene-wide field.
			var standTables = new Dictionary<float, float[]>();
			// Banded rules that found no climate to read, reported once rather than once each.
			int unbanded = 0;

			foreach (SceneTerrainPalette.Entry entry in palette.Entries)
			{
				List<PrefabSpawnRule> source = entry?.Source?.prefabSpawnRules;
				// A river's bars are bare: their art is borrowed from a biome whose rules belong to it.
				if (source == null || source.Count == 0 || entry.Role == PaletteRole.Sediment || entry.Role == PaletteRole.Path)
				{
					continue;
				}
				if (entry.BiomeIndex < 0 || entry.BiomeIndex >= field.Biomes.Count || entry.Biome == null)
				{
					report.Warnings.Add($"A palette entry ({entry.Slot}) names biome {entry.BiomeIndex}, which the field does not have; its rules were skipped.");
					continue;
				}

				string biomeKey = entry.Biome.Key;
				for (int r = 0; r < source.Count; r++)
				{
					PrefabSpawnRule rule = source[r];
					if (rule == null || !rule.enableSpawning || !seen.Add((entry.BiomeIndex, rule)))
					{
						continue;
					}

					/* A rule with no GUID would get a fresh random one from EnsureStableGuid on every run
					 * unless somebody saved the asset, which is exactly the reshuffle this is meant to
					 * prevent. Stand in with one made from where the rule sits, and say so. */
					string guid = rule.StableGuid;
					if (string.IsNullOrEmpty(guid))
					{
						guid = $"{biomeKey}/{entry.Slot}/{r}";
						report.Warnings.Add($"{entry.Biome.ResolvedDisplayName} / {entry.Slot} / '{rule.ruleName}' has no stable GUID; seeded from its position instead, so moving it will reshuffle it.");
					}

					/* Copying a list element in the inspector copies its GUID with it. Two rules with
					 * one seed place identical patterns; the occurrence count keeps them apart. */
					string duplicateKey = biomeKey + "|" + guid;
					duplicates.TryGetValue(duplicateKey, out int duplicateIndex);
					duplicates[duplicateKey] = duplicateIndex + 1;
					if (duplicateIndex > 0)
					{
						report.Warnings.Add($"{entry.Biome.ResolvedDisplayName} / {entry.Slot} / '{rule.ruleName}' shares its GUID with another rule of the biome (a copied list element?); it is told apart by order, so give it its own GUID.");
					}

					var outcome = new TerrainScatterReport.RuleOutcome
					{
						Biome = entry.Biome.ResolvedDisplayName,
						Slot = entry.Slot,
						RuleName = rule.ruleName,
						StableGuid = guid,
						Channel = rule.spawnChannel,
						LayerIndex = entry.LayerIndex,
						Carpet = rule.IsCarpet,
					};

					var scatterRule = new ScatterRule
					{
						Rule = rule,
						Entry = entry,
						BiomeKey = biomeKey,
						Guid = guid,
						DuplicateIndex = duplicateIndex,
						Seed = RuleSeed(seed, guid, biomeKey, rule.seedOffset, duplicateIndex),
						Outcome = outcome,
						Attribution = -1,
						Carpet = rule.IsCarpet,
					};
					scatterRule.ClumpSeed = Mix(scatterRule.Seed ^ 0xC1A4F00Du);
					scatterRule.ClusterSeed = Mix(scatterRule.Seed ^ 0x6C0B5E11u);
					ClusterShape(rule, out scatterRule.ClusterMetres, out scatterRule.ClusterPitch);
					if (rule.IsForested && !scatterRule.Carpet)
					{
						scatterRule.Forest = true;
						scatterRule.Stands = StandsFor(seed, rule, standTables);
						scatterRule.MixSeed = Mix(scatterRule.Seed ^ 0x3C6EF372u);
						scatterRule.VariantSeed = Mix(scatterRule.Seed ^ 0xA54FF53Au);
						scatterRule.PatchMetres = Mathf.Max(30f, rule.forestMetres * 0.25f);
						scatterRule.SlopeReference = rule.useSlopeConstraint ? Mathf.Max(5f, rule.slopeRange.max) : 45f;
					}
					if (options != null && options.HasLiquidWater)
					{
						// A rule on a biome's submerged layer is aquatic; anything else, trees included, grows on land.
						bool aquatic = entry.Role == PaletteRole.Submerged;
						scatterRule.WaterGated = true;
						scatterRule.WaterFrom = aquatic ? options.AquaticFadeStartMetres : options.LandFadeStartMetres;
						scatterRule.WaterTo = aquatic ? options.AquaticFullMetres : options.LandFullMetres;
						// Kelp and seagrass where the light reaches, sponges below: the rule's own depth band.
						scatterRule.Depth = aquatic ? rule.depthRange : Vector2.zero;
						if (aquatic)
						{
							/* Under a lake or a river its depth is below that water's own surface, and it grows only under the waters it
							 * names (decision 35 (a), 2026-10-18): a lakebed's rules the sea alone unless they say otherwise, so a coast's
							 * kelp never comes up in its ponds and the sea floor grows exactly as it did. */
							scatterRule.Aquatic = true;
							scatterRule.Waters = BiomeBedBands.RuleWaters(rule.waters, SlotWaters(entry));
							scatterRule.Current = rule.currentRange;
							scatterRule.BedInland = options.InlandWaterSurface;
							scatterRule.RiverAt = options.RiverAt;
							scatterRule.BedInlandFrom = options.InlandAquaticFadeStartMetres;
							scatterRule.BedInlandTo = options.InlandAquaticFullMetres;
						}
					}
					if (rule.useHeatBand && options?.HeatAt != null)
					{
						scatterRule.Heat = options.HeatAt;
					}
					if (rule.useClimateBand)
					{
						// No climate, no band: the rule grows as it did before bands existed (see Scatter).
						scatterRule.Climate = climate != null;
						unbanded += climate != null ? 0 : 1;
					}
					if (options != null && options.InlandWaterSurface != null && entry.Role != PaletteRole.Submerged)
					{
						// Land plants keep to the banks of rivers and lakes, and out of a dry wash's bed.
						scatterRule.Inland = options.InlandWaterSurface;
						scatterRule.InlandFrom = options.InlandFadeStartMetres;
						scatterRule.InlandTo = options.InlandFullMetres;
					}
					// Nothing at all in a point of interest's footprint, land or sea; trees off the ways too, details off a road's width.
					scatterRule.Excluded = rule.spawnChannel == PrefabSpawnChannel.TreeInstance || options?.DetailExcluded == null
						? options?.Excluded : options.DetailExcluded;
					rules.Add(scatterRule);

					if (entry.LayerIndex < 0 || entry.LayerIndex >= palette.Layers.Count)
					{
						Skip(scatterRule, $"its layer index {entry.LayerIndex} is outside the palette's {palette.Layers.Count} layers");
						continue;
					}
					if (!(rule.spawnProbabilityMultiplier > 0f) || (scatterRule.Carpet ? !(rule.carpetCoverage > 0f) : !(rule.densityPer100m2 > 0f)))
					{
						Skip(scatterRule, scatterRule.Carpet ? "its carpet coverage is zero" : "its density is zero");
						continue;
					}
					// Not on this world (its air, temperature or nature): skipped whole, no draw taken, like a rule with no prefabs.
					if (!GrowsOn(rule, world))
					{
						Skip(scatterRule, $"its world gate ({rule.worldGate.Signature()}) does not admit this world");
						continue;
					}
					scatterRule.Prefabs = options.Prefabs != null ? options.Prefabs(entry, rule) : rule.prefabs;
					if (!AnyPrefab(scatterRule.Prefabs))
					{
						Skip(scatterRule, "it has no prefabs");
						continue;
					}

					if (!attributionIndex.TryGetValue(entry, out int attribution))
					{
						attribution = attributionEntries.Count;
						attributionEntries.Add(entry);
						attributionIndex[entry] = attribution;
					}
					scatterRule.Attribution = attribution;
					// A carpet has no instances to space; coverage is the whole of what it writes.
					if (rule.minSpacing > 0f && !scatterRule.Carpet)
					{
						scatterRule.Spacing = new ScatterSpacing(rule.minSpacing, 1024);
					}
				}
			}
			if (unbanded > 0)
			{
				report.Warnings.Add($"{unbanded} rule(s) have a climate band, but the scene has no placement climate (not cut from a world); they grew without their bands.");
			}
			return rules;
		}

		/// <summary>
		/// True when a rule grows on a world (its <see cref="PrefabSpawnRule.worldGate"/>, decision 41): always for an open gate, so
		/// every rule without one is gathered exactly as before.
		/// </summary>
		public static bool GrowsOn(PrefabSpawnRule rule, in BiomeWorldConditions world)
		{
			return rule != null && (rule.worldGate.IsOpen || rule.worldGate.Admits(world));
		}

		/// <summary>True when the array holds at least one prefab (as <see cref="PrefabSpawnRule.HasValidPrefabs"/>, for a resolved array).</summary>
		private static bool AnyPrefab(GameObject[] prefabs)
		{
			if (prefabs == null)
			{
				return false;
			}
			for (int i = 0; i < prefabs.Length; i++)
			{
				if (prefabs[i] != null)
				{
					return true;
				}
			}
			return false;
		}

		private static void Skip(ScatterRule rule, string reason)
		{
			rule.Outcome.Skipped = true;
			rule.Outcome.SkipReason = reason;
		}

		private static void BuildPrototypes(List<ScatterRule> rules, TerrainScatterOptions options,
			List<DetailPrototype> detailPrototypes, List<TreePrototype> treePrototypes, List<float> treeFootprints, TerrainScatterReport report)
		{
			var treeIndex = new Dictionary<GameObject, int>();
			var treeRefusal = new Dictionary<GameObject, string>();

			foreach (ScatterRule scatterRule in rules)
			{
				if (!scatterRule.Runnable)
				{
					continue;
				}
				PrefabSpawnRule rule = scatterRule.Rule;
				string where = $"{scatterRule.Outcome.Biome} / {scatterRule.Outcome.Slot} / '{rule.ruleName}'";

				if (rule.spawnChannel == PrefabSpawnChannel.TreeInstance)
				{
					for (int p = 0; p < scatterRule.Prefabs.Length; p++)
					{
						GameObject prefab = scatterRule.Prefabs[p];
						if (prefab == null)
						{
							continue;
						}
						if (!treeIndex.TryGetValue(prefab, out int index))
						{
							if (!treeRefusal.TryGetValue(prefab, out string refusal))
							{
								refusal = ValidateTreePrefab(prefab);
								treeRefusal[prefab] = refusal;
							}
							if (refusal != null)
							{
								report.InvalidPrefabs.Add($"{where}: tree prefab '{prefab.name}' — {refusal}");
								continue;
							}
							index = treePrototypes.Count;
							// Trees share a prototype per prefab: scale, rotation and colour ride on the instance.
							treePrototypes.Add(new TreePrototype { prefab = prefab, bendFactor = 0.1f });
							treeFootprints.Add(FootprintRadius(prefab));
							treeIndex[prefab] = index;
							if (prefab.GetComponentInChildren<Collider>(true) != null)
							{
								report.TreePrototypesWithColliders++;
							}
						}
						if (!scatterRule.Prototypes.Contains(index))
						{
							scatterRule.Prototypes.Add(index);
						}
					}
				}
				else
				{
					DetailRenderMode mode = rule.detailRenderMode;
					if (mode == DetailRenderMode.GrassBillboard)
					{
						// Unity refuses mesh details drawn as billboards; Grass is the nearest mode that draws.
						report.Warnings.Add($"{where}: GrassBillboard cannot draw a mesh, so it scatters as Grass.");
						mode = DetailRenderMode.Grass;
					}
					for (int p = 0; p < scatterRule.Prefabs.Length; p++)
					{
						GameObject prefab = scatterRule.Prefabs[p];
						if (prefab == null)
						{
							continue;
						}
						string refusal = ValidateDetailPrefab(prefab, out bool instanced);
						DetailPrototype prototype = null;
						if (refusal == null)
						{
							prototype = DetailPrototypeFor(rule, prefab, mode, instanced, Mix(scatterRule.Seed + (uint)p));
							if (!prototype.Validate(out string unityRefusal))
							{
								refusal = string.IsNullOrEmpty(unityRefusal) ? "Unity refused it as a detail prototype" : unityRefusal;
							}
						}
						if (refusal != null)
						{
							report.InvalidPrefabs.Add($"{where}: detail prefab '{prefab.name}' — {refusal}");
							continue;
						}
						if (instanced && mode == DetailRenderMode.Grass)
						{
							report.Warnings.Add($"{where}: '{prefab.name}' is instanced, so Unity draws it with its own material and the Grass render mode does not apply.");
						}
						scatterRule.Prototypes.Add(detailPrototypes.Count);
						detailPrototypes.Add(prototype);
					}
					/* What one covered cell holds. Authored on the 64–255 scale of a byte; in coverage
					 * mode that is the share of the cell covered (255 = all of it), and the instances
					 * in it are the prototype's density times the density scales. Rescaled to whatever
					 * the scatter mode's top value is. */
					scatterRule.DetailValue = Mathf.Clamp(rule.detailInstancesPerSpawn, 1, 255);
				}

				scatterRule.Outcome.Prototypes = scatterRule.Prototypes.Count;
				if (scatterRule.Prototypes.Count == 0)
				{
					Skip(scatterRule, "none of its prefabs is valid (see invalid prefabs)");
				}
			}
		}

		/// <summary>A forest rule's stands, its quantile table shared with every rule of the same stand size.</summary>
		private static StandField StandsFor(uint sceneSeed, PrefabSpawnRule rule, Dictionary<float, float[]> tables)
		{
			uint seed = StandSeed(sceneSeed);
			float metres = Mathf.Max(1f, rule.forestMetres);
			if (!tables.TryGetValue(metres, out float[] quantiles))
			{
				quantiles = StandQuantiles(seed, metres);
				tables[metres] = quantiles;
			}
			return new StandField(seed, metres, rule.forestCover, rule.forestEdge, rule.forestOpen, quantiles);
		}

		/// <summary>
		/// The seed of a scene's stand field. The scene's alone — not a rule's, not a biome's — so every
		/// forest rule in the scene reads one field: see <see cref="StandField"/>.
		/// </summary>
		public static uint StandSeed(uint sceneSeed) => Mix(sceneSeed ^ 0x5717D5EEu);

		/// <summary>
		/// Gives every forest rule with a spacing one shared, per-point spacing — the canopy — in place of
		/// its own, each tree carrying the rule's spacing times its crown against the rule's mean crown.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A rule's own spacing keeps only its own trees apart. Two rules of one biome (spruce and pine in
		/// a taiga, oak and birch beside spruce in a forest) each knew nothing of the other, so a pine could
		/// stand half a metre from a spruce and the two crowns grew through each other; and gathering both
		/// into the same stands, which is the point of stands, would make that the rule rather than the
		/// accident. Sharing one canopy, two trees stand at least the mean of their spacings apart.
		/// </para>
		/// <para>
		/// <b>Crowns, not prefab slots.</b> A rule with several species has one spacing, which is right for
		/// none of them when an oak's crown is twice a birch's. Each prototype's footprint
		/// (<see cref="FootprintRadius"/>, its crown's reach) over the rule's mean footprint scales its
		/// trees' spacing, and so does each tree's own width: a broad oak keeps a wider ring clear than a
		/// slim birch of the same rule, and a small tree can stand closer than a large one. With no
		/// footprint to read (a test's cube) every prototype counts as the mean.
		/// </para>
		/// <para>
		/// Order still decides a conflict, as it always has: rules run in key order, so where two forest
		/// rules compete for one gap the first to reach it keeps it. The species patches
		/// (<see cref="PrefabSpawnRule.forestMix"/>) are what stop the first rule taking every stand.
		/// </para>
		/// </remarks>
		private static void ShareCanopy(List<ScatterRule> rules, List<float> footprints)
		{
			float widest = 0f;
			foreach (ScatterRule rule in rules)
			{
				if (!rule.Runnable || !rule.Forest || !(rule.Rule.minSpacing > 0f) || rule.Rule.spawnChannel != PrefabSpawnChannel.TreeInstance)
				{
					continue;
				}
				int count = rule.Prototypes.Count;
				rule.CrownScale = new float[count];
				float mean = 0f;
				int known = 0;
				for (int v = 0; v < count; v++)
				{
					float footprint = rule.Prototypes[v] < footprints.Count ? footprints[rule.Prototypes[v]] : 0f;
					if (footprint > 0f)
					{
						mean += footprint;
						known++;
					}
				}
				mean = known > 0 ? mean / known : 0f;
				float most = 1f;
				for (int v = 0; v < count; v++)
				{
					float footprint = rule.Prototypes[v] < footprints.Count ? footprints[rule.Prototypes[v]] : 0f;
					rule.CrownScale[v] = mean > 0f && footprint > 0f ? footprint / mean : 1f;
					most = Mathf.Max(most, rule.CrownScale[v]);
				}
				PrefabSpawnRule r = rule.Rule;
				float widestScale = r.useNonUniformScaling ? Mathf.Max(r.widthScaleRange.x, r.widthScaleRange.y) : Mathf.Max(r.uniformScaleRange.x, r.uniformScaleRange.y);
				widest = Mathf.Max(widest, r.minSpacing * most * Mathf.Max(0.05f, widestScale));
			}
			if (!(widest > 0f))
			{
				return;
			}
			var canopy = new ScatterSpacing(widest, 4096, true);
			foreach (ScatterRule rule in rules)
			{
				if (rule.CrownScale != null)
				{
					rule.Spacing = canopy;
					rule.Canopy = true;
				}
			}
		}

		/// <summary>
		/// True for a counted detail's reduced level: a bare MeshFilter (no renderer, so nothing draws it by accident) on a
		/// direct child named <c>LOD1</c>, <c>LOD2</c>…, which only the client's GPU detail scatter reads
		/// (BiomeArtGenerator.WriteBush). Unity's own detail drawing ignores it and draws the root's mesh, as it should.
		/// </summary>
		private static bool IsDetailLevel(GameObject prefab, MeshFilter filter)
		{
			Transform t = filter.transform;
			return IsCountedDetail(prefab) && t.parent == prefab.transform && t.name.StartsWith("LOD", StringComparison.Ordinal)
				&& int.TryParse(t.name.Substring(3), out int level) && level > 0 && !filter.TryGetComponent(out Renderer _);
		}

		/// <summary>Why a prefab cannot be a mesh detail, or null when it can.</summary>
		/// <remarks>
		/// Unity draws a mesh detail from the root's MeshFilter and the root renderer's first
		/// material and ignores everything else, so a prefab with a second mesh or material would
		/// silently draw as part of itself. That is refused here rather than baked broken.
		/// </remarks>
		private static string ValidateDetailPrefab(GameObject prefab, out bool instanced)
		{
			instanced = false;
			if (!prefab.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
			{
				return "needs a MeshFilter with a mesh on its root";
			}
			foreach (MeshFilter other in prefab.GetComponentsInChildren<MeshFilter>(true))
			{
				if (other != filter && !IsDetailLevel(prefab, other))
				{
					return "has more than one MeshFilter; a detail draws only the root's mesh";
				}
			}
			if (!prefab.TryGetComponent(out MeshRenderer renderer))
			{
				return "needs a MeshRenderer on its root";
			}
			Material[] materials = renderer.sharedMaterials;
			if (materials == null || materials.Length != 1 || materials[0] == null)
			{
				return $"needs exactly one material (it has {(materials == null ? 0 : materials.Length)}, or a missing one)";
			}
			if (filter.sharedMesh.subMeshCount != 1)
			{
				return $"its mesh has {filter.sharedMesh.subMeshCount} sub-meshes; a detail draws one";
			}
			instanced = materials[0].enableInstancing;
			if (!instanced && !filter.sharedMesh.isReadable)
			{
				// Non-instanced details are combined on the CPU, which reads the mesh.
				return "its material is not instanced, and a non-instanced detail needs a readable mesh (enable GPU instancing on the material, or Read/Write on the mesh)";
			}
			return null;
		}

		/// <summary>Why a prefab cannot be a terrain tree, or null when it can.</summary>
		private static string ValidateTreePrefab(GameObject prefab)
		{
			if (prefab.TryGetComponent(out LODGroup lodGroup))
			{
				return lodGroup.lodCount > 0 ? null : "its LODGroup has no LODs";
			}
			if (!prefab.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
			{
				return "needs a LODGroup, or a MeshFilter with a mesh, on its root";
			}
			if (!prefab.TryGetComponent(out MeshRenderer renderer) || renderer.sharedMaterial == null)
			{
				return "needs a MeshRenderer with a material on its root";
			}
			return null;
		}

		/// <summary>
		/// The horizontal reach of a tree prefab from its pivot, in its own metres: the farthest x or
		/// z of its first LOD's meshes (or every mesh, without a LODGroup), at width scale 1.
		/// </summary>
		/// <remarks>
		/// Read from the meshes' bounds through each renderer's transform relative to the root, not
		/// from <c>Renderer.bounds</c>, which is a world-space box only once an object is in a scene
		/// and is empty on a prefab asset. Farthest edge rather than extent, so a prefab whose mesh
		/// sits off its pivot still sinks by the side that overhangs.
		/// </remarks>
		public static float FootprintRadius(GameObject prefab)
		{
			if (prefab == null)
			{
				return 0f;
			}
			Renderer[] renderers = null;
			if (prefab.TryGetComponent(out LODGroup lodGroup) && lodGroup.lodCount > 0)
			{
				renderers = lodGroup.GetLODs()[0].renderers;
			}
			if (renderers == null || renderers.Length == 0)
			{
				renderers = prefab.GetComponentsInChildren<Renderer>(true);
			}
			Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
			float reach = 0f;
			foreach (Renderer renderer in renderers)
			{
				if (renderer == null)
				{
					continue;
				}
				Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
					: renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
				if (mesh == null)
				{
					continue;
				}
				Matrix4x4 toPrefab = toRoot * renderer.transform.localToWorldMatrix;
				Bounds bounds = mesh.bounds;
				for (int corner = 0; corner < 8; corner++)
				{
					Vector3 local = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
						(corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
					Vector3 p = toPrefab.MultiplyPoint3x4(local);
					reach = Mathf.Max(reach, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)));
				}
			}
			return reach;
		}

		/// <summary>
		/// True for a detail counted plant by plant rather than spread as cover: a shrub (its prefab name starts with
		/// <see cref="ProceduralArtCatalogue.BushPrefix"/>). See <see cref="CalibrateCountedDetails"/>.
		/// </summary>
		public static bool IsCountedDetail(GameObject prefab) => prefab != null && prefab.name.StartsWith(ProceduralArtCatalogue.BushPrefix, StringComparison.Ordinal);

		/// <summary>
		/// Makes one placement of a counted detail (a shrub) hold exactly one plant, whatever its size, and takes it out
		/// of every density scale.
		/// </summary>
		/// <remarks>
		/// <para>
		/// In coverage mode a cell's value is the share of it covered, and a covered cell holds Unity's coverage for the
		/// prototype (<see cref="TerrainData.ComputeDetailCoverage"/>), which falls with the prototype's size: a 3 m bush
		/// placed in a cell came out as a fraction of a bush, so most placements drew nothing and a rule's density meant
		/// nothing countable. So each counted prototype's density is scaled until a cell at its rule's value holds one
		/// instance (checked against Unity's own answer, a few rounds, since only Unity knows the formula), and the rule's
		/// density is then plants per 100 m².
		/// </para>
		/// <para>
		/// <b>No density scaling.</b> The terrain's and the quality preset's detail density thin grass, which nobody hides
		/// in. A shrub is cover: thinned by one player's settings, someone standing in it would be in the open to them
		/// only. The client's GPU scatter holds to that as well (FishMMO.Client DetailScatterSettings).
		/// </para>
		/// </remarks>
		/// <param name="report">Where a prototype that would not calibrate is reported; null to stay quiet (every tile after the first).</param>
		private static void CalibrateCountedDetails(TerrainData data, DetailPrototype[] prototypes, List<ScatterRule> rules, TerrainScatterReport report)
		{
			if (data.detailScatterMode != DetailScatterMode.CoverageMode || prototypes == null || prototypes.Length == 0)
			{
				return;
			}
			int res = Mathf.Max(1, data.detailResolution);
			float cellArea = data.size.x / res * (data.size.z / res);
			int maxValue = Mathf.Max(1, data.maxDetailScatterPerRes);
			bool changed = false;
			foreach (ScatterRule rule in rules)
			{
				if (rule.Carpet || rule.Rule.spawnChannel != PrefabSpawnChannel.DetailLayer)
				{
					continue;
				}
				foreach (int p in rule.Prototypes)
				{
					if (p < 0 || p >= prototypes.Length || !IsCountedDetail(prototypes[p].prototype))
					{
						continue;
					}
					DetailPrototype prototype = prototypes[p];
					if (prototype.useDensityScaling)
					{
						prototype.useDensityScaling = false;
						changed = true;
					}
					// The share of a cell one placement writes (ScatterTile's value), and the coverage that makes it one plant.
					float unit = Mathf.Clamp(Mathf.RoundToInt(rule.DetailValue / 255f * maxValue), 1, maxValue) / (float)maxValue;
					float wanted = 1f / (unit * cellArea);
					float have = 0f;
					for (int round = 0; round < 4; round++)
					{
						if (changed)
						{
							data.detailPrototypes = prototypes;
							changed = false;
						}
						have = data.ComputeDetailCoverage(p);
						if (!(have > 0f) || Mathf.Abs(have - wanted) <= wanted * 0.01f)
						{
							break;
						}
						prototype.density *= wanted / have;
						changed = true;
					}
					if (report != null && !(Mathf.Abs(have - wanted) <= wanted * 0.05f))
					{
						report.Warnings.Add($"'{prototype.prototype.name}': {have:F3} per m² at full coverage against the {wanted:F3} that makes one plant a placement (density {prototype.density:F2}); its counts will be off.");
					}
				}
			}
			if (changed)
			{
				data.detailPrototypes = prototypes;
			}
		}

		private static DetailPrototype DetailPrototypeFor(PrefabSpawnRule rule, GameObject prefab, DetailRenderMode mode, bool instanced, uint noise)
		{
			float minWidth, maxWidth, minHeight, maxHeight;
			if (rule.useNonUniformScaling)
			{
				minWidth = Mathf.Max(0.05f, rule.widthScaleRange.x);
				maxWidth = Mathf.Max(minWidth, rule.widthScaleRange.y);
				minHeight = Mathf.Max(0.05f, rule.heightScaleRange.x);
				maxHeight = Mathf.Max(minHeight, rule.heightScaleRange.y);
			}
			else
			{
				minWidth = Mathf.Max(0.05f, rule.uniformScaleRange.x);
				maxWidth = Mathf.Max(minWidth, rule.uniformScaleRange.y);
				minHeight = minWidth;
				maxHeight = maxWidth;
			}

			return new DetailPrototype
			{
				prototype = prefab,
				usePrototypeMesh = true,
				useInstancing = instanced,
				// Unity draws every instanced detail as VertexLit with the prefab's material.
				renderMode = instanced ? DetailRenderMode.VertexLit : mode,
				minWidth = minWidth,
				maxWidth = maxWidth,
				minHeight = minHeight,
				maxHeight = maxHeight,
				// The authored tints and their noise, as they are: what season or drought does to
				// them is the vegetation shader's business at run time, never baked here.
				healthyColor = rule.detailHealthyColor,
				dryColor = rule.detailDryColor,
				noiseSpread = Mathf.Max(0.05f, rule.detailNoiseSpread),
				noiseSeed = (int)(noise & 0x7FFFFFFF),
				alignToGround = rule.alignToTerrainNormal ? 1f : 0f,
				// Random rather than ordered placement inside a covered cell: nothing grows on a lattice.
				positionJitter = 1f,
				density = 1f,
				// Let the terrain's and the quality preset's density scale thin it.
				useDensityScaling = true,
			};
		}

		/// <summary>
		/// Which biome's rule made each detail prototype (every tile carries the same prototypes): the record the client reads
		/// to give a prototype's blades its own biome's look and its rule's healthy and dry tints (<see cref="TerrainDetailBiomes"/>).
		/// </summary>
		private static List<TerrainDetailBiomes.Entry> PrototypeBiomes(List<ScatterRule> rules, DetailPrototype[] prototypes)
		{
			var entries = new List<TerrainDetailBiomes.Entry>();
			foreach (ScatterRule rule in rules)
			{
				if (!rule.Runnable || rule.Rule.spawnChannel != PrefabSpawnChannel.DetailLayer || rule.Entry?.Biome == null)
				{
					continue;
				}
				int biome = BiomeRegistry.IDOf(rule.Entry.Biome);
				foreach (int p in rule.Prototypes)
				{
					if (p >= 0 && p < prototypes.Length && prototypes[p]?.prototype != null)
					{
						entries.Add(new TerrainDetailBiomes.Entry { Prefab = prototypes[p].prototype.name, NoiseSeed = prototypes[p].noiseSeed, BiomeID = biome });
					}
				}
			}
			return entries;
		}

		/// <summary>Writes a tile's <see cref="TerrainDetailBiomes"/> (made when it has none), replacing what an earlier cut wrote.</summary>
		private static void RecordBiomes(Terrain terrain, List<TerrainDetailBiomes.Entry> entries)
		{
			TerrainDetailBiomes record = terrain.GetComponent<TerrainDetailBiomes>();
			if (record == null)
			{
				record = terrain.gameObject.AddComponent<TerrainDetailBiomes>();
			}
			record.Prototypes = new List<TerrainDetailBiomes.Entry>(entries);
			EditorUtility.SetDirty(record);
		}

		/// <summary>For each layer, which biomes have an entry on it.</summary>
		private static bool[][] LayerBiomes(SceneTerrainPalette palette, int biomeCount)
		{
			var map = new bool[palette.Layers.Count][];
			for (int l = 0; l < map.Length; l++)
			{
				map[l] = new bool[biomeCount];
			}
			foreach (SceneTerrainPalette.Entry entry in palette.Entries)
			{
				if (entry != null && entry.LayerIndex >= 0 && entry.LayerIndex < map.Length && entry.BiomeIndex >= 0 && entry.BiomeIndex < biomeCount)
				{
					map[entry.LayerIndex][entry.BiomeIndex] = true;
				}
			}
			return map;
		}

		/// <summary>The fallback height frame: the scene's own lowest to highest ground.</summary>
		private static Func<float, float, float, float> SceneRelativeHeight(List<Terrain> tiles, TerrainScatterReport report)
		{
			float lowest = float.MaxValue, highest = float.MinValue;
			foreach (Terrain terrain in tiles)
			{
				TerrainData data = terrain.terrainData;
				int resolution = data.heightmapResolution;
				float[,] heights = data.GetHeights(0, 0, resolution, resolution);
				float min = float.MaxValue, max = float.MinValue;
				foreach (float h in heights)
				{
					min = Mathf.Min(min, h);
					max = Mathf.Max(max, h);
				}
				float y = terrain.transform.position.y;
				lowest = Mathf.Min(lowest, y + min * data.size.y);
				highest = Mathf.Max(highest, y + max * data.size.y);
			}
			report.Warnings.Add("No planet height normalisation was supplied; height bands were read against this scene's own lowest-to-highest ground, so they will not mean the same altitude in another scene.");
			float span = Mathf.Max(1e-3f, highest - lowest);
			return (x, y, z) => Mathf.Clamp01((y - lowest) / span);
		}

		// ── One tile ─────────────────────────────────────────────────

		/// <summary>Per-tile arrays, reused from tile to tile so a scene allocates them once.</summary>
		private sealed class Workspace
		{
			public readonly float[] BiomeWeights;
			public readonly float[] BiomeShare;
			public readonly int[] BiomeUsedDetail;
			public readonly int[] BiomeUsedTree;
			public float[] Heights = Array.Empty<float>();
			public float[] Slopes = Array.Empty<float>();
			public bool[] Solid;
			public float[][] Layers = Array.Empty<float[]>();
			public float[] LayerMax = Array.Empty<float>();
			public readonly float[][] Attribution;
			public readonly float[] AttributionMax;
			public readonly List<int[,]> DetailBuffers = new List<int[,]>();
			public readonly List<int> DetailPlaced = new List<int>();
			public int DetailBufferResolution;
			public readonly List<TreeInstance> Trees = new List<TreeInstance>(4096);
			/// <summary>Metres each of <see cref="Trees"/> is sunk below the surface, index for index.</summary>
			public readonly List<float> TreeSinks = new List<float>(4096);
			/// <summary>Each tree prototype's <see cref="FootprintRadius"/>, by prototype index.</summary>
			public float[] TreeFootprints = Array.Empty<float>();
			/// <summary>Summed carpet coverage per detail cell, for sharing a cell between carpets.</summary>
			public float[] CarpetSum = Array.Empty<float>();
			/// <summary>The carpets that reach the current tile.</summary>
			public readonly List<ScatterRule> TileCarpets = new List<ScatterRule>();
			/// <summary>The scene's placement climate, for climate-banded rules; null when it has none.</summary>
			public ScenePlacementClimate Placement;
			/// <summary>The climate over the current tile, built the first time a banded rule asks (null until then).</summary>
			public TerrainClimateGrid Climate;
			/// <summary>One chunk of a rule's visiting order, worked out before it is walked (<see cref="SampleRule"/>).</summary>
			public readonly CellPick[] Picks = new CellPick[PickChunk];
			/// <summary>The units a carpet wrote into each cell, for counting what it covered in row order.</summary>
			public int[] CarpetUnits;
			/// <summary>Each texel's biome weights for a band of alphamap rows (<see cref="ReadBiomes"/>).</summary>
			public float[] BandWeights = Array.Empty<float>();

			public Workspace(int biomes, int attributions)
			{
				BiomeWeights = new float[Math.Max(1, biomes)];
				BiomeShare = new float[Math.Max(1, biomes)];
				BiomeUsedDetail = new int[Math.Max(1, biomes)];
				BiomeUsedTree = new int[Math.Max(1, biomes)];
				Attribution = new float[attributions][];
				AttributionMax = new float[attributions];
			}

			public static float[] Fit(float[] array, int length)
			{
				return array != null && array.Length == length ? array : new float[length];
			}
		}

		/// <summary>Where on a tile, and in what frame, its cells are.</summary>
		private struct TileFrame
		{
			public Vector3 Origin;
			public Vector3 Size;
			public int HeightResolution;
			public int AlphaResolution;
			public int HolesResolution;
		}

		private static void ScatterTile(Terrain terrain, List<ScatterRule> rules, DetailPrototype[] detailPrototypes, TreePrototype[] treePrototypes,
			SceneTerrainPalette palette, SceneBiomeField field, bool[][] layerBiomes, List<SceneTerrainPalette.Entry> attributionEntries,
			Func<float, float, float, float> normalizedHeight, TerrainScatterOptions options, Workspace work, bool firstTile, TerrainScatterReport report)
		{
			TerrainData data = terrain.terrainData;
			long phase = Stopwatch.GetTimestamp();
			double Lap()
			{
				long now = Stopwatch.GetTimestamp();
				double seconds = (now - phase) / (double)Stopwatch.Frequency;
				phase = now;
				return seconds;
			}
			var frame = new TileFrame
			{
				Origin = terrain.transform.position,
				Size = data.size,
				HeightResolution = data.heightmapResolution,
				AlphaResolution = data.alphamapResolution,
				HolesResolution = data.holesResolution,
			};

			// ── Clear and configure: the same, empty or not, so a re-run never leaves last time's grass.
			data.treeInstances = Array.Empty<TreeInstance>();
			data.treePrototypes = Array.Empty<TreePrototype>();
			data.detailPrototypes = Array.Empty<DetailPrototype>();
			int perPatch = Mathf.Clamp(options.DetailResolutionPerPatch, 8, 128);
			int detailResolution = DetailResolutionFor(frame.Size.x, perPatch, options);
			data.SetDetailResolution(detailResolution, perPatch);
			data.SetDetailScatterMode(options.DetailScatterMode);
			data.detailPrototypes = detailPrototypes;
			data.treePrototypes = treePrototypes;
			data.RefreshPrototypes();
			detailResolution = data.detailResolution;
			CalibrateCountedDetails(data, detailPrototypes, rules, firstTile ? report : null);
			report.DetailResolution = detailResolution;

			if (options.ApplyDrawSettings)
			{
				terrain.detailObjectDistance = Mathf.Max(0f, options.DetailObjectDistance);
				terrain.detailObjectDensity = Mathf.Clamp01(options.DetailObjectDensity);
				terrain.treeDistance = Mathf.Max(0f, options.TreeDistance);
				terrain.treeBillboardDistance = Mathf.Max(5f, options.TreeBillboardDistance);
				terrain.treeCrossFadeLength = Mathf.Max(0f, options.TreeCrossFadeLength);
			}

			if (firstTile)
			{
				WarnVertexBudget(data, detailPrototypes, terrain.detailObjectDensity, report);
				if (data.terrainLayers == null || data.terrainLayers.Length != palette.Layers.Count)
				{
					report.Warnings.Add($"'{terrain.name}' has {(data.terrainLayers == null ? 0 : data.terrainLayers.Length)} terrain layers but the palette has {palette.Layers.Count}; channels may not mean what the rules expect. Write the splat before scattering.");
				}
			}

			bool anyRunnable = false;
			foreach (ScatterRule rule in rules)
			{
				anyRunnable |= rule.Runnable;
			}
			if (!anyRunnable)
			{
				Finish(terrain, data, work);
				return;
			}

			report.SetupSeconds += Lap();
			ReadGround(data, frame, work);
			// Last tile's climate is not this one's; rebuilt only if a banded rule reaches this tile.
			work.Climate = null;
			ReadAlphamaps(data, frame, rules, palette.Layers.Count, work, terrain.name, report);
			ReadBiomes(field, frame, layerBiomes, attributionEntries, work);
			report.ReadSeconds += Lap();

			Array.Clear(work.BiomeUsedDetail, 0, work.BiomeUsedDetail.Length);
			Array.Clear(work.BiomeUsedTree, 0, work.BiomeUsedTree.Length);
			int tileDetailUsed = 0;
			int tileTreesUsed = 0;
			work.Trees.Clear();
			work.TreeSinks.Clear();
			EnsureDetailBuffers(work, detailResolution, rules);

			int maxValue = Mathf.Max(1, data.maxDetailScatterPerRes);

			foreach (ScatterRule rule in rules)
			{
				// Carpets run after, all together: they share cells with each other, not with budgets.
				if (!rule.Runnable || rule.Carpet)
				{
					continue;
				}
				int layer = rule.Entry.LayerIndex;
				float[] weights = layer < work.Layers.Length ? work.Layers[layer] : null;
				if (weights == null || work.LayerMax[layer] < rule.Rule.minTextureWeight || work.AttributionMax[rule.Attribution] <= 0f)
				{
					// The rule's ground or its biome is not on this tile at all; nothing to visit.
					continue;
				}

				bool tree = rule.Rule.spawnChannel == PrefabSpawnChannel.TreeInstance;
				int biome = rule.Entry.BiomeIndex;
				int tileBudget = tree ? options.MaxTreesPerTile : options.MaxDetailSpawnsPerTile;
				int biomeBudget = Mathf.CeilToInt(tileBudget * Mathf.Max(options.MinimumBiomeBudgetShare, work.BiomeShare[biome]));
				int[] biomeUsed = tree ? work.BiomeUsedTree : work.BiomeUsedDetail;
				int tileUsed = tree ? tileTreesUsed : tileDetailUsed;

				var budget = new Budget
				{
					RuleLeft = rule.Rule.maxPerChunk > 0 ? rule.Rule.maxPerChunk : int.MaxValue,
					BiomeLeft = Mathf.Max(0, biomeBudget - biomeUsed[biome]),
					TileLeft = Mathf.Max(0, tileBudget - tileUsed),
				};

				int placed;
				if (tree)
				{
					int cellsX = Mathf.Max(1, Mathf.RoundToInt(frame.Size.x / Mathf.Max(0.25f, options.TreeCellMetres)));
					int cellsZ = Mathf.Max(1, Mathf.RoundToInt(frame.Size.z / Mathf.Max(0.25f, options.TreeCellMetres)));
					placed = SampleRule(rule, frame, work, weights, work.Attribution[rule.Attribution], cellsX, cellsZ, true,
						normalizedHeight, ref budget, work.Trees, null, 0, 0);
					tileTreesUsed += placed;
				}
				else
				{
					int value = Mathf.Clamp(Mathf.RoundToInt(rule.DetailValue / 255f * maxValue), 1, maxValue);
					placed = SampleRule(rule, frame, work, weights, work.Attribution[rule.Attribution], detailResolution, detailResolution, false,
						normalizedHeight, ref budget, null, work.DetailBuffers, value, maxValue);
					tileDetailUsed += placed;
					WriteDetails(data, rule, work, report);
				}
				biomeUsed[biome] += placed;
				rule.Outcome.Placed += placed;

				if (budget.StoppedBy != null)
				{
					rule.Outcome.BudgetStops++;
					report.BudgetCaps.Add($"'{terrain.name}': {rule.Outcome.Biome} / {rule.Outcome.Slot} / '{rule.Outcome.RuleName}' stopped at {placed:N0} by the {budget.StoppedBy}.");
				}
			}

			report.RuleSeconds += Lap();
			RunCarpets(data, rules, frame, work, detailResolution, maxValue, normalizedHeight, report);
			report.CarpetSeconds += Lap();

			report.DetailCellsCovered += tileDetailUsed;
			report.TreesPlaced += tileTreesUsed;
			Finish(terrain, data, work);
			report.WriteSeconds += Lap();
		}

		private static void Finish(Terrain terrain, TerrainData data, Workspace work)
		{
			/* The heightmap is the authority, as when snapping: each tree stands at the terrain's own
			 * interpolated height, less its sink, and is written with snapping off so the sink stays. */
			TreeInstance[] trees = work.Trees.ToArray();
			float sizeY = Mathf.Max(1e-4f, data.size.y);
			for (int i = 0; i < trees.Length; i++)
			{
				Vector3 position = trees[i].position;
				float ground = data.GetInterpolatedHeight(position.x, position.z);
				float sink = i < work.TreeSinks.Count ? work.TreeSinks[i] : 0f;
				position.y = Mathf.Clamp01((ground - sink) / sizeY);
				trees[i].position = position;
			}
			data.SetTreeInstances(trees, false);
			work.Trees.Clear();
			work.TreeSinks.Clear();
			terrain.Flush();
			EditorUtility.SetDirty(data);
			EditorUtility.SetDirty(terrain);
		}

		private static int DetailResolutionFor(float tileMetres, int perPatch, TerrainScatterOptions options)
		{
			int resolution = options.DetailResolution > 0
				? options.DetailResolution
				: Mathf.CeilToInt(tileMetres / Mathf.Max(0.05f, options.DetailCellMetres));
			// A whole number of patches, within Unity's limit.
			resolution = (resolution + perPatch - 1) / perPatch * perPatch;
			int ceiling = MaximumDetailResolution / perPatch * perPatch;
			return Mathf.Clamp(resolution, perPatch, ceiling);
		}

		/// <summary>
		/// Unity's own warning, made at bake time: a non-instanced detail patch is one combined mesh,
		/// and past 65,536 vertices it stops drawing correctly.
		/// </summary>
		private static void WarnVertexBudget(TerrainData data, DetailPrototype[] prototypes, float density, TerrainScatterReport report)
		{
			int patches = Mathf.Max(1, data.detailPatchCount);
			float patchArea = data.size.x / patches * (data.size.z / patches);
			for (int i = 0; i < prototypes.Length; i++)
			{
				DetailPrototype prototype = prototypes[i];
				if (prototype.useInstancing || prototype.prototype == null || !prototype.prototype.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
				{
					continue;
				}
				float instances = data.ComputeDetailCoverage(i) * patchArea;
				if (filter.sharedMesh.vertexCount * instances * density >= 65536f)
				{
					report.Warnings.Add($"Detail '{prototype.prototype.name}' is not instanced and a full patch of it is ~{filter.sharedMesh.vertexCount * instances * density:N0} vertices (over 65,536): enable GPU instancing on its material.");
				}
			}
		}

		/// <summary>Heights, slopes and holes of a tile, flattened for the hot loop.</summary>
		private static void ReadGround(TerrainData data, TileFrame frame, Workspace work)
		{
			int res = frame.HeightResolution;
			float[,] heights = data.GetHeights(0, 0, res, res);
			work.Heights = Workspace.Fit(work.Heights, res * res);
			work.Slopes = Workspace.Fit(work.Slopes, res * res);
			float[] flat = work.Heights;
			for (int z = 0; z < res; z++)
			{
				for (int x = 0; x < res; x++)
				{
					flat[z * res + x] = heights[z, x];
				}
			}

			/* Slope from central differences in metres, one-sided at the tile's edge, in degrees —
			 * what GetSteepness answers, without a native call per candidate. */
			float stepX = frame.Size.x / (res - 1);
			float stepZ = frame.Size.z / (res - 1);
			float rise = frame.Size.y;
			for (int z = 0; z < res; z++)
			{
				int z0 = Math.Max(0, z - 1), z1 = Math.Min(res - 1, z + 1);
				for (int x = 0; x < res; x++)
				{
					int x0 = Math.Max(0, x - 1), x1 = Math.Min(res - 1, x + 1);
					float gx = (flat[z * res + x1] - flat[z * res + x0]) * rise / ((x1 - x0) * stepX);
					float gz = (flat[z1 * res + x] - flat[z0 * res + x]) * rise / ((z1 - z0) * stepZ);
					work.Slopes[z * res + x] = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
				}
			}

			// Holes only when the tile has any: true is solid ground.
			work.Solid = null;
			int holes = frame.HolesResolution;
			if (holes > 0)
			{
				bool[,] solid = data.GetHoles(0, 0, holes, holes);
				bool anyHole = false;
				foreach (bool s in solid)
				{
					if (!s)
					{
						anyHole = true;
						break;
					}
				}
				if (anyHole)
				{
					work.Solid = new bool[holes * holes];
					for (int z = 0; z < holes; z++)
					{
						for (int x = 0; x < holes; x++)
						{
							work.Solid[z * holes + x] = solid[z, x];
						}
					}
				}
			}
		}

		/// <summary>The alphamap channels any runnable rule is gated on, flattened, with each one's peak.</summary>
		private static void ReadAlphamaps(TerrainData data, TileFrame frame, List<ScatterRule> rules, int paletteLayers,
			Workspace work, string tileName, TerrainScatterReport report)
		{
			int res = frame.AlphaResolution;
			int layers = data.alphamapLayers;
			if (work.Layers.Length != paletteLayers)
			{
				work.Layers = new float[paletteLayers][];
				work.LayerMax = new float[paletteLayers];
			}
			var needed = new bool[paletteLayers];
			foreach (ScatterRule rule in rules)
			{
				if (!rule.Runnable)
				{
					continue;
				}
				int layer = rule.Entry.LayerIndex;
				if (layer < layers)
				{
					needed[layer] = true;
				}
				else
				{
					report.Warnings.Add($"'{tileName}' has {layers} alphamap channels, so {rule.Outcome.Biome} / {rule.Outcome.Slot} / '{rule.Outcome.RuleName}' (channel {layer}) has no ground there.");
				}
			}

			float[,,] maps = layers > 0 ? data.GetAlphamaps(0, 0, res, res) : null;
			for (int l = 0; l < paletteLayers; l++)
			{
				if (!needed[l] || maps == null)
				{
					work.Layers[l] = null;
					work.LayerMax[l] = 0f;
					continue;
				}
				float[] flat = work.Layers[l] = Workspace.Fit(work.Layers[l], res * res);
				float peak = 0f;
				for (int y = 0; y < res; y++)
				{
					for (int x = 0; x < res; x++)
					{
						// Unity's alphamaps are [y, x, layer].
						float w = maps[y, x, l];
						flat[y * res + x] = w;
						peak = Mathf.Max(peak, w);
					}
				}
				work.LayerMax[l] = peak;
			}
		}

		/// <summary>
		/// Each rule entry's share of its channel at every alphamap texel, and each biome's share of
		/// the tile for the budget split. One field read per texel, shared by every rule.
		/// </summary>
		private static void ReadBiomes(SceneBiomeField field, TileFrame frame, bool[][] layerBiomes,
			List<SceneTerrainPalette.Entry> entries, Workspace work)
		{
			int res = frame.AlphaResolution;
			int biomes = field.Biomes.Count;
			Array.Clear(work.BiomeShare, 0, work.BiomeShare.Length);
			for (int e = 0; e < entries.Count; e++)
			{
				work.Attribution[e] = Workspace.Fit(work.Attribution[e], res * res);
				work.AttributionMax[e] = 0f;
			}

			/* The sum each entry divides by is per layer, not per entry: work out which biomes share
			 * each distinct layer once, then a texel costs one sum per layer and one division per
			 * entry rather than a pass over every biome for every entry. */
			var layerSlot = new Dictionary<int, int>();
			var entrySlot = new int[entries.Count];
			var sharers = new List<int[]>();
			for (int e = 0; e < entries.Count; e++)
			{
				int layer = entries[e].LayerIndex;
				if (!layerSlot.TryGetValue(layer, out int slot))
				{
					slot = sharers.Count;
					layerSlot[layer] = slot;
					var biomesOnLayer = new List<int>();
					for (int b = 0; b < biomes; b++)
					{
						if (layerBiomes[layer][b])
						{
							biomesOnLayer.Add(b);
						}
					}
					sharers.Add(biomesOnLayer.ToArray());
				}
				entrySlot[e] = slot;
			}
			if (biomes == 0)
			{
				// No weights to read: every share stays 0, as the texel-by-texel pass left it.
				for (int e = 0; e < entries.Count; e++)
				{
					Array.Clear(work.Attribution[e], 0, res * res);
				}
				return;
			}

			/* A band of rows at a time: each texel's weights and shares in parallel (the field only reads), then the
			 * biomes' shares of the tile summed on one thread, texel by texel in row order, as they always were. */
			const int BandRows = 32;
			int stride = Math.Max(1, biomes);
			if (work.BandWeights.Length < BandRows * res * stride)
			{
				work.BandWeights = new float[BandRows * res * stride];
			}
			float[] band = work.BandWeights;
			float[][] attribution = work.Attribution;
			int slots = sharers.Count;
			var most = new float[entries.Count];
			object gate = new object();
			var entryBiome = new int[entries.Count];
			for (int e = 0; e < entries.Count; e++)
			{
				entryBiome[e] = entries[e].BiomeIndex;
			}
			float step = 1f / (res - 1);
			for (int y0 = 0; y0 < res; y0 += BandRows)
			{
				int rows = Math.Min(BandRows, res - y0);
				Parallel.For(0, rows, Workers, () => (new float[stride], new float[slots], new float[entries.Count]), (r, _, local) =>
				{
					(float[] weights, float[] sums, float[] peak) = local;
					int y = y0 + r;
					// Alphamap texels sit on the tile's corners (texel i at i / (res - 1)), as the terrain shader samples them.
					float worldZ = frame.Origin.z + y * step * frame.Size.z;
					for (int x = 0; x < res; x++)
					{
						float worldX = frame.Origin.x + x * step * frame.Size.x;
						field.WeightsAt(worldX, worldZ, weights);
						Array.Copy(weights, 0, band, (r * res + x) * stride, biomes);
						for (int l = 0; l < slots; l++)
						{
							int[] onLayer = sharers[l];
							float sum = 0f;
							for (int n = 0; n < onLayer.Length; n++)
							{
								sum += weights[onLayer[n]];
							}
							sums[l] = sum;
						}
						int texel = y * res + x;
						for (int e = 0; e < entryBiome.Length; e++)
						{
							float own = weights[entryBiome[e]];
							float sum = sums[entrySlot[e]];
							float share = own > 0f && sum > 0f ? own / sum : 0f;
							attribution[e][texel] = share;
							if (share > peak[e])
							{
								peak[e] = share;
							}
						}
					}
					return local;
				}, local =>
				{
					lock (gate)
					{
						for (int e = 0; e < most.Length; e++)
						{
							most[e] = Mathf.Max(most[e], local.Item3[e]);
						}
					}
				});
				for (int t = 0; t < rows * res; t++)
				{
					for (int b = 0; b < biomes; b++)
					{
						work.BiomeShare[b] += band[t * stride + b];
					}
				}
			}
			for (int e = 0; e < entries.Count; e++)
			{
				// The largest share is the same whichever texel is met first.
				work.AttributionMax[e] = Mathf.Max(work.AttributionMax[e], most[e]);
			}
			float texels = res * res;
			for (int b = 0; b < biomes; b++)
			{
				work.BiomeShare[b] /= texels;
			}
		}

		private static void EnsureDetailBuffers(Workspace work, int resolution, List<ScatterRule> rules)
		{
			int variants = 0;
			foreach (ScatterRule rule in rules)
			{
				if (rule.Runnable && rule.Rule.spawnChannel == PrefabSpawnChannel.DetailLayer)
				{
					variants = Math.Max(variants, rule.Prototypes.Count);
				}
			}
			if (work.DetailBufferResolution != resolution)
			{
				work.DetailBuffers.Clear();
				work.DetailBufferResolution = resolution;
			}
			while (work.DetailBuffers.Count < variants)
			{
				work.DetailBuffers.Add(new int[resolution, resolution]);
			}
			while (work.DetailPlaced.Count < work.DetailBuffers.Count)
			{
				work.DetailPlaced.Add(0);
			}
		}

		/// <summary>Writes a detail rule's buffers into its prototypes' layers and clears them for the next rule.</summary>
		private static void WriteDetails(TerrainData data, ScatterRule rule, Workspace work, TerrainScatterReport report)
		{
			long began = Stopwatch.GetTimestamp();
			WriteDetailLayers(data, rule, work);
			report.DetailLayerSeconds += (Stopwatch.GetTimestamp() - began) / (double)Stopwatch.Frequency;
		}

		private static void WriteDetailLayers(TerrainData data, ScatterRule rule, Workspace work)
		{
			for (int v = 0; v < rule.Prototypes.Count; v++)
			{
				if (work.DetailPlaced[v] == 0)
				{
					// Layers start empty after the prototypes are assigned; an empty write is a wasted megabyte.
					continue;
				}
				int[,] buffer = work.DetailBuffers[v];
				data.SetDetailLayer(0, 0, rule.Prototypes[v], buffer);
				Array.Clear(buffer, 0, buffer.Length);
				work.DetailPlaced[v] = 0;
			}
		}

		// ── The hot loop ─────────────────────────────────────────────

		/// <summary>What is left of the three budgets a rule draws on, on one tile.</summary>
		private struct Budget
		{
			public int RuleLeft;
			public int BiomeLeft;
			public int TileLeft;
			public string StoppedBy;
		}

		/// <summary>Why a visited cell placed nothing, or that it passed every test but the spacing.</summary>
		private enum CellOutcome : byte
		{
			Passed,
			NoWeight,
			NoChance,
			NoBiome,
			NoHole,
			NoHeight,
			NoSlope,
		}

		/// <summary>
		/// What one visited cell decided before the spacing and the budgets: everything that is the cell's alone (its
		/// texture weight, roll, stands, groups, biome share, hole, water, keep-outs, banks, height, slope and climate
		/// bands, its variant and its tree or detail value), so it can be worked out on any thread.
		/// </summary>
		private struct CellPick
		{
			public CellOutcome Outcome;
			public int I, J, Variant, Value;
			public float WorldX, WorldZ, PointSpacing, Sink;
			public TreeInstance Tree;
		}

		/// <summary>Steps of the visiting order worked out together before they are walked: a budget that stops a rule wastes at most this many.</summary>
		private const int PickChunk = 16384;

		/// <summary>Steps one worker takes at a time inside a chunk: most cells are refused at their first test, which is too little work to hand out one by one.</summary>
		private const int PickBlock = 512;

		/// <summary>
		/// For the equivalence tests only: <see cref="SampleRule"/> works out and walks one cell at a time, on the calling
		/// thread, as it did before the chunks. A tile must come out the same either way.
		/// </summary>
		internal static bool VisitOneByOne;

		private static ParallelOptions Workers => new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };

		/// <summary>
		/// Visits every cell of one tile for one rule, in shuffled order, and places what passes.
		/// Returns how many were placed.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The cheap refusals come first — texture weight, then the density draw, which turns down most of what is
		/// left — so the biome share, the height delegate and the spacing test only run for candidates that could
		/// still be placed.
		/// </para>
		/// <para>
		/// <b>Two passes over each chunk of the visiting order.</b> What a cell decides on its own (<see cref="CellPick"/>)
		/// is worked out for a chunk of steps in parallel ( <see cref="RuleCells.Evaluate"/>: pure reads of the tile and of
		/// fields that are functions of position); then the chunk is walked in the visiting order, on one thread, for what
		/// depends on the cells before it: the spacing, the budgets, the tree list's order and the detail writes. A cell's
		/// numbers come from its own hash, so the walk places exactly what visiting the cells one by one did, and counts
		/// the same refusals up to where a budget stopped it.
		/// </para>
		/// </remarks>
		private static int SampleRule(ScatterRule scatterRule, TileFrame frame, Workspace work, float[] layerWeights, float[] attribution,
			int cellsX, int cellsZ, bool tree, Func<float, float, float, float> normalizedHeight,
			ref Budget budget, List<TreeInstance> trees, List<int[,]> detailBuffers, int detailValue, int detailMax)
		{
			PrefabSpawnRule rule = scatterRule.Rule;
			if (budget.RuleLeft <= 0 || budget.BiomeLeft <= 0 || budget.TileLeft <= 0)
			{
				budget.StoppedBy = budget.RuleLeft <= 0 ? "rule's maxPerChunk" : budget.BiomeLeft <= 0 ? "biome's share of the tile budget" : "tile budget";
				return 0;
			}
			if (scatterRule.Climate)
			{
				// Built here, once, so the workers only ever read it.
				EnsureClimate(frame, work);
			}

			var cells = new RuleCells(scatterRule, frame, work, layerWeights, attribution, cellsX, cellsZ, tree, normalizedHeight, detailValue, detailMax);
			int total = cells.Total;
			ScatterSpacing spacing = scatterRule.Spacing;
			bool canopy = cells.Canopy;
			List<int> prototypes = scatterRule.Prototypes;

			// The permutation: offset + k·stride (mod total), stride coprime with total.
			uint order = Hash(scatterRule.Seed ^ 0xA511E9B3u, cells.Gx0, cells.Gz0);
			int stride = CoprimeStride(total, order);
			int current = (int)(Mix(order + 1u) % (uint)total);

			CellPick[] picks = work.Picks;
			// Refusals counted in locals and added once: the outcome is a heap object.
			long noWeight = 0, noChance = 0, noBiome = 0, noHole = 0, noHeight = 0, noSlope = 0, noSpacing = 0;
			int placed = 0;
			bool stopped = false;
			int chunk = VisitOneByOne ? 1 : PickChunk;
			for (int k0 = 0; k0 < total && !stopped; k0 += chunk)
			{
				int steps = Math.Min(chunk, total - k0);
				int start = current;
				if (VisitOneByOne)
				{
					cells.Evaluate(start, ref picks[0]);
				}
				else
				{
					int blocks = (steps + PickBlock - 1) / PickBlock;
					Parallel.For(0, blocks, Workers, b =>
					{
						int from = b * PickBlock, to = Math.Min(steps, from + PickBlock);
						for (int s = from; s < to; s++)
						{
							cells.Evaluate((int)((start + (long)s * stride) % total), ref picks[s]);
						}
					});
				}
				current = (int)((start + (long)steps * stride) % total);

				for (int s = 0; s < steps; s++)
				{
					int k = k0 + s;
					switch (picks[s].Outcome)
					{
						case CellOutcome.NoWeight: noWeight++; continue;
						case CellOutcome.NoChance: noChance++; continue;
						case CellOutcome.NoBiome: noBiome++; continue;
						case CellOutcome.NoHole: noHole++; continue;
						case CellOutcome.NoHeight: noHeight++; continue;
						case CellOutcome.NoSlope: noSlope++; continue;
					}
					float worldX = picks[s].WorldX, worldZ = picks[s].WorldZ, pointSpacing = picks[s].PointSpacing;
					if (spacing != null && !(canopy ? spacing.IsClear(worldX, worldZ, pointSpacing) : spacing.IsClear(worldX, worldZ)))
					{
						noSpacing++;
						continue;
					}

					int variant = picks[s].Variant;
					if (tree)
					{
						trees.Add(picks[s].Tree);
						work.TreeSinks.Add(picks[s].Sink);
					}
					else
					{
						// Unity's detail maps are [y, x].
						detailBuffers[variant][picks[s].J, picks[s].I] = picks[s].Value;
						work.DetailPlaced[variant]++;
					}
					if (canopy)
					{
						spacing.Add(worldX, worldZ, pointSpacing);
					}
					else
					{
						spacing?.Add(worldX, worldZ);
					}
					placed++;

					if (--budget.RuleLeft <= 0 || --budget.BiomeLeft <= 0 || --budget.TileLeft <= 0)
					{
						if (k + 1 < total)
						{
							budget.StoppedBy = budget.RuleLeft <= 0 ? "rule's maxPerChunk" : budget.BiomeLeft <= 0 ? "biome's share of the tile budget" : "tile budget";
						}
						stopped = true;
						break;
					}
				}
			}

			TerrainScatterReport.RuleOutcome outcome = scatterRule.Outcome;
			outcome.RejectedTextureWeight += noWeight;
			outcome.RejectedChance += noChance;
			outcome.RejectedBiome += noBiome;
			outcome.RejectedHole += noHole;
			outcome.RejectedHeight += noHeight;
			outcome.RejectedSlope += noSlope;
			outcome.RejectedSpacing += noSpacing;
			return placed;
		}

		/// <summary>One rule's view of one tile for <see cref="SampleRule"/>: what every cell's own tests read, read-only.</summary>
		private sealed class RuleCells
		{
			private readonly ScatterRule scatterRule;
			private readonly PrefabSpawnRule rule;
			private readonly TileFrame frame;
			private readonly Workspace work;
			private readonly float[] layerWeights, attribution, heights, slopes;
			private readonly bool[] solid;
			private readonly int cellsX, cellsZ, alphaRes, heightRes, holesRes, variants, detailValue, detailMax;
			private readonly bool tree, useHeight, useSlope, clustered, forest;
			private readonly Func<float, float, float, float> normalizedHeight;
			private readonly float baseChance, minWeight, heightFalloff, slopeFalloff, clusterMetres, clusterPitch, forestMix, forestBias, forestCeiling;
			private readonly MinMaxRange heightRange, slopeRange;
			private readonly uint seed, clusterSeed;
			private readonly StandField stands;
			private readonly List<int> prototypes;
			public readonly int Total, Gx0, Gz0;
			public readonly bool Canopy;

			public RuleCells(ScatterRule scatterRule, TileFrame frame, Workspace work, float[] layerWeights, float[] attribution,
				int cellsX, int cellsZ, bool tree, Func<float, float, float, float> normalizedHeight, int detailValue, int detailMax)
			{
				this.scatterRule = scatterRule;
				this.frame = frame;
				this.work = work;
				this.layerWeights = layerWeights;
				this.attribution = attribution;
				this.cellsX = cellsX;
				this.cellsZ = cellsZ;
				this.tree = tree;
				this.normalizedHeight = normalizedHeight;
				this.detailValue = detailValue;
				this.detailMax = detailMax;
				rule = scatterRule.Rule;

				Total = cellsX * cellsZ;
				float cellX = frame.Size.x / cellsX;
				float cellZ = frame.Size.z / cellsZ;
				// The chance before weight and shares: density per square metre times a cell's area.
				baseChance = rule.densityPer100m2 * 0.01f * cellX * cellZ * rule.spawnProbabilityMultiplier;
				minWeight = rule.minTextureWeight;
				useHeight = rule.useHeightConstraint;
				useSlope = rule.useSlopeConstraint;
				heightRange = rule.heightRange;
				slopeRange = rule.slopeRange;
				heightFalloff = rule.heightFalloff;
				slopeFalloff = rule.slopeFalloff;
				seed = scatterRule.Seed;
				prototypes = scatterRule.Prototypes;
				variants = prototypes.Count;
				clustered = rule.IsClustered;
				clusterSeed = scatterRule.ClusterSeed;
				clusterMetres = scatterRule.ClusterMetres;
				clusterPitch = scatterRule.ClusterPitch;
				forest = scatterRule.Forest && tree;
				stands = scatterRule.Stands;
				forestMix = forest ? Mathf.Clamp01(rule.forestMix) : 0f;
				forestBias = forest ? Mathf.Clamp01(rule.forestScaleBias) : 0f;
				Canopy = scatterRule.Canopy && scatterRule.Spacing != null;
				/* The most a forest rule's chance can be multiplied by — its stands' heart at full cover, in
				 * the thickest of its species patches — so a roll above it is refused before the field is
				 * read. Most of a forest rule's cells go that way (a tree cell's chance is a few per cent), and
				 * the field is the dearest thing in the loop. A clustered forest rule has no such bound. */
				forestCeiling = forest && !clustered ? stands.Ceiling * (1f + forestMix) : float.PositiveInfinity;

				/* Cells in world integer coordinates, continuous from one tile into the next. From the
				 * origin over the cell, not a tile index: three tiles centred on zero start at -1.5,
				 * -0.5 and +0.5 tile widths, and rounding those to tile indices gives two of them one
				 * index and one identical pattern. */
				Gx0 = Mathf.RoundToInt(frame.Origin.x / cellX);
				Gz0 = Mathf.RoundToInt(frame.Origin.z / cellZ);

				alphaRes = frame.AlphaResolution;
				heightRes = frame.HeightResolution;
				holesRes = frame.HolesResolution;
				heights = work.Heights;
				slopes = work.Slopes;
				solid = work.Solid;
			}

			/// <summary>Everything cell <paramref name="index"/> decides on its own; safe from any thread.</summary>
			public void Evaluate(int index, ref CellPick pick)
			{
				int i = index % cellsX;
				int j = index / cellsX;

				uint h = Hash(seed, Gx0 + i, Gz0 + j);
				float u, v;
				if (tree)
				{
					u = (i + Unit(Mix(h + 1u))) / cellsX;
					v = (j + Unit(Mix(h + 2u))) / cellsZ;
				}
				else
				{
					// A detail cell is a cell: Unity scatters inside it.
					u = (i + 0.5f) / cellsX;
					v = (j + 0.5f) / cellsZ;
				}

				float weight = Bilinear(layerWeights, alphaRes, u, v);
				if (weight < minWeight)
				{
					pick.Outcome = CellOutcome.NoWeight;
					return;
				}
				float worldX = frame.Origin.x + u * frame.Size.x;
				float worldZ = frame.Origin.z + v * frame.Size.z;
				float closeness = 0f;
				float standMask = 0f;
				float roll = Unit(h);
				float chance = baseChance * weight;
				float share = 0f;
				if (forest)
				{
					if (roll >= chance * forestCeiling)
					{
						pick.Outcome = CellOutcome.NoChance;
						return;
					}
					/* A forest rule's biome share shrinks its stands instead of thinning them: across a seam the
					 * woods recede to their hearts and break into copses, as a real forest edge does, rather
					 * than the whole forest going uniformly sparse. The open ground's lone trees fade with the
					 * share. Steep ground shrinks them too (thin soil, rock, windthrow), on top of the slope band. */
					share = Bilinear(attribution, alphaRes, u, v);
					if (share <= 0f)
					{
						pick.Outcome = CellOutcome.NoBiome;
						return;
					}
					float steep = Mathf.Clamp01(Bilinear(slopes, heightRes, u, v) / scatterRule.SlopeReference);
					chance *= stands.Intensity(worldX, worldZ, share * (1f - StandSlopeThinning * steep * steep), share, out standMask);
					if (forestMix > 0f)
					{
						chance *= SpeciesPatch(scatterRule.MixSeed, worldX, worldZ, scatterRule.PatchMetres, forestMix);
					}
				}
				if (clustered)
				{
					chance *= ClusterIntensity(clusterSeed, worldX, worldZ, clusterMetres, clusterPitch, rule.clusterBackground, out closeness);
				}
				if (roll >= chance)
				{
					pick.Outcome = CellOutcome.NoChance;
					return;
				}

				if (!forest)
				{
					share = Bilinear(attribution, alphaRes, u, v);
					if (share <= 0f)
					{
						pick.Outcome = CellOutcome.NoBiome;
						return;
					}
					chance *= share;
				}

				if (solid != null)
				{
					int hx = Math.Min(holesRes - 1, (int)(u * holesRes));
					int hz = Math.Min(holesRes - 1, (int)(v * holesRes));
					if (!solid[hz * holesRes + hx])
					{
						pick.Outcome = CellOutcome.NoHole;
						return;
					}
				}

				float height01 = Bilinear(heights, heightRes, u, v);
				float worldY = frame.Origin.y + height01 * frame.Size.y;

				if (scatterRule.WaterGated)
				{
					// Counted with the height rejections: the water line is a height band of its own.
					float shore = ShoreBand(scatterRule, worldX, worldY, worldZ);
					if (shore <= 0f)
					{
						pick.Outcome = CellOutcome.NoHeight;
						return;
					}
					chance *= shore;
				}
				if (scatterRule.Excluded != null && scatterRule.Excluded(worldX, worldZ))
				{
					pick.Outcome = CellOutcome.NoHeight;
					return;
				}
				if (scatterRule.Inland != null)
				{
					float bank = InlandBand(scatterRule, worldX, worldY, worldZ);
					if (bank <= 0f)
					{
						pick.Outcome = CellOutcome.NoHeight;
						return;
					}
					chance *= bank;
				}
				if (useHeight)
				{
					float band = Band(normalizedHeight(worldX, worldY, worldZ), heightRange, heightFalloff);
					if (band <= 0f)
					{
						pick.Outcome = CellOutcome.NoHeight;
						return;
					}
					chance *= band;
				}
				if (useSlope)
				{
					float band = Band(Bilinear(slopes, heightRes, u, v), slopeRange, slopeFalloff);
					if (band <= 0f)
					{
						pick.Outcome = CellOutcome.NoSlope;
						return;
					}
					chance *= band;
				}
				if (scatterRule.Climate)
				{
					// Counted with the height rejections: up a slope, the band is the lapse rate's height band.
					float band = ClimateAt(scatterRule, frame, work, u, v, worldY);
					if (band <= 0f)
					{
						pick.Outcome = CellOutcome.NoHeight;
						return;
					}
					chance *= band;
				}
				if (scatterRule.Heat != null)
				{
					// The ground's heat (2026-10-18, I2q), counted with the height rejections like the climate's.
					float band = rule.HeatWeight(scatterRule.Heat(worldX, worldZ));
					if (band <= 0f)
					{
						pick.Outcome = CellOutcome.NoHeight;
						return;
					}
					chance *= band;
				}
				if (roll >= chance)
				{
					pick.Outcome = CellOutcome.NoChance;
					return;
				}

				int variant;
				if (forest && forestMix > 0f && variants > 1)
				{
					/* A rule's species stand in patches of their own (a birch grove inside the oaks) with a
					 * few strays across each patch's edge, rather than alternating tree by tree. */
					float pick01 = SpeciesPatch(scatterRule.VariantSeed, worldX, worldZ, scatterRule.PatchMetres, 1f) * 0.5f;
					pick01 = Mathf.Clamp01(pick01 + (Unit(Mix(h + 3u)) - 0.5f) * (1f - forestMix * 0.6f));
					variant = Math.Min(variants - 1, (int)(pick01 * variants));
				}
				else
				{
					variant = variants == 1 ? 0 : (int)(Mix(h + 3u) % (uint)variants);
				}

				pick.Outcome = CellOutcome.Passed;
				pick.I = i;
				pick.J = j;
				pick.Variant = variant;
				pick.WorldX = worldX;
				pick.WorldZ = worldZ;
				pick.PointSpacing = 0f;
				if (tree)
				{
					// Taller and narrower in a stand's heart (drawn up by its neighbours), shorter and broader in the open.
					float groupShift = clustered ? rule.clusterScaleBias * (closeness - 0.5f) : 0f;
					float standShift = forest ? forestBias * (standMask - 0.5f) : 0f;
					TreeInstance instance = TreeInstanceAt(rule, prototypes[variant], u, height01, v, h, groupShift + standShift, groupShift - 0.5f * standShift);
					if (Canopy)
					{
						pick.PointSpacing = rule.minSpacing * instance.widthScale * (scatterRule.CrownScale != null && variant < scatterRule.CrownScale.Length ? scatterRule.CrownScale[variant] : 1f);
					}
					pick.Tree = instance;
					float footprint = prototypes[variant] < work.TreeFootprints.Length ? work.TreeFootprints[prototypes[variant]] * instance.widthScale : 0f;
					float sinkSlope = rule.sinkSlopeFactor > 0f ? Bilinear(slopes, heightRes, u, v) : 0f;
					pick.Sink = SinkMetres(rule, footprint, sinkSlope, h);
				}
				else
				{
					/* A group's heart can ask for more than one in a cell; a draw cannot pass, so the rest
					 * goes on as cover, which keeps a group's count what its field says. */
					pick.Value = chance > 1f ? Math.Min(detailMax, (int)(detailValue * chance + 0.5f)) : detailValue;
				}
			}
		}

		/// <summary>
		/// A size draw along a rule's scale range leaned by its <see cref="PrefabSpawnRule.scaleSkew"/>: t^skew, exactly t at 1 (and
		/// for a skew left at 0 by an asset older than the field).
		/// </summary>
		public static float Skewed(float t, float skew) => skew > 0f && skew != 1f ? Mathf.Pow(Mathf.Clamp01(t), skew) : t;

		/// <summary>
		/// A tree at a sampled place, its size, turn and colour drawn from the cell's own hash so
		/// they never change while the cell does not.
		/// </summary>
		/// <remarks>
		/// The colour is a fixed per-instance variation around white — a little brightness and a
		/// little warm or cool — for the vegetation shader to tint seasonally on top of; a forest of
		/// identical tints would turn in autumn as one block. lightmapColor varies independently.
		/// Nothing seasonal is in either. <paramref name="heightShift"/> and <paramref name="widthShift"/>
		/// move the size's draws along the scale ranges (a clustered rule's lean toward large at a group's
		/// heart, small at its fringe; a forest rule's toward tall and narrow inside a stand, short and
		/// broad in the open); equal shifts of 0 leave it exactly as it was. A uniform scale follows the
		/// height's shift, which is the one a forest leans on hardest.
		/// </remarks>
		private static TreeInstance TreeInstanceAt(PrefabSpawnRule rule, int prototype, float u, float height01, float v, uint h, float heightShift, float widthShift)
		{
			float widthScale, heightScale;
			if (rule.useNonUniformScaling)
			{
				widthScale = Mathf.Max(0.05f, Mathf.Lerp(rule.widthScaleRange.x, rule.widthScaleRange.y, Skewed(Mathf.Clamp01(Unit(Mix(h + 4u)) + widthShift), rule.scaleSkew)));
				heightScale = Mathf.Max(0.05f, Mathf.Lerp(rule.heightScaleRange.x, rule.heightScaleRange.y, Skewed(Mathf.Clamp01(Unit(Mix(h + 5u)) + heightShift), rule.scaleSkew)));
			}
			else
			{
				widthScale = Mathf.Max(0.05f, Mathf.Lerp(rule.uniformScaleRange.x, rule.uniformScaleRange.y, Skewed(Mathf.Clamp01(Unit(Mix(h + 4u)) + heightShift), rule.scaleSkew)));
				heightScale = widthScale;
			}
			float degrees = Mathf.Lerp(rule.yRotationRange.x, rule.yRotationRange.y, Unit(Mix(h + 6u)));

			float brightness = Mathf.Lerp(0.84f, 1f, Unit(Mix(h + 7u)));
			float warmth = (Unit(Mix(h + 8u)) - 0.5f) * 0.08f;
			var color = new Color(
				Mathf.Clamp01(brightness + warmth),
				Mathf.Clamp01(brightness),
				Mathf.Clamp01(brightness - warmth), 1f);
			float lightmap = Mathf.Lerp(0.9f, 1f, Unit(Mix(h + 9u)));

			return new TreeInstance
			{
				position = new Vector3(u, Mathf.Clamp01(height01), v),
				widthScale = widthScale,
				heightScale = heightScale,
				rotation = degrees * Mathf.Deg2Rad,
				color = color,
				lightmapColor = new Color(lightmap, lightmap, lightmap, 1f),
				prototypeIndex = prototype,
			};
		}

		// ── Carpets ──────────────────────────────────────────────────

		/// <summary>
		/// Writes every carpet that reaches a tile: one pass summing their coverage per cell, one
		/// writing each one's share of it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Both passes run <see cref="CarpetCells.Coverage"/>, the same arithmetic, so the coverage a carpet adds to the
		/// sum is bit for bit the coverage it then divides by it. Storing each carpet's coverage instead would cost a
		/// float per cell per carpet — 64 MB per carpet at Unity's largest detail map — where recomputing costs one more
		/// pass of cheap arithmetic.
		/// </para>
		/// <para>
		/// <b>In parallel, rows at a time.</b> Every cell's coverage is its own. The sum takes the rows in parallel with
		/// the carpets inside each cell, in the carpets' order, so each cell's sum is added up in the same order as one
		/// carpet after another did. The writes take one carpet at a time (they share the detail buffers), its rows in
		/// parallel; what it covered is then counted on one thread in row order, so even the mean coverage it reports is
		/// summed as it always was.
		/// </para>
		/// </remarks>
		private static void RunCarpets(TerrainData data, List<ScatterRule> rules, TileFrame frame, Workspace work, int resolution, int maxValue,
			Func<float, float, float, float> normalizedHeight, TerrainScatterReport report)
		{
			work.TileCarpets.Clear();
			bool banded = false;
			foreach (ScatterRule rule in rules)
			{
				if (!rule.Runnable || !rule.Carpet)
				{
					continue;
				}
				int layer = rule.Entry.LayerIndex;
				float[] weights = layer < work.Layers.Length ? work.Layers[layer] : null;
				if (weights == null || work.AttributionMax[rule.Attribution] <= 0f
					|| work.LayerMax[layer] <= CarpetRampStart(rule.Rule.minTextureWeight, rule.Rule.carpetWeightRamp))
				{
					continue;
				}
				work.TileCarpets.Add(rule);
				banded |= rule.Climate;
			}
			if (work.TileCarpets.Count == 0)
			{
				return;
			}
			if (banded)
			{
				// Built here, once, so the workers only ever read it.
				EnsureClimate(frame, work);
			}

			int cells = resolution * resolution;
			work.CarpetSum = Workspace.Fit(work.CarpetSum, cells);
			Array.Clear(work.CarpetSum, 0, cells);
			int carpets = work.TileCarpets.Count;
			var views = new CarpetCells[carpets];
			for (int c = 0; c < carpets; c++)
			{
				ScatterRule rule = work.TileCarpets[c];
				views[c] = new CarpetCells(rule, frame, work, work.Layers[rule.Entry.LayerIndex], work.Attribution[rule.Attribution], resolution, normalizedHeight);
			}
			SumCarpets(views, work.CarpetSum, resolution);
			foreach (CarpetCells view in views)
			{
				WriteCarpet(view, work, resolution, maxValue);
				WriteDetails(data, view.Rule, work, report);
			}

			// What the tile's ground ended up carrying, every carpet together, quantised as the writes were.
			float[] sum = work.CarpetSum;
			for (int c = 0; c < cells; c++)
			{
				int units = (int)(Mathf.Min(1f, sum[c]) * maxValue);
				if (units > 0)
				{
					report.CarpetCellsCovered++;
					report.CarpetCoverageSum += units / (double)maxValue;
				}
			}
		}

		/// <summary>Why a carpet left a cell bare, or that it covered it.</summary>
		private enum CarpetOutcome
		{
			Covered,
			NoWeight,
			NoBiome,
			NoHole,
			NoHeight,
			NoSlope,
		}

		/// <summary>
		/// Every carpet's coverage of every cell added into <paramref name="sum"/>, rows in parallel, the carpets in order
		/// inside each cell; each carpet's refusals counted.
		/// </summary>
		private static void SumCarpets(CarpetCells[] views, float[] sum, int resolution)
		{
			int carpets = views.Length;
			// Per carpet: weight, biome, hole, height and slope refusals.
			var refused = new long[carpets * 5];
			object gate = new object();
			Parallel.For(0, resolution, Workers, () => new long[carpets * 5], (j, _, counts) =>
			{
				for (int i = 0; i < resolution; i++)
				{
					int cell = j * resolution + i;
					for (int c = 0; c < carpets; c++)
					{
						float coverage = views[c].Coverage(i, j, out CarpetOutcome outcome);
						if (outcome == CarpetOutcome.Covered)
						{
							sum[cell] += coverage;
						}
						else
						{
							counts[c * 5 + (int)outcome - 1]++;
						}
					}
				}
				return counts;
			}, counts =>
			{
				lock (gate)
				{
					for (int n = 0; n < counts.Length; n++)
					{
						refused[n] += counts[n];
					}
				}
			});
			for (int c = 0; c < carpets; c++)
			{
				TerrainScatterReport.RuleOutcome outcome = views[c].Rule.Outcome;
				outcome.RejectedTextureWeight += refused[c * 5];
				outcome.RejectedBiome += refused[c * 5 + 1];
				outcome.RejectedHole += refused[c * 5 + 2];
				outcome.RejectedHeight += refused[c * 5 + 3];
				outcome.RejectedSlope += refused[c * 5 + 4];
			}
		}

		/// <summary>
		/// One carpet's share of every cell — its coverage, scaled down where the carpets' sum passes a full cell —
		/// written into its detail buffers, rows in parallel.
		/// </summary>
		/// <remarks>
		/// Quantised down (never rounded up) to the detail map's integer scale, and split between the rule's
		/// prototypes as whole units with the remainder dealt from a per-cell hash, so two carpets at a
		/// half each, or one carpet's several variants, never sum past the full cell.
		/// </remarks>
		private static void WriteCarpet(CarpetCells view, Workspace work, int resolution, int maxValue)
		{
			ScatterRule scatterRule = view.Rule;
			List<int> prototypes = scatterRule.Prototypes;
			int variants = prototypes.Count;
			uint clumpSeed = scatterRule.ClumpSeed;
			float[] sum = work.CarpetSum;
			List<int[,]> buffers = work.DetailBuffers;
			int cells = resolution * resolution;
			int[] unitsOf = work.CarpetUnits = work.CarpetUnits != null && work.CarpetUnits.Length == cells ? work.CarpetUnits : new int[cells];
			int gx0 = Mathf.RoundToInt(view.Frame.Origin.x / (view.Frame.Size.x / resolution));
			int gz0 = Mathf.RoundToInt(view.Frame.Origin.z / (view.Frame.Size.z / resolution));

			// Per variant: cells written; then the cells shared with other carpets.
			var totals = new long[variants + 1];
			object gate = new object();
			Parallel.For(0, resolution, Workers, () => new long[variants + 1], (j, _, counts) =>
			{
				for (int i = 0; i < resolution; i++)
				{
					int cell = j * resolution + i;
					unitsOf[cell] = 0;
					float coverage = view.Coverage(i, j, out CarpetOutcome outcome);
					if (outcome != CarpetOutcome.Covered)
					{
						continue;
					}
					float total = sum[cell];
					if (total > 1f)
					{
						coverage /= total;
						counts[variants]++;
					}
					int units = (int)(coverage * maxValue);
					if (units <= 0)
					{
						continue;
					}
					unitsOf[cell] = units;
					if (variants == 1)
					{
						buffers[0][j, i] = units;
						counts[0]++;
					}
					else
					{
						// Whole units to every variant, the remainder dealt round from a per-cell start.
						int each = units / variants;
						int extra = units - each * variants;
						int start = (int)(Hash(clumpSeed, gx0 + i, gz0 + j) % (uint)variants);
						for (int n = 0; n < variants; n++)
						{
							int value = each + ((n - start + variants) % variants < extra ? 1 : 0);
							if (value > 0)
							{
								// Unity's detail maps are [y, x].
								buffers[n][j, i] = value;
								counts[n]++;
							}
						}
					}
				}
				return counts;
			}, counts =>
			{
				lock (gate)
				{
					for (int n = 0; n < counts.Length; n++)
					{
						totals[n] += counts[n];
					}
				}
			});
			for (int n = 0; n < variants; n++)
			{
				work.DetailPlaced[n] += (int)totals[n];
			}

			// What it covered, in row order: the mean is a double summed cell by cell, so the order is kept.
			long covered = 0;
			double coverageSum = 0d;
			for (int cell = 0; cell < cells; cell++)
			{
				int units = unitsOf[cell];
				if (units > 0)
				{
					covered++;
					coverageSum += units / (double)maxValue;
				}
			}
			TerrainScatterReport.RuleOutcome ruleOutcome = scatterRule.Outcome;
			ruleOutcome.Placed += covered;
			ruleOutcome.CoverageSum += coverageSum;
			ruleOutcome.SharedCells += totals[variants];
		}

		/// <summary>One carpet's view of one tile: what a cell's coverage reads, read-only, so cells can be worked on any thread.</summary>
		private sealed class CarpetCells
		{
			public readonly ScatterRule Rule;
			public readonly TileFrame Frame;
			private readonly Workspace work;
			private readonly float[] layerWeights, attribution, heights, slopes;
			private readonly bool[] solid;
			private readonly int resolution, alphaRes, heightRes, holesRes;
			private readonly float peak, minWeight, ramp, clumpMetres, clumpFloor, heightFalloff, slopeFalloff;
			private readonly uint clumpSeed;
			private readonly bool useHeight, useSlope;
			private readonly MinMaxRange heightRange, slopeRange;
			private readonly Func<float, float, float, float> normalizedHeight;

			public CarpetCells(ScatterRule scatterRule, TileFrame frame, Workspace work, float[] layerWeights, float[] attribution,
				int resolution, Func<float, float, float, float> normalizedHeight)
			{
				Rule = scatterRule;
				Frame = frame;
				this.work = work;
				this.layerWeights = layerWeights;
				this.attribution = attribution;
				this.resolution = resolution;
				this.normalizedHeight = normalizedHeight;
				PrefabSpawnRule rule = scatterRule.Rule;
				peak = Mathf.Clamp01(rule.carpetCoverage * rule.spawnProbabilityMultiplier);
				minWeight = rule.minTextureWeight;
				ramp = rule.carpetWeightRamp;
				clumpMetres = rule.carpetClumpMetres;
				clumpFloor = rule.carpetClumpFloor;
				clumpSeed = scatterRule.ClumpSeed;
				useHeight = rule.useHeightConstraint;
				useSlope = rule.useSlopeConstraint;
				heightRange = rule.heightRange;
				slopeRange = rule.slopeRange;
				heightFalloff = rule.heightFalloff;
				slopeFalloff = rule.slopeFalloff;
				alphaRes = frame.AlphaResolution;
				heightRes = frame.HeightResolution;
				holesRes = frame.HolesResolution;
				heights = work.Heights;
				slopes = work.Slopes;
				solid = work.Solid;
			}

			/// <summary>
			/// The carpet's coverage of cell (<paramref name="i"/>, <paramref name="j"/>): <c>carpetCoverage × ramp(texture
			/// weight) × biome share × bands × clump</c>, with the cheap refusals first; 0 and the reason where it has none.
			/// </summary>
			public float Coverage(int i, int j, out CarpetOutcome outcome)
			{
				float v = (j + 0.5f) / resolution;
				float u = (i + 0.5f) / resolution;
				ScatterRule scatterRule = Rule;
				TileFrame frame = Frame;

				float coverage = CarpetRamp(Bilinear(layerWeights, alphaRes, u, v), minWeight, ramp);
				if (coverage <= 0f)
				{
					outcome = CarpetOutcome.NoWeight;
					return 0f;
				}
				float share = Bilinear(attribution, alphaRes, u, v);
				if (share <= 0f)
				{
					outcome = CarpetOutcome.NoBiome;
					return 0f;
				}
				coverage *= share;

				if (solid != null)
				{
					int hx = Math.Min(holesRes - 1, (int)(u * holesRes));
					int hz = Math.Min(holesRes - 1, (int)(v * holesRes));
					if (!solid[hz * holesRes + hx])
					{
						outcome = CarpetOutcome.NoHole;
						return 0f;
					}
				}
				if (useSlope)
				{
					float band = Band(Bilinear(slopes, heightRes, u, v), slopeRange, slopeFalloff);
					if (band <= 0f)
					{
						outcome = CarpetOutcome.NoSlope;
						return 0f;
					}
					coverage *= band;
				}
				float worldX = frame.Origin.x + u * frame.Size.x;
				float worldZ = frame.Origin.z + v * frame.Size.z;
				if (scatterRule.WaterGated)
				{
					float bedY = frame.Origin.y + Bilinear(heights, heightRes, u, v) * frame.Size.y;
					float shore = ShoreBand(scatterRule, worldX, bedY, worldZ);
					if (shore <= 0f)
					{
						outcome = CarpetOutcome.NoHeight;
						return 0f;
					}
					coverage *= shore;
				}
				if (scatterRule.Excluded != null && scatterRule.Excluded(worldX, worldZ))
				{
					outcome = CarpetOutcome.NoHeight;
					return 0f;
				}
				if (scatterRule.Inland != null)
				{
					float bank = InlandBand(scatterRule, worldX, frame.Origin.y + Bilinear(heights, heightRes, u, v) * frame.Size.y, worldZ);
					if (bank <= 0f)
					{
						outcome = CarpetOutcome.NoHeight;
						return 0f;
					}
					coverage *= bank;
				}
				if (useHeight)
				{
					float worldY = frame.Origin.y + Bilinear(heights, heightRes, u, v) * frame.Size.y;
					float band = Band(normalizedHeight(worldX, worldY, worldZ), heightRange, heightFalloff);
					if (band <= 0f)
					{
						outcome = CarpetOutcome.NoHeight;
						return 0f;
					}
					coverage *= band;
				}
				if (scatterRule.Climate)
				{
					float groundY = frame.Origin.y + Bilinear(heights, heightRes, u, v) * frame.Size.y;
					float band = ClimateAt(scatterRule, frame, work, u, v, groundY);
					if (band <= 0f)
					{
						outcome = CarpetOutcome.NoHeight;
						return 0f;
					}
					coverage *= band;
				}
				if (scatterRule.Heat != null)
				{
					float band = scatterRule.Rule.HeatWeight(scatterRule.Heat(worldX, worldZ));
					if (band <= 0f)
					{
						outcome = CarpetOutcome.NoHeight;
						return 0f;
					}
					coverage *= band;
				}
				coverage *= peak * CarpetClump(clumpSeed, worldX, worldZ, clumpMetres, clumpFloor);
				outcome = CarpetOutcome.Covered;
				return coverage;
			}
		}

		/// <summary>
		/// A carpet's coverage against its texture's weight: a smoothstep from none to full across
		/// <paramref name="width"/>, centred on <paramref name="minWeight"/>.
		/// </summary>
		/// <remarks>
		/// The lower edge never goes below weight 0, so a carpet with a low threshold still has none
		/// where its texture is absent; it starts at 0 and stays continuous. Monotone in the weight by
		/// construction: more of the rule's ground never means less of the rule.
		/// </remarks>
		public static float CarpetRamp(float weight, float minWeight, float width)
		{
			float start = CarpetRampStart(minWeight, width);
			float end = Mathf.Max(start + 0.01f, minWeight + Mathf.Max(0.01f, width) * 0.5f);
			float t = (weight - start) / (end - start);
			if (t <= 0f)
			{
				return 0f;
			}
			if (t >= 1f)
			{
				return 1f;
			}
			return t * t * (3f - 2f * t);
		}

		/// <summary>The weight below which a carpet has no coverage.</summary>
		private static float CarpetRampStart(float minWeight, float width)
		{
			return Mathf.Max(0f, minWeight - Mathf.Max(0.01f, width) * 0.5f);
		}

		/// <summary>
		/// The swathe a carpet is in at a world position: 1 at the thickest, <paramref name="floor"/>
		/// at the thinnest, varying smoothly over about <paramref name="metres"/>.
		/// </summary>
		/// <remarks>
		/// Two octaves of value noise on a world-space lattice — a function of the seed and the world
		/// position, nothing else, so a tile seam is invisible to it and re-running gives the same
		/// swathes. Value noise sums bunch around a half, so the sum is stretched by a smoothstep over
		/// its middle half: without that, a floor of 0.4 would barely ever be reached and the
		/// swathes would read as an even lawn.
		/// </remarks>
		public static float CarpetClump(uint seed, float worldX, float worldZ, float metres, float floor)
		{
			if (floor >= 1f)
			{
				return 1f;
			}
			float scale = 1f / Mathf.Max(1f, metres);
			float x = worldX * scale;
			float z = worldZ * scale;
			float n = 0.65f * ValueNoise(seed, x, z) + 0.35f * ValueNoise(Mix(seed ^ 0x5BD1E995u), x * 2.03f + 0.37f, z * 2.03f + 0.71f);
			float t = Mathf.Clamp01((n - 0.25f) * 2f);
			t = t * t * (3f - 2f * t);
			float low = Mathf.Clamp01(floor);
			return low + (1f - low) * t;
		}

		/// <summary>
		/// A clustered rule's group radius and the distance between its group centres, metres.
		/// </summary>
		/// <remarks>
		/// <para>
		/// With <see cref="PrefabSpawnRule.clusterSize"/> set, the centres stand <c>√(size / density)</c>
		/// apart, so an average group holds that many instances whatever the rule's density: a fixed
		/// multiple of the radius gave a lone-oak rule (0.03 per 100 m²) groups of 0.6 trees, which is an
		/// even scatter by another name. The radius widens to fit that many at the rule's spacing,
		/// <c>1.3 × spacing × √size</c>: a group is a peaked Gaussian, not a filled disc, and narrower than
		/// this its heart jams at the spacing and refuses its members. Measured on a lone-oak rule (six to
		/// a grove, 8 m apart) the groves keep ~80% of the rule's trees at 1.3 against ~65% at 0.7; a
		/// woodland at 0.8 per 100 m² goes from far more even than random (its spacing) to plainly clumped.
		/// </para>
		/// <para>
		/// Without a size, the centres stand <see cref="PrefabSpawnRule.clusterSpacing"/> radii apart, as
		/// before. Either way the pitch is at least two radii, which <see cref="ClusterIntensity"/> needs.
		/// </para>
		/// </remarks>
		public static void ClusterShape(PrefabSpawnRule rule, out float metres, out float pitch)
		{
			metres = Mathf.Max(0f, rule.clusterMetres);
			if (rule.clusterSize > 0f)
			{
				metres = Mathf.Max(metres, 1.3f * Mathf.Max(0f, rule.minSpacing) * Mathf.Sqrt(rule.clusterSize));
				float perSquareMetre = rule.densityPer100m2 * 0.01f * rule.spawnProbabilityMultiplier;
				pitch = perSquareMetre > 0f ? Mathf.Sqrt(rule.clusterSize / perSquareMetre) : metres * Mathf.Max(2f, rule.clusterSpacing);
			}
			else
			{
				pitch = metres * Mathf.Max(2f, rule.clusterSpacing);
			}
			pitch = Mathf.Max(2f * metres, pitch);
		}

		/// <summary>
		/// How strongly a clustered rule's groups gather at a world position: a multiplier on its
		/// chance whose average over the world is 1, so the rule's density is unchanged, only moved.
		/// </summary>
		/// <param name="metres">The radius of a group (<see cref="ClusterShape"/>).</param>
		/// <param name="pitch">Distance between group centres, metres (<see cref="ClusterShape"/>); never less than two radii.</param>
		/// <param name="background">The share spread evenly between groups (<see cref="PrefabSpawnRule.clusterBackground"/>).</param>
		/// <param name="closeness">1 at the heart of the nearest group, falling to 0 beyond its fringe.</param>
		/// <remarks>
		/// <para>
		/// A Thomas cluster process, as a field: one group centre per cell of a world lattice of
		/// <paramref name="pitch"/>, jittered anywhere inside its cell, each a Gaussian of σ = radius / 2
		/// (so the radius holds 86% of a group) with a strength drawn as 3u² — mean 1, mostly small groups
		/// and the odd large one, which is what a meadow's flower patches and a hillside's rock piles look
		/// like. The sum is scaled by lattice area over a Gaussian's area, <c>P² / 2πσ²</c>, which makes
		/// its average exactly 1 before the even <paramref name="background"/> is mixed back in.
		/// </para>
		/// <para>
		/// Only the 3×3 lattice cells around the point are read: the pitch is at least two radii, so
		/// any centre further out is at least 4σ away and adds under e⁻⁸ of a group's peak. Like
		/// <see cref="CarpetClump"/> it is a function of the seed and the world position alone, so groups
		/// run straight through a tile seam and a re-run puts them back in the same places.
		/// </para>
		/// </remarks>
		public static float ClusterIntensity(uint seed, float worldX, float worldZ, float metres, float pitch, float background, out float closeness)
		{
			closeness = 0f;
			background = Mathf.Clamp01(background);
			if (!(metres > 0f) || background >= 1f)
			{
				return 1f;
			}
			float sigma = metres * 0.5f;
			pitch = Mathf.Max(2f * metres, pitch);
			float inverseTwoSigma2 = 1f / (2f * sigma * sigma);
			float norm = pitch * pitch * inverseTwoSigma2 / Mathf.PI;

			int cx = Mathf.FloorToInt(worldX / pitch);
			int cz = Mathf.FloorToInt(worldZ / pitch);
			float sum = 0f;
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					uint h = Hash(seed, cx + dx, cz + dz);
					float centreX = (cx + dx + Unit(Mix(h + 1u))) * pitch;
					float centreZ = (cz + dz + Unit(Mix(h + 2u))) * pitch;
					float ox = worldX - centreX, oz = worldZ - centreZ;
					float d2 = ox * ox + oz * oz;
					float falloff = Mathf.Exp(-d2 * inverseTwoSigma2);
					float u = Unit(Mix(h + 3u));
					float strength = 3f * u * u;
					sum += strength * falloff;
					// A faint group has no heart to grow large in.
					closeness = Mathf.Max(closeness, falloff * Mathf.Min(1f, strength));
				}
			}
			return background + (1f - background) * norm * sum;
		}

		// ── Forest stands ────────────────────────────────────────────

		/// <summary>How much a forest rule's stands shrink at its steepest ground: their cover × (1 − this × steepness²).</summary>
		public const float StandSlopeThinning = 0.6f;

		/// <summary>Entries in a stand field's quantile table.</summary>
		public const int StandQuantileCount = 256;

		private const int StandOctaves = 4;
		// Each octave turned against the last, so no lattice axis lines up from one to the next.
		private static readonly float[] StandCos = { 1f, Mathf.Cos(0.62f), Mathf.Cos(1.24f), Mathf.Cos(1.87f) };
		private static readonly float[] StandSin = { 0f, Mathf.Sin(0.62f), Mathf.Sin(1.24f), Mathf.Sin(1.87f) };

		/// <summary>
		/// Where a forest rule's trees stand: a scene-wide field cut at the rule's cover into stands, with
		/// a feathered edge, clearings and open ground between them, as a multiplier on the rule's chance
		/// whose average is 1.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why groups did not make forests.</b> The group field (<see cref="ClusterIntensity"/>) works
		/// at the scale of a group: a forest rule's thickets were 26 m in radius on a 52 m lattice, so
		/// from anywhere a player stands the woods were one even texture of slightly thicker and thinner
		/// patches. And a dense forest already sits near the packing its spacing allows, so a mean-1 field
		/// had nowhere to put more trees at a heart: it could only thin the glades, which the background
		/// share refilled. Each rule also had its own groups, and two species' independent groves fill
		/// each other's gaps, so a taiga's spruce and pine together were evener than either. What reads
		/// as forest is structure at hundreds of metres: woods with a closed interior, clearings, a ragged
		/// edge, lone trees in the fields beyond. That is this field.
		/// </para>
		/// <para>
		/// <b>The field.</b> <see cref="StandNoise"/>: four octaves of value noise from
		/// <see cref="PrefabSpawnRule.forestMetres"/> down to an eighth of it, each turned against the
		/// last, through a slow domain warp that bends the outlines so no stand is a lattice blob. It is a
		/// function of the scene's seed and the world position alone, the same for every forest rule of
		/// every biome: a biome's species stand in the same woods, and because each rule cuts the same
		/// field at its own level, a sparse neighbour's copses are the hearts of a dense one's forest and
		/// woods run on across a biome seam instead of stopping at it.
		/// </para>
		/// <para>
		/// <b>The cut.</b> Value-noise sums bunch round a half, so a fixed level would mean a different
		/// cover at every scale. The field's own distribution is measured instead — 96² samples over 24
		/// stand widths, sorted into <see cref="StandQuantileCount"/> quantiles — and the stands are where
		/// the field passes its (1 − cover) quantile, so that share of the ground is wood. The edge is a
		/// smoothstep across <see cref="PrefabSpawnRule.forestEdge"/> of the field's 15–85% spread, so a
		/// wood thins over tens of metres at its margin. Inside, the stand's density is the rule's average
		/// over the share the stands and the open ground take of it, <c>1 / (open + (1 − open) × mean
		/// mask)</c>, measured on the same table: the rule's density is still what it says, only gathered.
		/// </para>
		/// <para>
		/// <b>Shrinking, not thinning.</b> The cover can be scaled per point (<see cref="Intensity"/>):
		/// the scatter scales it by the rule's biome share and by steepness, so a stand recedes to its
		/// heart and breaks into copses across an ecotone or up a slope while its interior stays closed,
		/// which is how a forest edge looks, where multiplying the chance would thin the whole wood
		/// evenly. The cut follows the table, so the mean still falls with the share as a multiplier would.
		/// </para>
		/// </remarks>
		public sealed class StandField
		{
			public readonly uint Seed;
			public readonly float Metres;
			public readonly float Cover;
			public readonly float Open;
			/// <summary>Half the edge's width, in the field's own units.</summary>
			public readonly float EdgeNoise;
			/// <summary>Where the field is cut at full cover.</summary>
			public readonly float Threshold;
			/// <summary>The stands' share of the ground at full cover, soft edges counted: the mean of the mask.</summary>
			public readonly float MeanMask;
			/// <summary>The multiplier inside a stand: 1 / (open + (1 − open) × mean mask).</summary>
			public readonly float Norm;
			private readonly float[] quantiles;

			/// <param name="seed">The scene's stand seed (<see cref="StandSeed"/>).</param>
			/// <param name="metres">Size of the largest stands, metres.</param>
			/// <param name="cover">Share of the ground under stands.</param>
			/// <param name="edge">Edge width as a share of the field's 15–85% spread.</param>
			/// <param name="open">Density between stands as a share of the density inside one.</param>
			/// <param name="table">The field's quantiles for this seed and size (<see cref="StandQuantiles"/>); null measures them.</param>
			public StandField(uint seed, float metres, float cover, float edge, float open, float[] table = null)
			{
				Seed = seed;
				Metres = Mathf.Max(1f, metres);
				Cover = Mathf.Clamp01(cover);
				Open = Mathf.Clamp01(open);
				quantiles = table ?? StandQuantiles(seed, Metres);
				EdgeNoise = Mathf.Max(0f, edge) * (StandQuantile(quantiles, 0.85f) - StandQuantile(quantiles, 0.15f)) * 0.5f;
				Threshold = StandQuantile(quantiles, 1f - Cover);
				double sum = 0d;
				for (int k = 0; k < quantiles.Length; k++)
				{
					sum += StandMask(quantiles[k], Threshold, EdgeNoise);
				}
				MeanMask = (float)(sum / quantiles.Length);
				float mean = Open + (1f - Open) * MeanMask;
				Norm = mean > 1e-4f ? 1f / mean : 0f;
			}

			/// <summary>The most <see cref="Intensity"/> can be: a stand's heart.</summary>
			public float Ceiling => Norm;

			/// <summary>The raw field at a world position, about 0–1.</summary>
			public float Noise(float worldX, float worldZ) => StandNoise(Seed, worldX, worldZ, Metres);

			/// <summary>
			/// The multiplier on a forest rule's chance at a world position.
			/// </summary>
			/// <param name="coverScale">Scales the cover here (biome share, steepness): the stands shrink to their hearts.</param>
			/// <param name="openScale">Scales the open ground's lone trees here.</param>
			/// <param name="mask">1 inside a stand, 0 in the open, between across its edge.</param>
			public float Intensity(float worldX, float worldZ, float coverScale, float openScale, out float mask)
			{
				float threshold = coverScale >= 1f ? Threshold : StandQuantile(quantiles, 1f - Cover * Mathf.Max(0f, coverScale));
				mask = StandMask(Noise(worldX, worldZ), threshold, EdgeNoise);
				return (Open * Mathf.Clamp01(openScale) + (1f - Open) * mask) * Norm;
			}
		}

		/// <summary>
		/// The scene-wide stand field at a world position: about 0–1, bunched round a half, varying over
		/// <paramref name="metres"/> and down to an eighth of it. See <see cref="StandField"/>.
		/// </summary>
		public static float StandNoise(uint seed, float worldX, float worldZ, float metres)
		{
			float scale = 1f / Mathf.Max(1f, metres);
			float x = worldX * scale;
			float z = worldZ * scale;
			// A slow warp, so the stands' outlines meander instead of following the lattice.
			float warpX = ValueNoise(Mix(seed ^ 0x2C1B3C6Du), x * 0.5f + 0.31f, z * 0.5f + 0.77f) - 0.5f;
			float warpZ = ValueNoise(Mix(seed ^ 0x297A2D39u), x * 0.5f + 0.59f, z * 0.5f + 0.13f) - 0.5f;
			x += warpX * 1.2f;
			z += warpZ * 1.2f;
			float sum = 0f, total = 0f, amplitude = 1f, frequency = 1f;
			for (int octave = 0; octave < StandOctaves; octave++)
			{
				float c = StandCos[octave], s = StandSin[octave];
				float rx = (x * c - z * s) * frequency + octave * 17.17f;
				float rz = (x * s + z * c) * frequency + octave * 31.31f;
				sum += amplitude * ValueNoise(Mix(seed + (uint)octave * 0x9E3779B9u), rx, rz);
				total += amplitude;
				amplitude *= 0.5f;
				frequency *= 2.03f;
			}
			return sum / total;
		}

		/// <summary>
		/// The stand field's distribution: <see cref="StandQuantileCount"/> quantiles (at (k + ½) / count)
		/// of 96² samples spread over 24 stand widths. A function of the seed and size alone.
		/// </summary>
		public static float[] StandQuantiles(uint seed, float metres)
		{
			const int side = 96;
			float step = Mathf.Max(1f, metres) * 0.25f;
			var samples = new float[side * side];
			for (int j = 0; j < side; j++)
			{
				for (int i = 0; i < side; i++)
				{
					// Jittered inside each step, so the samples do not sit on the noise's own lattice.
					uint h = Hash(seed ^ 0x51ED270Bu, i, j);
					samples[j * side + i] = StandNoise(seed, (i + Unit(h)) * step, (j + Unit(Mix(h + 1u))) * step, metres);
				}
			}
			Array.Sort(samples);
			var table = new float[StandQuantileCount];
			for (int k = 0; k < table.Length; k++)
			{
				table[k] = samples[Math.Min(samples.Length - 1, (int)((k + 0.5f) / table.Length * samples.Length))];
			}
			return table;
		}

		/// <summary>The field value below which a share <paramref name="p"/> of the ground lies; ±∞ at 0 and 1.</summary>
		public static float StandQuantile(float[] table, float p)
		{
			if (p <= 0f)
			{
				return float.NegativeInfinity;
			}
			if (p >= 1f)
			{
				return float.PositiveInfinity;
			}
			float f = p * table.Length - 0.5f;
			if (f <= 0f)
			{
				return table[0];
			}
			if (f >= table.Length - 1)
			{
				return table[table.Length - 1];
			}
			int i = (int)f;
			return table[i] + (table[i + 1] - table[i]) * (f - i);
		}

		/// <summary>1 above the cut, 0 below it, a smoothstep across ±<paramref name="edge"/> round it.</summary>
		public static float StandMask(float noise, float threshold, float edge)
		{
			if (float.IsNegativeInfinity(threshold))
			{
				return 1f;
			}
			if (float.IsPositiveInfinity(threshold))
			{
				return 0f;
			}
			if (!(edge > 0f))
			{
				return noise >= threshold ? 1f : 0f;
			}
			float t = Mathf.Clamp01((noise - threshold + edge) / (2f * edge));
			return t * t * (3f - 2f * t);
		}

		/// <summary>
		/// A forest rule's species patch at a world position: 1 ± <paramref name="strength"/>, averaging 1,
		/// varying over about <paramref name="metres"/> — so where two rules share a stand, each is thicker
		/// in some parts of it and thinner in others, and a rule's prefabs gather by species.
		/// </summary>
		/// <remarks>The carpet's two-octave stretched noise (<see cref="CarpetClump"/> with no floor), which is symmetric about a half.</remarks>
		public static float SpeciesPatch(uint seed, float worldX, float worldZ, float metres, float strength)
		{
			float t = CarpetClump(seed, worldX, worldZ, metres, 0f);
			return 1f + Mathf.Clamp01(strength) * (2f * t - 1f);
		}

		/// <summary>
		/// A carpet's share of a cell: its coverage as it is while the carpets in the cell sum to no
		/// more than a full cell, scaled by the sum where they pass it.
		/// </summary>
		public static float CarpetShare(float coverage, float total)
		{
			return total > 1f ? coverage / total : coverage;
		}

		/// <summary>Value noise in [0, 1] on the integer lattice, quintic-smoothed.</summary>
		private static float ValueNoise(uint seed, float x, float z)
		{
			int ix = Mathf.FloorToInt(x);
			int iz = Mathf.FloorToInt(z);
			float fx = x - ix;
			float fz = z - iz;
			float sx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
			float sz = fz * fz * fz * (fz * (fz * 6f - 15f) + 10f);
			float a = Unit(Hash(seed, ix, iz));
			float b = Unit(Hash(seed, ix + 1, iz));
			float c = Unit(Hash(seed, ix, iz + 1));
			float d = Unit(Hash(seed, ix + 1, iz + 1));
			float south = a + (b - a) * sx;
			float north = c + (d - c) * sx;
			return south + (north - south) * sz;
		}

		/// <summary>
		/// Metres a tree instance is pushed into the ground: its rule's per-instance draw from
		/// <see cref="PrefabSpawnRule.sinkRange"/> plus the slope's extra.
		/// </summary>
		/// <remarks>
		/// An upright instance on a slope touches the ground with its uphill edge, and its downhill
		/// edge stands proud by the footprint radius times tan(slope); <see cref="PrefabSpawnRule.sinkSlopeFactor"/>
		/// is how much of that is taken back. Slopes past 60° count as 60°: the tangent runs away near
		/// vertical, and nothing is meant to stand there anyway. The draw is from the cell's hash, a
		/// stream of its own (offset 10) so adding a sink moves nothing else.
		/// </remarks>
		public static float SinkMetres(PrefabSpawnRule rule, float footprintRadius, float slopeDegrees, uint cellHash)
		{
			float low = Mathf.Min(rule.sinkRange.x, rule.sinkRange.y);
			float high = Mathf.Max(rule.sinkRange.x, rule.sinkRange.y);
			float sink = low + (high - low) * Unit(Mix(cellHash + 10u));
			if (rule.sinkSlopeFactor > 0f && footprintRadius > 0f && slopeDegrees > 0f)
			{
				sink += rule.sinkSlopeFactor * footprintRadius * Mathf.Tan(Mathf.Min(slopeDegrees, 60f) * Mathf.Deg2Rad);
			}
			return Mathf.Max(0f, sink);
		}

		// ── Sampling helpers ─────────────────────────────────────────

		/// <summary>A corner-aligned grid (sample i at i / (res - 1)) read bilinearly at (u, v) in [0, 1].</summary>
		private static float Bilinear(float[] grid, int res, float u, float v)
		{
			float fx = u * (res - 1);
			float fz = v * (res - 1);
			int x0 = (int)fx;
			int z0 = (int)fz;
			if (x0 > res - 2)
			{
				x0 = res - 2;
			}
			if (z0 > res - 2)
			{
				z0 = res - 2;
			}
			float tx = fx - x0;
			float tz = fz - z0;
			int a = z0 * res + x0;
			int b = a + res;
			float south = grid[a] + (grid[a + 1] - grid[a]) * tx;
			float north = grid[b] + (grid[b + 1] - grid[b]) * tx;
			return south + (north - south) * tz;
		}

		/// <summary>
		/// 1 inside the range, falling linearly to 0 over the falloff beyond either end.
		/// </summary>
		/// <remarks>
		/// WorldEditor treated the falloff as a hard widening of the window; as a ramp it thins a
		/// rule out toward its limit — the tree line is sparse before it is bare.
		/// </remarks>
		/// <summary>How far a rule may grow at a world height against the water line: 0 … 1 (<see cref="ScatterRule.WaterFrom"/>).</summary>
		/// <summary>How fully a land rule grows at a point beside inland water: 0 under it or in a dry bed, rising to 1 over its bank.</summary>
		private static float InlandBand(ScatterRule rule, float worldX, float worldY, float worldZ)
		{
			float surface = rule.Inland(worldX, worldZ);
			if (float.IsNegativeInfinity(surface))
			{
				return 1f;
			}
			if (float.IsPositiveInfinity(surface))
			{
				return 0f;
			}
			float t = Mathf.Clamp01((worldY - surface - rule.InlandFrom) / Mathf.Max(0.05f, rule.InlandTo - rule.InlandFrom));
			return t * t * (3f - 2f * t);
		}

		/// <summary>
		/// How fully a sea-floor rule grows at a world height for its band of depth (<see cref="ScatterRule.Depth"/>):
		/// 1 well inside it, thinning to 0 over the outer fifth at each closed end; 1 with no band. Mean sea level
		/// is world y = 0 (<see cref="TerrainScatterOptions.HasLiquidWater"/>).
		/// </summary>
		private static float DepthBand(ScatterRule rule, float worldY)
		{
			return DepthBand(rule.Depth, worldY);
		}

		/// <summary><see cref="DepthBand(ScatterRule, float)"/> for a band on its own.</summary>
		public static float DepthBand(Vector2 band, float worldY)
		{
			float from = Mathf.Max(0f, band.x), to = Mathf.Max(0f, band.y);
			if (from <= 0f && to <= 0f)
			{
				return 1f;
			}
			float depth = -worldY;
			float width = to > from ? 0.2f * (to - from) : Mathf.Max(0.5f, 0.25f * from);
			width = Mathf.Max(0.05f, width);
			float grow = 1f;
			if (from > 0f)
			{
				float t = Mathf.Clamp01((depth - from) / width);
				grow *= t * t * (3f - 2f * t);
			}
			if (to > 0f)
			{
				float t = Mathf.Clamp01((to - depth) / width);
				grow *= t * t * (3f - 2f * t);
			}
			return grow;
		}

		/// <summary>
		/// How fully a climate-banded rule grows at a point of the current tile: its band
		/// (<see cref="ClimateBand(PrefabSpawnRule, float, float)"/>) at the climate there, read from the
		/// tile's <see cref="TerrainClimateGrid"/>, which is built on the first call for the tile.
		/// </summary>
		/// <param name="worldY">The candidate's own world height: the band reads the temperature at it, not at a grid node's.</param>
		/// <remarks>The grid is built by <see cref="EnsureClimate"/> before any worker reads it; here it is only read.</remarks>
		private static float ClimateAt(ScatterRule rule, TileFrame frame, Workspace work, float u, float v, float worldY)
		{
			TerrainClimateGrid grid = work.Climate;
			if (grid == null)
			{
				throw new InvalidOperationException("The tile's climate grid is read before it was built.");
			}
			Vector2 at = grid.Read(u, v, worldY);
			return ClimateBand(rule.Rule, at.x, at.y);
		}

		/// <summary>
		/// Builds the current tile's climate grid if it is not built yet: on the calling thread, before the cells are
		/// handed out. A function of the tile alone, so building it before the first banded cell asks changes nothing.
		/// </summary>
		private static void EnsureClimate(TileFrame frame, Workspace work)
		{
			if (work.Climate == null)
			{
				work.Climate = BuildClimate(work.Placement, frame, work.Heights);
			}
		}

		/// <summary>
		/// How fully a rule grows in a climate, by its band (<see cref="PrefabSpawnRule.useClimateBand"/>):
		/// the temperature band times the humidity band, each from <see cref="ClimateBand(Vector2, float, float)"/>.
		/// 1 for a rule without a band, or no rule.
		/// </summary>
		/// <param name="temperature">The mean annual temperature where it stands, on the climate's scale (0 = 0 °C, 1 = 33.1 °C).</param>
		/// <param name="humidity">The humidity there, −1 … 1.</param>
		public static float ClimateBand(PrefabSpawnRule rule, float temperature, float humidity)
		{
			if (rule == null || !rule.useClimateBand)
			{
				return 1f;
			}
			return ClimateBand(rule.temperatureRange, rule.temperatureFalloff, temperature)
				* ClimateBand(rule.humidityRange, rule.humidityFalloff, humidity);
		}

		/// <summary>
		/// One axis of a climate band: 1 inside <paramref name="range"/>, 0 outside, and a smoothstep
		/// <paramref name="falloff"/> wide centred on each closed end — half at the end itself.
		/// </summary>
		/// <param name="range">x one end, y the other, in either order. An end at or past ±1 is open.</param>
		/// <param name="falloff">Width of each closed end's soft edge, in the value's units; 0 or less is a hard edge, the end itself inside.</param>
		/// <param name="value">The climate where the candidate stands. NaN (no reading) passes.</param>
		/// <remarks>
		/// <para>
		/// <b>Open at ±1, because the climate is.</b> The temperature and the humidity both stop at
		/// ±1 (<see cref="PlanetClimateField"/> clamps them there), so an end at 1 can never be
		/// passed; if it were a closed end its soft edge would halve the rule in the hottest or
		/// wettest places on a world, which no author asking for "warm" means. Treating it as open
		/// lets a rule say (0.55, 1) for "0.55 and warmer", and the rule's default (−1, 1) is no band
		/// on either axis even with the band switched on. Infinities are open by the same test.
		/// </para>
		/// <para>
		/// <b>Centred, not inside or outside.</b> A soft edge drawn wholly inside the range would make
		/// a "≥ 0.55" rule sparse well above 0.55; drawn wholly outside, it would grow below it. Centred,
		/// the stated end is where the rule is at half its density, so across a scene the species
		/// change over where the author said they do, and the counts either side balance. A smoothstep
		/// rather than the height band's linear ramp, so the density has no corner at either edge of
		/// the fade.
		/// </para>
		/// <para>
		/// Pure arithmetic, no random draw: multiplying a rule's chance by it moves no random sequence.
		/// </para>
		/// </remarks>
		public static float ClimateBand(Vector2 range, float falloff, float value)
		{
			if (float.IsNaN(value))
			{
				return 1f;
			}
			float low = Mathf.Min(range.x, range.y), high = Mathf.Max(range.x, range.y);
			float grow = 1f;
			if (low > -1f)
			{
				grow *= ClimateEdge(value - low, falloff);
			}
			if (high < 1f)
			{
				grow *= ClimateEdge(high - value, falloff);
			}
			return grow;
		}

		/// <summary>A closed end's soft edge, given how far inside the range the value is (negative outside).</summary>
		private static float ClimateEdge(float inside, float falloff)
		{
			if (!(falloff > 0f))
			{
				return inside >= 0f ? 1f : 0f;
			}
			float t = Mathf.Clamp01(inside / falloff + 0.5f);
			return t * t * (3f - 2f * t);
		}

		/// <summary>
		/// The scene's climate over one tile for climate-banded rules (<see cref="TerrainClimateGrid"/>: nodes 16 m apart at the
		/// ground's own height and 25 m above it, read back at a candidate's true height), the grid the splat painter's banded ground
		/// layers read too, so a layer and its plants authored with one band change over at one place. The scatter had a private copy
		/// of the same construction until 2026-10-18 (decision 35 rides along: one grid, not two); the arithmetic is the same to the bit.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why a grid.</b> A carpet visits every detail cell of its ground, twice, and a climate sample is a direction off the globe,
		/// a few noise octaves and a latitude table; horizontally the climate barely moves inside a tile, what moves within metres is
		/// the altitude, which the grid follows to first order (measured against <see cref="ScenePlacementClimate.SampleAt"/> on a 1 km
		/// tile of rolling relief, 2026-10-10: temperature to 10⁻⁵ anywhere, humidity to 0.006 at the ground, so it is only ever read
		/// at the ground a candidate stands on).
		/// </para>
		/// <para>
		/// Built lazily, once per tile, only when a banded rule reaches the tile; deterministic, and seamless across tiles.
		/// </para>
		/// </remarks>
		private static TerrainClimateGrid BuildClimate(ScenePlacementClimate climate, TileFrame frame, float[] heights)
		{
			Func<float, float, float, Vector2> climateAt = (x, y, z) =>
			{
				if (climate == null)
				{
					// Unreachable while ScatterRule.Climate requires a climate; kept so the grid can never throw.
					return new Vector2(float.NaN, float.NaN);
				}
				ClimateSample sample = climate.SampleAt(new Vector3(x, y, z), out _);
				return new Vector2(sample.Temperature, sample.Humidity);
			};
			return TerrainClimateGrid.Build(climateAt, frame.Origin, new Vector2(frame.Size.x, frame.Size.z),
				(u, v) => frame.Origin.y + Bilinear(heights, frame.HeightResolution, u, v) * frame.Size.y);
		}

		/// <summary>
		/// How fully a water-gated rule grows at a point against the water line: a land rule above the sea's tides, a bed rule under
		/// the waters it names (decision 35 (a), 2026-10-18) — under the sea by its depth below mean sea level exactly as always, under
		/// a lake or a river by its depth below that water's own surface (<see cref="BiomeBedBands.InlandRule"/>).
		/// </summary>
		private static float ShoreBand(ScatterRule rule, float worldX, float worldY, float worldZ)
		{
			if (!rule.Aquatic)
			{
				return WaterBand(rule, worldY) * DepthBand(rule, worldY);
			}
			float inland = rule.BedInland != null ? rule.BedInland(worldX, worldZ) : float.NegativeInfinity;
			// No inland water, or water the sea's tide reaches (an estuary's channel, a lagoon): the sea's floor.
			if (float.IsNegativeInfinity(inland) || float.IsNaN(inland) || !float.IsPositiveInfinity(inland) && BiomeBedBands.IsSeas(inland, 0f))
			{
				// The sea's own case, as it always was; a rule that does not grow under the sea, or wants a current, grows nothing here.
				if ((rule.Waters & BiomeBedWaters.Sea) == 0)
				{
					return 0f;
				}
				float sea = WaterBand(rule, worldY) * DepthBand(rule, worldY);
				return BiomeBedBands.Closes(rule.Current) ? sea * BiomeBedBands.Band(rule.Current, BiomeBedBands.DefaultCurrentFalloff, 0f) : sea;
			}
			Vector3 river = rule.RiverAt != null && !float.IsPositiveInfinity(inland) ? rule.RiverAt(worldX, worldZ) : Vector3.zero;
			BedWater water = BedWater.At(worldY, true, 0f, inland, river);
			return BiomeBedBands.InlandRule(in water, rule.Waters, rule.BedInlandFrom, rule.BedInlandTo, rule.Depth, rule.Current);
		}

		/// <summary>The waters a bed slot's rules grow under when they name none: the lakebed's the sea, the riverbed's a river, a bed layer's its own.</summary>
		private static BiomeBedWaters SlotWaters(SceneTerrainPalette.Entry entry)
		{
			if (entry.Source is BedTextureLayer bed)
			{
				return bed.waters == BiomeBedWaters.Default ? BiomeBedWaters.Any : bed.waters;
			}
			return entry.Slot == SceneTerrainPalette.SlotRiverbed ? BiomeBedBands.RiverbedWaters : BiomeBedBands.LakebedWaters;
		}

		private static float WaterBand(ScatterRule rule, float worldY)
		{
			float span = rule.WaterTo - rule.WaterFrom;
			if (Mathf.Abs(span) < 1e-4f)
			{
				return span >= 0f ? (worldY >= rule.WaterTo ? 1f : 0f) : (worldY <= rule.WaterTo ? 1f : 0f);
			}
			float t = Mathf.Clamp01((worldY - rule.WaterFrom) / span);
			return t * t * (3f - 2f * t);
		}

		private static float Band(float value, MinMaxRange range, float falloff)
		{
			if (value >= range.min && value <= range.max)
			{
				return 1f;
			}
			if (!(falloff > 0f))
			{
				return 0f;
			}
			float outside = value < range.min ? range.min - value : value - range.max;
			return outside >= falloff ? 0f : 1f - outside / falloff;
		}

		/// <summary>A stride coprime with <paramref name="total"/>, so stepping by it visits every index once.</summary>
		private static int CoprimeStride(int total, uint random)
		{
			if (total <= 2)
			{
				return 1;
			}
			// Somewhere in the middle half, so neighbours in visit order are far apart on the tile.
			int span = Math.Max(1, total / 2);
			int candidate = (total / 4 + (int)(random % (uint)span)) | 1;
			for (int attempt = 0; attempt < total; attempt++)
			{
				int stride = 1 + (candidate + attempt) % (total - 1);
				if (Gcd(stride, total) == 1)
				{
					return stride;
				}
			}
			return 1;
		}

		private static int Gcd(int a, int b)
		{
			while (b != 0)
			{
				int t = b;
				b = a % b;
				a = t;
			}
			return a;
		}

		/// <summary>The seed every one of a rule's random numbers derives from.</summary>
		private static uint RuleSeed(uint sceneSeed, string guid, string biomeKey, int seedOffset, int duplicateIndex)
		{
			uint h = Mix(sceneSeed ^ 0x6A09E667u);
			h = Mix(h ^ Fnv(guid));
			h = Mix(h ^ Fnv(biomeKey));
			h = Mix(h ^ (uint)seedOffset);
			return Mix(h ^ (uint)duplicateIndex * 0x9E3779B9u);
		}

		/// <summary>The hash of one cell of one rule: seed and integer world cell, nothing else.</summary>
		private static uint Hash(uint seed, int x, int z)
		{
			uint h = Mix(seed ^ 0x9E3779B9u);
			h = Mix(h ^ (uint)x);
			return Mix(h ^ ((uint)z * 0x85EBCA6Bu));
		}

		/// <summary>An avalanching 32-bit mix (lowbias32).</summary>
		private static uint Mix(uint h)
		{
			unchecked
			{
				h ^= h >> 16;
				h *= 0x7FEB352Du;
				h ^= h >> 15;
				h *= 0x846CA68Bu;
				h ^= h >> 16;
				return h;
			}
		}

		/// <summary>A hash as a float in [0, 1).</summary>
		private static float Unit(uint h)
		{
			return (h >> 8) * (1f / 16777216f);
		}

		/// <summary>FNV-1a of a string: stable across runs, platforms and .NET versions, unlike GetHashCode.</summary>
		private static uint Fnv(string text)
		{
			unchecked
			{
				uint h = 2166136261u;
				if (text != null)
				{
					for (int i = 0; i < text.Length; i++)
					{
						h ^= text[i];
						h *= 16777619u;
					}
				}
				return h;
			}
		}
	}
}
#endif
