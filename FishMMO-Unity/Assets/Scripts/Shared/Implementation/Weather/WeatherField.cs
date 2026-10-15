using System.Collections.Generic;
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
		/// <summary>The air's temperature on the climate scale, runtime offsets included: <see cref="Kelvin"/>, clamped to ±1.</summary>
		public float Temperature;
		/// <summary>
		/// The air's temperature, K, unclamped: the climate's, the air mass's and the hour's (<see cref="SurfaceTemperature"/>).
		/// What anything physical, and anything a player is told, reads.
		/// </summary>
		public float Kelvin;
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
		public static WeatherDriver.Synoptic OpenAirAt(WeatherTimeline timeline, WorldSceneSettings settings, Vector2 position, double worldSeconds, float season01, float localTime01,
			in WindBelts belts)
		{
			if (timeline != null && timeline.Driver)
			{
				// Through the scene's own drift: the wind it reports, with what was added, carries its weather along.
				AirDrift.At(DriftAir(timeline, settings, belts), worldSeconds, out double driftX, out double driftY);
				// Read where this scene stands on its world, so two scenes far apart have their own weather and two side by side share a front.
				PlanetOrigin(settings, out double originX, out double originY);
				driftX -= originX;
				driftY -= originY;
				return WeatherDriver.Sample(WeatherDriver.WorldSeed, position, worldSeconds, timeline.LatitudeDegrees, season01, localTime01, belts, driftX, driftY);
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
				Wind = WeatherDriver.PrevailingWind(latitude, belts) * AirDrift.StillWind,
			};
		}

		/// <summary>
		/// Where a scene stands on its world, in metres along its surface: east and north from the world's origin on the
		/// sphere the scene was cut from. The weather field is read at a scene position plus this.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>One world's weather, not one patch of it (2026-10-15).</b> The field was read at scene-local metres, so every
		/// scene on a world read the same patch of it — a scene at the pole and one on the equator had the same fronts at the
		/// same moment, offset only by how differently their winds had carried them. Now each reads its own place, on the
		/// atlas's sphere (the scale its scenes are cut at and players travel), so neighbours share a front crossing from one
		/// to the next and the far side of the world has its own.
		/// </para>
		/// <para>
		/// Folded into the drift (a feature at x0 is read at p − drift, so subtracting this reads p + origin), and the sky
		/// subtracts the same from the drift it hands the clouds: the cloud overhead and the weather under it stay one air.
		/// Zero for a scene placed nowhere.
		/// </para>
		/// </remarks>
		public static void PlanetOrigin(WorldSceneSettings settings, out double x, out double y)
		{
			x = 0.0;
			y = 0.0;
			FishMMO.Shared.Atlas.WorldAtlasScene entry = settings != null ? settings.AtlasEntry : null;
			if (entry == null || !entry.Placed)
			{
				return;
			}
			double radiusKm = entry.CutRadiusKm > 0f ? entry.CutRadiusKm
				: entry.Body != null ? entry.Body.AtlasRadiusKm : 30.0;
			double metres = radiusKm * 1000.0;
			double latitude = entry.Latitude * System.Math.PI / 180.0;
			double longitude = entry.Longitude * System.Math.PI / 180.0;
			x = longitude * System.Math.Cos(latitude) * metres;
			y = latitude * metres;
		}

		/// <summary>
		/// What carries a scene's air (<see cref="AirDrift"/>): its world, latitude and belts, whether its field runs, and
		/// the wind its author and its runtime entry add. The one place the sky, the weather and the server build it.
		/// </summary>
		public static AirDrift.Air DriftAir(WeatherTimeline timeline, WorldSceneSettings settings, in WindBelts belts)
		{
			return new AirDrift.Air
			{
				Seed = WeatherDriver.WorldSeed,
				Latitude = timeline != null ? timeline.LatitudeDegrees : 0f,
				Belts = belts,
				Driver = timeline == null || timeline.Driver,
				AuthoredWind = settings != null ? settings.AuthoredAir.Wind : 0f,
				Runtime = timeline != null ? timeline.Air : default,
			};
		}

		/// <summary>
		/// What carries a loaded scene's air at a moment (<see cref="DriftAir"/>), from the timeline and settings registered
		/// for it: what anything in a scene that the air moves (a windmill, the water's motes) reads.
		/// </summary>
		public static AirDrift.Air SceneAir(Scene scene, double worldSeconds)
		{
			WeatherQuery.TryGetTimeline(scene, out WeatherTimeline timeline);
			WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings settings);
			return DriftAir(timeline, settings, BeltsAt(timeline, settings, worldSeconds));
		}

		/// <summary>The belts a scene's air runs in: its world's (with the air's added gravity, as the sample takes it) at a moment.</summary>
		public static WindBelts BeltsAt(WeatherTimeline timeline, WorldSceneSettings settings, double worldSeconds)
		{
			WorldBody weatherBody = timeline != null && timeline.BodyOverride != null ? timeline.BodyOverride : SceneTime.BodyOf(settings);
			AirOffsets offsets = (settings != null ? settings.AuthoredAir : default) + (timeline != null ? timeline.Air.AtSeconds(worldSeconds) : default);
			return WindBelts.For(offsets.Apply(PlanetAir.For(SolarSystemProfile.Active, weatherBody)));
		}

		/// <summary>
		/// A new runtime entry for a scene, carrying on from the one it replaces (<see cref="AirOffsetEntry.Carried"/>): the
		/// clouds and the weather go on from where the old wind had taken them. What every setter of the scene's air uses.
		/// </summary>
		public static AirOffsetEntry NextAir(WeatherTimeline timeline, WorldSceneSettings settings, AirOffsets to, double startSeconds, double endSeconds)
		{
			AirOffsetEntry current = timeline != null ? timeline.Air : default;
			WindBelts belts = BeltsAt(timeline, settings, startSeconds);
			var next = new AirOffsetEntry
			{
				From = current.AtSeconds(startSeconds),
				To = to,
				StartSeconds = startSeconds,
				EndSeconds = endSeconds,
			};
			AirDrift.CarryOver(DriftAir(timeline, settings, belts), current, ref next);
			return next;
		}

		[System.ThreadStatic] private static List<VolcanicPlume.Plume> plumeScratch;

		/// <summary>
		/// The open air's wind at a place and tick, m/s, as <see cref="Sample"/> works it out there (with
		/// the scene's added air, but no volume): a pure function of the timeline, the place and the tick.
		/// Zero where the scene has no weather of its own or no air.
		/// </summary>
		/// <remarks>
		/// What a plume leans on where it stands, for drawing it: read at the viewer, as the sample is,
		/// every player saw the same plume leaning on their own wind.
		/// </remarks>
		public static Vector2 OpenWindAtSeconds(WeatherTimeline timeline, WorldSceneSettings settings, Vector2 position, double worldSeconds)
		{
			return OpenAirWithOffsets(timeline, settings, position, worldSeconds, out WeatherDriver.Synoptic air) ? air.Wind : Vector2.zero;
		}

		/// <summary>
		/// The open air at a place and moment with the scene's added air applied (<see cref="AirOffsets.Apply"/>) — its
		/// wind, pressure, instability and formations — as <see cref="Sample"/> works them out, with no storm and no
		/// volume: what something with memory of the weather (the sea) reads. False, and <paramref name="air"/> default,
		/// where the scene has no weather of its own or no air.
		/// </summary>
		public static bool OpenAirWithOffsets(WeatherTimeline timeline, WorldSceneSettings settings, Vector2 position, double worldSeconds, out WeatherDriver.Synoptic air)
		{
			air = default;
			if (timeline == null || timeline.SceneMode != WeatherSceneMode.Own)
			{
				return false;
			}
			WorldBody weatherBody = timeline.BodyOverride != null ? timeline.BodyOverride : SceneTime.BodyOf(settings);
			if (weatherBody != null && weatherBody.Atmosphere == FishMMO.Shared.Celestial.AtmosphereKind.None)
			{
				return false;
			}
			double worldHours = worldSeconds / 3600.0;
			SolarSystemProfile system = SolarSystemProfile.Active;
			float season01 = CelestialMath.Season01(system, weatherBody, worldHours);
			float localTime01 = system != null && weatherBody != null
				? (float)CelestialMath.LocalTime01(system, weatherBody, worldHours, timeline.LongitudeDegrees)
				: 0.5f;
			AirOffsets offsets = (settings != null ? settings.AuthoredAir : default) + timeline.Air.AtSeconds(worldSeconds);
			PlanetAir planet = offsets.Apply(PlanetAir.For(system, weatherBody));
			air = offsets.Apply(OpenAirAt(timeline, settings, position, worldSeconds, season01, localTime01, WindBelts.For(planet)));
			return true;
		}

		/// <summary>The weather at a place and tick: the world time at that tick (<see cref="WeatherTimeline.WorldSecondsAt(uint)"/>), sampled.</summary>
		public static WeatherSample Sample(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, Vector3 position, uint tick)
		{
			return SampleAtSeconds(timeline, settings, scene, position, timeline != null ? timeline.WorldSecondsAt(tick) : 0.0);
		}

		/// <summary>
		/// The weather at a place and a moment of world time. Pure in the timeline, the place and the
		/// moment, so a storm's own air read at its maturity (StormCellAir) is the same number on every
		/// machine whatever the world clock is doing now.
		/// </summary>
		public static WeatherSample SampleAtSeconds(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, Vector3 position, double worldSeconds)
		{
			if (!Open(timeline, settings, scene, position, worldSeconds, out WeatherSample sample, out OpenState state))
			{
				return sample;
			}
			return Resolve(timeline, settings, scene, position, worldSeconds, sample, state);
		}

		/// <summary>
		/// The open air at a place and a moment, with no storm in it: <see cref="WeatherSample.OpenAir"/>,
		/// <see cref="WeatherSample.OpenColumn"/>, <see cref="WeatherSample.Ground"/>, the planet and the temperature,
		/// and nothing that falls. The same numbers <see cref="SampleAtSeconds"/> gives for them, for less: what the
		/// storm schedule asks of every place it might start one (<see cref="StormSchedule"/>).
		/// </summary>
		public static WeatherSample OpenAtSeconds(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, Vector3 position, double worldSeconds)
		{
			Open(timeline, settings, scene, position, worldSeconds, out WeatherSample sample, out _);
			return sample;
		}

		/// <summary>What the open air's reading hands on to the rest of a full sample.</summary>
		private struct OpenState
		{
			public WorldBody Body;
			public FishMMO.Shared.Celestial.AtmosphereKind Atmosphere;
			public SolarSystemProfile System;
			public double Hours;
			public float Season01;
			public float Kelvin;
		}

		/// <summary>The open air (see <see cref="OpenAtSeconds"/>); false where the scene has no weather to read.</summary>
		private static bool Open(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, Vector3 position, double worldSeconds,
			out WeatherSample sample, out OpenState state)
		{
			state = default;
			sample = new WeatherSample { Frame = WeatherFrame.Clear, Background = WeatherFrame.Clear };
			/* The climate of THIS moment: its season and the air added to it then, not whatever this machine last
			 * wrote onto the settings (SceneClimate). A storm born, or a ground wetted, at a moment is then the same
			 * wherever and whenever that moment is worked out. */
			SceneClimate.OffsetsAt(settings, timeline, worldSeconds, out float runtimeTemperature, out float runtimeHumidity);
			BiomeReading reading = BiomeSampler.Read(position, settings, runtimeTemperature, runtimeHumidity);
			sample.Biome = reading.Biome;
			sample.Kelvin = reading.Climate.KelvinOrScale;
			sample.Temperature = ClimateModel.ToScale(sample.Kelvin);

			WeatherSceneMode mode = timeline != null ? timeline.SceneMode : WeatherSceneMode.None;
			// The body this weather is for: the scene's own, or the one a test bed stands the scene on.
			WorldBody weatherBody = timeline != null && timeline.BodyOverride != null ? timeline.BodyOverride : SceneTime.BodyOf(settings);
			// A giant's scenes have its habitable layer's air, not its whole depth's (CloudWorld).
			FishMMO.Shared.Celestial.AtmosphereKind atmosphere = FishMMO.Shared.Celestial.CloudWorld.AtmosphereOf(weatherBody);
			bool airless = atmosphere == FishMMO.Shared.Celestial.AtmosphereKind.None;

			// Where and when. The season and the hour are worked out here, on both sides, from the
			// clock anchor the server sent; sending them instead would freeze them at whatever they
			// were when the client joined.
			double worldHours = worldSeconds / 3600.0;
			SolarSystemProfile system = SolarSystemProfile.Active;
			float localTime01 = system != null && weatherBody != null && timeline != null
				? (float)CelestialMath.LocalTime01(system, weatherBody, worldHours, timeline.LongitudeDegrees)
				: 0.5f;
			float daylightShare = DaylightShare(system, weatherBody, worldHours, timeline != null ? timeline.LatitudeDegrees : 0f);
			// The latitude the place's own climate is read at: its true one in a scene cut from the globe.
			double climateLatitude = settings != null && (timeline == null || timeline.BodyOverride == null)
				? settings.LatitudeAt(position)
				: (timeline != null ? timeline.LatitudeDegrees : 0.0);

			if (timeline == null || mode != WeatherSceneMode.Own || airless)
			{
				// No weather here, but an airless world under the sky still has its days: scorching noons, frozen nights.
				if (airless && timeline != null && mode == WeatherSceneMode.Own)
				{
					sample.Kelvin += SurfaceTemperature.DayKelvin(atmosphere, reading.Climate.Continentality, 0f, localTime01, daylightShare,
						system != null && weatherBody != null ? CelestialMath.SolarDayHours(system, weatherBody) : 24.0);
					sample.Temperature = ClimateModel.ToScale(sample.Kelvin);
				}
				return false;
			}

			float season01 = CelestialMath.Season01(system, weatherBody, worldHours);

			// What is added to the air here: the scene's authored offsets, the runtime ones and any
			// volume's. Their temperature is already in the climate reading (it is the climate's); a
			// volume's is local, so it is added here.
			AirOffsets volume = WeatherVolumeRegistry.OffsetsAt(scene, position);
			AirOffsets offsets = (settings != null ? settings.AuthoredAir : default) + timeline.Air.AtSeconds(worldSeconds) + volume;
			sample.Offsets = offsets;

			PlanetAir planet = offsets.Apply(PlanetAir.For(system, weatherBody));
			sample.Planet = planet;
			WindBelts belts = WindBelts.For(planet);

			// The air over this place.
			WeatherDriver.Synoptic air = OpenAirAt(timeline, settings, new Vector2(position.x, position.z), worldSeconds, season01, localTime01, belts);
			// Over this place: its biome's and its world's humidity lean on the air, so a desert
			// stays mostly dry under a front that soaks the forest next door.
			air = WeatherDriver.OverPlace(air, reading.Climate.Humidity);
			// And how warm it is here decides how much water that air can carry at all.
			air = WeatherDriver.InClimate(air, reading.Climate.Temperature);
			// And how much of a day it is having: toward the poles the sun stops setting for part of
			// the year and stops rising for another, and the weather's daily rhythm is the sun's.
			if (daylightShare >= 0f)
			{
				air = WeatherDriver.UnderSun(air, daylightShare);
			}
			air = offsets.Apply(air);
			// The place's climate, the air mass the field has brought and the hour of the day, and a volume's own warmth.
			sample.Kelvin = SurfaceTemperature.AirKelvin(reading.Climate, air, system, weatherBody, climateLatitude, worldHours, localTime01, daylightShare)
				+ volume.Temperature;
			sample.Temperature = ClimateModel.ToScale(sample.Kelvin);
			sample.OpenAir = air;

			// Under water by the height the reading was taken on: planet-relative (water line fixed) in a scene cut from the
			// globe, the scene's own climate asset's otherwise.
			ScenePlacementClimate placement = settings != null ? settings.PlacementClimate : null;
			float waterLine = placement != null && placement.Generated ? ClimateModel.DefaultWaterSurfaceHeight
				: settings != null && settings.Climate != null ? settings.Climate.WaterSurfaceHeight : ClimateModel.DefaultWaterSurfaceHeight;
			bool underWater = reading.IsGrounded && settings != null && reading.Height < waterLine;
			GroundTraits ground = GroundTraits.Of(reading.IsGrounded ? reading.Biome : null, underWater);
			sample.Ground = ground;
			float kelvin = sample.Kelvin;
			// The weather this air makes with no storm over it.
			sample.OpenColumn = AirColumn.Of(planet, kelvin, air.Humidity, air.Pressure, air.Instability);
			state = new OpenState
			{
				Body = weatherBody,
				Atmosphere = atmosphere,
				System = system,
				Hours = worldHours,
				Season01 = season01,
				Kelvin = kelvin,
			};
			return true;
		}

		/// <summary>How much of the day the sun is up at a latitude today, 0 … 1; -1 when the body has no day to share.</summary>
		public static float DaylightShare(SolarSystemProfile system, WorldBody body, double worldHours, double latitude)
		{
			if (system == null || body == null)
			{
				return -1f;
			}
			double day = CelestialMath.SolarDayHours(system, body);
			if (double.IsInfinity(day) || day <= 1e-6)
			{
				return -1f;
			}
			return Mathf.Clamp01((float)(CelestialMath.DaylightHours(system, body, worldHours, latitude) / day));
		}

		/// <summary>The rest of a full sample, from its open air: what falls, the storms, the volumes.</summary>
		private static WeatherSample Resolve(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, Vector3 position, double worldSeconds,
			WeatherSample sample, in OpenState state)
		{
			WorldBody weatherBody = state.Body;
			FishMMO.Shared.Celestial.AtmosphereKind atmosphere = state.Atmosphere;
			SolarSystemProfile system = state.System;
			double worldHours = state.Hours;
			float season01 = state.Season01;
			float kelvin = state.Kelvin;
			PlanetAir planet = sample.Planet;
			GroundTraits ground = sample.Ground;
			WeatherDriver.Synoptic air = sample.OpenAir;

			// The plumes over the scene's vents and its eruptions, leaning on this wind: what falls out
			// of them here falls out of the plume the sky draws (VolcanicPlume, VolcanicVents).
			var position2 = new Vector2(position.x, position.z);
			List<VolcanicPlume.Plume> plumes = plumeScratch ??= new List<VolcanicPlume.Plume>();
			VolcanicVents.PlumesInTheirOwnWindAtSeconds(timeline, settings, worldSeconds, planet, position2, plumes);
			float steadyFallout = VolcanicVents.FalloutAt(plumes, position2, false, out WeatherSubstance steadySubstance);
			float fallout = VolcanicVents.FalloutAt(plumes, position2, true, out WeatherSubstance falloutSubstance);

			AirColumn open = sample.OpenColumn;
			sample.Background = WeatherPhysics.Frame(air, open, planet, ground, sample.Temperature, 1f, steadyFallout, steadySubstance, out _);
			AddAurora(ref sample.Background, timeline, system, weatherBody, worldSeconds, worldHours, season01);
			sample.Background = WeatherDriver.UnderAtmosphere(sample.Background, atmosphere);
			StormsInHeavyRain(ref sample.Background, sample.Temperature);

			// The storms: each does to the air under it what its kind of storm does.
			float emission = 1f;
			for (int i = 0; i < timeline.Cells.Count; i++)
			{
				StormCell cell = timeline.Cells[i];
				float weight = cell.InfluenceAtSeconds(position, worldSeconds);
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

			WeatherFrame frame = WeatherPhysics.Frame(air, column, planet, ground, sample.Temperature, emission, fallout, falloutSubstance, out sample.Substance);
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
