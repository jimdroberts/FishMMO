using FishMMO.Shared.Biomes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Weather
{
	/// <summary>What the weather is at one place and tick.</summary>
	public struct WeatherSample
	{
		public WeatherFrame Frame;
		public BiomeTemplate Biome;
		/// <summary>Local climate temperature, runtime offsets included.</summary>
		public float Temperature;
		/// <summary>0 in the open … 1 fully under cover.</summary>
		public float Shelter;

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
			if (mode == WeatherSceneMode.Own)
			{
				profile?.AccumulateBackground(ref accumulator);
			}
			timeline.AccumulateSceneLayers(tick, ref accumulator);
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
