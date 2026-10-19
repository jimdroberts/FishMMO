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
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Lake");
			try
			{
				// A spec entry is shared, so its beds are set here and taken off after.
				var quillwort = new BiomeArtSpec.Scatter { Name = "Quillwort", Channel = PrefabSpawnChannel.DetailLayer, Prefabs = new[] { "Detail_Quillwort" }, Density = 4f, Depth = new Vector2(1f, 6f), Waters = BiomeBedWaters.Lake };
				var weed = new BiomeArtSpec.Scatter { Name = "FennelWeed", Channel = PrefabSpawnChannel.DetailLayer, Prefabs = new[] { "Detail_FennelWeed" }, Density = 3f, Current = new Vector2(0f, 1.2f) };
				entry.BedLayer(Ground.Silt, BiomeBedWaters.Lake, 3f, 9f, scatter: quillwort);
				entry.UnderRiver(weed);
				BiomeTemplate biome = MakeBiome("Lake");
				var source = new FakeSource(created);

				var first = new BiomeArtAuthoring.Report();
				Assert.IsTrue(BiomeArtAuthoring.Author(biome, entry, source, first, true));
				Assert.That(biome.BedTextureLayers.Count, Is.EqualTo(1), "one bed layer:\n" + first);
				BedTextureLayer halde = biome.BedTextureLayers[0];
				Assert.AreSame(source.Layer(Ground.Silt), halde.terrainLayer);
				Assert.That(halde.waters, Is.EqualTo(BiomeBedWaters.Lake));
				Assert.That(halde.depthRange, Is.EqualTo(new Vector2(3f, 9f)));
				Assert.That(halde.useCurrentBand, Is.False);
				Assert.That(halde.prefabSpawnRules.Exists(r => r.ruleName == "Quillwort" && r.waters == BiomeBedWaters.Lake && r.depthRange == new Vector2(1f, 6f)), Is.True);
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
				entry.BedLayers.Clear();
				entry.RiverBed = null;
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
