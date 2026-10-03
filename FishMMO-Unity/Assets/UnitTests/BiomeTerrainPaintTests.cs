using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The biome painting stages of the scene generator, without a scene: the field's soft edges
	/// sum to one and its dominant biome is the one painted most, the palette shares art and keeps
	/// a biome's base ground before anybody's variation, and the splat weighing gives steep ground
	/// to cliffs, drowned ground to the submerged layer and honours a detail's bands.
	/// </summary>
	[TestFixture]
	public class BiomeTerrainPaintTests
	{
		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (Object asset in created)
			{
				if (asset != null)
				{
					Object.DestroyImmediate(asset);
				}
			}
			created.Clear();
		}

		private BiomeTemplate Biome(string name)
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.name = name;
			created.Add(biome);
			return biome;
		}

		private TerrainLayer Layer(string name)
		{
			var texture = new Texture2D(4, 4);
			texture.name = name;
			created.Add(texture);
			var layer = new TerrainLayer { diffuseTexture = texture, name = name };
			created.Add(layer);
			return layer;
		}

		private static TerrainLayer Resolve(TerrainTextureLayer source) => source != null ? source.terrainLayer : null;

		/// <summary>An 8×8 field of 16 m cells: the west half one biome, the east half the other.</summary>
		private static SceneBiomeField HalfAndHalf(BiomeTemplate west, BiomeTemplate east, float blendMetres = 32f)
		{
			var cells = new byte[64];
			for (int z = 0; z < 8; z++)
			{
				for (int x = 0; x < 8; x++)
				{
					cells[z * 8 + x] = (byte)(x < 4 ? 0 : 1);
				}
			}
			return SceneBiomeField.FromCells(8, 8, 16f, Vector2.zero, new[] { west, east }, cells, blendMetres);
		}

		[Test]
		public void FieldWeightsSumToOneAndTheDominantBiomeIsTheCellsOwn()
		{
			SceneBiomeField field = HalfAndHalf(Biome("West"), Biome("East"));
			var weights = new float[2];
			for (float x = 1f; x < 128f; x += 7f)
			{
				for (float z = 1f; z < 128f; z += 7f)
				{
					field.WeightsAt(x, z, weights);
					Assert.That(weights[0] + weights[1], Is.EqualTo(1f).Within(1e-4f), $"weights at ({x}, {z})");
				}
			}
			Assert.That(field.DominantIndexAt(4f, 64f, weights), Is.EqualTo(0), "deep in the west half");
			Assert.That(field.DominantIndexAt(124f, 64f, weights), Is.EqualTo(1), "deep in the east half");

			// The boundary meanders (the warp), so look along it rather than at one point.
			bool mixed = false;
			for (float z = 1f; z < 128f; z += 3f)
			{
				for (float x = 40f; x <= 88f; x += 4f)
				{
					field.WeightsAt(x, z, weights);
					mixed |= weights[0] > 0.2f && weights[0] < 0.8f;
				}
			}
			Assert.That(mixed, Is.True, "the boundary is soft, not a step");
		}

		[Test]
		public void TheBakedMapStoresEachCellsIdentityNotTheBlur()
		{
			BiomeTemplate west = Biome("West"), east = Biome("East");
			SceneBiomeField field = HalfAndHalf(west, east);
			var map = ScriptableObject.CreateInstance<SceneBiomeMap>();
			created.Add(map);
			field.WriteTo(map);

			Assert.That(map.Width, Is.EqualTo(8));
			Assert.That(map.Height, Is.EqualTo(8));
			Assert.That(map.WorldSize, Is.EqualTo(new Vector2(128f, 128f)));
			Assert.That(map.IDAt(new Vector3(60f, 0f, 10f)), Is.EqualTo(BiomeRegistry.IDOf(west)), "the cell just west of the boundary");
			Assert.That(map.IDAt(new Vector3(68f, 0f, 10f)), Is.EqualTo(BiomeRegistry.IDOf(east)), "the cell just east of it");
		}

		[Test]
		public void TwoBiomesOnTheSameArtShareOneChannel()
		{
			TerrainLayer grass = Layer("Grass");
			BiomeTemplate meadow = Biome("Meadow"), steppe = Biome("Steppe");
			meadow.MainTextureLayer.terrainLayer = grass;
			steppe.MainTextureLayer.terrainLayer = grass;

			SceneTerrainPalette palette = SceneTerrainPalette.Build(HalfAndHalf(meadow, steppe), Resolve, null);

			Assert.That(palette.Layers.Count, Is.EqualTo(1));
			Assert.That(palette.Entries.Count, Is.EqualTo(2), "each biome keeps its own entry, for its own spawn rules");
			Assert.That(palette.Entries[0].LayerIndex, Is.EqualTo(0));
			Assert.That(palette.Entries[1].LayerIndex, Is.EqualTo(0));
		}

		[Test]
		public void PastTheCapEveryBiomesBaseGroundOutranksAnyDetail()
		{
			BiomeTemplate rich = Biome("Rich"), plain = Biome("Plain");
			rich.MainTextureLayer.terrainLayer = Layer("Rich Main");
			for (int i = 0; i < SceneTerrainPalette.MaximumLayers + 4; i++)
			{
				rich.DetailTextureLayers.Add(new TerrainTextureLayer { terrainLayer = Layer($"Rich Detail {i}") });
			}
			plain.MainTextureLayer.terrainLayer = Layer("Plain Main");

			SceneTerrainPalette palette = SceneTerrainPalette.Build(HalfAndHalf(rich, plain), Resolve, null);

			Assert.That(palette.Layers.Count, Is.EqualTo(SceneTerrainPalette.MaximumLayers));
			Assert.That(palette.Dropped.Count, Is.EqualTo(2 + SceneTerrainPalette.MaximumLayers + 4 - SceneTerrainPalette.MaximumLayers), "every candidate past the cap is reported");
			Assert.That(palette.EntriesFor(1).Count, Is.EqualTo(1), "the plain biome's main layer survives the rich biome's details");
			Assert.That(palette.EntriesFor(1)[0].Role, Is.EqualTo(PaletteRole.Main));
		}

		[Test]
		public void SlotKeysNameTheLayerTheyCameFrom()
		{
			BiomeTemplate biome = Biome("Slots");
			biome.MainTextureLayer.terrainLayer = Layer("Main");
			var detail = new TerrainTextureLayer { terrainLayer = Layer("Detail") };
			biome.DetailTextureLayers.Add(new TerrainTextureLayer());
			biome.DetailTextureLayers.Add(detail);

			SceneTerrainPalette palette = SceneTerrainPalette.Build(HalfAndHalf(biome, biome), Resolve, null);
			SceneTerrainPalette.Entry entry = null;
			foreach (SceneTerrainPalette.Entry e in palette.Entries)
			{
				if (e.Source == detail)
				{
					entry = e;
				}
			}

			Assert.That(entry, Is.Not.Null);
			Assert.That(entry.Slot, Is.EqualTo("detail/1"), "the index is the authored slot, not the position among kept layers");
			Assert.That(SceneTerrainPalette.SlotLayer(biome, entry.Slot), Is.SameAs(detail));
			Assert.That(SceneTerrainPalette.SlotLayer(biome, SceneTerrainPalette.SlotMain), Is.SameAs(biome.MainTextureLayer));
		}

		private SceneTerrainPalette.Entry Find(SceneTerrainPalette palette, PaletteRole role)
		{
			foreach (SceneTerrainPalette.Entry entry in palette.Entries)
			{
				if (entry.Role == role)
				{
					return entry;
				}
			}
			return null;
		}

		[Test]
		public void SteepGroundGoesToTheCliffAndFlatGroundToTheBase()
		{
			BiomeTemplate biome = Biome("Crag");
			biome.MainTextureLayer.terrainLayer = Layer("Turf");
			biome.CliffTextureLayers.Add(new CliffTextureLayer { terrainLayer = Layer("Rock"), minCliffAngle = 35f, maxCliffAngle = 60f });
			SceneBiomeField field = HalfAndHalf(biome, biome);
			SceneTerrainPalette palette = SceneTerrainPalette.Build(field, Resolve, null);
			var weigher = new BiomeSplatPainter.Weigher(palette, field, new BiomeSplatOptions { NormalizedHeight = (x, y, z) => 0.5f });
			var weights = new float[palette.Layers.Count];
			int main = Find(palette, PaletteRole.Main).LayerIndex, cliff = Find(palette, PaletteRole.Cliff).LayerIndex;

			Assert.That(weigher.Weigh(64f, 50f, 64f, 5f, weights), Is.True);
			Assert.That(weights[main], Is.EqualTo(1f).Within(1e-4f), "flat ground is all base");

			weigher.Weigh(64f, 50f, 64f, 60f, weights);
			Assert.That(weights[cliff], Is.EqualTo(1f).Within(1e-4f), "a face at the cliff's full angle is all cliff");
			Assert.That(weights[main] + weights[cliff], Is.EqualTo(1f).Within(1e-4f));
		}

		[Test]
		public void GroundUnderTheWaterLineTakesTheSubmergedLayer()
		{
			BiomeTemplate biome = Biome("Shore");
			biome.MainTextureLayer.terrainLayer = Layer("Sand");
			biome.LakebedTextureLayer.terrainLayer = Layer("Silt");
			SceneBiomeField field = HalfAndHalf(biome, biome);
			SceneTerrainPalette palette = SceneTerrainPalette.Build(field, Resolve, null);
			var weights = new float[palette.Layers.Count];
			int under = Find(palette, PaletteRole.Submerged).LayerIndex;

			var wet = new BiomeSplatPainter.Weigher(palette, field, new BiomeSplatOptions { HasLiquidWater = true });
			wet.Weigh(64f, -5f, 64f, 2f, weights);
			Assert.That(weights[under], Is.EqualTo(1f).Within(1e-4f), "five metres down is sea floor");
			wet.Weigh(64f, 5f, 64f, 2f, weights);
			Assert.That(weights[under], Is.EqualTo(0f).Within(1e-4f), "five metres up is dry");

			var dry = new BiomeSplatPainter.Weigher(palette, field, new BiomeSplatOptions { HasLiquidWater = false });
			dry.Weigh(64f, -5f, 64f, 2f, weights);
			Assert.That(weights[under], Is.EqualTo(0f).Within(1e-4f), "a world without water has no sea floor below zero");
		}

		[Test]
		public void ADetailStaysInsideItsHeightBand()
		{
			BiomeTemplate biome = Biome("Heath");
			biome.MainTextureLayer.terrainLayer = Layer("Heather");
			biome.DetailTextureLayers.Add(new TerrainTextureLayer
			{
				terrainLayer = Layer("Scree"),
				useHeightConstraint = true,
				heightRange = new MinMaxRange(0.8f, 1f),
				heightFalloff = 0.01f,
				blendSharpness = 0.1f,
			});
			SceneBiomeField field = HalfAndHalf(biome, biome);
			SceneTerrainPalette palette = SceneTerrainPalette.Build(field, Resolve, null);
			var weights = new float[palette.Layers.Count];
			int detail = Find(palette, PaletteRole.Detail).LayerIndex;

			var low = new BiomeSplatPainter.Weigher(palette, field, new BiomeSplatOptions { NormalizedHeight = (x, y, z) => 0.2f });
			float lowTotal = 0f, highTotal = 0f;
			var high = new BiomeSplatPainter.Weigher(palette, field, new BiomeSplatOptions { NormalizedHeight = (x, y, z) => 0.9f });
			for (float x = 3f; x < 128f; x += 11f)
			{
				low.Weigh(x, 10f, 40f, 2f, weights);
				lowTotal += weights[detail];
				high.Weigh(x, 10f, 40f, 2f, weights);
				highTotal += weights[detail];
			}
			Assert.That(lowTotal, Is.EqualTo(0f).Within(1e-4f), "below its band the detail never shows");
			Assert.That(highTotal, Is.GreaterThan(0.5f), "inside its band it does");
		}

		[Test]
		public void BandIsOneInsideAndEasesOutOverItsFalloff()
		{
			Assert.That(BiomeSplatPainter.Band(0.5f, 0.2f, 0.8f, 0.1f), Is.EqualTo(1f));
			Assert.That(BiomeSplatPainter.Band(0.85f, 0.2f, 0.8f, 0.1f), Is.EqualTo(0.5f).Within(1e-4f));
			Assert.That(BiomeSplatPainter.Band(0.95f, 0.2f, 0.8f, 0.1f), Is.EqualTo(0f));
			Assert.That(BiomeSplatPainter.Band(0.1f, 0.2f, 0.8f, 0f), Is.EqualTo(0f), "no falloff is a hard edge");
		}
	}
}
