using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The summer decides what grows (2026-10-10): a biome's warmest-season range is part of its envelope
	/// where the body's seasons are known, the spec table draws Köppen's lines (trees from a 10 °C summer,
	/// tundra and bog from a thaw, the Ice Sheet where nothing thaws), and the Ice Sheet is tabled, named,
	/// dressed and worn like the biomes it joins.
	/// </summary>
	[TestFixture]
	public class WarmestSeasonBiomeTests
	{
		private readonly List<ScriptableObject> created = new List<ScriptableObject>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (ScriptableObject asset in created)
			{
				Object.DestroyImmediate(asset);
			}
			created.Clear();
		}

		private BiomeTemplate MakeBiome(float minWarmest, float maxWarmest)
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.MinWarmestSeason = minWarmest;
			biome.MaxWarmestSeason = maxWarmest;
			created.Add(biome);
			return biome;
		}

		private static ClimateSample Sample(float temperature, float warmest, bool known)
		{
			return new ClimateSample { Temperature = temperature, Humidity = 0.6f, ElevationTier = 4, WarmestSeason = warmest, SeasonKnown = known };
		}

		[Test]
		public void AForestNeedsATenDegreeSummer()
		{
			BiomeTemplate forest = MakeBiome(BiomeSpecTable.TenDegrees, 1f);
			// The same −15 °C year: an ice cap's −6 °C summer and a Siberian taiga's +19 °C one.
			Assert.IsFalse(forest.ContainsClimate(Sample(-0.45f, -6f / 33.1f, true)), "no forest under a summer that never thaws");
			Assert.IsTrue(forest.ContainsClimate(Sample(-0.45f, 19f / 33.1f, true)), "a warm summer carries a forest whatever the year's mean");
		}

		[Test]
		public void WithoutSeasonsTheSummerAsksNothing()
		{
			BiomeTemplate forest = MakeBiome(BiomeSpecTable.TenDegrees, 1f);
			ClimateSample unknown = Sample(-0.45f, -1f, false);
			Assert.IsTrue(forest.ContainsClimate(unknown), "a scene's own climate asset has no seasons; the envelope reads as it always did");
			Assert.AreEqual(0f, forest.WarmestSeasonDistance(unknown));
		}

		[Test]
		public void ASummerMissCountsAsDistance()
		{
			BiomeTemplate tundra = MakeBiome(0f, BiomeSpecTable.TenDegrees);
			float near = tundra.ClimateDistance(Sample(-0.5f, -0.05f, true));
			float far = tundra.ClimateDistance(Sample(-0.5f, -0.5f, true));
			Assert.Greater(far, near, "the further the summer from the range, the further the reading from the envelope");
			Assert.AreEqual(0f, tundra.ClimateDistance(Sample(-0.5f, 0.1f, true)), 1e-6f);
		}

		[Test]
		public void EverySeasonRowNamesATabledBiome()
		{
			foreach (BiomeSpecTable.Season season in BiomeSpecTable.Seasons)
			{
				Assert.IsTrue(Array.Exists(BiomeSpecTable.Entries, e => e.Name == season.Name), $"{season.Name}: a warmest-season row with no table entry is never written");
				Assert.LessOrEqual(season.MinWarmest, season.MaxWarmest, season.Name);
			}
		}

		[Test]
		public void KoppensLinesAreDrawnWhereTheyBelong()
		{
			foreach (string trees in new[] { "Taiga", "Forest", "Woodland", "Jungle", "Mangrove" })
			{
				BiomeSpecTable.SeasonFor(trees, out float min, out _);
				Assert.AreEqual(BiomeSpecTable.TenDegrees, min, 1e-6f, $"{trees} grows trees: a 10 °C summer");
			}
			foreach (string low in new[] { "Tundra", "Peat Bog", "Alpine Meadow" })
			{
				BiomeSpecTable.SeasonFor(low, out float min, out _);
				Assert.AreEqual(0f, min, 1e-6f, $"{low} needs a thaw");
			}
			BiomeSpecTable.SeasonFor("Tundra", out _, out float tundraMax);
			Assert.AreEqual(BiomeSpecTable.TenDegrees, tundraMax, 1e-6f, "above a 10 °C summer the forest takes the tundra's ground");
			BiomeSpecTable.SeasonFor("Ice Sheet", out _, out float sheetMax);
			Assert.AreEqual(0f, sheetMax, 1e-6f, "the ice sheet lies where no month thaws");
			BiomeSpecTable.SeasonFor("Desert", out float desertMin, out float desertMax);
			Assert.AreEqual(-1f, desertMin);
			Assert.AreEqual(1f, desertMax, "a biome with no row asks nothing of the summer");
		}

		[Test]
		public void TheIceSheetIsTabledCreatedDressedAndWorn()
		{
			BiomeSpecTable.Entry sheet = Array.Find(BiomeSpecTable.Entries, e => e.Name == IceSheetSurface.BiomeName);
			Assert.AreEqual(IceSheetSurface.BiomeName, sheet.Name, "the Ice Sheet is in the table");
			Assert.AreEqual(BiomeResolver.AnyElevationTier, sheet.ElevationTier, "from the shore to the mountains");
			Assert.IsTrue(BiomeSpecTable.TryGetIdentity(IceSheetSurface.BiomeName, out _), "the table can create its asset");
			BiomeArtSpec.Entry art = BiomeArtSpec.For(IceSheetSurface.BiomeName);
			Assert.IsNotNull(art, "it has ground art");
			Assert.IsTrue(TerrainProcessTable.TryGetProfile("Periglacial", out _));
		}

		[Test]
		public void IcebergsChokeTheWaterInFrontOfACalvingFace()
		{
			Assert.AreEqual(IceOccurrence.BergsPerKm2(IcebergSize.Growler), IceOccurrence.BergsPerKm2(IcebergSize.Growler, false));
			Assert.Greater(IceOccurrence.BergsPerKm2(IcebergSize.Growler, true), IceOccurrence.BergsPerKm2(IcebergSize.Growler, false) * 4f,
				"a face sheds fragments by the hundred");
			Assert.AreEqual(IceOccurrence.BergsPerKm2(IcebergSize.Large, true), IceOccurrence.BergsPerKm2(IcebergSize.Large, false),
				"the big bergs are no commoner at the front than anywhere they drift to");
		}

		[Test]
		public void AFieldWithoutABodyKnowsNoSeasons()
		{
			PlanetClimateField field = PlanetClimateField.For(null, null);
			ClimateSample climate = field.ClimateAt(Vector3.up, 0f, 0f, out _);
			Assert.IsFalse(climate.SeasonKnown);
			Assert.AreEqual(0f, field.WarmestLatitudeTemperature(-72.0));
		}
	}
}
