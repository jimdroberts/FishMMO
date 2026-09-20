using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Weather
{
	/// <summary>What the weather is at one place and tick.</summary>
	public struct WeatherSample
	{
		public WeatherFrame Frame;
		/// <summary>
		/// The weather here without the storm cells: the biome's own background and whatever scene
		/// layers are running. The sky uses it for the far distance, where the weather map does not
		/// reach — a cell overhead must not raise the coverage of the whole sky.
		/// </summary>
		public WeatherFrame Background;
		public BiomeTemplate Biome;
		/// <summary>Local climate temperature, runtime offsets included.</summary>
		public float Temperature;
		/// <summary>0 in the open … 1 fully under cover.</summary>
		public float Shelter;
		/// <summary>What the driver says the air is doing here: pressure, humidity, instability, wind.</summary>
		public WeatherDriver.Synoptic Air;
		/// <summary>
		/// How much of this weather the drifting field decided, 0..1. 1 is the field alone; 0 is a
		/// preset or layer at full strength standing in for it. The sky scales the field's own
		/// terms — the formations, the front's slope — by this, so a preset's sky is the preset's.
		/// </summary>
		public float DriverWeight;

		public PrecipitationKind Precipitation => Frame.DominantPrecipitation;
		public float StormSeverity => Frame.StormSeverity;
		public bool IsSheltered => Shelter >= 0.5f;
		/// <summary>How much of the weather reaches someone standing here, 0..1.</summary>
		public float Exposure => 1f - Shelter;

		public bool Matches(WeatherKindMask kinds)
		{
			if ((kinds & WeatherKindMask.Lightning) != 0 && Frame[WeatherChannel.LightningRate] > 0.05f) return true;
			if ((kinds & WeatherKindMask.Wind) != 0 && Frame[WeatherChannel.WindSpeed] > 0.3f) return true;
			if ((kinds & WeatherKindMask.Fog) != 0 && Frame[WeatherChannel.FogDensity] > 0.2f) return true;
			if (Frame[WeatherChannel.Precipitation] < 0.02f) return false;
			if ((kinds & WeatherKindMask.Rain) != 0 && Frame[WeatherChannel.RainWeight] > 0.2f) return true;
			if ((kinds & WeatherKindMask.Snow) != 0 && Frame[WeatherChannel.SnowWeight] > 0.2f) return true;
			if ((kinds & WeatherKindMask.Hail) != 0 && Frame[WeatherChannel.HailWeight] > 0.2f) return true;
			if ((kinds & WeatherKindMask.Ash) != 0 && Frame[WeatherChannel.AshWeight] > 0.2f) return true;
			if ((kinds & WeatherKindMask.Sand) != 0 && Frame[WeatherChannel.SandWeight] > 0.2f) return true;
			return false;
		}
	}

	/// <summary>
	/// Samples a scene's weather at a position: biome background, scene layers and storm cells,
	/// re-typed for the local temperature and shaped by weather volumes. A pure function of its
	/// inputs, run identically on server and client.
	/// </summary>
	public static class WeatherField
	{
		/// <summary>The profile a biome uses: its own when authored, else the climate's default.</summary>
		public static BiomeWeatherProfile ProfileFor(BiomeTemplate biome, WorldSceneSettings settings)
		{
			if (biome != null && biome.Weather != null && biome.Weather.IsAuthored)
			{
				return biome.Weather;
			}
			ClimateSettings climate = settings != null ? settings.Climate : null;
			return climate != null ? climate.DefaultWeather : null;
		}

		public static WeatherSample Sample(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, Vector3 position, uint tick)
		{
			var sample = new WeatherSample { Frame = WeatherFrame.Clear };
			BiomeReading reading = BiomeSampler.Read(position, settings);
			sample.Biome = reading.Biome;
			sample.Temperature = reading.Climate.Temperature;

			WeatherSceneMode mode = timeline != null ? timeline.SceneMode : WeatherSceneMode.None;
			bool airless = settings != null && settings.Body != null && !settings.Body.HasWeather;
			if (timeline == null || mode == WeatherSceneMode.None || mode == WeatherSceneMode.Auto || airless)
			{
				return sample;
			}

			// A biome guessed from no terrain and no map (a scene built from meshes) is the sea
			// floor by default; its "nothing falls here" must not silence the weather. Use the
			// climate's default instead.
			BiomeWeatherProfile profile = ProfileFor(reading.IsGrounded ? reading.Biome : null, settings);
			var accumulator = new WeatherAccumulator();
			if (mode == WeatherSceneMode.Own && timeline.Driver)
			{
				// What the weather is doing here and now, before anything the biome or a cell adds:
				// the drifting field of highs and lows, worked out from the world clock. This is the
				// part that actually makes weather happen — without it the background was one fixed
				// frame per biome that never changed, and the sky only ever varied when a storm cell
				// happened to pass. Both sides compute it from the same seed and the same clock, so
				// it costs nothing on the wire and two machines never disagree.
				// The season and the hour are worked out here, on both sides, from the clock anchor
				// the server sent. Sending them instead would freeze them at whatever they were when
				// the client joined.
				double worldSeconds = timeline.WorldSecondsAt(tick);
				double worldHours = worldSeconds / 3600.0;
				SolarSystemProfile system = SolarSystemProfile.Active;
				WorldBody sceneBody = timeline.BodyOverride != null ? timeline.BodyOverride : SceneTime.BodyOf(settings);
				// The season of the body this scene is on, from where its sun stands — not the fraction
				// of the home calendar gone. See CelestialMath.Season01.
				float season01 = CelestialMath.Season01(system, sceneBody, worldHours);
				float localTime01 = system != null && sceneBody != null
					? (float)CelestialMath.LocalTime01(system, sceneBody, worldHours, timeline.LongitudeDegrees)
					: 0.5f;

				WeatherDriver.Synoptic air = WeatherDriver.Sample(
					WeatherDriver.WorldSeed,
					new Vector2(position.x, position.z),
					worldSeconds,
					timeline.LatitudeDegrees,
					season01,
					localTime01);
				// Over this place: its biome's and its world's humidity lean on the air, so a desert
				// stays mostly dry under a front that soaks the forest next door.
				air = WeatherDriver.OverPlace(air, reading.Climate.Humidity);
				sample.Air = air;
				// The field carries its own temperature anomaly on top of the biome's climate.
				sample.Temperature = Mathf.Clamp(sample.Temperature + air.Temperature * 0.35f, -1f, 1f);
				// A preset or a layer overrides the field rather than adding to it: everything
				// blends by taking the greater, so at full weight the field would show through any
				// weather that asked for less of something — Clear could never clear the sky. The
				// field steps back by the strongest layer's strength and returns as it fades.
				float driverWeight = 1f - timeline.OverrideAt(tick, sample.Temperature);
				sample.DriverWeight = driverWeight;
				if (driverWeight > 0f)
				{
					accumulator.Add(WeatherDriver.Background(air), driverWeight);
					if (profile != null)
					{
						var biome = new WeatherAccumulator();
						profile.AccumulateBackground(ref biome);
						if (biome.HasAny)
						{
							accumulator.Add(biome.Resolve(), driverWeight);
						}
					}
				}
			}
			timeline.AccumulateSceneLayers(tick, ref accumulator, sample.Temperature);
			// Everything but the cells is the background, and the sky needs it on its own.
			sample.Background = accumulator.HasAny ? accumulator.Resolve() : WeatherFrame.Clear;
			StormsInHeavyRain(ref sample.Background, sample.Temperature);
			if (mode == WeatherSceneMode.Own)
			{
				for (int i = 0; i < timeline.Cells.Count; i++)
				{
					StormCell cell = timeline.Cells[i];
					float weight = cell.InfluenceAt(position, tick, timeline.TickDelta);
					if (weight <= 0f)
					{
						continue;
					}
					WeatherPreset preset = WeatherPreset.Get<WeatherPreset>(cell.PresetID);
					if (preset == null)
					{
						continue;
					}
					if (profile != null)
					{
						weight *= profile.SuitabilityOf(preset);
						preset = profile.VariantOf(preset);
					}
					accumulator.Add(preset.Evaluate(), weight);
				}
			}

			WeatherFrame frame = accumulator.HasAny ? accumulator.Resolve() : WeatherFrame.Clear;
			frame.RetypeForTemperature(sample.Temperature);
			StormsInHeavyRain(ref frame, sample.Temperature);
			if (profile != null && profile.Forbidden != WeatherKindMask.None)
			{
				frame.Suppress(profile.Forbidden);
			}
			float shelter = 0f;
			WeatherVolumeRegistry.Apply(scene, position, ref frame, ref shelter);
			frame.DeriveSurfaceRates();
			sample.Frame = frame;
			sample.Shelter = shelter;
			return sample;
		}

		/// <summary>
		/// Heavy rain thunders. Wherever rain or hail is coming down hard, whatever brought it — the
		/// drifting field, a preset, a storm cell — there is a chance of lightning, rising with how
		/// hard it falls.
		/// </summary>
		/// <remarks>
		/// One rule on the resolved weather, instead of lightning being something only a preset with
		/// a lightning layer could have: a Heavy Rain preset had none, so the heaviest rain in the
		/// game was always silent. It replaced a scheme that looked for storm cells running into one
		/// another — every pair of cells tested wherever the weather was sampled — which cost far
		/// more than it gave. Rain and hail only: snow, ash and sand fall hard without thunder. The
		/// background has not been retyped for the temperature where it falls, so that is done on a
		/// copy here, or a blizzard's unretyped "rain" would thunder.
		/// </remarks>
		public static void StormsInHeavyRain(ref WeatherFrame frame, float temperature)
		{
			float falling = frame[WeatherChannel.Precipitation];
			if (falling <= HeavyRainStarts)
			{
				return;
			}
			WeatherFrame typed = frame;
			typed.RetypeForTemperature(temperature);
			float water = Mathf.Clamp01(typed[WeatherChannel.RainWeight] + typed[WeatherChannel.HailWeight]);
			float heavy = Mathf.Clamp01((falling - HeavyRainStarts) / (1f - HeavyRainStarts)) * water;
			frame[WeatherChannel.LightningRate] = Mathf.Max(frame[WeatherChannel.LightningRate], heavy * HeavyRainLightning);
		}

		/// <summary>How hard it has to be raining before it can thunder.</summary>
		public const float HeavyRainStarts = 0.5f;
		/// <summary>The lightning rate under the very heaviest rain. A rate of 1 is a strike about every two seconds.</summary>
		public const float HeavyRainLightning = 0.35f;

		/// <summary>The scene's weather mode once Auto is decided: dungeons get none, everything else its own.</summary>
		public static WeatherSceneMode ResolveMode(WorldSceneSettings settings, string sceneName)
		{
			WeatherSceneMode mode = settings != null ? settings.WeatherMode : WeatherSceneMode.Auto;
			if (mode != WeatherSceneMode.Auto)
			{
				return mode == WeatherSceneMode.Fixed && (settings == null || settings.FixedWeather == null) ? WeatherSceneMode.None : mode;
			}
			if (settings == null)
			{
				return WeatherSceneMode.None;
			}
			if (settings.Body != null && !settings.Body.HasWeather)
			{
				return WeatherSceneMode.None;
			}
			return DungeonTemplate.GetBySceneName(sceneName) != null ? WeatherSceneMode.None : WeatherSceneMode.Own;
		}
	}
}
