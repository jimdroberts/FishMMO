using System;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Everything a weather layer can say about the world. A frame is one value per channel,
	/// each 0..1 unless noted, and blending layers means combining these channels.
	/// </summary>
	public enum WeatherChannel : byte
	{
		CloudCover = 0,
		CloudDensity = 1,
		/// <summary>0 low … 1 high cloud base.</summary>
		CloudBase = 2,
		/// <summary>How much is falling.</summary>
		Precipitation = 3,
		/// <summary>0 fine … 1 large drops/flakes.</summary>
		DropSize = 4,
		// What is falling: a normalised mix.
		RainWeight = 5,
		SnowWeight = 6,
		HailWeight = 7,
		AshWeight = 8,
		SandWeight = 9,
		/// <summary>0 calm … 1 = 30 m/s.</summary>
		WindSpeed = 10,
		WindGust = 11,
		/// <summary>Degrees the wind blows toward, 0..360. Blended as a vector with <see cref="WindSpeed"/>.</summary>
		WindHeading = 12,
		/// <summary>How much fog, 0..1: the light its drops take out of the air, by <c>AirPhysics.FogExtinction</c>.</summary>
		FogDensity = 13,
		/// <summary>How deep the fog lies: its top above the ground, 0..1 of <c>FogLayer.ChannelMetres</c>.</summary>
		FogHeight = 14,
		/// <summary>
		/// How far the fog has lifted off the ground, 0 lying on it … 1 a sheet of stratus (<c>FogLayer.Lift</c>).
		/// The name is older than the physics: the whole fog is drawn as a volume, and this is where its base is.
		/// </summary>
		VolumetricFog = 15,
		/// <summary>
		/// 0 none … 1 = a strike about every eight seconds, which is a severe storm. See
		/// <c>SkySchedule.StrikesPerSecondAtFullRate</c>, which is where the number lives; this said
		/// "every couple of seconds" until that was deliberately calmed down, and was then wrong.
		/// </summary>
		LightningRate = 16,
		Aurora = 17,
		/// <summary>How wet exposed surfaces get. Derived from rain unless a layer says more.</summary>
		WetnessTarget = 18,
		SnowCoverRate = 19,
		AshCoverRate = 20,
		SandCoverRate = 21,
		/// <summary>−1..1, added to the scene's climate temperature.</summary>
		TemperatureOffset = 22,
		/// <summary>−1..1, added to the scene's climate humidity.</summary>
		HumidityOffset = 23,
	}

	/// <summary>How a channel combines when several layers write to it.</summary>
	public enum WeatherBlendRule : byte
	{
		/// <summary>The strongest wins. Two cloudy layers are not twice as cloudy.</summary>
		Max = 0,
		/// <summary><c>1 − Π(1 − x)</c>: layers strengthen each other but never pass 1.</summary>
		SoftSum = 1,
		/// <summary>Averaged by how much each layer is precipitating.</summary>
		PrecipitationWeighted = 2,
		/// <summary>Wind: summed as vectors with <see cref="WeatherChannel.WindSpeed"/>.</summary>
		Vector = 3,
		/// <summary>Summed and clamped to −1..1.</summary>
		Add = 4,
	}

	/// <summary>A set of kinds of weather, for what a volume shelters from or suppresses.</summary>
	[Flags]
	public enum WeatherKindMask : ushort
	{
		None = 0,
		Clouds = 1,
		Rain = 2,
		Snow = 4,
		Hail = 8,
		Ash = 16,
		Sand = 32,
		Wind = 64,
		Fog = 128,
		Lightning = 256,
		Aurora = 512,
		ClearSky = 1024,
		Precipitation = Rain | Snow | Hail | Ash | Sand,
		All = 0x3FF,
	}

	/// <summary>What mostly falls.</summary>
	public enum PrecipitationKind : byte
	{
		None = 0,
		Rain = 1,
		Sleet = 2,
		Snow = 3,
		Hail = 4,
		Ash = 5,
		Sand = 6,
	}

	/// <summary>How a scene gets its weather.</summary>
	public enum WeatherSceneMode : byte
	{
		/// <summary>None for dungeons (named by a DungeonTemplate), Own for everything else.</summary>
		Auto = 0,
		/// <summary>No weather at all.</summary>
		None = 1,
		/// <summary>The scene's own weather: the air over it, and the storms that air makes.</summary>
		Own = 2,
	}

	/// <summary>The channel table: counts, blend rules and helpers.</summary>
	public static class WeatherChannels
	{
		public const int Count = 24;

		private static readonly WeatherBlendRule[] rules = BuildRules();

		public static WeatherBlendRule RuleOf(WeatherChannel channel) => rules[(int)channel];

		public static bool IsPrecipitationType(WeatherChannel channel)
		{
			return channel >= WeatherChannel.RainWeight && channel <= WeatherChannel.SandWeight;
		}

		private static WeatherBlendRule[] BuildRules()
		{
			var r = new WeatherBlendRule[Count];
			for (int i = 0; i < Count; i++)
			{
				r[i] = WeatherBlendRule.Max;
			}
			r[(int)WeatherChannel.Precipitation] = WeatherBlendRule.SoftSum;
			r[(int)WeatherChannel.WindGust] = WeatherBlendRule.SoftSum;
			r[(int)WeatherChannel.WetnessTarget] = WeatherBlendRule.SoftSum;
			r[(int)WeatherChannel.SnowCoverRate] = WeatherBlendRule.SoftSum;
			r[(int)WeatherChannel.AshCoverRate] = WeatherBlendRule.SoftSum;
			r[(int)WeatherChannel.SandCoverRate] = WeatherBlendRule.SoftSum;
			r[(int)WeatherChannel.DropSize] = WeatherBlendRule.PrecipitationWeighted;
			for (int c = (int)WeatherChannel.RainWeight; c <= (int)WeatherChannel.SandWeight; c++)
			{
				r[c] = WeatherBlendRule.PrecipitationWeighted;
			}
			r[(int)WeatherChannel.WindSpeed] = WeatherBlendRule.Vector;
			r[(int)WeatherChannel.WindHeading] = WeatherBlendRule.Vector;
			r[(int)WeatherChannel.TemperatureOffset] = WeatherBlendRule.Add;
			r[(int)WeatherChannel.HumidityOffset] = WeatherBlendRule.Add;
			return r;
		}
	}
}
