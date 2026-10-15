using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// What a scene's climate has added to it at a moment of world time: the warmth of the air authored on it and added
	/// at runtime, and its body's season. One sum, read by the server, the client and the test bed alike.
	/// </summary>
	/// <remarks>
	/// The weather read these from <see cref="WorldSceneSettings.RuntimeTemperatureOffset"/> and
	/// <see cref="WorldSceneSettings.RuntimeHumidityOffset"/>, which hold "now" as each machine last wrote it (the
	/// server about once a second, a client once a second, the bed every frame). A sample of an earlier moment then
	/// carried the season and the admin's air of the moment it was TAKEN, not of the moment it was OF, and two
	/// machines sampling the same moment at different times disagreed. Everything decided from the weather of a moment
	/// (where a storm is born, what the ground holds) has to be the same wherever and whenever it is worked out, so the
	/// field reads this, of the moment itself (<see cref="WeatherField.SampleAtSeconds"/>).
	/// </remarks>
	public static class SceneClimate
	{
		// One reading of the season serves every read of the same moment: a frame asks for one moment many times.
		private static WorldBody cachedBody, cachedOwn;
		private static double cachedLatitude = double.NaN;
		private static double cachedHours = double.NaN;
		private static float cachedContinentality = float.NaN;
		private static float cachedTemperature, cachedHumidity;

		/// <summary>
		/// The runtime climate offsets of a scene at <paramref name="worldSeconds"/>, on the climate scale: the air authored
		/// on it and added at runtime, and its season.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The season is the body's climate table's (<see cref="PlanetClimateTable"/>) at the scene's true latitude
		/// (<see cref="WorldSceneSettings.ClimateLatitude"/>, never the sun path's override) and how continental its centre
		/// is: the moment against the year's mean there, lag and all. A scene's climate already carries the year's mean.
		/// </para>
		/// <para>
		/// The body is the timeline's override when there is one (the bed stands a scene on any body, at its own latitude):
		/// then the offset is that body's moment less the scene's own year there, so the scene reads the other world.
		/// </para>
		/// </remarks>
		public static void OffsetsAt(WorldSceneSettings settings, WeatherTimeline timeline, double worldSeconds,
			out float temperature, out float humidity)
		{
			float added = ((settings != null ? settings.AuthoredAir : default) + (timeline != null ? timeline.Air.AtSeconds(worldSeconds) : default)).TemperatureScale;
			BodyShareAt(settings, timeline, worldSeconds, out float bodyTemperature, out float bodyHumidity);
			temperature = Mathf.Clamp(added + bodyTemperature, -2f, 2f);
			humidity = Mathf.Clamp(bodyHumidity, -2f, 2f);
		}

		/// <summary>
		/// The body's share of <see cref="OffsetsAt"/>, on the climate scale: the season on the scene's own body, or another
		/// body's whole difference when the timeline stands the scene on it.
		/// </summary>
		public static void BodyShareAt(WorldSceneSettings settings, WeatherTimeline timeline, double worldSeconds,
			out float temperature, out float humidity)
		{
			WorldBody own = SceneTime.BodyOf(settings);
			bool overridden = timeline != null && timeline.BodyOverride != null;
			WorldBody body = overridden ? timeline.BodyOverride : own;
			double latitude = overridden || settings == null ? (timeline != null ? timeline.LatitudeDegrees : 0.0) : settings.ClimateLatitude;
			ScenePlacementClimate placement = settings != null ? settings.PlacementClimate : null;
			float continentality = placement != null ? placement.CentreContinentality : 0.5f;
			Season(SolarSystemProfile.Active, body, own, worldSeconds / 3600.0, latitude, continentality, out temperature, out humidity);
		}

		/// <summary>Writes the offsets of <paramref name="worldSeconds"/> onto the settings, for what reads them from there.</summary>
		public static void Apply(WorldSceneSettings settings, WeatherTimeline timeline, double worldSeconds)
		{
			if (settings == null)
			{
				return;
			}
			OffsetsAt(settings, timeline, worldSeconds, out float temperature, out float humidity);
			settings.RuntimeTemperatureOffset = temperature;
			settings.RuntimeHumidityOffset = humidity;
		}

		/// <summary>The season at a latitude on a body, on the climate scale, and the body's difference from the scene's own when it is another.</summary>
		private static void Season(SolarSystemProfile system, WorldBody body, WorldBody own, double hours, double latitude, float continentality,
			out float temperature, out float humidity)
		{
			temperature = 0f;
			humidity = 0f;
			if (system == null || body == null)
			{
				return;
			}
			if (ReferenceEquals(body, cachedBody) && ReferenceEquals(own, cachedOwn) && hours == cachedHours && latitude == cachedLatitude
				&& continentality == cachedContinentality)
			{
				temperature = cachedTemperature;
				humidity = cachedHumidity;
				return;
			}
			PlanetClimateTable table = PlanetClimateTable.For(system, body);
			if (table == null)
			{
				return;
			}
			float kelvin = table.SeasonKelvin(latitude, hours, continentality);
			float waterShift = 0f;
			if (!ReferenceEquals(body, own))
			{
				// Another world under the scene: its year here less the scene's own (the scene's climate already holds that).
				PlanetClimateTable ownTable = own != null ? PlanetClimateTable.For(system, own) : null;
				float ownAnnual = ownTable != null ? ownTable.AnnualKelvin(latitude, continentality) : (float)ClimateModel.ToKelvin(0f);
				kelvin += table.AnnualKelvin(latitude, continentality) - ownAnnual;
				float ownWater = own != null ? CloudWorld.WaterOf(own) : 0.7f;
				waterShift = (CloudWorld.WaterOf(body) - ownWater) * 1.2f;
			}
			temperature = kelvin / (float)ClimateModel.KelvinPerUnit;
			// Warm air holds more, so the same vapour reads drier in summer: the season's share of the humidity.
			humidity = Mathf.Clamp(waterShift - 0.3f * temperature, -2f, 2f);
			cachedBody = body;
			cachedOwn = own;
			cachedHours = hours;
			cachedLatitude = latitude;
			cachedContinentality = continentality;
			cachedTemperature = temperature;
			cachedHumidity = humidity;
		}

		/// <summary>
		/// What foliage shows at a scene now, from its climate rather than the calendar: how far through its thermal year it
		/// is (0 the coldest point, 0.5 the warmest), how warm it is for growing (−1 dormant … 1 growing), and how hard its
		/// winter is (0 none … 1 a winter that strips the trees).
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>From the temperature, not the axial tilt (2026-10-15).</b> The strength was |latitude| ÷ 23.5° — Earth's tilt —
		/// on every body, so a scene at 47° on a 9° world dropped its leaves like Moscow while its winter barely froze, and a
		/// mild coast went as bare as a continent's heart. Plants go dormant below about 5 °C and deciduous trees exist where
		/// winters are cold enough to need to; both are read straight off the climate table here.
		/// </para>
		/// <para>The phase follows the temperature, a month or two behind the sun, as the leaves do.</para>
		/// </remarks>
		public static bool Foliage(WorldSceneSettings settings, double worldHours, out float phase01, out float growing, out float winter)
		{
			phase01 = 0f;
			growing = 0f;
			winter = 0f;
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = SceneTime.BodyOf(settings);
			PlanetClimateTable table = system != null && body != null ? PlanetClimateTable.For(system, body) : null;
			if (table == null || settings == null)
			{
				return false;
			}
			double latitude = settings.ClimateLatitude;
			ScenePlacementClimate placement = settings.PlacementClimate;
			float continentality = placement != null ? placement.CentreContinentality : 0.5f;
			// The place's own year: the scene's centre on the ground, mean and season.
			float annual = settings.SampleClimateAt(Vector3.zero, 0.5f, out _, 0f, 0f).KelvinOrScale;
			float now = annual + table.SeasonKelvin(latitude, worldHours, continentality);
			table.Extremes(latitude, continentality, out float warmest, out float coldest);
			float shift = annual - table.AnnualKelvin(latitude, continentality);
			float coldestC = coldest + shift - 273.15f;
			const float GrowthC = 5f;
			growing = Mathf.Clamp((now - 273.15f - GrowthC) / 10f, -1f, 1f);
			winter = Mathf.Clamp01((GrowthC - coldestC) / 10f);
			phase01 = Mathf.Repeat((table.PhaseAt(worldHours) - table.ColdestPhase(latitude, continentality)) / PlanetClimateTable.Phases, 1f);
			return true;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetCache()
		{
			cachedBody = null;
			cachedOwn = null;
			cachedHours = double.NaN;
		}
	}
}
