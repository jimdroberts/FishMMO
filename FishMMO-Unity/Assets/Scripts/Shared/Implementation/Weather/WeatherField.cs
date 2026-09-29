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
		/// The weather here without the storm cells: what this air would do with no storm over it.
		/// The sky uses it for the far distance, where the weather map does not reach — a cell
		/// overhead must not raise the coverage of the whole sky.
		/// </summary>
		public WeatherFrame Background;
		public BiomeTemplate Biome;
		/// <summary>Local climate temperature, runtime offsets included.</summary>
		public float Temperature;
		/// <summary>0 in the open … 1 fully under cover.</summary>
		public float Shelter;

		/// <summary>
		/// What is falling here, or null when it is the kinds' own defaults — water rain, water
		/// snow, volcanic ash.
		/// </summary>
		/// <remarks>
		/// Worked out from the physics both peers share — what the world's clouds condense out of,
		/// what the ground lets loose or puts out — so it costs nothing on the wire. The client reads
		/// it for the colour, the sound and the cover it leaves; the server for what it melts at and
		/// whether it can be breathed.
		/// </remarks>
		public WeatherSubstance Substance;
		/// <summary>The air here: pressure, humidity, instability, wind — storms and offsets included.</summary>
		public WeatherDriver.Synoptic Air;
		/// <summary>The same air with no storm over it: what <see cref="Background"/> was worked out from.</summary>
		public WeatherDriver.Synoptic OpenAir;
		/// <summary>The air's vertical structure here: cloud base, freezing level, how far a cloud can grow.</summary>
		public AirColumn Column;
		/// <summary>The same with no storm over it: what a storm would form in.</summary>
		public AirColumn OpenColumn;
		/// <summary>The world's air this was worked out on, offsets to its pull included.</summary>
		public PlanetAir Planet;
		/// <summary>Everything added to the air here: the scene's authored offsets, the runtime ones and any volume's.</summary>
		public AirOffsets Offsets;
		/// <summary>What the ground here gives the air.</summary>
		public GroundTraits Ground;

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
	/// Samples a scene's weather at a position, from its air: the drifting field over this place and
	/// this world, what is added to it, and the storms it makes. A pure function of its inputs, run
	/// identically on server and client.
	/// </summary>
	public static class WeatherField
	{
		/// <summary>
		/// The air over a place before anything local is done to it: the drifting field, or — with the
		/// field switched off — a still, ordinary air, so that what is added to it is all there is.
		/// </summary>
		public static WeatherDriver.Synoptic OpenAirAt(WeatherTimeline timeline, Vector2 position, double worldSeconds, float season01, float localTime01, in WindBelts belts)
		{
			if (timeline != null && timeline.Driver)
			{
				return WeatherDriver.Sample(WeatherDriver.WorldSeed, position, worldSeconds, timeline.LatitudeDegrees, season01, localTime01, belts);
			}
			float latitude = timeline != null ? timeline.LatitudeDegrees : 0f;
			return new WeatherDriver.Synoptic
			{
				Humidity = 0.5f,
				Pressure = 0f,
				Temperature = 0f,
				Instability = 0.25f,
				ColumnType = WeatherDriver.BaseColumnType(0.25f),
				LocalTime01 = Mathf.Repeat(localTime01, 1f),
				Wind = WeatherDriver.PrevailingWind(latitude, belts) * 6f,
			};
		}

		public static WeatherSample Sample(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, Vector3 position, uint tick)
		{
			var sample = new WeatherSample { Frame = WeatherFrame.Clear, Background = WeatherFrame.Clear };
			BiomeReading reading = BiomeSampler.Read(position, settings);
			sample.Biome = reading.Biome;
			sample.Temperature = reading.Climate.Temperature;

			WeatherSceneMode mode = timeline != null ? timeline.SceneMode : WeatherSceneMode.None;
			// The body this weather is for: the scene's own, or the one a test bed stands the scene on.
			WorldBody weatherBody = timeline != null && timeline.BodyOverride != null ? timeline.BodyOverride : SceneTime.BodyOf(settings);
			FishMMO.Shared.Celestial.AtmosphereKind atmosphere = weatherBody != null ? weatherBody.Atmosphere : FishMMO.Shared.Celestial.AtmosphereKind.Standard;
			bool airless = atmosphere == FishMMO.Shared.Celestial.AtmosphereKind.None;
			if (timeline == null || mode != WeatherSceneMode.Own || airless)
			{
				return sample;
			}

			// Where and when. The season and the hour are worked out here, on both sides, from the
			// clock anchor the server sent; sending them instead would freeze them at whatever they
			// were when the client joined.
			double worldSeconds = timeline.WorldSecondsAt(tick);
			double worldHours = worldSeconds / 3600.0;
			SolarSystemProfile system = SolarSystemProfile.Active;
			float season01 = CelestialMath.Season01(system, weatherBody, worldHours);
			float localTime01 = system != null && weatherBody != null
				? (float)CelestialMath.LocalTime01(system, weatherBody, worldHours, timeline.LongitudeDegrees)
				: 0.5f;

			// What is added to the air here: the scene's authored offsets, the runtime ones and any
			// volume's. Their temperature is already in the climate reading (it is the climate's); a
			// volume's is local, so it is added here.
			AirOffsets volume = WeatherVolumeRegistry.OffsetsAt(scene, position);
			AirOffsets offsets = (settings != null ? settings.AuthoredAir : default) + timeline.AirAt(tick) + volume;
			sample.Offsets = offsets;
			sample.Temperature = Mathf.Clamp(sample.Temperature + volume.TemperatureScale, -1f, 1f);

			PlanetAir planet = offsets.Apply(PlanetAir.For(system, weatherBody));
			sample.Planet = planet;
			WindBelts belts = WindBelts.For(planet);

			// The air over this place.
			WeatherDriver.Synoptic air = OpenAirAt(timeline, new Vector2(position.x, position.z), worldSeconds, season01, localTime01, belts);
			// Over this place: its biome's and its world's humidity lean on the air, so a desert
			// stays mostly dry under a front that soaks the forest next door.
			air = WeatherDriver.OverPlace(air, reading.Climate.Humidity);
			// And how warm it is here decides how much water that air can carry at all.
			air = WeatherDriver.InClimate(air, reading.Climate.Temperature);
			// And how much of a day it is having: toward the poles the sun stops setting for part of
			// the year and stops rising for another, and the weather's daily rhythm is the sun's.
			if (system != null && weatherBody != null)
			{
				double day = CelestialMath.SolarDayHours(system, weatherBody);
				if (!double.IsInfinity(day) && day > 1e-6)
				{
					air = WeatherDriver.UnderSun(air, (float)(CelestialMath.DaylightHours(system, weatherBody, worldHours, timeline.LatitudeDegrees) / day));
				}
			}
			air = offsets.Apply(air);
			// The field carries its own air mass's warmth on top of the place's climate.
			sample.Temperature = Mathf.Clamp(sample.Temperature + air.Temperature * 0.35f, -1f, 1f);
			sample.OpenAir = air;

			bool underWater = reading.IsGrounded && settings != null && settings.Climate != null && reading.Height < settings.Climate.WaterSurfaceHeight;
			GroundTraits ground = GroundTraits.Of(reading.IsGrounded ? reading.Biome : null, underWater);
			sample.Ground = ground;
			float kelvin = planet.SurfaceKelvin(sample.Temperature);

			// The weather this air makes with no storm over it.
			AirColumn open = AirColumn.Of(planet, kelvin, air.Humidity, air.Pressure, air.Instability);
			sample.OpenColumn = open;
			sample.Background = WeatherPhysics.Frame(air, open, planet, ground, sample.Temperature, 1f, out _);
			AddAurora(ref sample.Background, timeline, system, weatherBody, worldSeconds, worldHours, season01);
			sample.Background = WeatherDriver.UnderAtmosphere(sample.Background, atmosphere);
			StormsInHeavyRain(ref sample.Background, sample.Temperature);

			// The storms: each does to the air under it what its kind of storm does.
			float emission = 1f;
			for (int i = 0; i < timeline.Cells.Count; i++)
			{
				StormCell cell = timeline.Cells[i];
				float weight = cell.InfluenceAt(position, tick, timeline.TickDelta);
				if (weight <= 0f)
				{
					continue;
				}
				air = StormPhysics.Perturb(air, cell.Kind, weight);
				emission = Mathf.Max(emission, StormPhysics.EmissionBoost(cell.Kind, weight));
			}
			sample.Air = air;
			AirColumn column = AirColumn.Of(planet, kelvin, air.Humidity, air.Pressure, air.Instability);
			sample.Column = column;

			WeatherFrame frame = WeatherPhysics.Frame(air, column, planet, ground, sample.Temperature, emission, out sample.Substance);
			AddAurora(ref frame, timeline, system, weatherBody, worldSeconds, worldHours, season01);
			frame = WeatherDriver.UnderAtmosphere(frame, atmosphere);
			StormsInHeavyRain(ref frame, sample.Temperature);
			float shelter = 0f;
			WeatherVolumeRegistry.Apply(scene, position, ref frame, ref shelter);
			frame.DeriveSurfaceRates();
			sample.Frame = frame;
			sample.Shelter = shelter;
			return sample;
		}

		/// <summary>
		/// The aurora is weather too, of a kind: the star's doing, the body's field's and its air's, at
		/// this latitude. How much of the star's output reaches this body stands in for how hard its
		/// wind blows here.
		/// </summary>
		private static void AddAurora(ref WeatherFrame frame, WeatherTimeline timeline, SolarSystemProfile system, WorldBody body, double worldSeconds, double worldHours, float season01)
		{
			if (system == null || body == null)
			{
				return;
			}
			float wind = (float)(CelestialMath.Insolation(system, body, worldHours) / System.Math.Max(1e-6, CelestialMath.MeanHomeInsolation(system)));
			// At the MAGNETIC latitude: the ring is round the magnetic pole, which stands off the
			// turning pole by the body's dipole tilt.
			frame[WeatherChannel.Aurora] = WeatherDriver.Aurora(WeatherDriver.WorldSeed, worldSeconds,
				body.MagneticLatitude(timeline.LatitudeDegrees, timeline.LongitudeDegrees), season01, body.MagneticField, wind);
		}

		/// <summary>
		/// Heavy rain thunders. Wherever rain or hail is coming down hard, whatever brought it — the
		/// drifting field, what was added to the air, a storm cell — there is a chance of lightning,
		/// rising with how hard it falls.
		/// </summary>
		/// <remarks>
		/// One rule on the resolved weather, so heavy rain is never silent whatever brought it. Rain
		/// and hail only: snow, ash and sand fall hard without thunder. It is typed for the
		/// temperature on a copy here, so a blizzard's untyped "rain" does not thunder.
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
				return mode;
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
