namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// A scene's time of day. The whole scene shares one time: its body's rotation and sun at the
	/// scene's longitude, or the developer-authored fixed time.
	/// </summary>
	public static class SceneTime
	{
		/// <summary>0.5 is noon. Without a body or solar system, the home world's day at longitude 0.</summary>
		public static double LocalTime01(WorldSceneSettings settings, double worldHours)
		{
			if (settings != null && settings.TimeMode == SceneTimeMode.Fixed)
			{
				return settings.FixedTimeOfDay01;
			}
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = settings != null && settings.Body != null ? settings.Body : system != null ? system.HomeWorld : null;
			if (system == null || body == null)
			{
				double day = CelestialMath.HomeSolarDayHours(null);
				double t = worldHours / day;
				return t - System.Math.Floor(t);
			}
			return CelestialMath.LocalTime01(system, body, worldHours, settings != null ? settings.Longitude : 0.0);
		}

		public static bool IsDaylight(WorldSceneSettings settings, double worldHours)
		{
			if (settings != null && settings.TimeMode == SceneTimeMode.Fixed)
			{
				return settings.FixedTimeOfDay01 > 0.25f && settings.FixedTimeOfDay01 < 0.75f;
			}
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = settings != null && settings.Body != null ? settings.Body : system != null ? system.HomeWorld : null;
			if (system == null || body == null)
			{
				double t = LocalTime01(settings, worldHours);
				return t > 0.25 && t < 0.75;
			}
			return CelestialMath.IsDaylight(system, body, worldHours, settings != null ? settings.Latitude : 0.0, settings != null ? settings.Longitude : 0.0);
		}

		/// <summary>"HH:MM" for a day fraction.</summary>
		public static string Format(double time01)
		{
			int minutes = (int)System.Math.Floor((time01 - System.Math.Floor(time01)) * 1440.0) % 1440;
			return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
		}

		/// <summary>The body a scene sits on: its own, or the home world.</summary>
		public static WorldBody BodyOf(WorldSceneSettings settings)
		{
			if (settings != null && settings.Body != null)
			{
				return settings.Body;
			}
			SolarSystemProfile system = SolarSystemProfile.Active;
			return system != null ? system.HomeWorld : null;
		}
	}
}
