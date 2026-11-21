using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Decision 35 (2026-10-18, phase I2l): a biome's extra bed layers by depth and its riverbed rules, written by the authoring from
	/// the spec, and the palette carrying the riverbed beside the lakebed. Unity-only: templates and terrain layers are native objects
	/// (the pure parts are WorldDesign.BiomeLakeRiverPlacesTests).
	/// </summary>
	public partial class BiomeProceduralArtTests
	{
		[Test]
		public void Authoring_WritesBedLayersAndRiverbedRules_OnceAndKeepsHandEdits()
		{
			/* The Lake's own beds since its round (G10): the pondweed halde at 3-9 m and the varved gyttja below. A spec entry is
			 * shared by every fixture, so the riverbed rule added here and the band moved below are put back after: clearing the
			 * entry's beds instead, as this test once did, took the Lake's real beds away from every test that ran after it. */
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Lake");
			Assume.That(entry.BedLayers.Count, Is.GreaterThan(0), "the Lake names its beds");
			BiomeArtSpec.Layer riverBed = entry.RiverBed;
			Vector2 firstDepth = entry.BedLayers[0].Depth;
			try
			{
				var weed = new BiomeArtSpec.Scatter { Name = "FennelWeed", Channel = PrefabSpawnChannel.DetailLayer, Prefabs = new[] { "Detail_FennelWeed" }, Density = 3f, Current = new Vector2(0f, 1.2f) };
				entry.UnderRiver(weed);
				BiomeTemplate biome = MakeBiome("Lake");
				var source = new FakeSource(created);

				var first = new BiomeArtAuthoring.Report();
				Assert.IsTrue(BiomeArtAuthoring.Author(biome, entry, source, first, true));
				Assert.That(biome.BedTextureLayers.Count, Is.EqualTo(entry.BedLayers.Count), "a bed layer for each the spec names:\n" + first);
				BiomeArtSpec.Entry.BedSpec spec = entry.BedLayers[0];
				BedTextureLayer halde = biome.BedTextureLayers[0];
				Assert.AreSame(source.Layer(spec.Layer.Family), halde.terrainLayer);
				Assert.That(halde.waters, Is.EqualTo(spec.Waters));
				Assert.That(halde.depthRange, Is.EqualTo(spec.Depth));
				Assert.That(halde.useCurrentBand, Is.EqualTo(spec.Current.HasValue));
				foreach (BiomeArtSpec.Scatter scatter in spec.Layer.Scatter)
				{
					Assert.That(halde.prefabSpawnRules.Exists(r => r.ruleName == scatter.Name), Is.True, "the bed's rule " + scatter.Name);
				}
				Assert.That(biome.RiverbedTextureLayer.prefabSpawnRules.Exists(r => r.ruleName == "FennelWeed" && r.currentRange == new Vector2(0f, 1.2f)), Is.True, "the riverbed's rule");
				Assert.That(BiomeLocalArt.SlotsOf(biome), Does.Contain("bed/0"));
				Assert.AreSame(halde, BiomeLocalArt.CommittedLayer(biome, "bed/0"));

				var second = new BiomeArtAuthoring.Report();
				Assert.IsFalse(BiomeArtAuthoring.Author(biome, entry, source, second, true), "a second run changed something:\n" + second);

				// A hand-moved band is kept when the spec moves its own.
				halde.depthRange = new Vector2(2.5f, 9f);
				entry.BedLayers[0].Depth = new Vector2(4f, 10f);
				var third = new BiomeArtAuthoring.Report();
				BiomeArtAuthoring.Author(biome, entry, source, third, true);
				Assert.That(halde.depthRange, Is.EqualTo(new Vector2(2.5f, 9f)), "a hand-tuned band was replaced:\n" + third);
			}
			finally
			{
				entry.BedLayers[0].Depth = firstDepth;
				entry.RiverBed = riverBed;
			}
		}

		[Test]
		public void Palette_CarriesTheRiverbedBesideTheLakebed_AndTheBedLayers()
		{
			BiomeTemplate biome = MakeBiome("Riverside");
			biome.MainTextureLayer.terrainLayer = new TerrainLayer { name = "Grass", diffuseTexture = MakeTexture("Grass") };
			biome.LakebedTextureLayer.terrainLayer = new TerrainLayer { name = "Mud", diffuseTexture = MakeTexture("Mud") };
			biome.RiverbedTextureLayer.terrainLayer = new TerrainLayer { name = "Gravel", diffuseTexture = MakeTexture("Gravel") };
			biome.BedTextureLayers.Add(new BedTextureLayer { terrainLayer = new TerrainLayer { name = "Ooze", diffuseTexture = MakeTexture("Ooze") }, depthRange = new Vector2(9f, 0f) });
			foreach (Object o in new Object[] { biome.MainTextureLayer.terrainLayer, biome.LakebedTextureLayer.terrainLayer, biome.RiverbedTextureLayer.terrainLayer, biome.BedTextureLayers[0].terrainLayer })
			{
				created.Add(o);
			}
			SceneBiomeField field = SceneBiomeField.FromCells(4, 4, 32f, Vector2.zero, new[] { biome }, new byte[16], 16f);
			SceneTerrainPalette palette = SceneTerrainPalette.Build(field, layer => layer.terrainLayer, null);
			var slots = new List<string>();
			foreach (SceneTerrainPalette.Entry e in palette.Entries)
			{
				if (e.Role == PaletteRole.Submerged)
				{
					slots.Add(e.Slot);
				}
			}
			Assert.That(slots, Is.EqualTo(new[] { "lakebed", "riverbed", "bed/0" }), "the riverbed is painted though the lakebed exists (decision 35 (h))");
			Assert.That(palette.Layers.Count, Is.EqualTo(4));
		}
	}
}
