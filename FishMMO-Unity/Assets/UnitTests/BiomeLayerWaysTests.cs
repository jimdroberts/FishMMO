using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Decision 19 of the biome suite (2026-10-16, the Taiga round): a biome's main and detail layers may carry ways of their
	/// own (TerrainTextureLayer.ways: a path, road and cobble ground each), and where one is set the terrain draws that way
	/// across that ground with it, the biome's own way grounds everywhere else. Empty is the biome's own, as every ground was
	/// before, so a template authored from a spec that sets none cuts the same scene. These are the pure parts: the slot
	/// keys, the spec hook, the palette's choice of slice, and that the cut, the authoring and the reader are wired to them.
	/// </summary>
	public class BiomeLayerWaysTests
	{
		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		// ── Slot keys ─────────────────────────────────────────────────

		/// <summary>A ground layer's way key is its ground's slot and the way's, and splits back into the two.</summary>
		[Test]
		public void WaySlot_RoundTrips_ForTheMainAndEveryDetailLayer()
		{
			var grounds = new List<string> { BiomeLocalArt.SlotMain };
			for (int i = 0; i < 12; i++)
			{
				grounds.Add(BiomeLocalArt.DetailSlot(i));
			}
			Assert.That(BiomeLocalArt.WaySlots, Is.EqualTo(new[] { BiomeLocalArt.SlotPath, BiomeLocalArt.SlotRoad, BiomeLocalArt.SlotCobbles }));
			foreach (string ground in grounds)
			{
				foreach (string way in BiomeLocalArt.WaySlots)
				{
					string key = BiomeLocalArt.WaySlot(ground, way);
					Assert.That(BiomeLocalArt.TryParseWaySlot(key, out string g, out string w), Is.True, key);
					Assert.That(g, Is.EqualTo(ground), key);
					Assert.That(w, Is.EqualTo(way), key);
				}
			}
			Assert.That(BiomeLocalArt.WaySlot("detail/2", BiomeLocalArt.SlotCobbles), Is.EqualTo("detail/2/path/stone"), "the cobbles of detail/2, not a path of 'detail/2/path'");
		}

		/// <summary>
		/// No key a biome's texture slots already have reads as a ground layer's way — the biome's own ways (<c>road</c>,
		/// <c>path</c>, <c>path/stone</c>) least of all — and nor does a way on a slot that carries none (cliffs, beds, bars).
		/// </summary>
		[Test]
		public void TheSlotKeysTheBiomeAlreadyHas_AreNotWaySlots()
		{
			var keys = new List<string>
			{
				BiomeLocalArt.SlotMain, BiomeLocalArt.SlotRiverbed, BiomeLocalArt.SlotLakebed, BiomeLocalArt.SlotRoad, BiomeLocalArt.SlotPath,
				BiomeLocalArt.SlotCobbles, BiomeLocalArt.SlotBarSand, BiomeLocalArt.SlotBarGravel, null, string.Empty, "/road", "detail//road",
				"detail/x/road", "detail/-1/road", "detail/02/road", "main/", "main/gravel", "mainroad",
			};
			for (int i = 0; i < 6; i++)
			{
				keys.Add(BiomeLocalArt.DetailSlot(i));
				keys.Add(BiomeLocalArt.CliffSlot(i));
				foreach (string way in BiomeLocalArt.WaySlots)
				{
					keys.Add(BiomeLocalArt.WaySlot(BiomeLocalArt.CliffSlot(i), way));
				}
			}
			foreach (string way in BiomeLocalArt.WaySlots)
			{
				keys.Add(BiomeLocalArt.WaySlot(BiomeLocalArt.SlotRiverbed, way));
				keys.Add(BiomeLocalArt.WaySlot(BiomeLocalArt.SlotLakebed, way));
				keys.Add(BiomeLocalArt.WaySlot(BiomeLocalArt.SlotRoad, way));
			}
			foreach (string key in keys)
			{
				Assert.That(BiomeLocalArt.TryParseWaySlot(key, out _, out _), Is.False, $"'{key}'");
			}
			// The palette keys the biome's own ways exactly as the sidecar does, so a way key never collides with them.
			Assert.That(SceneTerrainPalette.SlotPathEarth, Is.EqualTo(BiomeLocalArt.SlotPath));
			Assert.That(SceneTerrainPalette.SlotPathGravel, Is.EqualTo(BiomeLocalArt.SlotRoad));
			Assert.That(SceneTerrainPalette.SlotPathStone, Is.EqualTo(BiomeLocalArt.SlotCobbles));
		}

		/// <summary>The template's ways read and write one way at a time, each empty unless set; nothing else is a way.</summary>
		[Test]
		public void GroundWays_StartEmpty_AndAreReadOneWayAtATime()
		{
			var layer = new TerrainTextureLayer();
			Assert.That(layer.ways.IsEmpty, Is.True, "a texture layer serialised before the ways existed has none: the biome's own");
			foreach (string way in BiomeLocalArt.WaySlots)
			{
				Assert.That(BiomeLocalArt.WayGround(layer.ways, way), Is.Null, way);
			}
			Assert.That(BiomeLocalArt.WayGround(layer.ways, BiomeLocalArt.SlotBarSand), Is.Null);
			Assert.Throws<ArgumentException>(() => BiomeLocalArt.SetWayGround(ref layer.ways, BiomeLocalArt.SlotBarSand, null));
			BiomeLocalArt.SetWayGround(ref layer.ways, BiomeLocalArt.SlotRoad, null);
			Assert.That(layer.ways.IsEmpty, Is.True);
			Assert.That(BiomeTemplate.Authored.GroundWay("detail/2/road"), Is.EqualTo("groundWays/detail/2/road"));
		}

		// ── Spec ──────────────────────────────────────────────────────

		/// <summary>
		/// A spec layer names no ways unless its biome's file says so (so authoring writes none and the cut is unchanged), the
		/// hook sets each way on its own and null keeps the one set, and every way a biome's file names is a generated ground.
		/// </summary>
		[Test]
		public void SpecLayerWays_AreUnsetByDefault_SetOneByOne_AndNameGeneratedGrounds()
		{
			var layer = new BiomeArtSpec.Layer { Family = Ground.Moss };
			foreach (string way in BiomeLocalArt.WaySlots)
			{
				Assert.That(layer.WayGround(way), Is.Null, way);
			}
			Assert.That(layer.Ways(road: Ground.Gravel), Is.SameAs(layer), "the hook chains, as D(...).Ways(...)");
			Assert.That(layer.WayGround(BiomeLocalArt.SlotRoad), Is.EqualTo(Ground.Gravel));
			Assert.That(layer.WayGround(BiomeLocalArt.SlotPath), Is.Null);
			layer.Ways(path: Ground.Mud);
			Assert.That(layer.WayRoad, Is.EqualTo(Ground.Gravel), "null keeps the road already set");
			Assert.That(layer.WayPath, Is.EqualTo(Ground.Mud));
			Assert.That(layer.WayGround(BiomeLocalArt.SlotBarGravel), Is.Null, "a river bar is no way");

			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				var layers = new List<(string slot, BiomeArtSpec.Layer layer)> { (BiomeLocalArt.SlotMain, entry.Main) };
				for (int i = 0; i < entry.Details.Length; i++)
				{
					layers.Add((BiomeLocalArt.DetailSlot(i), entry.Details[i]));
				}
				foreach ((string slot, BiomeArtSpec.Layer l) in layers)
				{
					foreach (string way in BiomeLocalArt.WaySlots)
					{
						string ground = l?.WayGround(way);
						if (ground != null)
						{
							Assert.That(SurfaceCatalogue.TryGround(ground, out _), Is.True, $"{entry.Biome} {slot}/{way}: '{ground}' is a generated ground");
						}
					}
				}
				if (entry.Bed != null)
				{
					foreach (string way in BiomeLocalArt.WaySlots)
					{
						Assert.That(entry.Bed.WayGround(way), Is.Null, $"{entry.Biome}: the sea floor carries no ways (nothing reads them)");
					}
				}
			}
		}

		// ── The palette's choice ──────────────────────────────────────

		private static SceneTerrainPalette.Entry WayEntry(string slot, int layer) => new SceneTerrainPalette.Entry { Role = PaletteRole.Path, Slot = slot, LayerIndex = layer };

		private static SceneTerrainPalette.Entry GroundEntry(PaletteRole role, string slot, int layer) => new SceneTerrainPalette.Entry { Role = role, Slot = slot, LayerIndex = layer };

		/// <summary>
		/// Across a ground carrying a way of its own the way's slice is that ground's; across every other ground of the biome,
		/// the biome's own; with neither, none. A way set on one detail layer never reaches another, nor the main layer.
		/// </summary>
		[Test]
		public void WayLayer_TakesTheGroundsOwnWay_ElseTheBiomes()
		{
			var taiga = new List<SceneTerrainPalette.Entry>
			{
				GroundEntry(PaletteRole.Main, "main", 0),
				WayEntry(SceneTerrainPalette.SlotPathEarth, 3),
				WayEntry(SceneTerrainPalette.SlotPathGravel, 4),
				WayEntry(SceneTerrainPalette.SlotPathStone, 5),
				GroundEntry(PaletteRole.Detail, "detail/0", 1),
				// The bog layer's corduroy, given before the biome's road in no particular order: the key decides, not the order.
				WayEntry(BiomeLocalArt.WaySlot("detail/1", SceneTerrainPalette.SlotPathGravel), 6),
				GroundEntry(PaletteRole.Detail, "detail/1", 2),
				WayEntry(BiomeLocalArt.WaySlot("main", SceneTerrainPalette.SlotPathEarth), 7),
			};
			Assert.That(SceneTerrainPalette.WayLayer(taiga, "detail/1", SceneTerrainPalette.SlotPathGravel), Is.EqualTo(6), "the bog's corduroy");
			Assert.That(SceneTerrainPalette.WayLayer(taiga, "detail/0", SceneTerrainPalette.SlotPathGravel), Is.EqualTo(4), "the other detail: the biome's road");
			Assert.That(SceneTerrainPalette.WayLayer(taiga, "main", SceneTerrainPalette.SlotPathGravel), Is.EqualTo(4), "the main layer: the biome's road");
			Assert.That(SceneTerrainPalette.WayLayer(taiga, "detail/1", SceneTerrainPalette.SlotPathEarth), Is.EqualTo(3), "the bog's path: the biome's");
			Assert.That(SceneTerrainPalette.WayLayer(taiga, "main", SceneTerrainPalette.SlotPathEarth), Is.EqualTo(7), "the main layer's own path");
			Assert.That(SceneTerrainPalette.WayLayer(taiga, "cliff/0", SceneTerrainPalette.SlotPathStone), Is.EqualTo(5), "a cliff: the biome's cobbles");
			Assert.That(SceneTerrainPalette.WayLayer(taiga, null, SceneTerrainPalette.SlotPathStone), Is.EqualTo(5));

			// Only the ground's own way, the biome lacking one: the rest of the biome has none (−1, the shader's fallback chain).
			var bare = new List<SceneTerrainPalette.Entry>
			{
				GroundEntry(PaletteRole.Main, "main", 0),
				GroundEntry(PaletteRole.Detail, "detail/0", 1),
				WayEntry(BiomeLocalArt.WaySlot("detail/0", SceneTerrainPalette.SlotPathGravel), 2),
				// A ground entry under a way's key is no way.
				GroundEntry(PaletteRole.Detail, BiomeLocalArt.WaySlot("main", SceneTerrainPalette.SlotPathGravel), 3),
			};
			Assert.That(SceneTerrainPalette.WayLayer(bare, "detail/0", SceneTerrainPalette.SlotPathGravel), Is.EqualTo(2));
			Assert.That(SceneTerrainPalette.WayLayer(bare, "main", SceneTerrainPalette.SlotPathGravel), Is.EqualTo(-1));
			Assert.That(SceneTerrainPalette.WayLayer(null, "main", SceneTerrainPalette.SlotPathGravel), Is.EqualTo(-1));

			// No ground carries a way of its own: exactly the biome's, as PathLayerMap chose before 2026-10-16.
			var plain = taiga.FindAll(e => !BiomeLocalArt.TryParseWaySlot(e.Slot, out _, out _));
			foreach (string ground in new[] { "main", "detail/0", "detail/1" })
			{
				Assert.That(SceneTerrainPalette.WayLayer(plain, ground, SceneTerrainPalette.SlotPathEarth), Is.EqualTo(3));
				Assert.That(SceneTerrainPalette.WayLayer(plain, ground, SceneTerrainPalette.SlotPathGravel), Is.EqualTo(4));
				Assert.That(SceneTerrainPalette.WayLayer(plain, ground, SceneTerrainPalette.SlotPathStone), Is.EqualTo(5));
			}
		}

		// ── Wiring ────────────────────────────────────────────────────

		/// <summary>
		/// The cut hands the palette each biome's ground-layer ways beside its own, the palette's per-layer path maps (which the
		/// layer table's path row carries to the shader) choose through <see cref="SceneTerrainPalette.WayLayer"/>, the biome
		/// art authoring writes the ways and moves LOCAL ones to the sidecar, and only the template reader reads them at the cut.
		/// </summary>
		[Test]
		public void CutPaletteAndAuthoring_AreWiredToTheLayerWays()
		{
			string generator = File.ReadAllText(Path.Combine(WorldFolder, "SceneGenerator.cs"));
			Assert.That(generator, Does.Contain("surfaces.AddRange(grounds.LayerWays(biome));"), "PathSurfaces adds the ground layers' own ways");
			foreach (string slot in new[] { "SlotPathEarth", "SlotPathGravel", "SlotPathStone" })
			{
				Assert.That(generator, Does.Contain($"palette.PathLayerMap(SceneTerrainPalette.{slot}, coverage)"), slot);
			}
			string palette = File.ReadAllText(Path.Combine(WorldFolder, "Terrain/SceneTerrainPalette.cs"));
			Assert.That(palette, Does.Contain("int way = WayLayer(byBiome[entry.BiomeIndex], entry.Variant != null ? BiomeLocalArt.GroundSlot(entry.Ground) : entry.Slot, slot);"),
				"PathLayerMap chooses per ground (a climate variant's override by the ground it stands in for, 2026-10-18)");
			string authoring = File.ReadAllText(Path.Combine(WorldFolder, "ProceduralArt/BiomeArtAuthoring.cs"));
			Assert.That(authoring, Does.Contain("AuthorLayerWays(biome, entry, source, report, apply);"));
			Assert.That(authoring, Does.Contain("foreach ((string key, TerrainTextureLayer ground, string way) in WaysOf(biome))"), "LOCAL ways move to the sidecar");
			string reader = File.ReadAllText(Path.Combine(WorldFolder, "Terrain/BiomeTemplateArt.cs"));
			Assert.That(reader, Does.Contain("public List<(string slot, TerrainTextureLayer source)> LayerWays(BiomeTemplate biome)"));

			// Nothing else at the cut reads a layer's ways or the spec's.
			foreach (string file in Directory.GetFiles(WorldFolder, "*.cs", SearchOption.AllDirectories))
			{
				string name = file.Replace('\\', '/');
				if (name.EndsWith("/BiomeTemplateArt.cs", StringComparison.Ordinal) || name.Contains("/ProceduralArt/"))
				{
					continue;
				}
				string text = File.ReadAllText(file);
				Assert.That(text, Does.Not.Contain(".ways."), $"{name} reads a layer's ways itself");
				Assert.That(text, Does.Not.Contain(".WayGround(").And.Not.Contain(".WayRoad").And.Not.Contain(".WayPath").And.Not.Contain(".WayCobbles"),
					$"{name} reads the spec's layer ways");
			}
		}
	}
}
