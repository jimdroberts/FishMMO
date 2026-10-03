using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The terrain scatter: the same scene scatters the same way whatever order its biomes, layers
	/// and tiles come in; spacing holds across a tile seam; a rule grows only on its texture and
	/// only in its biome; a prefab Unity could not draw is refused rather than baked broken; a
	/// carpet is continuous, rises with its texture, shares a cell with other carpets and runs
	/// straight through a seam; and a tree is sunk by its rule's sink and the slope's extra.
	/// </summary>
	/// <remarks>
	/// Tiny synthetic scenes: 64 m tiles with a 33² heightmap and a 32² alphamap, fields built from
	/// cells, prefabs built in code. Nothing touches the asset database.
	/// </remarks>
	[TestFixture]
	public class TerrainScatterTests
	{
		private const float TileMetres = 64f;
		private const int AlphaResolution = 32;

		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			for (int i = created.Count - 1; i >= 0; i--)
			{
				Object obj = created[i];
				if (obj is BiomeTemplate biome)
				{
					BiomeRegistry.Unregister(biome);
				}
				if (obj != null)
				{
					Object.DestroyImmediate(obj);
				}
			}
			created.Clear();
		}

		// ── Tests ────────────────────────────────────────────────────

		[Test]
		public void TheSameSceneScattersIdenticallyTwiceAndInAnyTileOrder()
		{
			Texture2D ground = Texture("ground");
			BiomeTemplate biome = Biome("ScatterDeterminism", ground);
			PrefabSpawnRule sunk = TreeRule("trees", TreePrefab("tree"), 4f, 3f);
			// A sink and a slope, so the heights written with snapping off are part of what must repeat.
			sunk.sinkRange = new Vector2(0.05f, 0.2f);
			sunk.sinkSlopeFactor = 0.3f;
			biome.MainTextureLayer.prefabSpawnRules.Add(sunk);
			biome.MainTextureLayer.prefabSpawnRules.Add(DetailRule("grass", DetailPrefab("grass", true), 20f));
			SceneBiomeField field = Field(new[] { biome }, 8, 4, 16f, (x, z) => 0);
			SceneTerrainPalette palette = Palette(field);
			Func<int, int, float> rolling = (x, z) => 0.4f + 0.1f * Mathf.Sin(x * 0.3f) * Mathf.Cos(z * 0.2f);
			Terrain west = Tile(new Vector3(0f, 0f, 0f), palette, (layer, x, y) => 1f, rolling);
			Terrain east = Tile(new Vector3(TileMetres, 0f, 0f), palette, (layer, x, y) => 1f, rolling);

			TerrainScatter.Scatter(new[] { west, east }, palette, field, 1234u, Options());
			TreeInstance[] firstWest = west.terrainData.treeInstances;
			TreeInstance[] firstEast = east.terrainData.treeInstances;
			int[,] firstGrass = west.terrainData.GetDetailLayer(0, 0, west.terrainData.detailWidth, west.terrainData.detailHeight, 0);
			Assert.That(firstWest.Length, Is.GreaterThan(0), "the rule should place trees");

			TerrainScatter.Scatter(new[] { east, west }, palette, field, 1234u, Options());
			AssertSameTrees(firstWest, west.terrainData.treeInstances, "west, re-run in reverse tile order");
			AssertSameTrees(firstEast, east.terrainData.treeInstances, "east, re-run in reverse tile order");
			int[,] secondGrass = west.terrainData.GetDetailLayer(0, 0, west.terrainData.detailWidth, west.terrainData.detailHeight, 0);
			Assert.That(secondGrass, Is.EqualTo(firstGrass), "the grass map is the same cell for cell");

			TerrainScatter.Scatter(new[] { west, east }, palette, field, 4321u, Options());
			Assert.That(SameTrees(firstWest, west.terrainData.treeInstances), Is.False, "a different scene seed is a different forest");
		}

		[Test]
		public void ReorderingTheBiomesAndTheirLayersMovesNothing()
		{
			/* Seeds come from the rule's GUID, its biome's key and the cell's world position — never
			 * a layer index or a list position. Swapping the biomes swaps the order of the field,
			 * the palette's entries and the alphamap channels, and every tree must stay put. */
			Texture2D shared = Texture("shared");
			BiomeTemplate a = Biome("ScatterOrderA", shared);
			BiomeTemplate b = Biome("ScatterOrderB", shared);
			GameObject pine = TreePrefab("pine");
			GameObject oak = TreePrefab("oak");
			a.DetailTextureLayers.Add(new TerrainTextureLayer { albedoTexture = Texture("needles") });
			b.DetailTextureLayers.Add(new TerrainTextureLayer { albedoTexture = Texture("leaves") });
			a.DetailTextureLayers[0].prefabSpawnRules.Add(TreeRule("pines", pine, 6f, 2f));
			b.DetailTextureLayers[0].prefabSpawnRules.Add(TreeRule("oaks", oak, 6f, 2f));

			var resolve = Resolver();
			SceneBiomeField forward = Field(new[] { a, b }, 8, 8, 8f, (x, z) => x < 4 ? 0 : 1, resolve);
			SceneBiomeField backward = Field(new[] { b, a }, 8, 8, 8f, (x, z) => x < 4 ? 1 : 0, resolve);
			SceneTerrainPalette forwardPalette = SceneTerrainPalette.Build(forward, resolve, null);
			SceneTerrainPalette backwardPalette = SceneTerrainPalette.Build(backward, resolve, null);
			Assume.That(forwardPalette.Layers[1], Is.Not.SameAs(backwardPalette.Layers[1]), "the swap should reorder the palette's channels");

			// Weights by layer identity, so each palette's alphamap means the same ground.
			TerrainLayer sharedLayer = resolve(a.MainTextureLayer);
			Func<TerrainLayer, int, int, float> weights = (layer, x, y) => layer == sharedLayer ? 0.2f : 0.4f;

			Terrain first = Tile(Vector3.zero, forwardPalette, weights);
			TerrainScatter.Scatter(new[] { first }, forwardPalette, forward, 77u, Options());
			Terrain second = Tile(Vector3.zero, backwardPalette, weights);
			TerrainScatter.Scatter(new[] { second }, backwardPalette, backward, 77u, Options());

			Assert.That(first.terrainData.treeInstances.Length, Is.GreaterThan(0));
			AssertSameTrees(first.terrainData.treeInstances, second.terrainData.treeInstances, "after swapping the biome order");
		}

		[Test]
		public void SpacingHoldsAcrossATileSeam()
		{
			const float spacing = 5f;
			Texture2D ground = Texture("ground");
			BiomeTemplate biome = Biome("ScatterSeam", ground);
			// Dense enough that every cell wants a tree: only spacing decides.
			biome.MainTextureLayer.prefabSpawnRules.Add(TreeRule("crowded", TreePrefab("tree"), 50f, spacing));
			SceneBiomeField field = Field(new[] { biome }, 8, 4, 16f, (x, z) => 0);
			SceneTerrainPalette palette = Palette(field);
			Terrain west = Tile(new Vector3(0f, 0f, 0f), palette, (layer, x, y) => 1f);
			Terrain east = Tile(new Vector3(TileMetres, 0f, 0f), palette, (layer, x, y) => 1f);

			TerrainScatterReport report = TerrainScatter.Scatter(new[] { west, east }, palette, field, 9u, Options());

			var points = new List<Vector2>();
			points.AddRange(WorldXZ(west));
			points.AddRange(WorldXZ(east));
			Assert.That(points.Count, Is.GreaterThan(100), "a dense rule should fill both tiles");
			Assert.That(points.Exists(p => p.x >= TileMetres - spacing && p.x < TileMetres), "trees close to the seam on the west");
			Assert.That(points.Exists(p => p.x >= TileMetres && p.x < TileMetres + spacing), "trees close to the seam on the east");
			for (int i = 0; i < points.Count; i++)
			{
				for (int j = i + 1; j < points.Count; j++)
				{
					Assert.That(Vector2.Distance(points[i], points[j]), Is.GreaterThanOrEqualTo(spacing - 1e-3f),
						$"trees at {points[i]} and {points[j]} are closer than the rule's spacing");
				}
			}
			Assert.That(report.Rules[0].RejectedSpacing, Is.GreaterThan(0), "spacing did the deciding");
		}

		[Test]
		public void ARuleGrowsOnlyWhereItsTextureIsStrongEnough()
		{
			Texture2D grass = Texture("grass");
			Texture2D moss = Texture("moss");
			BiomeTemplate biome = Biome("ScatterTexture", grass);
			var mossLayer = new TerrainTextureLayer { albedoTexture = moss };
			PrefabSpawnRule mossy = TreeRule("mossy", TreePrefab("stump"), 20f, 0f);
			mossy.minTextureWeight = 0.5f;
			mossLayer.prefabSpawnRules.Add(mossy);
			biome.DetailTextureLayers.Add(mossLayer);

			var resolve = Resolver();
			SceneBiomeField field = Field(new[] { biome }, 4, 4, 16f, (x, z) => 0, resolve);
			SceneTerrainPalette palette = SceneTerrainPalette.Build(field, resolve, null);
			TerrainLayer moss01 = resolve(mossLayer);
			// Moss on the west half of the alphamap (texels 0–15), grass on the east.
			Terrain tile = Tile(Vector3.zero, palette, (layer, x, y) => (layer == moss01) == (x < AlphaResolution / 2) ? 1f : 0f);

			TerrainScatterReport report = TerrainScatter.Scatter(new[] { tile }, palette, field, 3u, Options());

			List<Vector2> points = WorldXZ(tile);
			Assert.That(points.Count, Is.GreaterThan(20));
			// Texel 15 is at 15/31 of the tile and texel 16 at 16/31; the weight crosses 0.5 halfway, at exactly 32 m.
			Assert.That(points.TrueForAll(p => p.x < TileMetres * 0.5f + 1e-3f), "every tree stands on the moss half");
			Assert.That(report.Rules[0].RejectedTextureWeight, Is.GreaterThan(0));
		}

		[Test]
		public void ARuleGrowsOnlyInItsOwnBiomeEvenOnSharedGround()
		{
			/* Two biomes on one grass layer: one alphamap channel, at full weight everywhere. Only the
			 * biome gate keeps the pines in the west and the oaks in the east. The field's edge is
			 * soft by half a cell (4 m), nudged by up to a cell (8 m) of noise, and read at 2 m
			 * texels, so the margins are 14 m either side of the 32 m line. */
			Texture2D grass = Texture("grass");
			BiomeTemplate west = Biome("ScatterBiomeWest", grass);
			BiomeTemplate east = Biome("ScatterBiomeEast", grass);
			west.MainTextureLayer.prefabSpawnRules.Add(TreeRule("pines", TreePrefab("pine"), 10f, 0f));
			east.MainTextureLayer.prefabSpawnRules.Add(TreeRule("oaks", TreePrefab("oak"), 10f, 0f));
			var resolve = Resolver();
			SceneBiomeField field = Field(new[] { west, east }, 8, 8, 8f, (x, z) => x < 4 ? 0 : 1, resolve);
			SceneTerrainPalette palette = SceneTerrainPalette.Build(field, resolve, null);
			Assume.That(palette.Layers.Count, Is.EqualTo(1), "both biomes paint the one grass layer");
			Terrain tile = Tile(Vector3.zero, palette, (layer, x, y) => 1f);

			TerrainScatterReport report = TerrainScatter.Scatter(new[] { tile }, palette, field, 5u, Options());

			TreePrototype[] prototypes = tile.terrainData.treePrototypes;
			int pine = Array.FindIndex(prototypes, p => p.prefab.name == "pine");
			int oak = Array.FindIndex(prototypes, p => p.prefab.name == "oak");
			var pines = new List<float>();
			var oaks = new List<float>();
			foreach (TreeInstance tree in tile.terrainData.treeInstances)
			{
				(tree.prototypeIndex == pine ? pines : oaks).Add(tree.position.x * TileMetres);
			}
			Assert.That(pines.Count, Is.GreaterThan(10));
			Assert.That(oaks.Count, Is.GreaterThan(10));
			Assert.That(pines.TrueForAll(x => x < 46.5f), "no pine deep in the east");
			Assert.That(oaks.TrueForAll(x => x > 17.5f), "no oak deep in the west");
			Assert.That(report.Rules.TrueForAll(r => r.RejectedBiome > 0), "the biome gate did the deciding");
		}

		[Test]
		public void PrefabsUnityCannotDrawAreRefusedAndReported()
		{
			Texture2D ground = Texture("ground");
			BiomeTemplate biome = Biome("ScatterInvalid", ground);

			GameObject good = DetailPrefab("good", true);
			GameObject twoMeshes = DetailPrefab("two meshes", true);
			GameObject child = DetailPrefab("child", true);
			child.transform.SetParent(twoMeshes.transform);
			GameObject noMesh = Track(new GameObject("no mesh"));
			PrefabSpawnRule grass = DetailRule("grass", good, 10f);
			grass.prefabs = new[] { good, twoMeshes, noMesh };
			biome.MainTextureLayer.prefabSpawnRules.Add(grass);

			PrefabSpawnRule ghosts = TreeRule("ghosts", Track(new GameObject("empty tree")), 2f, 0f);
			biome.MainTextureLayer.prefabSpawnRules.Add(ghosts);

			SceneBiomeField field = Field(new[] { biome }, 4, 4, 16f, (x, z) => 0);
			SceneTerrainPalette palette = Palette(field);
			Terrain tile = Tile(Vector3.zero, palette, (layer, x, y) => 1f);

			TerrainScatterReport report = TerrainScatter.Scatter(new[] { tile }, palette, field, 11u, Options());

			Assert.That(tile.terrainData.detailPrototypes.Length, Is.EqualTo(1), "only the valid grass becomes a prototype");
			Assert.That(tile.terrainData.detailPrototypes[0].prototype, Is.SameAs(good));
			Assert.That(tile.terrainData.treePrototypes.Length, Is.EqualTo(0));
			Assert.That(tile.terrainData.treeInstances.Length, Is.EqualTo(0));
			Assert.That(report.InvalidPrefabs.Count, Is.EqualTo(3), string.Join("\n", report.InvalidPrefabs));
			Assert.That(report.InvalidPrefabs.Exists(line => line.Contains("'two meshes'")));
			Assert.That(report.InvalidPrefabs.Exists(line => line.Contains("'no mesh'")));
			Assert.That(report.InvalidPrefabs.Exists(line => line.Contains("'empty tree'")));
			TerrainScatterReport.RuleOutcome ghostOutcome = report.Rules.Find(r => r.RuleName == "ghosts");
			Assert.That(ghostOutcome.Skipped, Is.True, "a tree rule with no valid prefab never runs");
			Assert.That(report.DetailCellsCovered, Is.GreaterThan(0), "the valid grass still scatters");
		}

		[Test]
		public void ARerunClearsWhatTheLastRunPlaced()
		{
			Texture2D ground = Texture("ground");
			BiomeTemplate biome = Biome("ScatterRerun", ground);
			PrefabSpawnRule trees = TreeRule("trees", TreePrefab("tree"), 5f, 0f);
			biome.MainTextureLayer.prefabSpawnRules.Add(trees);
			SceneBiomeField field = Field(new[] { biome }, 4, 4, 16f, (x, z) => 0);
			SceneTerrainPalette palette = Palette(field);
			Terrain tile = Tile(Vector3.zero, palette, (layer, x, y) => 1f);

			TerrainScatter.Scatter(new[] { tile }, palette, field, 1u, Options());
			Assume.That(tile.terrainData.treeInstances.Length, Is.GreaterThan(0));

			trees.enableSpawning = false;
			TerrainScatter.Scatter(new[] { tile }, palette, field, 1u, Options());
			Assert.That(tile.terrainData.treeInstances.Length, Is.EqualTo(0));
			Assert.That(tile.terrainData.treePrototypes.Length, Is.EqualTo(0));
		}

		// ── Carpets ──────────────────────────────────────────────────

		[Test]
		public void ACarpetIsContinuousAndRisesWithItsTextureWeight()
		{
			/* One layer whose weight climbs linearly from 0 in the west to 1 in the east, and a carpet
			 * with no swathes (floor 1): its coverage is then the ramp alone, and along every row it
			 * must never fall, never jump, be bare where the weight is under the ramp and full where
			 * it is over it — with no bare cell anywhere on the full ground, which is the speckle. */
			Texture2D ground = Texture("ground");
			BiomeTemplate biome = Biome("CarpetRamp", ground);
			PrefabSpawnRule carpet = CarpetRule("turf", DetailPrefab("turf", true), 1f, floor: 1f);
			carpet.minTextureWeight = 0.5f;
			carpet.carpetWeightRamp = 0.4f;
			biome.MainTextureLayer.prefabSpawnRules.Add(carpet);
			SceneBiomeField field = Field(new[] { biome }, 4, 4, 16f, (x, z) => 0);
			SceneTerrainPalette palette = Palette(field);
			Terrain tile = Tile(Vector3.zero, palette, (layer, x, y) => x / (float)(AlphaResolution - 1));

			TerrainScatterReport report = TerrainScatter.Scatter(new[] { tile }, palette, field, 21u, Options());

			TerrainData data = tile.terrainData;
			int full = data.maxDetailScatterPerRes;
			int[,] map = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, 0);
			int resolution = data.detailWidth;
			for (int j = 0; j < resolution; j++)
			{
				for (int i = 0; i < resolution; i++)
				{
					float weight = (i + 0.5f) / resolution;
					if (weight <= 0.3f)
					{
						Assert.That(map[j, i], Is.EqualTo(0), $"cell ({i}, {j}) at weight {weight:0.00} is under the ramp and must be bare");
					}
					if (weight >= 0.7f)
					{
						Assert.That(map[j, i], Is.EqualTo(full), $"cell ({i}, {j}) at weight {weight:0.00} is over the ramp and must be full");
					}
					if (i > 0)
					{
						Assert.That(map[j, i], Is.GreaterThanOrEqualTo(map[j, i - 1]), $"coverage fell from cell {i - 1} to {i} on row {j} as the weight rose");
						Assert.That(map[j, i] - map[j, i - 1], Is.LessThanOrEqualTo(full / 6), $"coverage jumped between cells {i - 1} and {i} on row {j}");
					}
				}
			}
			TerrainScatterReport.RuleOutcome outcome = report.Rules[0];
			Assert.That(outcome.Carpet, Is.True);
			Assert.That(outcome.Placed, Is.GreaterThan(0));
			Assert.That(outcome.MeanCoverage, Is.InRange(0.01d, 1d));
			Assert.That(report.CarpetCellsCovered, Is.EqualTo(outcome.Placed));
			Assert.That(report.DetailCellsCovered, Is.EqualTo(0), "a carpet is not counted against the scattered detail budget");
		}

		[Test]
		public void OverlappingCarpetsShareACellAndNeverExceedIt()
		{
			// Two textures at half weight each, each with a full carpet: each would fill the cell alone.
			Texture2D lush = Texture("lush");
			Texture2D dry = Texture("dry");
			BiomeTemplate biome = Biome("CarpetShare", lush);
			var dryLayer = new TerrainTextureLayer { albedoTexture = dry };
			biome.DetailTextureLayers.Add(dryLayer);
			biome.MainTextureLayer.prefabSpawnRules.Add(CarpetRule("lush turf", DetailPrefab("lush", true), 1f, floor: 1f));
			dryLayer.prefabSpawnRules.Add(CarpetRule("dry turf", DetailPrefab("dry", true), 1f, floor: 1f));

			var resolve = Resolver();
			SceneBiomeField field = Field(new[] { biome }, 4, 4, 16f, (x, z) => 0, resolve);
			SceneTerrainPalette palette = SceneTerrainPalette.Build(field, resolve, null);
			Assume.That(palette.Layers.Count, Is.EqualTo(2));
			Terrain tile = Tile(Vector3.zero, palette, (layer, x, y) => 0.5f);

			TerrainScatterReport report = TerrainScatter.Scatter(new[] { tile }, palette, field, 8u, Options());

			TerrainData data = tile.terrainData;
			int full = data.maxDetailScatterPerRes;
			int[,] a = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, 0);
			int[,] b = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, 1);
			for (int j = 0; j < data.detailHeight; j++)
			{
				for (int i = 0; i < data.detailWidth; i++)
				{
					Assert.That(a[j, i] + b[j, i], Is.LessThanOrEqualTo(full), $"cell ({i}, {j}) holds more than a full cell");
					Assert.That(a[j, i] + b[j, i], Is.GreaterThanOrEqualTo(full - 2), $"cell ({i}, {j}) should be filled between them");
					Assert.That(Mathf.Abs(a[j, i] - b[j, i]), Is.LessThanOrEqualTo(1), $"equal carpets should share cell ({i}, {j}) equally");
				}
			}
			Assert.That(report.Rules.TrueForAll(r => r.SharedCells > 0), "both carpets gave up a share");
		}

		[Test]
		public void CarpetsAreDeterministicAndSeamlessAcrossTiles()
		{
			/* Two tiles overlapping by half: every cell of the overlap is computed once from each tile.
			 * Swathes are a function of world position alone, so both must write the same value there —
			 * which is what makes a seam between abutting tiles invisible — and re-running, in either
			 * tile order, must write the same maps. */
			Texture2D ground = Texture("ground");
			BiomeTemplate biome = Biome("CarpetSeam", ground);
			biome.MainTextureLayer.prefabSpawnRules.Add(CarpetRule("turf", DetailPrefab("turf", true), 0.9f, floor: 0.3f, clumpMetres: 8f));
			SceneBiomeField field = Field(new[] { biome }, 8, 4, 16f, (x, z) => 0);
			SceneTerrainPalette palette = Palette(field);
			Terrain west = Tile(Vector3.zero, palette, (layer, x, y) => 1f);
			Terrain east = Tile(new Vector3(TileMetres * 0.5f, 0f, 0f), palette, (layer, x, y) => 1f);

			TerrainScatter.Scatter(new[] { west, east }, palette, field, 55u, Options());
			int[,] westMap = Layer0(west);
			int[,] eastMap = Layer0(east);
			int half = westMap.GetLength(1) / 2;
			var distinct = new HashSet<int>();
			for (int j = 0; j < westMap.GetLength(0); j++)
			{
				for (int i = 0; i < half; i++)
				{
					Assert.That(eastMap[j, i], Is.EqualTo(westMap[j, half + i]), $"the overlap disagrees at world cell ({half + i}, {j})");
					distinct.Add(westMap[j, half + i]);
				}
			}
			Assert.That(distinct.Count, Is.GreaterThan(8), "the swathes should vary the coverage, or the seam test proves nothing");

			TerrainScatter.Scatter(new[] { east, west }, palette, field, 55u, Options());
			Assert.That(Layer0(west), Is.EqualTo(westMap), "west, re-run in reverse tile order");
			Assert.That(Layer0(east), Is.EqualTo(eastMap), "east, re-run in reverse tile order");
		}

		[Test]
		public void CarpetSwathesAreSmoothBoundedAndSeeded()
		{
			const float metres = 16f;
			const float floor = 0.4f;
			float previous = TerrainScatter.CarpetClump(7u, -100f, 3f, metres, floor);
			bool differs = false;
			for (float x = -99.75f; x <= 100f; x += 0.25f)
			{
				float value = TerrainScatter.CarpetClump(7u, x, 3f, metres, floor);
				Assert.That(value, Is.InRange(floor, 1f), $"swathe at x {x} is outside floor..1");
				Assert.That(Mathf.Abs(value - previous), Is.LessThan(0.08f), $"swathe jumps at x {x}");
				Assert.That(TerrainScatter.CarpetClump(7u, x, 3f, metres, floor), Is.EqualTo(value), "same seed and place, same swathe");
				differs |= Mathf.Abs(TerrainScatter.CarpetClump(8u, x, 3f, metres, floor) - value) > 0.05f;
				previous = value;
			}
			Assert.That(differs, Is.True, "another seed should give other swathes");
			Assert.That(TerrainScatter.CarpetClump(7u, 12f, 3f, metres, 1f), Is.EqualTo(1f), "a floor of 1 is an even carpet");

			for (float w = 0f; w < 1f; w += 0.01f)
			{
				Assert.That(TerrainScatter.CarpetRamp(w + 0.01f, 0.2f, 0.25f), Is.GreaterThanOrEqualTo(TerrainScatter.CarpetRamp(w, 0.2f, 0.25f)), $"the ramp falls at weight {w}");
			}
			Assert.That(TerrainScatter.CarpetRamp(0f, 0.05f, 0.5f), Is.EqualTo(0f), "no texture, no carpet, however low the threshold");
			Assert.That(TerrainScatter.CarpetShare(0.6f, 1.5f), Is.EqualTo(0.4f).Within(1e-6f));
			Assert.That(TerrainScatter.CarpetShare(0.6f, 0.9f), Is.EqualTo(0.6f));
		}

		// ── Groups ───────────────────────────────────────────────────

		[Test]
		public void TheGroupFieldAveragesOneGathersAndIsSeeded()
		{
			const float metres = 6f, pitch = 24f, background = 0.2f;
			double sum = 0d;
			int samples = 0;
			var values = new List<float>(400 * 400);
			bool differs = false;
			for (float z = -600f; z < 600f; z += 3f)
			{
				for (float x = -600f; x < 600f; x += 3f)
				{
					float value = TerrainScatter.ClusterIntensity(11u, x, z, metres, pitch, background, out float closeness);
					Assert.That(value, Is.GreaterThanOrEqualTo(background - 1e-5f), $"below the even share at ({x}, {z})");
					Assert.That(closeness, Is.InRange(0f, 1f));
					sum += value;
					samples++;
					values.Add(value);
					if (samples % 97 == 0)
					{
						Assert.That(TerrainScatter.ClusterIntensity(11u, x, z, metres, pitch, background, out _), Is.EqualTo(value), "same seed and place, same field");
						differs |= Mathf.Abs(TerrainScatter.ClusterIntensity(12u, x, z, metres, pitch, background, out _) - value) > 0.1f;
					}
				}
			}
			Assert.That(sum / samples, Is.EqualTo(1d).Within(0.08d), "groups move a rule's density, they must not change it");
			Assert.That(differs, Is.True, "another seed should give other groups");

			// Gathered: the densest fifth of the ground holds well over a fifth of the instances.
			values.Sort();
			double top = 0d;
			for (int i = values.Count * 4 / 5; i < values.Count; i++)
			{
				top += values[i];
			}
			Assert.That(top / sum, Is.GreaterThan(0.5d), "the field should concentrate the density into groups");

			Assert.That(TerrainScatter.ClusterIntensity(11u, 5f, 5f, 0f, pitch, background, out _), Is.EqualTo(1f), "no radius is an even scatter");
			Assert.That(TerrainScatter.ClusterIntensity(11u, 5f, 5f, metres, pitch, 1f, out _), Is.EqualTo(1f), "an even share of 1 is an even scatter");
		}

		[Test]
		public void AGroupHoldsItsSizeWhateverTheDensityAndFitsItsSpacing()
		{
			foreach (float density in new[] { 0.02f, 0.3f, 2f, 20f })
			{
				var rule = new PrefabSpawnRule { densityPer100m2 = density, minSpacing = 8f, clusterMetres = 5f, clusterSize = 6f, clusterBackground = 0f };
				TerrainScatter.ClusterShape(rule, out float metres, out float pitch);
				// One group per pitch², so an average group holds density × pitch², unless two radii set a wider floor.
				float members = density * 0.01f * pitch * pitch;
				if (pitch > 2f * metres + 1e-3f)
				{
					Assert.That(members, Is.EqualTo(6f).Within(1e-3f), $"density {density}: a group should hold its size");
				}
				else
				{
					Assert.That(members, Is.GreaterThan(6f), $"density {density}: only a dense rule's groups are held apart by their radius");
				}
				Assert.That(metres, Is.GreaterThanOrEqualTo(1.3f * 8f * Mathf.Sqrt(6f) - 1e-3f), "the group should be wide enough for its members at the rule's spacing");
				Assert.That(pitch, Is.GreaterThanOrEqualTo(2f * metres), "group centres stand at least two radii apart");
			}

			var legacy = new PrefabSpawnRule { densityPer100m2 = 1f, clusterMetres = 5f, clusterSpacing = 4f };
			TerrainScatter.ClusterShape(legacy, out float legacyMetres, out float legacyPitch);
			Assert.That(legacyMetres, Is.EqualTo(5f));
			Assert.That(legacyPitch, Is.EqualTo(20f), "without a size, centres stand Cluster Spacing radii apart");
		}

		[Test]
		public void AClusteredRuleStandsInGroups()
		{
			Texture2D ground = Texture("ground");
			GameObject prefab = TreePrefab("stone");

			List<Vector2> Scatter(bool grouped)
			{
				BiomeTemplate biome = Biome(grouped ? "ScatterGrouped" : "ScatterEven", ground);
				PrefabSpawnRule rule = TreeRule("stones", prefab, 1f, 0f);
				if (grouped)
				{
					rule.clusterMetres = 4f;
					rule.clusterSpacing = 4f;
					rule.clusterBackground = 0.1f;
				}
				biome.MainTextureLayer.prefabSpawnRules.Add(rule);
				// Nine tiles, so enough groups stand on the ground for the count to settle.
				SceneBiomeField field = Field(new[] { biome }, 12, 12, 16f, (x, z) => 0);
				SceneTerrainPalette palette = Palette(field);
				var tiles = new List<Terrain>();
				for (int z = 0; z < 3; z++)
				{
					for (int x = 0; x < 3; x++)
					{
						tiles.Add(Tile(new Vector3(x * TileMetres, 0f, z * TileMetres), palette, (layer, ax, ay) => 1f));
					}
				}
				TerrainScatter.Scatter(tiles, palette, field, 77u, Options());
				var points = new List<Vector2>();
				foreach (Terrain tile in tiles)
				{
					points.AddRange(WorldXZ(tile));
				}
				return points;
			}

			List<Vector2> even = Scatter(false);
			List<Vector2> grouped = Scatter(true);
			Assert.That(even.Count, Is.GreaterThan(250));
			// A strong group's heart saturates (one per cell at most), so a little of the count is lost there.
			Assert.That(grouped.Count, Is.GreaterThan(even.Count * 0.7f), "grouping should move instances, not drop them");
			Assert.That(MeanNearestNeighbour(grouped), Is.LessThan(0.75f * MeanNearestNeighbour(even)),
				"grouped instances should stand much closer to their neighbours than an even scatter's");
		}

		[Test]
		public void AGroupsHeartAsksForMoreThanOneDetailInACell()
		{
			Texture2D ground = Texture("ground");
			BiomeTemplate biome = Biome("ScatterGroupedDetail", ground);
			PrefabSpawnRule rule = DetailRule("flowers", DetailPrefab("flowers", true), 20f);
			rule.clusterMetres = 4f;
			rule.clusterBackground = 0.1f;
			biome.MainTextureLayer.prefabSpawnRules.Add(rule);
			SceneBiomeField field = Field(new[] { biome }, 4, 4, 16f, (x, z) => 0);
			SceneTerrainPalette palette = Palette(field);
			Terrain tile = Tile(Vector3.zero, palette, (layer, x, y) => 1f);

			TerrainScatter.Scatter(new[] { tile }, palette, field, 5u, Options());

			int[,] map = Layer0(tile);
			int most = 0, covered = 0;
			foreach (int value in map)
			{
				most = Math.Max(most, value);
				covered += value > 0 ? 1 : 0;
			}
			int full = tile.terrainData.maxDetailScatterPerRes;
			Assert.That(covered, Is.GreaterThan(0));
			Assert.That(most, Is.GreaterThan(Mathf.RoundToInt(128f / 255f * full)), "a group's heart should fill past what one draw writes");
			Assert.That(most, Is.LessThanOrEqualTo(full), "never past a full cell");
		}

		private static float MeanNearestNeighbour(List<Vector2> points)
		{
			double total = 0d;
			for (int i = 0; i < points.Count; i++)
			{
				float nearest = float.MaxValue;
				for (int j = 0; j < points.Count; j++)
				{
					if (i != j)
					{
						nearest = Mathf.Min(nearest, (points[i] - points[j]).sqrMagnitude);
					}
				}
				total += Mathf.Sqrt(nearest);
			}
			return (float)(total / Mathf.Max(1, points.Count));
		}

		// ── Sink ─────────────────────────────────────────────────────

		[Test]
		public void ASunkTreeStandsBelowTheSurfaceByItsSink()
		{
			Texture2D ground = Texture("ground");
			BiomeTemplate biome = Biome("SinkFlat", ground);
			PrefabSpawnRule rule = TreeRule("trees", TreePrefab("tree"), 10f, 0f);
			rule.sinkRange = new Vector2(0.3f, 0.3f);
			biome.MainTextureLayer.prefabSpawnRules.Add(rule);
			SceneBiomeField field = Field(new[] { biome }, 4, 4, 16f, (x, z) => 0);
			SceneTerrainPalette palette = Palette(field);
			Terrain tile = Tile(Vector3.zero, palette, (layer, x, y) => 1f, (x, z) => 0.5f);

			TerrainScatter.Scatter(new[] { tile }, palette, field, 2u, Options());

			TerrainData data = tile.terrainData;
			Assert.That(data.treeInstances.Length, Is.GreaterThan(10));
			foreach (TreeInstance tree in data.treeInstances)
			{
				float surface = data.GetInterpolatedHeight(tree.position.x, tree.position.z);
				Assert.That(tree.position.y * data.size.y, Is.EqualTo(surface - 0.3f).Within(1e-3f), "a tree should stand its sink below the surface");
			}
		}

		[Test]
		public void ASlopeSinksATreeByItsFootprintTimesTheTangent()
		{
			/* A plane rising 20 m over the 64 m tile: tan(slope) = 0.3125 everywhere. The cube's
			 * footprint is 0.5 m and its width 1, so a slope factor of 1 adds 0.5 × 0.3125 m. */
			const float heightMetres = 40f;
			Texture2D ground = Texture("ground");
			BiomeTemplate biome = Biome("SinkSlope", ground);
			PrefabSpawnRule rule = TreeRule("trees", TreePrefab("tree"), 10f, 0f);
			rule.sinkRange = new Vector2(0.1f, 0.1f);
			rule.sinkSlopeFactor = 1f;
			biome.MainTextureLayer.prefabSpawnRules.Add(rule);
			SceneBiomeField field = Field(new[] { biome }, 4, 4, 16f, (x, z) => 0);
			SceneTerrainPalette palette = Palette(field);
			Terrain tile = Tile(Vector3.zero, palette, (layer, x, y) => 1f, (x, z) => 0.25f + 0.5f * x / 32f, heightMetres);
			Assume.That(TerrainScatter.FootprintRadius(rule.prefabs[0]), Is.EqualTo(0.5f).Within(1e-5f));

			TerrainScatter.Scatter(new[] { tile }, palette, field, 4u, Options());

			TerrainData data = tile.terrainData;
			float expected = 0.1f + 1f * 0.5f * (20f / TileMetres);
			Assert.That(data.treeInstances.Length, Is.GreaterThan(10));
			foreach (TreeInstance tree in data.treeInstances)
			{
				float surface = data.GetInterpolatedHeight(tree.position.x, tree.position.z);
				Assert.That(surface - tree.position.y * heightMetres, Is.EqualTo(expected).Within(2e-3f), "the slope should add footprint × tan(slope) to the sink");
			}
		}

		// ── Scene building ───────────────────────────────────────────

		private T Track<T>(T obj) where T : Object
		{
			created.Add(obj);
			return obj;
		}

		private Texture2D Texture(string name)
		{
			return Track(new Texture2D(4, 4) { name = name });
		}

		private BiomeTemplate Biome(string name, Texture2D mainAlbedo)
		{
			var biome = Track(ScriptableObject.CreateInstance<BiomeTemplate>());
			biome.name = name;
			biome.DisplayName = name;
			biome.MainTextureLayer.albedoTexture = mainAlbedo;
			return biome;
		}

		/// <summary>One terrain layer per albedo texture, so biomes sharing a texture share a channel.</summary>
		private Func<TerrainTextureLayer, TerrainLayer> Resolver()
		{
			var layers = new Dictionary<Texture2D, TerrainLayer>();
			return source =>
			{
				if (source == null || source.albedoTexture == null)
				{
					return null;
				}
				if (!layers.TryGetValue(source.albedoTexture, out TerrainLayer layer))
				{
					layer = Track(new TerrainLayer { diffuseTexture = source.albedoTexture, name = source.albedoTexture.name });
					layers[source.albedoTexture] = layer;
				}
				return layer;
			};
		}

		private Func<TerrainTextureLayer, TerrainLayer> lastResolver;

		private SceneBiomeField Field(BiomeTemplate[] biomes, int width, int height, float cellMetres, Func<int, int, int> cell,
			Func<TerrainTextureLayer, TerrainLayer> resolve = null)
		{
			lastResolver = resolve ?? Resolver();
			var cells = new byte[width * height];
			for (int z = 0; z < height; z++)
			{
				for (int x = 0; x < width; x++)
				{
					cells[z * width + x] = (byte)cell(x, z);
				}
			}
			return SceneBiomeField.FromCells(width, height, cellMetres, Vector2.zero, biomes, cells, blendMetres: 0f, seed: 1u);
		}

		private SceneTerrainPalette Palette(SceneBiomeField field)
		{
			return SceneTerrainPalette.Build(field, lastResolver, null);
		}

		/// <summary>A 64 m tile, its alphamap from <paramref name="weight"/> and, when given, its heightmap (33², normalised) from <paramref name="height"/>.</summary>
		private Terrain Tile(Vector3 origin, SceneTerrainPalette palette, Func<TerrainLayer, int, int, float> weight,
			Func<int, int, float> height = null, float heightMetres = 20f)
		{
			var data = Track(new TerrainData { heightmapResolution = 33, size = new Vector3(TileMetres, heightMetres, TileMetres) });
			if (height != null)
			{
				var heights = new float[33, 33];
				for (int z = 0; z < 33; z++)
				{
					for (int x = 0; x < 33; x++)
					{
						// Unity's heightmaps are [y, x].
						heights[z, x] = height(x, z);
					}
				}
				data.SetHeights(0, 0, heights);
			}
			data.alphamapResolution = AlphaResolution;
			var layers = new TerrainLayer[palette.Layers.Count];
			for (int l = 0; l < layers.Length; l++)
			{
				layers[l] = palette.Layers[l];
			}
			data.terrainLayers = layers;
			var maps = new float[AlphaResolution, AlphaResolution, layers.Length];
			for (int y = 0; y < AlphaResolution; y++)
			{
				for (int x = 0; x < AlphaResolution; x++)
				{
					for (int l = 0; l < layers.Length; l++)
					{
						maps[y, x, l] = weight(layers[l], x, y);
					}
				}
			}
			data.SetAlphamaps(0, 0, maps);

			GameObject host = Track(Terrain.CreateTerrainGameObject(data));
			host.transform.position = origin;
			return host.GetComponent<Terrain>();
		}

		private static TerrainScatterOptions Options()
		{
			return new TerrainScatterOptions
			{
				NormalizedHeight = (x, y, z) => 0.5f,
				DetailResolution = 32,
				DetailResolutionPerPatch = 8,
				TreeCellMetres = 2f,
			};
		}

		private static PrefabSpawnRule TreeRule(string name, GameObject prefab, float density, float spacing)
		{
			return new PrefabSpawnRule
			{
				ruleName = name,
				prefabs = new[] { prefab },
				spawnChannel = PrefabSpawnChannel.TreeInstance,
				densityPer100m2 = density,
				minSpacing = spacing,
				minTextureWeight = 0.3f,
				maxPerChunk = 0,
			};
		}

		private static PrefabSpawnRule DetailRule(string name, GameObject prefab, float density)
		{
			return new PrefabSpawnRule
			{
				ruleName = name,
				prefabs = new[] { prefab },
				spawnChannel = PrefabSpawnChannel.DetailLayer,
				densityPer100m2 = density,
				minSpacing = 0f,
				minTextureWeight = 0.3f,
				maxPerChunk = 0,
				detailInstancesPerSpawn = 128,
			};
		}

		private static PrefabSpawnRule CarpetRule(string name, GameObject prefab, float coverage, float floor, float clumpMetres = 16f)
		{
			return new PrefabSpawnRule
			{
				ruleName = name,
				prefabs = new[] { prefab },
				spawnChannel = PrefabSpawnChannel.DetailLayer,
				detailPlacement = DetailPlacement.Carpet,
				densityPer100m2 = 20f,
				minSpacing = 0f,
				minTextureWeight = 0.25f,
				carpetWeightRamp = 0.3f,
				carpetCoverage = coverage,
				carpetClumpFloor = floor,
				carpetClumpMetres = clumpMetres,
				maxPerChunk = 0,
			};
		}

		private static int[,] Layer0(Terrain terrain)
		{
			TerrainData data = terrain.terrainData;
			return data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, 0);
		}

		private GameObject TreePrefab(string name)
		{
			GameObject tree = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
			tree.name = name;
			return tree;
		}

		private GameObject DetailPrefab(string name, bool instanced)
		{
			var mesh = Track(new Mesh { name = name });
			mesh.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f) };
			mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();

			Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color");
			var material = Track(new Material(shader) { enableInstancing = instanced });

			var prefab = Track(new GameObject(name));
			prefab.AddComponent<MeshFilter>().sharedMesh = mesh;
			prefab.AddComponent<MeshRenderer>().sharedMaterial = material;
			return prefab;
		}

		private static List<Vector2> WorldXZ(Terrain terrain)
		{
			var points = new List<Vector2>();
			Vector3 origin = terrain.transform.position;
			Vector3 size = terrain.terrainData.size;
			foreach (TreeInstance tree in terrain.terrainData.treeInstances)
			{
				points.Add(new Vector2(origin.x + tree.position.x * size.x, origin.z + tree.position.z * size.z));
			}
			return points;
		}

		private static bool SameTrees(TreeInstance[] a, TreeInstance[] b)
		{
			if (a.Length != b.Length)
			{
				return false;
			}
			for (int i = 0; i < a.Length; i++)
			{
				if (a[i].prototypeIndex != b[i].prototypeIndex || a[i].position.x != b[i].position.x || a[i].position.y != b[i].position.y || a[i].position.z != b[i].position.z ||
					a[i].widthScale != b[i].widthScale || a[i].heightScale != b[i].heightScale || a[i].rotation != b[i].rotation ||
					!a[i].color.Equals(b[i].color))
				{
					return false;
				}
			}
			return true;
		}

		private static void AssertSameTrees(TreeInstance[] expected, TreeInstance[] actual, string context)
		{
			Assert.That(actual.Length, Is.EqualTo(expected.Length), $"tree count, {context}");
			Assert.That(SameTrees(expected, actual), Is.True, $"every tree in the same place (height and sink included) with the same size, turn and colour, {context}");
		}
	}
}
