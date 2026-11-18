namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The weather at a place and tick, as gameplay reads it: the frame of channels, the air, the temperature and the
	/// shelter. Core vocabulary (Phase 4 of the core and modules plan, 2026-11-17).
	/// </summary>
	/// <remarks>
	/// Predicted exposure, buff volumes, AI shelter and spawn conditions read this through <see cref="WeatherQuery"/>; the
	/// Climate and Weather module produces it (its full <c>WeatherSample</c> carries the simulation's own types too). With
	/// no weather installed every reading is <see cref="Calm"/>: nothing falls, no wind, a mild temperature, open air.
	/// </remarks>
	public struct WeatherReading
	{
		/// <summary>The weather's channels here.</summary>
		public WeatherFrame Frame;

		/// <summary>The biome's background weather here, before any storm.</summary>
		public WeatherFrame Background;

		/// <summary>What the air is doing here.</summary>
		public WeatherAir Air;

		/// <summary>The temperature, −1 (freezing) to 1 (scorching), 0 mild.</summary>
		public float Temperature;

		/// <summary>The temperature in kelvin.</summary>
		public float Kelvin;

		/// <summary>0 open sky, 1 fully sheltered.</summary>
		public float Shelter;

		/// <summary>The weather where nothing provides any: calm, mild, open.</summary>
		public static WeatherReading Calm => new WeatherReading { Kelvin = 288.15f };

		/// <summary>What is falling, the frame's dominant precipitation.</summary>
		public PrecipitationKind Precipitation => Frame.DominantPrecipitation;

		/// <summary>The frame's storm severity.</summary>
		public float StormSeverity => Frame.StormSeverity;

		/// <summary>Whether the place is at least half sheltered.</summary>
		public bool IsSheltered => Shelter >= 0.5f;

		/// <summary>1 − shelter: how much of the weather reaches here.</summary>
		public float Exposure => 1f - Shelter;

		/// <summary>Whether any of <paramref name="kinds"/> is happening here, by the frame's channels.</summary>
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
}
