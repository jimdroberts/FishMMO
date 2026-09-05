using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.NameGeneration.Editor;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The biome layer under naming and terrain: templates register by key and ID,
	/// the resolver picks a biome from height and climate the same way every time,
	/// climate settings turn height into temperature and humidity, a scene map hands
	/// back the biome at a position, races draw home biomes from their affinities,
	/// and place names take on a climate variant's vocabulary.
	/// </summary>
	[TestFixture]
	public class BiomeSystemTests
	{
		private readonly List<ScriptableObject> created = new List<ScriptableObject>();

		[OneTimeSetUp]
		public void LoadAssets()
		{
			NamingTemplateEditorLoader.Reload();
			Assume.That(BiomeRegistry.Count, Is.GreaterThan(0), "No BiomeTemplate assets registered.");
		}

		[TearDown]
		public void DestroyCreated()
		{
			foreach (ScriptableObject asset in created)
			{
				BiomeRegistry.Unregister(asset as BiomeTemplate);
				Object.DestroyImmediate(asset);
			}
			created.Clear();
		}

		private BiomeTemplate MakeBiome(string name, int tier, float minH, float maxH, float minT, float maxT, float weight = 1f)
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.name = name;
			biome.DisplayName = name;
			biome.ElevationTier = tier;
			biome.MinHeight = minH;
			biome.MaxHeight = maxH;
			biome.MinTemperature = minT;
			biome.MaxTemperature = maxT;
			biome.SelectionWeight = weight;
			created.Add(biome);
			return biome;
		}

		private ClimateSettings MakeClimate()
		{
			var climate = ScriptableObject.CreateInstance<ClimateSettings>();
			created.Add(climate);
			return climate;
		}

		// ── Registry ─────────────────────────────────────────────────

		[Test]
		public void EveryPortedBiome_RegistersByKeyAndID()
		{
			Assert.GreaterOrEqual(BiomeRegistry.Count, 58, "42 ported + 16 authored biomes expected.");
			foreach (string key in BiomeRegistry.SupportedBiomes)
			{
				BiomeTemplate biome = BiomeRegistry.Get(key);
				Assert.IsNotNull(biome, key);
				Assert.IsTrue(BiomeRegistry.TryGetByID(BiomeRegistry.IDOf(biome), out BiomeTemplate byId), key);
				Assert.AreSame(biome, byId, "ID lookup must return the same asset as key lookup.");
			}
		}

		[Test]
		public void PlacedOnlyBiomes_AreNeverSelectable()
		{
			foreach (BiomeTemplate biome in BiomeRegistry.Selectable)
			{
				Assert.Greater(biome.SelectionWeight, 0f, biome.name);
			}
			Assert.IsFalse(BiomeRegistry.Get("castle").IsSelectable, "Castle is placed by designers, not chosen from climate.");
		}

		[Test]
		public void Templates_CarryTerrainLayers_AndNaming()
		{
			BiomeTemplate forest = BiomeRegistry.Get("forest");
			Assert.IsTrue(forest.HasValidTextureLayers(), "Forest kept its WorldEditor albedo layers.");
			Assert.IsTrue(forest.Naming.IsUsable, "Forest merged the naming asset.");
			Assert.IsNotEmpty(forest.Naming.RuntimePhonology.DungeonSuffixes);
			Assert.IsTrue(BiomeRegistry.Get("lake").Naming.IsUsable, "Lake and River carry naming data like every other biome.");
			CollectionAssert.AreEqual(BiomeRegistry.SupportedBiomes, NameGenerator.SupportedBiomes, "Every shipped biome is nameable, so the generator offers all of them.");
		}

		// ── Resolver ─────────────────────────────────────────────────

		[Test]
		public void Resolver_PrefersTheBiomeWhoseEnvelopeContainsTheClimate()
		{
			BiomeRegistry.Clear();
			BiomeTemplate cold = MakeBiome("Cold", 5, 0.6f, 0.75f, -1f, -0.3f);
			BiomeTemplate warm = MakeBiome("Warm", 5, 0.6f, 0.75f, 0.2f, 1f);
			BiomeRegistry.Register(cold);
			BiomeRegistry.Register(warm);
			try
			{
				Assert.AreSame(cold, BiomeResolver.Select(0.7f, -0.6f, 0f, 5));
				Assert.AreSame(warm, BiomeResolver.Select(0.7f, 0.6f, 0f, 5));
			}
			finally
			{
				NamingTemplateEditorLoader.Reload();
			}
		}

		[Test]
		public void Resolver_OutsideEveryEnvelope_FallsBackToTheNearest_NeverToWeightZero()
		{
			BiomeRegistry.Clear();
			BiomeTemplate near = MakeBiome("Near", 5, 0.6f, 0.75f, 0.0f, 0.3f);
			BiomeTemplate far = MakeBiome("Far", 5, 0.6f, 0.75f, -1f, -0.8f);
			BiomeTemplate placed = MakeBiome("Placed", 5, 0.6f, 0.75f, 0.5f, 1f, weight: 0f);
			BiomeRegistry.Register(near);
			BiomeRegistry.Register(far);
			BiomeRegistry.Register(placed);
			try
			{
				Assert.AreSame(near, BiomeResolver.Select(0.7f, 0.9f, 0f, 5), "Nearest envelope wins even though 'Placed' contains the climate.");
			}
			finally
			{
				NamingTemplateEditorLoader.Reload();
			}
		}

		[Test]
		public void Resolver_IsDeterministic_AcrossTheWholeMap()
		{
			ClimateSettings climate = MakeClimate();
			for (int i = 0; i <= 40; i++)
			{
				float height = i / 40f;
				ClimateSample sample = climate.Evaluate(height, 0.5f);
				BiomeTemplate first = BiomeResolver.Select(height, sample);
				BiomeTemplate second = BiomeResolver.Select(height, sample);
				Assert.IsNotNull(first, $"height {height}");
				Assert.AreSame(first, second, $"height {height}");
			}
		}

		[Test]
		public void Resolver_SeaFloorIsOcean_PeaksAreFrozen()
		{
			ClimateSettings climate = MakeClimate();
			Assert.AreEqual(0, BiomeResolver.Select(0.05f, climate.Evaluate(0.05f, 0.5f)).ElevationTier);
			BiomeTemplate peak = BiomeResolver.Select(0.98f, climate.Evaluate(0.98f, 0.5f));
			Assert.AreEqual(8, peak.ElevationTier, peak.name);
		}

		// ── Climate ──────────────────────────────────────────────────

		[Test]
		public void Climate_TemperatureFallsWithHeight_AndGlobalOffsetShiftsIt()
		{
			ClimateSettings climate = MakeClimate();
			float low = climate.Evaluate(0.1f, 0.5f).Temperature;
			float high = climate.Evaluate(0.9f, 0.5f).Temperature;
			Assert.Greater(low, high);

			climate.GlobalTemperatureOffset = 0.5f;
			Assert.Greater(climate.Evaluate(0.1f, 0.5f).Temperature, low);
		}

		[Test]
		public void Climate_TiersFollowTheBoundaries()
		{
			ClimateSettings climate = MakeClimate();
			Assert.AreEqual(0, climate.TierForHeight(0.1f));
			Assert.AreEqual(4, climate.TierForHeight(0.5f));
			Assert.AreEqual(8, climate.TierForHeight(0.99f));
			Assert.AreEqual(8, climate.TierForHeight(1f));
		}

		[Test]
		public void ClimateVariant_MatchesItsWindow_BiomeOwnBeforeDefaults()
		{
			ClimateSettings climate = MakeClimate();
			climate.DefaultVariants.Add(new BiomeClimateVariant { Name = "Frozen", MinTemperature = -1f, MaxTemperature = -0.45f });
			BiomeTemplate bare = MakeBiome("Bare", 5, 0.6f, 0.75f, -1f, 1f);
			BiomeTemplate own = MakeBiome("Own", 5, 0.6f, 0.75f, -1f, 1f);
			own.ClimateVariants.Add(new BiomeClimateVariant { Name = "Frostwood", MinTemperature = -1f, MaxTemperature = -0.45f });

			var cold = new ClimateSample { Temperature = -0.8f, Humidity = 0f, ElevationTier = 5 };
			var mild = new ClimateSample { Temperature = 0f, Humidity = 0f, ElevationTier = 5 };
			Assert.AreEqual("Frozen", climate.ResolveVariant(bare, cold).Name);
			Assert.AreEqual("Frostwood", climate.ResolveVariant(own, cold).Name, "A biome's own variants take precedence.");
			Assert.IsNull(climate.ResolveVariant(bare, mild));
			Assert.AreEqual("Frostwood", own.FindOwnVariant("frostwood")?.Name, "Variant keys are case-insensitive.");
		}

		// ── Scene map ────────────────────────────────────────────────

		[Test]
		public void SceneBiomeMap_ReturnsTheCellUnderAPosition()
		{
			BiomeTemplate forest = BiomeRegistry.Get("forest");
			BiomeTemplate desert = BiomeRegistry.Get("desert");
			var map = ScriptableObject.CreateInstance<SceneBiomeMap>();
			created.Add(map);
			map.Set(2, 1, new[] { BiomeRegistry.IDOf(forest), BiomeRegistry.IDOf(desert) }, new Vector2(0f, 0f), new Vector2(100f, 100f));

			Assert.AreSame(forest, map.Sample(new Vector3(10f, 0f, 50f)));
			Assert.AreSame(desert, map.Sample(new Vector3(90f, 0f, 50f)));
			Assert.IsFalse(map.Contains(new Vector3(-1f, 0f, 50f)));
			Assert.AreEqual(0, map.IDAt(new Vector3(500f, 0f, 500f)), "Outside the rect is no biome.");
			Assert.AreEqual(0.5f, map.Latitude01(new Vector3(50f, 0f, 50f)), 0.001f);
		}

		// ── Races ────────────────────────────────────────────────────

		[Test]
		public void EveryRace_HasAtLeastOneRegisteredHomeBiome()
		{
			foreach (string key in RaceRegistry.SupportedRaces)
			{
				RaceTemplate race = RaceRegistry.Get(key);
				Assert.IsNotEmpty(race.BiomeAffinities, race.name);
				bool anyResolves = false;
				foreach (BiomeAffinity affinity in race.BiomeAffinities)
				{
					anyResolves |= affinity.Biome != null;
				}
				Assert.IsTrue(anyResolves, $"{race.name}: no affinity points at a registered biome.");
			}
		}

		[Test]
		public void PickHomeBiome_IsSeeded_AndHonoursWeights()
		{
			RaceTemplate merfolk = RaceRegistry.Get("merfolk");
			BiomeTemplate a = merfolk.PickHomeBiome(new DeterministicRNG(42));
			BiomeTemplate b = merfolk.PickHomeBiome(new DeterministicRNG(42));
			Assert.AreSame(a, b, "Same seed, same home.");

			var counts = new Dictionary<BiomeTemplate, int>();
			for (int seed = 1; seed <= 400; seed++)
			{
				BiomeTemplate home = merfolk.PickHomeBiome(new DeterministicRNG(seed));
				Assert.Greater(merfolk.AffinityFor(BiomeRegistry.IDOf(home)), 0f, "Only weighted biomes may be drawn.");
				counts.TryGetValue(home, out int n);
				counts[home] = n + 1;
			}
			BiomeTemplate reef = BiomeRegistry.Get("coralreef");
			Assert.IsTrue(counts.ContainsKey(reef));
			foreach (KeyValuePair<BiomeTemplate, int> pair in counts)
			{
				Assert.GreaterOrEqual(counts[reef], pair.Value, $"Coral Reef (weight 3) should be drawn at least as often as {pair.Key.name}.");
			}
		}

		[Test]
		public void RacesForBiome_ListsTheRacesThatFavourIt_HeaviestFirst()
		{
			List<(RaceTemplate race, float weight)> cave = RaceRegistry.RacesForBiome(BiomeRegistry.IDOf(BiomeRegistry.Get("cave")));
			Assert.Greater(cave.Count, 5);
			for (int i = 1; i < cave.Count; i++)
			{
				Assert.GreaterOrEqual(cave[i - 1].weight, cave[i].weight);
			}
			Assert.IsTrue(cave.Exists(pair => pair.race.NamingKey == "kobold"));
			Assert.IsEmpty(RaceRegistry.RacesForBiome(0));
		}

		// ── Naming with biomes ───────────────────────────────────────

		[Test]
		public void DungeonAndPOI_AcceptABiomeByID_AndRejectUnusableOnes()
		{
			var generator = new NameGenerator();
			int forest = BiomeRegistry.IDOf(BiomeRegistry.Get("forest"));
			DungeonNameEntry byId = generator.Generate(new DungeonRequest { BiomeID = forest, RegionSeed = "seed-7" });
			DungeonNameEntry byKey = generator.Generate(new DungeonRequest { Biome = "forest", RegionSeed = "seed-7" });
			Assert.AreEqual(byKey.Name, byId.Name, "ID and key must name the same biome the same way.");
			Assert.IsNotEmpty(generator.Generate(new POIRequest { BiomeID = forest, POIType = POIType.Shrine, RegionSeed = "seed-7" }).Name);

			var bare = ScriptableObject.CreateInstance<BiomeTemplate>();
			bare.name = "Bare";
			created.Add(bare);
			BiomeRegistry.Register(bare);
			Assert.Throws<System.ArgumentException>(() => generator.Generate(new DungeonRequest { Biome = "bare", RegionSeed = "seed-7" }),
				"A biome without naming data is refused, not guessed.");
			Assert.IsFalse(new List<string>(NameGenerator.SupportedBiomes).Contains("bare"), "and it is not offered to tools.");
			Assert.Throws<System.ArgumentException>(() => generator.Generate(new DungeonRequest { BiomeID = 12345, RegionSeed = "seed-7" }));
		}

		[Test]
		public void ClimateVariant_LeadsPlaceNames()
		{
			var generator = new NameGenerator();
			var frozen = new BiomeClimateVariant { Name = "Frozen", Adjectives = new[] { "Frostbound" }, DungeonPrefixes = new[] { "The Frostbound" } };
			int frostbound = 0, total = 0;
			for (int seed = 1; seed <= 60; seed++)
			{
				string name = generator.Generate(new DungeonRequest { Biome = "forest", RegionSeed = "seed-" + seed, Variant = frozen }).Name;
				total++;
				if (name.StartsWith("The Frostbound")) frostbound++;
			}
			Assert.Greater(frostbound, total / 4, "A variant's prefix should lead a good share of the dungeons named under it.");

			string byKey = generator.Generate(new DungeonRequest { Biome = "forest", RegionSeed = "seed-3", ClimateVariant = "frozen" }).Name;
			string byObject = generator.Generate(new DungeonRequest { Biome = "forest", RegionSeed = "seed-3", Variant = BiomeRegistry.Get("forest").FindOwnVariant("frozen") }).Name;
			Assert.AreEqual(byObject, byKey, "A variant key resolves on the biome to the same variant object.");
		}

		[Test]
		public void City_WithoutABiome_DrawsTheRacesHome()
		{
			var generator = new NameGenerator();
			CityNameEntry withHome = generator.Generate(new CityRequest { Race = "merfolk", RegionSeed = "seed-11" });
			CityNameEntry withoutHome = generator.Generate(new CityRequest { Race = "merfolk", RegionSeed = "seed-11", UseRaceHomeBiome = false });
			Assert.IsNotEmpty(withHome.Name);
			Assert.IsNotEmpty(withoutHome.Name);
			Assert.AreEqual(withHome.Name, generator.Generate(new CityRequest { Race = "merfolk", RegionSeed = "seed-11" }).Name, "The home draw is seeded.");
		}

		// ── Scene object naming ──────────────────────────────────────

		[Test]
		public void VariantIndex_RoundTripsThroughTheWireByte()
		{
			BiomeTemplate forest = BiomeRegistry.Get("forest");
			Assume.That(forest.ClimateVariants.Count, Is.GreaterThan(1));
			BiomeClimateVariant second = forest.ClimateVariants[1];
			byte index = SceneObjectNameResolver.VariantIndexOf(forest, null, second);
			Assert.AreEqual(2, index);
			Assert.AreSame(second, SceneObjectNameResolver.VariantAt(forest, null, index));
			Assert.IsNull(SceneObjectNameResolver.VariantAt(forest, null, 0));
			Assert.IsNull(SceneObjectNameResolver.VariantAt(forest, null, 200), "An index past the list is no variant, not an exception.");
			Assert.AreEqual(0, SceneObjectNameResolver.VariantIndexOf(forest, null, new BiomeClimateVariant { Name = "Foreign" }));
		}

		[Test]
		public void DungeonNamer_UsesTheBiomeItWasHanded_OverNone()
		{
			var settings = new SceneObjectNamingSettings { Mode = SceneObjectNamingMode.Dungeon };
			BiomeTemplate volcanic = BiomeRegistry.Get("volcanic");
			Assert.IsTrue(SceneObjectNameResolver.TryBuild(settings, null, 5, CharacterGender.Unspecified, out string name, out string error, null, volcanic), error);
			Assert.IsNotEmpty(name);
			Assert.IsFalse(SceneObjectNameResolver.TryBuild(settings, null, 5, CharacterGender.Unspecified, out _, out error));
			StringAssert.Contains("biome", error);
		}
	}
}
