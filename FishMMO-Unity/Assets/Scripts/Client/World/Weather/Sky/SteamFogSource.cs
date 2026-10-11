using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// How much the open water round the camera smokes this moment — the sea and any lake or river apart —
	/// and how tall and how far downwind the steam stands, for the ground mist's march (FishMist.hlsl,
	/// <c>_FishMistSteam</c>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The water's temperature</b> is the climate's (<see cref="WaterTemperature"/>): the annual mean air
	/// at the water — the scene's own climate field at the sea's water line, or at the ground under a lake,
	/// so a mountain lake is the colder — with the season added damped and late. That is the simplest
	/// correct source: the weather's sample is the air of the moment (its air mass and its season), which
	/// is exactly what water does NOT follow, and the planet's climate field is what the scene's climate
	/// already evaluates, so reading it again directly would only duplicate that and miss a hand-made
	/// scene's authored climate. Read once a second: it moves over weeks.
	/// </para>
	/// <para>
	/// <b>The air</b> is the column over the camera: its temperature and dew point
	/// (<see cref="AirColumn.SurfaceKelvin"/>, <see cref="AirColumn.DewPointKelvin"/>). On a clear calm
	/// night the land's air chills by radiation and drains out over the water
	/// (<see cref="GroundMist.NightCalm"/>, <see cref="GroundMist.NightCooling"/>) — why river and lake steam
	/// is a dawn thing, and thickest just before sunrise. The open sea warms the air over it from below
	/// through the night, so it takes half of that.
	/// </para>
	/// <para>
	/// <b>Whether it smokes</b> is the mixing line (<see cref="SteamPhysics.SteamFogExcess"/>): no tuned
	/// threshold, the 8–10 K rule comes out of the saturation curve. A frozen lake does not smoke at all; a
	/// sea in pack ice smokes only from its leads (its open share).
	/// </para>
	/// </remarks>
	public sealed class SteamFogSource
	{
		private const float Kelvin = 273.15f;
		/// <summary>The real seconds between readings of the water's temperature.</summary>
		private const float WaterInterval = 1f;
		/// <summary>The deepest the steam is drawn, m: the mist march's ceiling (FishMist.hlsl FISH_MIST_DEEP).</summary>
		private const float DeepestDrawn = 40f;

		private static readonly int SteamId = Shader.PropertyToID("_FishMistSteam");

		private float nextWater = float.NegativeInfinity;

		/// <summary>Reads the water's temperature again on the next publish: the clock was set to another moment.</summary>
		public void ReadWaterSoon() => nextWater = float.NegativeInfinity;
		private int waterScene = int.MinValue;
		private WaterTemperature.Reading water;

		/// <summary>The water round the camera as last read.</summary>
		public WaterTemperature.Reading Water => water;
		/// <summary>How much the sea and the inland water smoke now, 0..1.</summary>
		public float SeaSteam { get; private set; }
		public float InlandSteam { get; private set; }
		/// <summary>How deep the steam stands, m.</summary>
		public float Depth { get; private set; }

		/// <summary>
		/// Works the steam out and publishes it; true when any water round the camera smokes. Off (and
		/// published as none) when <paramref name="draw"/> is false, with no weather, no open water, or a world
		/// whose air and water are not water's.
		/// </summary>
		public bool Publish(WeatherPresentation presentation, in AirColumn column, float windMetresPerSecond, float clearSky, float sunAltitudeDegrees,
			Vector3 viewer, bool draw)
		{
			if (!draw || presentation == null || !presentation.HasContext || !SurfaceWater.Present)
			{
				return Off();
			}
			WeatherContext context = presentation.Context;
			PlanetAir planet = context.Sample.Planet;
			if (!planet.HasAir || planet.Condensate != Condensate.Water)
			{
				return Off();
			}

			int scene = context.Scene.IsValid() ? context.Scene.handle : int.MinValue;
			if (Time.unscaledTime >= nextWater || scene != waterScene)
			{
				nextWater = Time.unscaledTime + WaterInterval;
				waterScene = scene;
				float seaLevel = SurfaceWater.TryGetLevel(out float level) ? level : 0f;
				water = WaterTemperature.At(context.Settings, planet, new Vector3(viewer.x, seaLevel, viewer.z), viewer, context.WorldHours);
			}

			float airC = column.SurfaceKelvin - Kelvin;
			float dewC = Mathf.Min(airC, column.DewPointKelvin - Kelvin);
			float chill = GroundMist.NightCooling * GroundMist.NightCalm(windMetresPerSecond, clearSky, sunAltitudeDegrees);
			float seaAirC = airC - 0.5f * chill;
			float inlandAirC = airC - chill;

			float seaExcess = water.SeaOpen > 0f ? SteamPhysics.SteamFogExcess(water.SeaC, seaAirC, Mathf.Min(dewC, seaAirC), true) : float.NegativeInfinity;
			float inlandExcess = water.InlandFrozen ? float.NegativeInfinity : SteamPhysics.SteamFogExcess(water.InlandC, inlandAirC, Mathf.Min(dewC, inlandAirC), false);
			SeaSteam = SteamPhysics.SteamFogReadiness(seaExcess, windMetresPerSecond) * water.SeaOpen;
			InlandSteam = SteamPhysics.SteamFogReadiness(inlandExcess, windMetresPerSecond);
			if (SeaSteam <= 0.001f && InlandSteam <= 0.001f)
			{
				return Off();
			}

			float seaDepth = SeaSteam > 0.001f ? SteamPhysics.SteamFogDepth(seaExcess, windMetresPerSecond) : 0f;
			float inlandDepth = InlandSteam > 0.001f ? SteamPhysics.SteamFogDepth(inlandExcess, windMetresPerSecond) : 0f;
			Depth = Mathf.Min(DeepestDrawn, Mathf.Max(seaDepth, inlandDepth));
			float difference = Mathf.Max(SeaSteam > 0.001f ? water.SeaC - seaAirC : 0f, InlandSteam > 0.001f ? water.InlandC - inlandAirC : 0f);
			float lean = SteamPhysics.SteamLean(windMetresPerSecond, difference);
			Shader.SetGlobalVector(SteamId, new Vector4(SeaSteam, InlandSteam, Depth, lean));
			return true;
		}

		private bool Off()
		{
			SeaSteam = 0f;
			InlandSteam = 0f;
			Depth = 0f;
			Shader.SetGlobalVector(SteamId, Vector4.zero);
			return false;
		}
	}
}
