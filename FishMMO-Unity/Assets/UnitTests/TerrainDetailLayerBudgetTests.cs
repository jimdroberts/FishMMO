using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Unity keeps a terrain's detail layer index in a byte (TerrainDetailLayerBudget): the scatter gives each tile only the
	/// prototypes of the rules that reach it, so every rule's map lands in its own prototype, and the cut checks that every
	/// painted plant stands on its own biome's ground. 2026-10-27: Moron 2's 578 prototypes on every tile put Woodland's
	/// pasture grass (layer 560) into the Forest's holly bush (layer 48), a carpet of bushes over every woodland.
	/// </summary>
	public class TerrainDetailLayerBudgetTests
	{
		private static string Source(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative));

		/// <summary>A scene like Moron 2's: rules of one to three variants, back to back, <paramref name="prototypes"/> in all.</summary>
		private static List<IReadOnlyList<int>> Rules(int prototypes)
		{
			var rules = new List<IReadOnlyList<int>>();
			int next = 0;
			for (int r = 0; next < prototypes; r++)
			{
				var list = new List<int>();
				for (int v = 0; v < 1 + r % 3 && next < prototypes; v++)
				{
					list.Add(next++);
				}
				rules.Add(list);
			}
			return rules;
		}

		/// <summary>
		/// Writes every taken rule's map as Unity stores it (the layer index in a byte) and returns, per scene prototype, the scene
		/// prototype whose layer actually holds its map (−1 where the tile does not carry it).
		/// </summary>
		private static int[] StoredAs(int[] layerOf, List<int> kept)
		{
			var stored = new int[layerOf.Length];
			for (int p = 0; p < layerOf.Length; p++)
			{
				stored[p] = -1;
				if (layerOf[p] < 0)
				{
					continue;
				}
				int layer = TerrainDetailLayerBudget.StoredLayer(layerOf[p]);
				stored[p] = layer < kept.Count ? kept[layer] : -1;
			}
			return stored;
		}

		[Test]
		public void StoredLayer_PastTheByte_IsAnotherPlantsLayer_AsMeasuredOnMoron2()
		{
			// Woodland's pasture grass and Yorkshire fog, painted into the Forest's holly and the meadow's carline thistle.
			LogAssert.AreEqual(48, TerrainDetailLayerBudget.StoredLayer(560), "layer 560 is stored as 48");
			LogAssert.AreEqual(30, TerrainDetailLayerBudget.StoredLayer(542), "layer 542 is stored as 30");
			LogAssert.AreEqual(255, TerrainDetailLayerBudget.StoredLayer(255), "the last addressable layer is itself");
		}

		[Test]
		public void TheWholeSceneOnEveryTile_PutsRulesInOtherRulesPrototypes()
		{
			// The old assignment: the scene's index is the tile's. What the guard below would have caught.
			const int scene = 578;
			var identity = new int[scene];
			var all = new List<int>();
			for (int p = 0; p < scene; p++)
			{
				identity[p] = p;
				all.Add(p);
			}
			int[] stored = StoredAs(identity, all);
			int wrong = 0;
			for (int p = 0; p < scene; p++)
			{
				wrong += stored[p] != p ? 1 : 0;
			}
			LogAssert.AreEqual(scene - TerrainDetailLayerBudget.MaxLayers, wrong, "every prototype from 256 up lands in another's layer");
			LogAssert.AreEqual(48, stored[560], "the woodland pasture grass in the forest holly");
		}

		/// <summary>
		/// Paints a tile as the scatter does: each rule that places cells asks the tile for its prototypes' layers before it
		/// writes, and its map is stored as Unity stores it. Returns, per scene prototype, the scene prototype whose layer holds
		/// its map (−1 where it was not written).
		/// </summary>
		private static int[] PaintTile(TerrainDetailLayerBudget.TileLayers tile, List<IReadOnlyList<int>> rules, System.Func<int, bool> places, int scene, int limit)
		{
			tile.Begin(scene, limit);
			var holder = new int[scene];
			for (int p = 0; p < scene; p++)
			{
				holder[p] = -1;
			}
			for (int r = 0; r < rules.Count; r++)
			{
				if (!places(r))
				{
					continue;
				}
				foreach (int p in rules[r])
				{
					int layer = tile.LayerFor(p, out _);
					if (layer < 0)
					{
						continue;
					}
					int stored = TerrainDetailLayerBudget.StoredLayer(layer);
					holder[p] = stored < tile.Kept.Count ? tile.Kept[stored] : -1;
				}
			}
			return holder;
		}

		[Test]
		public void TileLayers_ASceneOf578_EveryPaintingRuleLandsInItsOwnPrototype()
		{
			List<IReadOnlyList<int>> rules = Rules(578);
			// The tile is painted by a band of early rules and by the rules at the end of the list (Woodland's, 530 up).
			bool Places(int r) => rules[r][0] < 90 || rules[r][0] >= 530;
			var tile = new TerrainDetailLayerBudget.TileLayers();
			int[] holder = PaintTile(tile, rules, Places, 578, TerrainDetailLayerBudget.MaxLayers - TerrainScatter.SiteHeadroom);

			LogAssert.IsTrue(tile.Kept.Count <= TerrainDetailLayerBudget.MaxLayers - TerrainScatter.SiteHeadroom, $"the tile holds {tile.Kept.Count}, inside the room");
			for (int r = 0; r < rules.Count; r++)
			{
				foreach (int p in rules[r])
				{
					LogAssert.AreEqual(Places(r) ? p : -1, holder[p], $"rule {r}'s prototype {p}: its own map, or none where it paints nothing");
					LogAssert.AreEqual(Places(r), tile.LayerOf(p) >= 0, $"prototype {p} has a layer only where it is painted");
				}
			}
			LogAssert.IsTrue(tile.LayerOf(560) >= 0 && tile.LayerOf(560) < TerrainDetailLayerBudget.MaxLayers, "the pasture grass has a layer Unity can address");
			LogAssert.AreEqual(tile.LayerOf(560), tile.LayerFor(560, out bool again), "asking again gives the same layer");
			LogAssert.IsFalse(again, "and gives none new");
		}

		[Test]
		public void TileLayers_PastTheRoom_RefusesRatherThanSharing()
		{
			List<IReadOnlyList<int>> rules = Rules(600);
			var tile = new TerrainDetailLayerBudget.TileLayers();
			int[] holder = PaintTile(tile, rules, r => true, 600, 240);

			LogAssert.AreEqual(240, tile.Kept.Count, "filled to the room");
			int refused = 0;
			for (int p = 0; p < 600; p++)
			{
				if (tile.LayerOf(p) < 0)
				{
					refused++;
					LogAssert.AreEqual(-1, holder[p], $"refused prototype {p} wrote nothing");
				}
				else
				{
					LogAssert.AreEqual(p, holder[p], $"kept prototype {p} holds its own map");
				}
			}
			LogAssert.AreEqual(360, refused, "the rest refused");
			LogAssert.AreEqual(-1, tile.LayerFor(-1, out _), "not a scene index");
			tile.Begin(600, 999);
			LogAssert.AreEqual(TerrainDetailLayerBudget.MaxLayers, tile.Limit, "a room past Unity's is Unity's");
			LogAssert.AreEqual(0, tile.Kept.Count, "a new tile starts empty");
		}

		[Test]
		public void CountForeign_FindsAMapInAnotherBiomesPrototype()
		{
			const int forest = 11, woodland = 22;
			// Ten blocks: the first two forest, the rest woodland. Layer 0 is the forest's holly, layer 1 the woodland's grass.
			var biomes = new List<ICollection<int>>();
			var layers = new List<int[]>();
			for (int b = 0; b < 10; b++)
			{
				biomes.Add(new HashSet<int> { b < 2 ? forest : woodland });
				// The holly painted over every woodland block (the bug), the woodland grass where it should be.
				layers.Add(b < 2 ? new[] { 0 } : new[] { 0, 1 });
			}
			var painted = new int[2];
			var foreign = new int[2];
			TerrainDetailLayerBudget.CountForeign(layers, biomes, new[] { forest, woodland }, painted, foreign);
			LogAssert.AreEqual(10, painted[0], "the holly is painted in every block");
			LogAssert.AreEqual(8, foreign[0], "eight of them woodland");
			LogAssert.IsTrue(TerrainDetailLayerBudget.IsMisplaced(painted[0], foreign[0]), "the holly is misplaced");
			LogAssert.IsFalse(TerrainDetailLayerBudget.IsMisplaced(painted[1], foreign[1]), "the woodland grass is where it belongs");
		}

		[Test]
		public void IsMisplaced_ABlendsReach_IsNot()
		{
			LogAssert.IsFalse(TerrainDetailLayerBudget.IsMisplaced(40, 1), "one block past its biome is a blend's reach");
			LogAssert.IsFalse(TerrainDetailLayerBudget.IsMisplaced(40, 4), "a tenth or less is a blend's reach");
			LogAssert.IsTrue(TerrainDetailLayerBudget.IsMisplaced(40, 5), "more than a tenth is not");
			LogAssert.IsTrue(TerrainDetailLayerBudget.IsMisplaced(3, 3), "nothing of its biome near any of its blocks");
			LogAssert.IsFalse(TerrainDetailLayerBudget.IsMisplaced(0, 0), "unpainted");
		}

		[Test]
		public void Sources_WriteEveryLayerThroughTheTilesOwnIndex()
		{
			string scatter = Source("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/Terrain/TerrainScatter.cs");
			string modes = Source("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/Terrain/TerrainScatter.Modes.cs");
			LogAssert.IsFalse(scatter.Contains("SetDetailLayer(0, 0, rule.Prototypes["), "the scatter never writes a scene index as a tile's layer");
			LogAssert.IsFalse(scatter.Contains("data.detailPrototypes = detailPrototypes;"), "no tile is given the whole scene's prototypes");
			LogAssert.IsTrue(scatter.Contains("work.Tile.LayerFor(scene, out bool added);"), "a tile's layers are given as they are first written");
			LogAssert.IsTrue(scatter.Contains("int layer = TileLayer(work, rule.Prototypes[v]);"), "the writes go through the tile's map");
			LogAssert.IsTrue(modes.Contains("int p = TileLayer(work, scene);"), "the anchors read the tile's layers");
			string grove = Source("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Features/PointOfInterestHerbGrove.cs");
			LogAssert.IsTrue(grove.Contains("prototypes.Length >= TerrainDetailLayerBudget.MaxLayers"), "a site never adds a layer past Unity's last");
			string generator = Source("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGenerator.cs");
			string repaint = Source("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/Terrain/BiomeRepaintTool.cs");
			LogAssert.IsTrue(generator.Contains("TerrainDetailLayerCheck.Check(terrains, result.BiomeMap, result.Notes);"), "the cut checks its detail layers");
			LogAssert.IsTrue(repaint.Contains("TerrainDetailLayerCheck.Check("), "so does a repaint");
		}
	}
}
