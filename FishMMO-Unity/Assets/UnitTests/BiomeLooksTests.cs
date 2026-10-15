using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Every biome on every world has its own looks for what grows and runs in it (2026-10-15: "at least 10 variations for
	/// every biome/planet type to fit atmosphere and fantasy vibe"): BiomeLooks.
	/// </summary>
	public class BiomeLooksTests
	{
		private static BiomeLooks.WorldTraits Earth => BiomeLooks.WorldTraits.Earthlike;

		private static BiomeLooks.WorldTraits World(float starKelvin, AtmosphereKind air = AtmosphereKind.Standard,
			Condensate condensate = Condensate.Water, float temperature = 0.45f, float water = 0.7f)
			=> new BiomeLooks.WorldTraits
			{
				Atmosphere = air, MeanTemperature = temperature, Water = water, Condensate = condensate, StarKelvin = starKelvin, Seed = 5u,
			};

		private static float Distance(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

		[Test]
		public void EveryClassHasAtLeastTenDistinctLooksOfEveryKind()
		{
			// The home world, a frozen K-star world (Galris), a red dwarf's, a thin-aired one, and a methane world.
			var worlds = new[]
			{
				("home", Earth), ("Galris", World(4975f, temperature: -2f, water: 0.13f)), ("red dwarf", World(3100f)),
				("thin air", World(5800f, AtmosphereKind.Thin, water: 0.02f)), ("methane", World(5800f, condensate: Condensate.Methane, temperature: -5f)),
			};
			foreach (var (name, world) in worlds)
			{
				foreach (uint hash in new[] { 17u, 12345u, BiomeLooks.Hash("tundra") })
				{
					for (int c = 0; c <= (int)BiomeLookClass.Aquatic; c++)
					{
						BiomeLooks.Look look = BiomeLooks.For((BiomeLookClass)c, hash, world);
						string what = $"{(BiomeLookClass)c} on {name} ({hash})";
						Assert.AreEqual(BiomeLooks.Variants, look.Variants.Length);
						Assert.GreaterOrEqual(Distinct(look, v => v.Foliage), 10, $"{what}: foliage");
						Assert.GreaterOrEqual(Distinct(look, v => v.Bloom), 10, $"{what}: flowers");
						Assert.GreaterOrEqual(Distinct(look, v => v.Accent), 10, $"{what}: fungi and lichen");
						Assert.GreaterOrEqual(Distinct(look, v => v.Coat), 10, $"{what}: animals");
					}
				}
			}
		}

		private static int Distinct(BiomeLooks.Look look, System.Func<BiomeLooks.Variant, Color> of)
		{
			int distinct = 0;
			for (int i = 0; i < look.Variants.Length; i++)
			{
				bool unique = true;
				for (int j = 0; j < i; j++)
				{
					if (Distance(of(look.Variants[i]), of(look.Variants[j])) < 0.04f)
					{
						unique = false;
						break;
					}
				}
				distinct += unique ? 1 : 0;
			}
			return distinct;
		}

		[Test]
		public void TheSameBiomeOnTheSameWorldIsAlwaysTheSame()
		{
			BiomeLooks.Look a = BiomeLooks.For(BiomeLookClass.Forest, 99u, Earth), b = BiomeLooks.For(BiomeLookClass.Forest, 99u, Earth);
			for (int i = 0; i < BiomeLooks.Variants; i++)
			{
				Assert.AreEqual(a.Variants[i].Foliage, b.Variants[i].Foliage);
				Assert.AreEqual(a.Variants[i].Coat, b.Variants[i].Coat);
			}
		}

		[Test]
		public void BiomesOfOneClassStillDiffer()
		{
			BiomeLooks.Look a = BiomeLooks.For(BiomeLookClass.Temperate, BiomeLooks.Hash("plains"), Earth);
			BiomeLooks.Look b = BiomeLooks.For(BiomeLookClass.Temperate, BiomeLooks.Hash("grassland"), Earth);
			float apart = 0f;
			for (int i = 0; i < BiomeLooks.Variants; i++)
			{
				apart += Distance(a.Variants[i].Foliage, b.Variants[i].Foliage) + Distance(a.Variants[i].Bloom, b.Variants[i].Bloom);
			}
			Assert.Greater(apart, 0.5f);
		}

		[Test]
		public void TheClassesThemselvesDiffer()
		{
			Color meadow = BiomeLooks.For(BiomeLookClass.Temperate, 1u, Earth).Variants[0].Foliage;
			Color desert = BiomeLooks.For(BiomeLookClass.Desert, 1u, Earth).Variants[0].Foliage;
			Color cave = BiomeLooks.For(BiomeLookClass.Cave, 1u, Earth).Variants[0].Foliage;
			Assert.Greater(meadow.g, meadow.r, "a meadow is green");
			Assert.Greater(Distance(meadow, desert), 0.15f, "a desert is sage and straw");
			Assert.Greater(cave.b, meadow.b + 0.1f, "a cave's growth is teal");
			float tundra = BiomeLooks.For(BiomeLookClass.Cold, 1u, Earth).Variants[0].Height;
			float jungle = BiomeLooks.For(BiomeLookClass.Jungle, 1u, Earth).Variants[0].Height;
			Assert.Greater(jungle, tundra * 2f, "jungle grass towers over tundra's");
		}

		[Test]
		public void TheStarDecidesTheColourOfLeaves()
		{
			Color sun = BiomeLooks.For(BiomeLookClass.Temperate, 1u, World(5800f)).Variants[0].Foliage;
			Color k = BiomeLooks.For(BiomeLookClass.Temperate, 1u, World(4500f)).Variants[0].Foliage;
			Color red = BiomeLooks.For(BiomeLookClass.Temperate, 1u, World(3000f)).Variants[0].Foliage;
			Color hot = BiomeLooks.For(BiomeLookClass.Temperate, 1u, World(9000f)).Variants[0].Foliage;
			Assert.Greater(sun.g, Mathf.Max(sun.r, sun.b), "green under a sun like ours");
			Assert.Greater(k.r / k.g, sun.r / sun.g, "olive to amber under a K star");
			Assert.Less(red.maxColorComponent, 0.3f, "dark under a red dwarf");
			Assert.Greater(red.r + red.b, red.g * 1.5f, "and plum, not green");
			Assert.Greater(hot.b / hot.g, sun.b / sun.g + 0.2f, "teal under a hot white star");
		}

		[Test]
		public void ThinAirGrowsLowAndPaleThickAirTallAndBroad()
		{
			BiomeLooks.Variant thin = BiomeLooks.For(BiomeLookClass.Temperate, 1u, World(5800f, AtmosphereKind.Thin)).Variants[0];
			BiomeLooks.Variant standard = BiomeLooks.For(BiomeLookClass.Temperate, 1u, World(5800f)).Variants[0];
			BiomeLooks.Variant thick = BiomeLooks.For(BiomeLookClass.Temperate, 1u, World(5800f, AtmosphereKind.Thick)).Variants[0];
			Assert.Less(thin.Height, standard.Height * 0.7f);
			Assert.Greater(thick.Height, standard.Height * 1.15f);
			Assert.Greater(thick.Width, standard.Width);
			Color.RGBToHSV(thin.Foliage, out _, out float sThin, out _);
			Color.RGBToHSV(standard.Foliage, out _, out float sStandard, out _);
			Assert.Less(sThin, sStandard, "waxy and pale");
		}

		[Test]
		public void AMethaneWorldGrowsNothingGreen()
		{
			BiomeLooks.Look look = BiomeLooks.For(BiomeLookClass.Temperate, 1u, World(5800f, condensate: Condensate.Methane, temperature: -5f));
			for (int i = 0; i < BiomeLooks.FirstFantastic; i++)
			{
				Color c = look.Variants[i].Foliage;
				Assert.Less(c.g, Mathf.Max(c.r, c.b) + 0.05f, $"{look.Variants[i].Name}: {c}");
			}
		}

		[Test]
		public void TwoRareFantasticLooksAndTheCommonOneCommonest()
		{
			BiomeLooks.Look look = BiomeLooks.For(BiomeLookClass.Forest, 3u, Earth);
			Assert.AreNotEqual(look.Variants[BiomeLooks.FirstFantastic].Name, look.Variants[BiomeLooks.FirstFantastic + 1].Name);
			var counts = new int[BiomeLooks.Variants];
			for (int i = 0; i < 10000; i++)
			{
				counts[look.Pick((i + 0.5f) / 10000f)]++;
			}
			Assert.AreEqual(0, System.Array.IndexOf(counts, Mathf.Max(counts)), "the common look is commonest");
			for (int i = 0; i < BiomeLooks.Variants; i++)
			{
				Assert.Greater(counts[i], 50, $"look {i} is found");
			}
			Assert.Less(counts[BiomeLooks.FirstFantastic] + counts[BiomeLooks.FirstFantastic + 1], 1000, "the fantastic is rare");
			Assert.AreEqual(1f, look.Cumulative[BiomeLooks.Variants - 1]);
		}

		[TestCase("Alpine Meadow", BiomeLookClass.Alpine)]
		[TestCase("Ice Cave", BiomeLookClass.Ice)]
		[TestCase("Volcanic Cave", BiomeLookClass.Volcanic)]
		[TestCase("Swamp Cave", BiomeLookClass.Cave)]
		[TestCase("Jungle Temple", BiomeLookClass.Jungle)]
		[TestCase("Forest Ruins", BiomeLookClass.Forest)]
		[TestCase("Coastal Water", BiomeLookClass.Aquatic)]
		[TestCase("Rocky Coast", BiomeLookClass.Coastal)]
		[TestCase("Methane Lake", BiomeLookClass.Alien)]
		[TestCase("Tholin Plain", BiomeLookClass.Alien)]
		[TestCase("Plains", BiomeLookClass.Temperate)]
		[TestCase("Salt Flat", BiomeLookClass.Desert)]
		[TestCase("Savanna", BiomeLookClass.Arid)]
		[TestCase("Taiga", BiomeLookClass.Cold)]
		[TestCase("Peat Bog", BiomeLookClass.Wetland)]
		[TestCase("Geyser Basin", BiomeLookClass.Volcanic)]
		[TestCase("Ice Geyser Field", BiomeLookClass.Alien)]
		[TestCase("Karst", BiomeLookClass.Forest)]
		public void ABiomesNameSaysItsClass(string name, BiomeLookClass expected)
		{
			Assert.AreEqual(expected, BiomeLooks.ClassOf(name, 0f, 0f));
		}

		[Test]
		public void ANamelessBiomeIsClassedByItsClimate()
		{
			Assert.AreEqual(BiomeLookClass.Desert, BiomeLooks.ClassOf("Crater", 0.8f, -0.6f));
			Assert.AreEqual(BiomeLookClass.Cold, BiomeLooks.ClassOf("Crater", -0.7f, 0f));
			Assert.AreEqual(BiomeLookClass.Wetland, BiomeLooks.ClassOf("Somewhere", 0.2f, 0.8f));
		}

		[TestCase("Ground_Grass", 1f, 0f, 0f)]
		[TestCase("Ground_GrassMeadow", 1f, 0f, 0f)]
		[TestCase("Ground_Moss", 1f, 0f, 0f)]
		[TestCase("Ground_Lichen", 0f, 1f, 0f)]
		[TestCase("Ground_Lava", 0f, 0f, 0f)]
		[TestCase("Ground_Sand", 0f, 0f, 1f)]
		[TestCase("Ground_Regolith", 0f, 0f, 1f)]
		[TestCase("Ground_Snow", 0f, 0f, 0.3f)]
		public void AGroundLayerIsLivingLichenOrMineral(string layer, float foliage, float accent, float mineral)
		{
			BiomeLooks.GroundShare share = BiomeLooks.GroundShareOf(layer);
			Assert.That(share.Foliage, Is.EqualTo(foliage).Within(1e-4f), "living");
			Assert.That(share.Accent, Is.EqualTo(accent).Within(1e-4f), "lichen");
			Assert.That(share.Mineral, Is.EqualTo(mineral).Within(1e-4f), "mineral");
		}

		[Test]
		public void LitterIsPartlyLivingAndNoLayerIsMoreThanWhole()
		{
			foreach (string layer in new[] { "Ground_ForestFloor", "Ground_NeedleLitter", "Ground_Peat", "Ground_PathLitter", "Ground_GrassDry", "Ground_Heath", "Ground_JungleFloor" })
			{
				BiomeLooks.GroundShare share = BiomeLooks.GroundShareOf(layer);
				Assert.That(share.Foliage, Is.InRange(0.2f, 0.9f), layer);
				Assert.That(share.Foliage + share.Accent + share.Mineral, Is.LessThanOrEqualTo(1.0001f), layer);
			}
		}

		[Test]
		public void OnlyBareOrThinAiredWorldsRecolourTheirMineralGround()
		{
			Assert.AreEqual(0f, BiomeLooks.WorldTraits.Earthlike.GroundStrength, "the home world's ground is its biomes'");
		}
	}
}
