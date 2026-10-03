#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
	/// and the height and slope bands. The cell's area is in it, so the expected count per square
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
	/// <b>Carpets are coverage, not candidates.</b> A detail rule placed as a
	/// <see cref="DetailPlacement.Carpet"/> skips the draw entirely: every detail cell of its ground
	/// is written with a share of the cell, <c>carpetCoverage × ramp(texture weight) × biome share ×
	/// height and slope bands × clump</c>. The ramp is a smoothstep across
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
		public static TerrainScatterReport Scatter(IReadOnlyList<Terrain> tiles, SceneTerrainPalette palette, SceneBiomeField field,
			uint seed, TerrainScatterOptions options)
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
			List<ScatterRule> rules = GatherRules(palette, field, seed, options, attributionEntries, report);

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
			report.DetailPrototypes = detailPrototypes.Count;
			report.TreePrototypes = treePrototypes.Count;

			bool[][] layerBiomes = LayerBiomes(palette, field.Biomes.Count);
			Func<float, float, float, float> normalizedHeight = options.NormalizedHeight ?? SceneRelativeHeight(ordered, report);

			if (palette.Layers.Count == 0)
			{
				report.Warnings.Add("The palette has no layers, so no rule has ground to grow on.");
			}

			var workspace = new Workspace(field.Biomes.Count, attributionEntries.Count) { TreeFootprints = treeFootprints.ToArray() };
			DetailPrototype[] detailArray = detailPrototypes.ToArray();
			TreePrototype[] treeArray = treePrototypes.ToArray();
			bool firstTile = true;
			foreach (Terrain terrain in ordered)
			{
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
			/// <summary>
			/// Which side of the water line the rule grows on, as a smoothstep over world y from
			/// <see cref="WaterFrom"/> (none) to <see cref="WaterTo"/> (full); unused when <see cref="WaterGated"/> is false.
			/// </summary>
			public bool WaterGated;
			public float WaterFrom, WaterTo;
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
			List<SceneTerrainPalette.Entry> attributionEntries, TerrainScatterReport report)
		{
			var rules = new List<ScatterRule>();
			var seen = new HashSet<(int, PrefabSpawnRule)>();
			var duplicates = new Dictionary<string, int>();
			var attributionIndex = new Dictionary<SceneTerrainPalette.Entry, int>();

			foreach (SceneTerrainPalette.Entry entry in palette.Entries)
			{
				List<PrefabSpawnRule> source = entry?.Source?.prefabSpawnRules;
				if (source == null || source.Count == 0)
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
					if (options != null && options.HasLiquidWater)
					{
						// A rule on a biome's submerged layer is aquatic; anything else, trees included, grows on land.
						bool aquatic = entry.Role == PaletteRole.Submerged;
						scatterRule.WaterGated = true;
						scatterRule.WaterFrom = aquatic ? options.AquaticFadeStartMetres : options.LandFadeStartMetres;
						scatterRule.WaterTo = aquatic ? options.AquaticFullMetres : options.LandFullMetres;
					}
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
			return rules;
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
			if (prefab.GetComponentsInChildren<MeshFilter>(true).Length != 1)
			{
				return "has more than one MeshFilter; a detail draws only the root's mesh";
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

			ReadGround(data, frame, work);
			ReadAlphamaps(data, frame, rules, palette.Layers.Count, work, terrain.name, report);
			ReadBiomes(field, frame, layerBiomes, attributionEntries, work);

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
					WriteDetails(data, rule, work);
				}
				biomeUsed[biome] += placed;
				rule.Outcome.Placed += placed;

				if (budget.StoppedBy != null)
				{
					rule.Outcome.BudgetStops++;
					report.BudgetCaps.Add($"'{terrain.name}': {rule.Outcome.Biome} / {rule.Outcome.Slot} / '{rule.Outcome.RuleName}' stopped at {placed:N0} by the {budget.StoppedBy}.");
				}
			}

			RunCarpets(data, rules, frame, work, detailResolution, maxValue, normalizedHeight, report);

			report.DetailCellsCovered += tileDetailUsed;
			report.TreesPlaced += tileTreesUsed;
			Finish(terrain, data, work);
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
			var sums = new float[sharers.Count];

			float[] weights = work.BiomeWeights;
			float step = 1f / (res - 1);
			for (int y = 0; y < res; y++)
			{
				// Alphamap texels sit on the tile's corners (texel i at i / (res - 1)), as the terrain shader samples them.
				float worldZ = frame.Origin.z + y * step * frame.Size.z;
				for (int x = 0; x < res; x++)
				{
					float worldX = frame.Origin.x + x * step * frame.Size.x;
					if (biomes > 0)
					{
						field.WeightsAt(worldX, worldZ, weights);
					}
					for (int b = 0; b < biomes; b++)
					{
						work.BiomeShare[b] += weights[b];
					}
					for (int l = 0; l < sums.Length; l++)
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
					for (int e = 0; e < entries.Count; e++)
					{
						float own = weights[entries[e].BiomeIndex];
						float sum = sums[entrySlot[e]];
						float share = own > 0f && sum > 0f ? own / sum : 0f;
						work.Attribution[e][texel] = share;
						if (share > work.AttributionMax[e])
						{
							work.AttributionMax[e] = share;
						}
					}
				}
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
		private static void WriteDetails(TerrainData data, ScatterRule rule, Workspace work)
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

		/// <summary>
		/// Visits every cell of one tile for one rule, in shuffled order, and places what passes.
		/// Returns how many were placed.
		/// </summary>
		/// <remarks>
		/// Allocation-free: everything it reads is flattened beforehand, and the only writes are into
		/// pre-sized buffers, the tree list (pre-sized, amortised) and the rule's spacing hash
		/// (amortised). The cheap refusals come first — texture weight, then the density draw, which
		/// turns down most of what is left — so the biome share, the height delegate and the spacing
		/// test only run for candidates that could still be placed.
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

			int total = cellsX * cellsZ;
			float cellX = frame.Size.x / cellsX;
			float cellZ = frame.Size.z / cellsZ;
			// The chance before weight and shares: density per square metre times a cell's area.
			float baseChance = rule.densityPer100m2 * 0.01f * cellX * cellZ * rule.spawnProbabilityMultiplier;
			float minWeight = rule.minTextureWeight;
			bool useHeight = rule.useHeightConstraint;
			bool useSlope = rule.useSlopeConstraint;
			MinMaxRange heightRange = rule.heightRange;
			MinMaxRange slopeRange = rule.slopeRange;
			float heightFalloff = rule.heightFalloff;
			float slopeFalloff = rule.slopeFalloff;
			ScatterSpacing spacing = scatterRule.Spacing;
			uint seed = scatterRule.Seed;
			List<int> prototypes = scatterRule.Prototypes;
			int variants = prototypes.Count;
			bool clustered = rule.IsClustered;
			uint clusterSeed = scatterRule.ClusterSeed;
			float clusterMetres = scatterRule.ClusterMetres;
			float clusterPitch = scatterRule.ClusterPitch;

			/* Cells in world integer coordinates, continuous from one tile into the next. From the
			 * origin over the cell, not a tile index: three tiles centred on zero start at -1.5,
			 * -0.5 and +0.5 tile widths, and rounding those to tile indices gives two of them one
			 * index and one identical pattern. */
			int gx0 = Mathf.RoundToInt(frame.Origin.x / cellX);
			int gz0 = Mathf.RoundToInt(frame.Origin.z / cellZ);

			int alphaRes = frame.AlphaResolution;
			int heightRes = frame.HeightResolution;
			int holesRes = frame.HolesResolution;
			float[] heights = work.Heights;
			float[] slopes = work.Slopes;
			bool[] solid = work.Solid;

			// The permutation: offset + k·stride (mod total), stride coprime with total.
			uint order = Hash(seed ^ 0xA511E9B3u, gx0, gz0);
			int stride = CoprimeStride(total, order);
			int current = (int)(Mix(order + 1u) % (uint)total);

			// Refusals counted in locals and added once: the outcome is a heap object.
			long noWeight = 0, noChance = 0, noBiome = 0, noHole = 0, noHeight = 0, noSlope = 0, noSpacing = 0;
			int placed = 0;
			for (int k = 0; k < total; k++)
			{
				int index = current;
				current += stride;
				if (current >= total)
				{
					current -= total;
				}
				int i = index % cellsX;
				int j = index / cellsX;

				uint h = Hash(seed, gx0 + i, gz0 + j);
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
					noWeight++;
					continue;
				}
				float worldX = frame.Origin.x + u * frame.Size.x;
				float worldZ = frame.Origin.z + v * frame.Size.z;
				float closeness = 0f;
				float roll = Unit(h);
				float chance = baseChance * weight;
				if (clustered)
				{
					chance *= ClusterIntensity(clusterSeed, worldX, worldZ, clusterMetres, clusterPitch, rule.clusterBackground, out closeness);
				}
				if (roll >= chance)
				{
					noChance++;
					continue;
				}

				float share = Bilinear(attribution, alphaRes, u, v);
				if (share <= 0f)
				{
					noBiome++;
					continue;
				}
				chance *= share;

				if (solid != null)
				{
					int hx = Math.Min(holesRes - 1, (int)(u * holesRes));
					int hz = Math.Min(holesRes - 1, (int)(v * holesRes));
					if (!solid[hz * holesRes + hx])
					{
						noHole++;
						continue;
					}
				}

				float height01 = Bilinear(heights, heightRes, u, v);
				float worldY = frame.Origin.y + height01 * frame.Size.y;

				if (scatterRule.WaterGated)
				{
					// Counted with the height rejections: the water line is a height band of its own.
					float shore = WaterBand(scatterRule, worldY);
					if (shore <= 0f)
					{
						noHeight++;
						continue;
					}
					chance *= shore;
				}
				if (useHeight)
				{
					float band = Band(normalizedHeight(worldX, worldY, worldZ), heightRange, heightFalloff);
					if (band <= 0f)
					{
						noHeight++;
						continue;
					}
					chance *= band;
				}
				if (useSlope)
				{
					float band = Band(Bilinear(slopes, heightRes, u, v), slopeRange, slopeFalloff);
					if (band <= 0f)
					{
						noSlope++;
						continue;
					}
					chance *= band;
				}
				if (roll >= chance)
				{
					noChance++;
					continue;
				}

				if (spacing != null && !spacing.IsClear(worldX, worldZ))
				{
					noSpacing++;
					continue;
				}

				int variant = variants == 1 ? 0 : (int)(Mix(h + 3u) % (uint)variants);
				if (tree)
				{
					TreeInstance instance = TreeInstanceAt(rule, prototypes[variant], u, height01, v, h, clustered ? rule.clusterScaleBias * (closeness - 0.5f) : 0f);
					trees.Add(instance);
					float footprint = prototypes[variant] < work.TreeFootprints.Length ? work.TreeFootprints[prototypes[variant]] * instance.widthScale : 0f;
					float sinkSlope = rule.sinkSlopeFactor > 0f ? Bilinear(slopes, heightRes, u, v) : 0f;
					work.TreeSinks.Add(SinkMetres(rule, footprint, sinkSlope, h));
				}
				else
				{
					/* A group's heart can ask for more than one in a cell; a draw cannot pass, so the rest
					 * goes on as cover, which keeps a group's count what its field says. Unity's detail
					 * maps are [y, x]. */
					int value = chance > 1f ? Math.Min(detailMax, (int)(detailValue * chance + 0.5f)) : detailValue;
					detailBuffers[variant][j, i] = value;
					work.DetailPlaced[variant]++;
				}
				spacing?.Add(worldX, worldZ);
				placed++;

				if (--budget.RuleLeft <= 0 || --budget.BiomeLeft <= 0 || --budget.TileLeft <= 0)
				{
					if (k + 1 < total)
					{
						budget.StoppedBy = budget.RuleLeft <= 0 ? "rule's maxPerChunk" : budget.BiomeLeft <= 0 ? "biome's share of the tile budget" : "tile budget";
					}
					break;
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

		/// <summary>
		/// A tree at a sampled place, its size, turn and colour drawn from the cell's own hash so
		/// they never change while the cell does not.
		/// </summary>
		/// <remarks>
		/// The colour is a fixed per-instance variation around white — a little brightness and a
		/// little warm or cool — for the vegetation shader to tint seasonally on top of; a forest of
		/// identical tints would turn in autumn as one block. lightmapColor varies independently.
		/// Nothing seasonal is in either. <paramref name="sizeShift"/> moves the size's draw along the
		/// scale range (a clustered rule's lean toward large at a group's heart, small at its fringe);
		/// 0 leaves it exactly as it was.
		/// </remarks>
		private static TreeInstance TreeInstanceAt(PrefabSpawnRule rule, int prototype, float u, float height01, float v, uint h, float sizeShift)
		{
			float widthScale, heightScale;
			float widthDraw = Mathf.Clamp01(Unit(Mix(h + 4u)) + sizeShift);
			if (rule.useNonUniformScaling)
			{
				widthScale = Mathf.Max(0.05f, Mathf.Lerp(rule.widthScaleRange.x, rule.widthScaleRange.y, widthDraw));
				heightScale = Mathf.Max(0.05f, Mathf.Lerp(rule.heightScaleRange.x, rule.heightScaleRange.y, Mathf.Clamp01(Unit(Mix(h + 5u)) + sizeShift)));
			}
			else
			{
				widthScale = Mathf.Max(0.05f, Mathf.Lerp(rule.uniformScaleRange.x, rule.uniformScaleRange.y, widthDraw));
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
		/// Both passes run <see cref="SampleCarpet"/>, the same arithmetic in the same order, so the
		/// coverage a carpet adds to the sum is bit for bit the coverage it then divides by it. Storing
		/// each carpet's coverage instead would cost a float per cell per carpet — 64 MB per carpet at
		/// Unity's largest detail map — where recomputing costs one more pass of cheap arithmetic.
		/// </remarks>
		private static void RunCarpets(TerrainData data, List<ScatterRule> rules, TileFrame frame, Workspace work, int resolution, int maxValue,
			Func<float, float, float, float> normalizedHeight, TerrainScatterReport report)
		{
			work.TileCarpets.Clear();
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
			}
			if (work.TileCarpets.Count == 0)
			{
				return;
			}

			int cells = resolution * resolution;
			work.CarpetSum = Workspace.Fit(work.CarpetSum, cells);
			Array.Clear(work.CarpetSum, 0, cells);
			foreach (ScatterRule rule in work.TileCarpets)
			{
				int layer = rule.Entry.LayerIndex;
				SampleCarpet(rule, frame, work, work.Layers[layer], work.Attribution[rule.Attribution], resolution, normalizedHeight, false, maxValue);
			}
			foreach (ScatterRule rule in work.TileCarpets)
			{
				int layer = rule.Entry.LayerIndex;
				SampleCarpet(rule, frame, work, work.Layers[layer], work.Attribution[rule.Attribution], resolution, normalizedHeight, true, maxValue);
				WriteDetails(data, rule, work);
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

		/// <summary>
		/// One carpet over one tile, every cell in row order. With <paramref name="write"/> false it
		/// adds each cell's coverage into <see cref="Workspace.CarpetSum"/> and counts refusals; with
		/// it true it writes the cell's share — its coverage, scaled down where the carpets' sum passes
		/// a full cell — into the rule's detail buffers.
		/// </summary>
		/// <remarks>
		/// Allocation-free, like <see cref="SampleRule"/>, with the cheap refusals first. Quantised
		/// down (never rounded up) to the detail map's integer scale, and split between the rule's
		/// prototypes as whole units with the remainder dealt from a per-cell hash, so two carpets at a
		/// half each, or one carpet's several variants, never sum past the full cell.
		/// </remarks>
		private static void SampleCarpet(ScatterRule scatterRule, TileFrame frame, Workspace work, float[] layerWeights, float[] attribution,
			int resolution, Func<float, float, float, float> normalizedHeight, bool write, int maxValue)
		{
			PrefabSpawnRule rule = scatterRule.Rule;
			float peak = Mathf.Clamp01(rule.carpetCoverage * rule.spawnProbabilityMultiplier);
			float minWeight = rule.minTextureWeight;
			float ramp = rule.carpetWeightRamp;
			float clumpMetres = rule.carpetClumpMetres;
			float clumpFloor = rule.carpetClumpFloor;
			uint clumpSeed = scatterRule.ClumpSeed;
			bool useHeight = rule.useHeightConstraint;
			bool useSlope = rule.useSlopeConstraint;
			MinMaxRange heightRange = rule.heightRange;
			MinMaxRange slopeRange = rule.slopeRange;
			float heightFalloff = rule.heightFalloff;
			float slopeFalloff = rule.slopeFalloff;
			List<int> prototypes = scatterRule.Prototypes;
			int variants = prototypes.Count;

			int alphaRes = frame.AlphaResolution;
			int heightRes = frame.HeightResolution;
			int holesRes = frame.HolesResolution;
			float[] heights = work.Heights;
			float[] slopes = work.Slopes;
			bool[] solid = work.Solid;
			float[] sum = work.CarpetSum;
			List<int[,]> buffers = work.DetailBuffers;
			int gx0 = Mathf.RoundToInt(frame.Origin.x / (frame.Size.x / resolution));
			int gz0 = Mathf.RoundToInt(frame.Origin.z / (frame.Size.z / resolution));

			long noWeight = 0, noBiome = 0, noHole = 0, noHeight = 0, noSlope = 0, covered = 0, shared = 0;
			double coverageSum = 0d;
			for (int j = 0; j < resolution; j++)
			{
				float v = (j + 0.5f) / resolution;
				for (int i = 0; i < resolution; i++)
				{
					float u = (i + 0.5f) / resolution;
					int cell = j * resolution + i;

					float coverage = CarpetRamp(Bilinear(layerWeights, alphaRes, u, v), minWeight, ramp);
					if (coverage <= 0f)
					{
						noWeight++;
						continue;
					}
					float share = Bilinear(attribution, alphaRes, u, v);
					if (share <= 0f)
					{
						noBiome++;
						continue;
					}
					coverage *= share;

					if (solid != null)
					{
						int hx = Math.Min(holesRes - 1, (int)(u * holesRes));
						int hz = Math.Min(holesRes - 1, (int)(v * holesRes));
						if (!solid[hz * holesRes + hx])
						{
							noHole++;
							continue;
						}
					}
					if (useSlope)
					{
						float band = Band(Bilinear(slopes, heightRes, u, v), slopeRange, slopeFalloff);
						if (band <= 0f)
						{
							noSlope++;
							continue;
						}
						coverage *= band;
					}
					float worldX = frame.Origin.x + u * frame.Size.x;
					float worldZ = frame.Origin.z + v * frame.Size.z;
					if (scatterRule.WaterGated)
					{
						float shore = WaterBand(scatterRule, frame.Origin.y + Bilinear(heights, heightRes, u, v) * frame.Size.y);
						if (shore <= 0f)
						{
							noHeight++;
							continue;
						}
						coverage *= shore;
					}
					if (useHeight)
					{
						float worldY = frame.Origin.y + Bilinear(heights, heightRes, u, v) * frame.Size.y;
						float band = Band(normalizedHeight(worldX, worldY, worldZ), heightRange, heightFalloff);
						if (band <= 0f)
						{
							noHeight++;
							continue;
						}
						coverage *= band;
					}
					coverage *= peak * CarpetClump(clumpSeed, worldX, worldZ, clumpMetres, clumpFloor);

					if (!write)
					{
						sum[cell] += coverage;
						continue;
					}

					float total = sum[cell];
					if (total > 1f)
					{
						coverage /= total;
						shared++;
					}
					int units = (int)(coverage * maxValue);
					if (units <= 0)
					{
						continue;
					}
					if (variants == 1)
					{
						buffers[0][j, i] = units;
						work.DetailPlaced[0]++;
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
								work.DetailPlaced[n]++;
							}
						}
					}
					covered++;
					coverageSum += units / (double)maxValue;
				}
			}

			TerrainScatterReport.RuleOutcome outcome = scatterRule.Outcome;
			if (!write)
			{
				outcome.RejectedTextureWeight += noWeight;
				outcome.RejectedBiome += noBiome;
				outcome.RejectedHole += noHole;
				outcome.RejectedHeight += noHeight;
				outcome.RejectedSlope += noSlope;
				return;
			}
			outcome.Placed += covered;
			outcome.CoverageSum += coverageSum;
			outcome.SharedCells += shared;
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
