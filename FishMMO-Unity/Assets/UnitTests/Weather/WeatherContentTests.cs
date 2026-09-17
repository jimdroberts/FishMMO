using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The default weather content: what each layer kind writes, what the named presets add up
	/// to, the profile each kind of biome gets, biome variants, and the generated textures.
	/// </summary>
	[TestFixture]
	public class WeatherContentTests
	{
		private readonly List<Object> created = new List<Object>();
		private Dictionary<WeatherLayerKind, WeatherLayerTemplate> templates;

		[SetUp]
		public void SetUp()
		{
			templates = new Dictionary<WeatherLayerKind, WeatherLayerTemplate>();
			foreach (WeatherLayerKind kind in (WeatherLayerKind[])System.Enum.GetValues(typeof(WeatherLayerKind)))
			{
				var template = Make<WeatherLayerTemplate>(kind.ToString());
				template.Kind = kind;
				WeatherContentGenerator.DefineTemplate(template);
				templates[kind] = template;
			}
		}

		[TearDown]
		public void TearDown()
		{
			foreach (Object asset in created)
			{
				Object.DestroyImmediate(asset);
			}
			created.Clear();
		}

		private T Make<T>(string name) where T : ScriptableObject
		{
			T asset = ScriptableObject.CreateInstance<T>();
			asset.name = name;
			created.Add(asset);
			return asset;
		}

		private WeatherPreset Preset(string name, params (WeatherLayerKind kind, float intensity)[] layers)
		{
			WeatherPreset preset = Make<WeatherPreset>(name);
			preset.DisplayName = name;
			foreach (var (kind, intensity) in layers)
			{
				preset.Layers.Add(new WeatherPresetLayer { Template = templates[kind], Intensity = intensity });
			}
			return preset;
		}

		[Test]
		public void EveryKindWritesTheChannelsItIsNamedFor()
		{
			WeatherFrame rain = templates[WeatherLayerKind.Rain].Evaluate(1f);
			LogAssert.IsTrue(rain[WeatherChannel.Precipitation] > 0.9f);
			LogAssert.AreEqual(1f, rain[WeatherChannel.RainWeight], "rain falls as rain");
			LogAssert.AreEqual(1f, templates[WeatherLayerKind.Sand].Evaluate(1f)[WeatherChannel.SandWeight]);
			LogAssert.IsTrue(templates[WeatherLayerKind.Wind].Evaluate(1f)[WeatherChannel.WindSpeed] > 0.9f);
			LogAssert.IsTrue(templates[WeatherLayerKind.Fog].Evaluate(1f)[WeatherChannel.FogDensity] > 0.9f);
			LogAssert.IsTrue(templates[WeatherLayerKind.Lightning].Evaluate(1f)[WeatherChannel.LightningRate] > 0.9f);
			LogAssert.IsTrue(templates[WeatherLayerKind.Aurora].Evaluate(1f)[WeatherChannel.Aurora] > 0.9f);
			LogAssert.IsTrue(templates[WeatherLayerKind.Clouds].Evaluate(1f)[WeatherChannel.CloudCover] > 0.9f);
			LogAssert.IsTrue(templates[WeatherLayerKind.Snow].Evaluate(1f)[WeatherChannel.TemperatureOffset] < 0f, "snow chills");

			foreach (WeatherLayerTemplate template in templates.Values)
			{
				WeatherFrame none = template.Evaluate(0f);
				LogAssert.IsTrue(none[WeatherChannel.Precipitation] < 0.01f, $"{template.Kind} at intensity 0 drops nothing");
			}
		}

		[Test]
		public void MoreIntensityMeansMoreWeather()
		{
			foreach (WeatherLayerTemplate template in templates.Values)
			{
				foreach (WeatherChannelCurve curve in template.Channels)
				{
					if (curve.Channel == WeatherChannel.CloudBase || curve.Channel == WeatherChannel.WindHeading || curve.Channel == WeatherChannel.TemperatureOffset || curve.Channel == WeatherChannel.HumidityOffset)
					{
						continue;
					}
					float previous = float.MinValue;
					for (float i = 0f; i <= 1.0001f; i += 0.1f)
					{
						float value = curve.Evaluate(i);
						LogAssert.IsTrue(value >= previous - 1e-4f, $"{template.Kind} {curve.Channel} falls at {i:0.0}");
						previous = value;
					}
				}
			}
		}

		[Test]
		public void TheNamedPresetsAreWhatTheySay()
		{
			WeatherFrame blizzard = Preset("Blizzard", (WeatherLayerKind.Snow, 1f), (WeatherLayerKind.Wind, 0.85f), (WeatherLayerKind.Clouds, 0.95f), (WeatherLayerKind.Fog, 0.6f)).Evaluate();
			LogAssert.AreEqual(PrecipitationKind.Snow, blizzard.DominantPrecipitation);
			LogAssert.IsTrue(blizzard[WeatherChannel.WindSpeed] > 0.8f, "a blizzard is windy");
			LogAssert.IsTrue(blizzard[WeatherChannel.FogDensity] >= 0.6f);

			WeatherFrame storm = Preset("Thunderstorm", (WeatherLayerKind.Rain, 0.85f), (WeatherLayerKind.Clouds, 1f), (WeatherLayerKind.Wind, 0.6f), (WeatherLayerKind.Lightning, 0.7f)).Evaluate();
			LogAssert.AreEqual(PrecipitationKind.Rain, storm.DominantPrecipitation);
			LogAssert.IsTrue(storm[WeatherChannel.LightningRate] > 0.5f);
			LogAssert.IsTrue(storm.StormSeverity > 0.5f, $"a thunderstorm is severe ({storm.StormSeverity:0.00})");

			WeatherFrame sand = Preset("Sandstorm", (WeatherLayerKind.Sand, 0.9f), (WeatherLayerKind.Wind, 0.9f)).Evaluate();
			LogAssert.AreEqual(PrecipitationKind.Sand, sand.DominantPrecipitation);
			LogAssert.IsTrue(sand[WeatherChannel.HumidityOffset] < 0f, "sand dries the air");

			WeatherFrame clear = Preset("Clear", (WeatherLayerKind.Clouds, 0f)).Evaluate();
			LogAssert.AreEqual(PrecipitationKind.None, clear.DominantPrecipitation);
			LogAssert.IsTrue(clear[WeatherChannel.CloudCover] < 0.1f);
		}

		private Dictionary<string, WeatherPreset> AllPresets()
		{
			var presets = new Dictionary<string, WeatherPreset>();
			foreach (string name in new[] { "Clear", "Fair", "Overcast", "Mist", "Sprinkle", "Light Rain", "Medium Rain", "Heavy Rain", "Thunderstorm",
				"Light Snow", "Heavy Snow", "Blizzard", "Hailstorm", "Ashfall", "Sandstorm", "Windy", "Aurora Night",
				"Swamp Rain", "Jungle Downpour", "Tundra Snow", "Dune Sandstorm", "Eruption Ashfall" })
			{
				presets[name] = Preset(name, (WeatherLayerKind.Clouds, 0.5f), (WeatherLayerKind.Fog, 0.2f), (WeatherLayerKind.Wind, 0.2f));
			}
			return presets;
		}

		private BiomeWeatherProfile ProfileFor(string biomeName)
		{
			BiomeTemplate biome = Make<BiomeTemplate>(biomeName);
			return WeatherContentGenerator.ProfileForBiome(biome, AllPresets());
		}

		private static bool Spawns(BiomeWeatherProfile profile, string preset)
		{
			return profile.CellSpawns.Exists(s => s.Preset != null && s.Preset.name == preset && s.Weight > 0f);
		}

		[Test]
		public void EachKindOfBiomeGetsItsOwnWeather()
		{
			BiomeWeatherProfile desert = ProfileFor("Desert");
			LogAssert.IsTrue(Spawns(desert, "Sandstorm"));
			LogAssert.IsTrue(desert.Variants.Exists(v => v.Preset.name == "Sandstorm" && v.Variant.name == "Dune Sandstorm"));

			BiomeWeatherProfile glacier = ProfileFor("Glacier");
			LogAssert.IsTrue(Spawns(glacier, "Blizzard"));
			LogAssert.IsTrue((glacier.Forbidden & WeatherKindMask.Sand) != 0, "no sand on a glacier");

			BiomeWeatherProfile swamp = ProfileFor("Swamp");
			LogAssert.IsTrue(Spawns(swamp, "Thunderstorm"));
			LogAssert.IsTrue(swamp.Background.Count > 0, "a swamp is misty even without a storm");
			LogAssert.IsTrue(swamp.Variants.Exists(v => v.Variant.name == "Swamp Rain"));

			BiomeWeatherProfile volcanic = ProfileFor("Volcanic");
			LogAssert.IsTrue(Spawns(volcanic, "Ashfall"));

			BiomeWeatherProfile cave = ProfileFor("Ice Cave");
			LogAssert.AreEqual(WeatherKindMask.All, cave.Forbidden, "nothing falls in a cave");
			LogAssert.AreEqual(0, cave.CellSpawns.Count);

			LogAssert.AreEqual(WeatherKindMask.All, ProfileFor("Coral Reef").Forbidden, "or under the sea");
			LogAssert.IsNull(ProfileFor("Forest"), "ordinary land uses the climate's default");
			LogAssert.IsNull(ProfileFor("Castle"));
		}

		[Test]
		public void TheTemperateDefaultHasMostlyFairWeather()
		{
			Dictionary<string, WeatherPreset> presets = AllPresets();
			BiomeWeatherProfile profile = WeatherContentGenerator.TemperateProfile(presets);
			LogAssert.IsTrue(profile.IsAuthored);
			float fair = 0f, all = 0f;
			foreach (WeightedWeatherPreset spawn in profile.CellSpawns)
			{
				all += spawn.Weight;
				if (spawn.Preset.name == "Fair" || spawn.Preset.name == "Overcast")
				{
					fair += spawn.Weight;
				}
			}
			LogAssert.IsTrue(fair / all > 0.3f, "fair and overcast are the commonest");
			LogAssert.IsTrue(profile.SuitabilityOf(presets["Sandstorm"]) < 0.5f, "sandstorms fade over farmland");
			LogAssert.AreEqual(1f, profile.SuitabilityOf(presets["Light Rain"]));
		}

		[Test]
		public void ABiomeCanSwapAPresetForItsVariant()
		{
			Dictionary<string, WeatherPreset> presets = AllPresets();
			var profile = new BiomeWeatherProfile();
			LogAssert.IsFalse(profile.IsAuthored);
			profile.Variants.Add(new WeatherPresetVariant { Preset = presets["Heavy Rain"], Variant = presets["Swamp Rain"] });
			LogAssert.IsTrue(profile.IsAuthored, "a variant alone is authoring");
			LogAssert.AreSame(presets["Swamp Rain"], profile.VariantOf(presets["Heavy Rain"]));
			LogAssert.AreSame(presets["Light Rain"], profile.VariantOf(presets["Light Rain"]), "other presets pass through");
			LogAssert.IsNull(profile.VariantOf(null));
			profile.Variants.Add(new WeatherPresetVariant { Preset = presets["Blizzard"], Variant = null });
			LogAssert.AreSame(presets["Blizzard"], profile.VariantOf(presets["Blizzard"]), "an empty variant is ignored");
		}

		[Test]
		public void ABiomeGuessedWithoutGroundDoesNotSilenceTheWeather()
		{
			// Only a sea-floor biome that forbids everything is registered, so a position with no
			// terrain and no map (a scene built from meshes) resolves to it by default.
			var saved = new List<BiomeTemplate>();
			foreach (string key in BiomeRegistry.SupportedBiomes)
			{
				if (BiomeRegistry.TryGet(key, out BiomeTemplate existing))
				{
					saved.Add(existing);
				}
			}
			BiomeTemplate seabed = Make<BiomeTemplate>("Test Seabed");
			seabed.DisplayName = "Test Seabed";
			seabed.MinHeight = 0f;
			seabed.MaxHeight = 1f;
			seabed.SelectionWeight = 1f;
			seabed.Weather = new BiomeWeatherProfile { Forbidden = WeatherKindMask.All };
			WeatherLayerTemplate rain = templates[WeatherLayerKind.Rain];
			rain.AddToCache(rain.name + System.Guid.NewGuid().ToString("N"));
			try
			{
				BiomeRegistry.Clear();
				BiomeRegistry.Register(seabed);
				BiomeReading reading = BiomeSampler.Read(new Vector3(0f, 0f, 0f), (FishMMO.Shared.WorldSceneSettings)null);
				Assume.That(reading.Biome, Is.SameAs(seabed), "the resolver did not pick the only biome");
				LogAssert.IsFalse(reading.IsGrounded, "no terrain and no map: the biome is a guess");

				var timeline = new WeatherTimeline { SceneMode = WeatherSceneMode.Own };
				timeline.Layers.Add(new WeatherLayerEntry { Handle = 1, TemplateID = rain.ID, From = 1f, To = 1f });
				WeatherSample sample = WeatherField.Sample(timeline, null, default, Vector3.zero, 10);
				LogAssert.IsTrue(sample.Frame[WeatherChannel.Precipitation] > 0.5f,
					"a guessed sea floor must not forbid the rain a scene layer asked for");
			}
			finally
			{
				rain.RemoveFromCache();
				BiomeRegistry.Clear();
				foreach (BiomeTemplate biome in saved)
				{
					BiomeRegistry.Register(biome);
				}
			}
		}

		// ── Textures ──

		[Test]
		public void TheAtlasHasFiveRowsOfShapesAndThreeSpare()
		{
			Texture2D atlas = WeatherTextureBaker.BuildAtlas(WeatherTextureBaker.DefaultSeed);
			try
			{
				LogAssert.AreEqual(512, atlas.width);
				LogAssert.AreEqual(1024, atlas.height);
				Color32[] pixels = atlas.GetPixels32();
				for (int row = 0; row < WeatherTextureBaker.Rows; row++)
				{
					long alpha = 0;
					int top = atlas.height - (row + 1) * WeatherTextureBaker.Tile;
					for (int y = top; y < top + WeatherTextureBaker.Tile; y++)
					{
						for (int x = 0; x < atlas.width; x++)
						{
							alpha += pixels[y * atlas.width + x].a;
						}
					}
					if (row < 5)
					{
						LogAssert.IsTrue(alpha > 1000, $"row {row} has shapes");
					}
					else
					{
						LogAssert.AreEqual(0L, alpha, $"row {row} is spare");
					}
				}
				// Tiles keep a clear border so mipmaps do not bleed into neighbours.
				LogAssert.AreEqual((byte)0, pixels[(atlas.height - 1) * atlas.width + 0].a);
			}
			finally
			{
				Object.DestroyImmediate(atlas);
			}
		}

		[Test]
		public void TheSameSeedBakesTheSamePixels()
		{
			Texture2D a = WeatherTextureBaker.BuildAtlas(5);
			Texture2D b = WeatherTextureBaker.BuildAtlas(5);
			Texture2D c = WeatherTextureBaker.BuildAtlas(6);
			try
			{
				CollectionAssert.AreEqual(a.GetPixels32(), b.GetPixels32());
				CollectionAssert.AreNotEqual(a.GetPixels32(), c.GetPixels32(), "a different seed makes different flakes");
			}
			finally
			{
				Object.DestroyImmediate(a);
				Object.DestroyImmediate(b);
				Object.DestroyImmediate(c);
			}
		}

		[Test]
		public void TheNoiseTiles()
		{
			const int size = 64;
			float worst = 0f;
			for (int i = 0; i < size; i++)
			{
				float t = i / (float)size;
				// The value just past the right edge is the value at the left edge.
				worst = Mathf.Max(worst, Mathf.Abs(WeatherTextureBaker.Fbm(1f, t, 4, 9) - WeatherTextureBaker.Fbm(0f, t, 4, 9)));
				worst = Mathf.Max(worst, Mathf.Abs(WeatherTextureBaker.Fbm(t, 1f, 4, 9) - WeatherTextureBaker.Fbm(t, 0f, 4, 9)));
			}
			Assert.That(worst, Is.LessThan(1e-4f));
			float lo = 1f, hi = 0f;
			for (int i = 0; i < 200; i++)
			{
				float v = WeatherTextureBaker.Fbm(i * 0.0371f, i * 0.0613f, 4, 9);
				lo = Mathf.Min(lo, v);
				hi = Mathf.Max(hi, v);
			}
			LogAssert.IsTrue(hi - lo > 0.2f, "and is not flat");
		}
	}
}
