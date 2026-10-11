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
		// One sample of the season serves every read of the same moment: a frame asks for one moment many times.
		private static WorldBody cachedBody;
		private static double cachedLatitude = double.NaN;
		private static double cachedHours = double.NaN;
		private static bool cachedSeasonal;
		private static float cachedTemperature, cachedHumidity;

		/// <summary>
		/// The runtime climate offsets of a scene at <paramref name="worldSeconds"/>, clamped as the settings hold them.
		/// </summary>
		/// <remarks>
		/// The body is the timeline's override when there is one (the bed stands a scene on any body, at its own
		/// latitude) and the scene's own otherwise, at the scene's latitude. A scene's climate already carries its own
		/// body's orbit mean, so only the season is added for it; a body it is not on contributes all of its offset.
		/// </remarks>
		public static void OffsetsAt(WorldSceneSettings settings, WeatherTimeline timeline, double worldSeconds,
			out float temperature, out float humidity)
		{
			float added = ((settings != null ? settings.AuthoredAir : default) + (timeline != null ? timeline.Air.AtSeconds(worldSeconds) : default)).TemperatureScale;
			WorldBody own = settings != null ? SceneTime.BodyOf(settings) : null;
			bool overridden = timeline != null && timeline.BodyOverride != null;
			WorldBody body = overridden ? timeline.BodyOverride : own;
			double latitude = overridden || settings == null ? (timeline != null ? timeline.LatitudeDegrees : 0.0) : settings.Latitude;
			Season(SolarSystemProfile.Active, body, body == own, worldSeconds / 3600.0, latitude, out float bodyTemperature, out float bodyHumidity);
			temperature = Mathf.Clamp(added + bodyTemperature, -2f, 2f);
			humidity = Mathf.Clamp(bodyHumidity, -2f, 2f);
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

		private static void Season(SolarSystemProfile system, WorldBody body, bool seasonal, double hours, double latitude,
			out float temperature, out float humidity)
		{
			temperature = 0f;
			humidity = 0f;
			if (system == null || body == null)
			{
				return;
			}
			if (ReferenceEquals(body, cachedBody) && seasonal == cachedSeasonal && hours == cachedHours && latitude == cachedLatitude)
			{
				temperature = cachedTemperature;
				humidity = cachedHumidity;
				return;
			}
			if (seasonal)
			{
				CelestialMath.SeasonalClimateOffsets(system, body, hours, latitude, out temperature, out humidity);
			}
			else
			{
				CelestialMath.ClimateOffsets(system, body, hours, out temperature, out humidity, latitude);
			}
			cachedBody = body;
			cachedSeasonal = seasonal;
			cachedHours = hours;
			cachedLatitude = latitude;
			cachedTemperature = temperature;
			cachedHumidity = humidity;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetCache()
		{
			cachedBody = null;
			cachedHours = double.NaN;
		}
	}
}
