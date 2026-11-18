using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A ground layer's own ways (decision 19, 2026-10-16): the authoring writes the ways the spec gives a main or detail
	/// layer, marked way by way, and keeps a hand edit; the palette draws a way across that ground with the layer's own
	/// ground and across the rest of the biome with the biome's. Unity-only: templates and terrain layers are native objects.
	/// </summary>
	public partial class BiomeProceduralArtTests
	{
		[Test]
		public void Authoring_WritesALayersOwnWays_OnceAndKeepsHandEdits()
		{
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Taiga");
			BiomeArtSpec.Layer main = entry.Main, moss = entry.Details[0];
			(string, string, string) mainWas = (main.WayPath, main.WayRoad, main.WayCobbles);
			(string, string, string) mossWas = (moss.WayPath, moss.WayRoad, moss.WayCobbles);
			try
			{
				// A spec entry is shared, so its ways are set here and put back after.
				main.WayPath = Ground.Mud;
				main.WayRoad = main.WayCobbles = null;
				moss.WayRoad = Ground.Gravel;
				moss.WayPath = moss.WayCobbles = null;
				BiomeTemplate biome = MakeBiome("Taiga");
				var source = new FakeSource(created);

				var first = new BiomeArtAuthoring.Report();
				Assert.IsTrue(BiomeArtAuthoring.Author(biome, entry, source, first, true));
				TerrainTextureLayer mossSlot = biome.DetailTextureLayers[0];
				Assert.AreSame(source.Layer(Ground.Gravel), mossSlot.ways.road, "the moss layer's own road:\n" + first);
				Assert.That(mossSlot.ways.path, Is.Null, "the moss layer's path is the biome's");
				Assert.AreSame(source.Layer(Ground.Mud), biome.MainTextureLayer.ways.path);
				Assert.That(biome.MainTextureLayer.ways.road, Is.Null);
				Assert.That(biome.DetailTextureLayers[1].ways.IsEmpty, Is.True, "a layer the spec gives no ways has none");
				Assert.That(biome.AuthoredFingerprint(BiomeTemplate.Authored.GroundWay("detail/0/road")), Is.EqualTo("Ground_" + Ground.Gravel));
				Assert.That(biome.AuthoredFingerprint(BiomeTemplate.Authored.GroundWay("detail/0/path")), Is.Null, "an unset way records no mark");
				Assert.That(biome.AuthoredFingerprint(BiomeTemplate.Authored.GroundWay("detail/1/road")), Is.Null);

				var second = new BiomeArtAuthoring.Report();
				Assert.IsFalse(BiomeArtAuthoring.Author(biome, entry, source, second, true), "a second run changed something:\n" + second);
				Assert.That(second.ValuesFilled + second.ValuesKept, Is.EqualTo(0));

				// Hand edits: other art on the moss road, the main path cleared. The spec then adds cobbles to the moss.
				var mine = new TerrainLayer { name = "MyBogRoad" };
				created.Add(mine);
				mossSlot.ways.road = mine;
				biome.MainTextureLayer.ways.path = null;
				moss.WayCobbles = Ground.Flagstone;
				var third = new BiomeArtAuthoring.Report();
				BiomeArtAuthoring.Author(biome, entry, source, third, true);
				Assert.AreSame(mine, mossSlot.ways.road, "a hand-picked way was replaced:\n" + third);
				Assert.That(biome.MainTextureLayer.ways.path, Is.Null, "a way cleared by hand was refilled");
				Assert.AreSame(source.Layer(Ground.Flagstone), mossSlot.ways.cobbles, "the spec's new way was not written");
				Assert.That(third.ValuesKept, Is.EqualTo(2));

				// The spec drops the cobbles again: the tool's untouched way goes back to the biome's own.
				moss.WayCobbles = null;
				var fourth = new BiomeArtAuthoring.Report();
				BiomeArtAuthoring.Author(biome, entry, source, fourth, true);
				Assert.That(mossSlot.ways.cobbles, Is.Null, "the untouched way did not follow the spec:\n" + fourth);

				// A detail slot holding hand-made art gets none of the spec's ways.
				BiomeTemplate handMade = MakeBiome("Taiga");
				var other = new TerrainLayer { name = "MyMoss" };
				created.Add(other);
				handMade.DetailTextureLayers.Add(new TerrainTextureLayer { terrainLayer = other });
				BiomeArtAuthoring.Author(handMade, entry, source, new BiomeArtAuthoring.Report(), true);
				Assert.AreSame(other, handMade.DetailTextureLayers[0].terrainLayer);
				Assert.That(handMade.DetailTextureLayers[0].ways.IsEmpty, Is.True, "the spec's moss road reached hand-made art");
			}
			finally
			{
				(main.WayPath, main.WayRoad, main.WayCobbles) = mainWas;
				(moss.WayPath, moss.WayRoad, moss.WayCobbles) = mossWas;
			}
		}

		/// <summary>
		/// The palette lays a ground layer's own way beside the biome's and its path map points that ground at it; the biome's
		/// other grounds keep the biome's way, and the scene-wide fallback slice stays the biome's.
		/// </summary>
		[Test]
		public void Palette_DrawsAGroundsOwnWayAcrossIt_AndTheBiomesElsewhere()
		{
			BiomeTemplate taiga = MakeBiome("Taiga");
			TerrainLayer litter = new TerrainLayer { name = "Ground_NeedleLitter", diffuseTexture = MakeTexture("litter") };
			TerrainLayer bog = new TerrainLayer { name = "Ground_Moss", diffuseTexture = MakeTexture("moss") };
			TerrainLayer gravel = new TerrainLayer { name = "Ground_Gravel", diffuseTexture = MakeTexture("gravel") };
			TerrainLayer logs = new TerrainLayer { name = "Ground_Corduroy", diffuseTexture = MakeTexture("logs") };
			created.AddRange(new Object[] { litter, bog, gravel, logs });
			taiga.MainTextureLayer.terrainLayer = litter;
			taiga.DetailTextureLayers.Add(new TerrainTextureLayer { terrainLayer = bog });
			taiga.DetailTextureLayers[0].ways.road = logs;
			taiga.RoadTextureLayer.terrainLayer = gravel;

			var grounds = new BiomeTemplateArt.Grounds();
			List<(string slot, TerrainTextureLayer source)> ways = grounds.LayerWays(taiga);
			Assert.That(ways.Count, Is.EqualTo(1));
			Assert.That(ways[0].slot, Is.EqualTo("detail/0/road"));
			Assert.AreSame(logs, ways[0].source.terrainLayer);

			SceneBiomeField field = SceneBiomeField.FromCells(4, 4, 16f, Vector2.zero, new[] { taiga }, new byte[16], 16f);
			SceneTerrainPalette palette = SceneTerrainPalette.Build(field, (b, slot, layer) => layer?.terrainLayer, null, null, b =>
			{
				var surfaces = new List<(string, TerrainTextureLayer)> { (SceneTerrainPalette.SlotPathGravel, b.RoadTextureLayer) };
				surfaces.AddRange(grounds.LayerWays(b));
				return surfaces;
			});
			int Index(TerrainLayer layer) => new List<TerrainLayer>(palette.Layers).IndexOf(layer);
			float[] coverage = field.Coverage();
			float[] road = palette.PathLayerMap(SceneTerrainPalette.SlotPathGravel, coverage);
			Assert.That(road[Index(bog)], Is.EqualTo(Index(logs)), "the bog's road is its corduroy");
			Assert.That(road[Index(litter)], Is.EqualTo(Index(gravel)), "the rest of the taiga's is its gravel");
			Assert.That(road[Index(gravel)], Is.EqualTo(-1f), "a way ground is no ground a way crosses");
			Assert.That(palette.PathLayers(coverage).y, Is.EqualTo(Index(gravel)), "the scene-wide fallback is the biome's road");
			float[] earth = palette.PathLayerMap(SceneTerrainPalette.SlotPathEarth, coverage);
			Assert.That(earth[Index(bog)], Is.EqualTo(-1f), "no path anywhere: the bog's own road gives it none");

			// Without the ground's way: exactly the biome's, as before.
			taiga.DetailTextureLayers[0].ways = default;
			SceneTerrainPalette plain = SceneTerrainPalette.Build(field, (b, slot, layer) => layer?.terrainLayer, null, null, b =>
			{
				var surfaces = new List<(string, TerrainTextureLayer)> { (SceneTerrainPalette.SlotPathGravel, b.RoadTextureLayer) };
				surfaces.AddRange(new BiomeTemplateArt.Grounds().LayerWays(b));
				return surfaces;
			});
			float[] before = plain.PathLayerMap(SceneTerrainPalette.SlotPathGravel, coverage);
			int gravelSlice = new List<TerrainLayer>(plain.Layers).IndexOf(gravel);
			Assert.That(before[new List<TerrainLayer>(plain.Layers).IndexOf(bog)], Is.EqualTo(gravelSlice));
			Assert.That(plain.Layers.Count, Is.EqualTo(3), "no slice for a way nobody set");
		}
	}
}
