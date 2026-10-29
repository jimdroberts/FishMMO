using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// What the glow modes read of the sky and the weather (2026-10-25, I6i; <see cref="SceneGlowSky"/>), worked out once a frame
	/// from the sky's own celestial state and the scene's weather and handed to the shaders (<c>_FishGlowSkyA..D</c>,
	/// <c>_FishPlumeTide</c>, <c>_FishTidalStress</c>; FishGlowModes.hlsl) and to the site lights (<see cref="Current"/>).
	/// </summary>
	/// <remarks>
	/// <b>Shared by every player in a scene.</b> The sky's numbers are the celestial model at the world clock. The weather's are read at
	/// the scene's origin (not the viewer: two players apart see the same glow) on whole knots of the world clock and eased between
	/// them (<see cref="SceneGlowModes.KnotLerp"/>, <see cref="SceneGlowModes.KnotMean"/>): wind and fog a minute apart, the rain to
	/// come and the sea's state an hour apart, so each is the same whatever this machine sampled before. A knot once read is kept
	/// (the weather at a moment does not change), the cache dropped with the scene.
	/// </remarks>
	public static class GlowSky
	{
		private static readonly int AId = Shader.PropertyToID("_FishGlowSkyA");
		private static readonly int BId = Shader.PropertyToID("_FishGlowSkyB");
		private static readonly int CId = Shader.PropertyToID("_FishGlowSkyC");
		private static readonly int DId = Shader.PropertyToID("_FishGlowSkyD");
		private static readonly int PlumeTideId = Shader.PropertyToID("_FishPlumeTide");
		private static readonly int TidalStressId = Shader.PropertyToID("_FishTidalStress");
		private static readonly int VentBurstId = Shader.PropertyToID("_FishVentBurst");
		private static readonly int GroundClockId = Shader.PropertyToID("_FishGroundClock");

		/// <summary>The knots the weather is read on, seconds: wind and fog, and the rain to come and the sea.</summary>
		public const double FineKnotSeconds = 60.0, CoarseKnotSeconds = 3600.0;

		/// <summary>How far ahead the rain is read (decision 75), and how far back the sea's wind, seconds.</summary>
		public const double RainAheadSeconds = 6.0 * 3600.0, SeaBackSeconds = 3.0 * 3600.0;

		/// <summary>The wind that counts as full, m/s (the WindSpeed channel is 0..1 of 30 m/s).</summary>
		public const float FullWindChannel = 0.5f;

		/// <summary>The latest, for the site lights; calm before the first frame.</summary>
		public static SceneGlowSky Current { get; private set; } = SceneGlowSky.Calm;

		private static readonly Dictionary<long, (float wind, float fog)> fine = new Dictionary<long, (float, float)>();
		private static readonly Dictionary<long, (float wind, float rain)> coarse = new Dictionary<long, (float, float)>();
		private static int cachedScene = int.MinValue;
		private static WeatherContext context;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Current = SceneGlowSky.Calm;
			fine.Clear();
			coarse.Clear();
			cachedScene = int.MinValue;
		}

		/// <summary>Works out and publishes the frame's numbers (SkySystem, after the sun is placed).</summary>
		public static void Publish(CelestialState state, in WeatherContext weather, float sunHeight)
		{
			SceneGlowSky sky = SceneGlowSky.Calm;
			sky.SunHeight = sunHeight;
			if (state != null)
			{
				Sky(state, ref sky);
			}
			Weather(in weather, state != null ? state.Hours * 3600.0 : weather.WorldHours * 3600.0, ref sky);
			Current = sky;
			Shader.SetGlobalVector(AId, sky.A);
			Shader.SetGlobalVector(BId, sky.B);
			Shader.SetGlobalVector(CId, sky.C);
			Shader.SetGlobalVector(DId, sky.D);
			Shader.SetGlobalFloat(PlumeTideId, sky.PlumeTide);
			Shader.SetGlobalVector(TidalStressId, sky.TidalStress);
			Shader.SetGlobalFloat(VentBurstId, sky.VentBurst);
			// The clocks a clocked ground layer follows (2026-10-26, GROUND-BANDS; FishGroundClock.hlsl): the local year and hour.
			Shader.SetGlobalVector(GroundClockId, GroundClockOf(state));
			// The glow table's anchors as the scene's lights come and go (2026-10-25, I6i-b): a light and its stone on one biome's row.
			GlowModeTable.Refresh();
			// The burn mosaic's clock, wind and night (2026-10-25, I2t), at the same moment and the same prevailing way.
			BurnMosaicTable.Frame(in sky, state != null ? state.Hours * 3600.0 : weather.WorldHours * 3600.0);
		}

		/// <summary>
		/// The ground's clocks at the sky's moment (<see cref="FishMMO.Shared.Biomes.BiomeGroundClock.ShaderClock"/>): the local year from the
		/// body's season, turned in the south, and the local solar time; zero (no clock known) with no celestial state.
		/// </summary>
		public static Vector4 GroundClockOf(CelestialState state)
		{
			if (state == null)
			{
				return Vector4.zero;
			}
			bool year = state.System != null && state.Observer != null;
			float phase = year ? FishMMO.Shared.Biomes.BiomeGroundClock.LocalYear(CelestialMath.Season01(state.System, state.Observer, state.Hours), (float)state.Latitude) : 0f;
			return FishMMO.Shared.Biomes.BiomeGroundClock.ShaderClock(phase, year, (float)state.LocalTime01, true);
		}

		private static void Sky(CelestialState state, ref SceneGlowSky sky)
		{
			SolarSystemProfile system = state.System;
			WorldBody body = state.Observer;
			double hours = state.Hours;
			sky.LocalTime01 = (float)state.LocalTime01;
			if (system != null && body != null)
			{
				double solarDay = CelestialMath.SolarDayHours(system, body);
				double daylight = CelestialMath.DaylightHours(system, body, hours, state.Latitude);
				sky.NightShare = solarDay > 0.0 && !double.IsInfinity(solarDay) ? Mathf.Clamp01(1f - (float)(daylight / solarDay)) : 0.5f;
				sky.Day = SceneGlowModes.DayNumber(hours, solarDay, state.LocalTime01);
				sky.Wet = SceneGlowModes.WetOf(CelestialMath.Season01(system, body, hours), (float)state.Latitude);
			}
			bool isMoon = body != null && body.Parent != null && !(body.Parent is StarBody);
			CelestialBody host = isMoon ? CelestialMath.HostPlanet(body) : null;
			double orbit = isMoon ? CelestialMath.OrbitHours(system, body) : 0.0;
			sky.OrbitTurn = SceneGlowModes.TurnOf(hours, orbit, isMoon ? body.Orbit.OffsetDegrees : 0.0, SceneGlowModes.FallbackOrbitHours);
			sky.FieldTurn = SceneGlowModes.FieldTurnOf(hours, host is WorldBody giant ? CelestialMath.RotationHours(system, giant) : 0.0, orbit);
			sky.PlumeTide = SceneGlowModes.PlumeTideOf(sky.OrbitTurn);
			sky.MoonLight = state.Moon >= 0 && state.Moon < state.Bodies.Count ? Mathf.Clamp01(state.Bodies[state.Moon].Illumination) : 0f;
			sky.ParentLight = 0f;
			if (host != null)
			{
				foreach (SkyBodyState b in state.Bodies)
				{
					if (ReferenceEquals(b.Body, host))
					{
						sky.ParentLight = Mathf.Clamp01(b.Illumination);
						break;
					}
				}
			}
		}

		private static void Weather(in WeatherContext weather, double worldSeconds, ref SceneGlowSky sky)
		{
			SkySystem.AirAt(out float latitude, out WindBelts belts);
			Vector2 way = WeatherDriver.PrevailingWind(latitude, belts);
			way = way.sqrMagnitude > 1e-8f ? way.normalized : Vector2.right;
			sky.WayX = way.x;
			sky.WayZ = way.y;
			int scene = weather.Scene.IsValid() ? weather.Scene.handle : 0;
			sky.Turbidity = weather.Scene.IsValid() ? TurbidityEpisodes.Strength(TurbidityEpisodes.SeedOf(FishMMO.Shared.SceneNames.Of(weather.Scene)), worldSeconds) : 0f;
			// The scene's own vent (decision 95): on the motion clock, as the glows and the later vent effect read it.
			sky.VentBurst = weather.Scene.IsValid() ? VentBursts.At(WorldMotion.Wrapped(WorldMotion.ShaderWrapSeconds), VentBursts.SceneSeed(FishMMO.Shared.SceneNames.Of(weather.Scene))) : 0f;
			if (weather.Timeline == null || weather.Settings == null)
			{
				return;
			}
			if (scene != cachedScene)
			{
				cachedScene = scene;
				fine.Clear();
				coarse.Clear();
			}
			context = weather;
			sky.Wind = SceneGlowModes.KnotLerp(worldSeconds, FineKnotSeconds, k => Fine(k).wind);
			sky.Fog = SceneGlowModes.KnotLerp(worldSeconds, FineKnotSeconds, k => Fine(k).fog);
			sky.Rain = SceneGlowModes.KnotMean(worldSeconds, CoarseKnotSeconds, RainAheadSeconds, k => Coarse(k).rain);
			sky.Sea = SceneGlowModes.KnotMean(worldSeconds - SeaBackSeconds, CoarseKnotSeconds, SeaBackSeconds, k => Coarse(k).wind);
			Trim(fine, (long)System.Math.Floor(worldSeconds / FineKnotSeconds), 4);
			Trim(coarse, (long)System.Math.Floor(worldSeconds / CoarseKnotSeconds), 10);
		}

		private static (float wind, float fog) Fine(long knot)
		{
			if (!fine.TryGetValue(knot, out (float wind, float fog) v))
			{
				WeatherFrame f = Sample(knot * FineKnotSeconds);
				v = (Mathf.Clamp01(f[WeatherChannel.WindSpeed] / FullWindChannel), Mathf.Clamp01(f[WeatherChannel.FogDensity]));
				fine[knot] = v;
			}
			return v;
		}

		private static (float wind, float rain) Coarse(long knot)
		{
			if (!coarse.TryGetValue(knot, out (float wind, float rain) v))
			{
				WeatherFrame f = Sample(knot * CoarseKnotSeconds);
				v = (Mathf.Clamp01(f[WeatherChannel.WindSpeed] / FullWindChannel), Mathf.Clamp01(f[WeatherChannel.Precipitation]));
				coarse[knot] = v;
			}
			return v;
		}

		/// <summary>The weather at the scene's origin at a moment of world time (pure in the timeline and the moment).</summary>
		private static WeatherFrame Sample(double worldSeconds)
			=> WeatherField.SampleAtSeconds(context.Timeline, context.Settings, context.Scene, Vector3.zero, worldSeconds).Frame;

		private static readonly List<long> stale = new List<long>();

		/// <summary>Drops knots far behind or ahead of now, so the caches stay small.</summary>
		private static void Trim<T>(Dictionary<long, T> cache, long now, long keep)
		{
			if (cache.Count < 64)
			{
				return;
			}
			stale.Clear();
			foreach (long k in cache.Keys)
			{
				if (k < now - keep || k > now + keep)
				{
					stale.Add(k);
				}
			}
			foreach (long k in stale)
			{
				cache.Remove(k);
			}
		}
	}
}
