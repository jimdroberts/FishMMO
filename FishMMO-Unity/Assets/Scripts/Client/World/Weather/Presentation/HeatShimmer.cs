using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// How hard the air over the ground is boiling: the heat shimmer's physics, from the weather the client
	/// already has. Pure functions, so the thresholds are tested and not only looked at; the renderer feature
	/// (<see cref="FishHeatShimmerFeature"/>) and its shader turn the result into a refraction of the frame.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What shimmer is.</b> Sunlit dry ground heats the air touching it far faster than the air can carry
	/// the heat away, and the bottom metre or two goes superadiabatic: a skin of air twenty or thirty kelvin
	/// hotter than the air at head height, rising off the ground in plumes. Air's refractive index falls by
	/// about 1e-6 per kelvin, so those plumes are lenses: a ray that grazes the ground through them is bent
	/// a little this way and that as they rise past, and whatever lies behind them wobbles. The strength of the
	/// lensing is the refractive structure parameter Cn², which in a convective surface layer goes as the
	/// sensible heat flux to the 4/3 (free-convection similarity, Wyngaard 1971) and falls off with height as
	/// z^(-4/3) — which is why it hugs the ground and why it is a GRAZING effect.
	/// </para>
	/// <para>
	/// <b>Path-integrated.</b> The angle a ray is thrown through is a random walk over every eddy it passes,
	/// so its variance is the integral of Cn² along the ray. A ray to your feet crosses the layer in a couple
	/// of metres; a ray to the ground three hundred metres off runs along inside it the whole way. Variance
	/// grows with that length, the jitter with its square root — nothing at your feet, the far flats near the
	/// horizon boiling. The shader does the geometry per pixel; this class gives it the air.
	/// </para>
	/// <para>
	/// <b>The heat flux</b> (<see cref="SensibleHeat"/>) is the surface energy budget, kept to the terms the
	/// weather knows: the sun on the ground through the cloud, less what the ground reflects and radiates
	/// back, of which a dry surface hands most to the air as heat and a wet or green one most as evaporation
	/// (the Bowen ratio). Wind does not make more heat; it mixes the layer away
	/// (<see cref="Turbulence"/>), which is why the strongest shimmer is on a still, cloudless desert noon.
	/// </para>
	/// </remarks>
	public static class HeatShimmer
	{
		/// <summary>The sensible heat flux the strength is measured against, W/m²: a clear desert noon (400–600 measured).</summary>
		public const float FullHeatFlux = 400f;

		/// <summary>
		/// The wind speed at which mechanical mixing takes over from free convection, m/s. Below it the layer's
		/// structure is set by the heat alone; above it Cn² falls as the square of the wind (θ* = H / ρc_p u*,
		/// and Cn² ∝ θ*²), so a fresh breeze over the same hot ground shimmers a fraction as hard.
		/// </summary>
		public const float MixingWind = 3f;

		/// <summary>How much the refractive index of air falls per kelvin near sea level (−(n−1)/T at 300 K).</summary>
		public const float IndexPerKelvin = 1e-6f;

		/// <summary>ρ·c_p of near-surface air, J/(m³·K).</summary>
		public const float AirHeatCapacity = 1200f;

		/// <summary>
		/// The bulk transfer coefficient between the ground's skin and the air just over it. Small, because the
		/// skin's own molecular layer resists: what makes a sunlit road 25 K hotter than the air a metre up.
		/// </summary>
		public const float SkinTransfer = 0.008f;

		/// <summary>The slowest the air over the skin ever moves, m/s: the convective plumes themselves, in a dead calm.</summary>
		public const float ConvectiveFloor = 1.5f;

		/// <summary>
		/// The clear-sky sunlight on level ground, W/m², for a sun this many degrees up (Haurwitz 1945):
		/// 1098 · sin h · exp(−0.057 / sin h). About 1000 at a high sun, a third of that at 15°.
		/// </summary>
		public static float ClearSkySunlight(float sunAltitudeDegrees)
		{
			if (sunAltitudeDegrees <= 0f)
			{
				return 0f;
			}
			float s = Mathf.Sin(sunAltitudeDegrees * Mathf.Deg2Rad);
			return 1098f * s * Mathf.Exp(-0.057f / Mathf.Max(0.01f, s));
		}

		/// <summary>
		/// The share of clear-sky sunlight that comes through a cloud cover (Kasten &amp; Czeplak 1980):
		/// 1 − 0.75·N^3.4, N the cover in oktas over eight. Thin cloud (density low) counts for less of a cover.
		/// </summary>
		public static float CloudTransmission(float cloudCover, float cloudDensity)
		{
			float n = Mathf.Clamp01(cloudCover) * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(cloudDensity));
			return 1f - 0.75f * Mathf.Pow(n, 3.4f);
		}

		/// <summary>
		/// The sensible heat flux off the ground, 0..1 of <see cref="FullHeatFlux"/>: what heats the air
		/// touching it. Zero at night, when the ground cools below the air and the layer is stable.
		/// </summary>
		/// <param name="dryness">0 lush or damp ground (evaporation takes most of the sun), 1 bare dry sand or rock (<see cref="GroundDryness(BiomeTemplate)"/>).</param>
		/// <param name="wet">How wet the surface is now, 0..1 (the weather's cover): rain on hot ground goes to steam, not heat.</param>
		/// <param name="snow">How much snow lies, 0..1: snow reflects most of the sun and is pinned at its melting point.</param>
		public static float SensibleHeat(float sunAltitudeDegrees, float cloudCover, float cloudDensity, float dryness, float wet, float snow)
		{
			float sunlight = ClearSkySunlight(sunAltitudeDegrees) * CloudTransmission(cloudCover, cloudDensity);
			if (sunlight <= 0f)
			{
				return 0f;
			}
			dryness = Mathf.Clamp01(dryness);
			snow = Mathf.Clamp01(snow);
			// What the ground keeps of it: a fifth reflected by soil and plants, a third by pale sand, four fifths by snow.
			float albedo = Mathf.Lerp(Mathf.Lerp(0.2f, 0.3f, dryness), 0.8f, snow);
			// Less the net long-wave the ground loses upward, ~100 W/m² under a clear day sky, less under cloud.
			float longwave = 100f * (1f - 0.8f * Mathf.Clamp01(cloudCover));
			float net = (1f - albedo) * sunlight - longwave;
			if (net <= 0f)
			{
				return 0f;
			}
			// A fifth goes down into the ground; of the rest, a dry surface gives ~85 % to the air as heat and a
			// green one ~25 % (Bowen ratio 5 against 0.3). Water on it takes the rest as evaporation, snow melts with it.
			float bowenShare = Mathf.Lerp(0.25f, 0.85f, dryness) * (1f - 0.85f * Mathf.Clamp01(wet)) * (1f - snow);
			return Mathf.Clamp01(0.8f * net * bowenShare / FullHeatFlux);
		}

		/// <summary>
		/// The air's refractive turbulence near the ground, 0..1 (Cn² against a still desert noon's): the heat
		/// flux to the 4/3, mixed away by the wind (<see cref="MixingWind"/>).
		/// </summary>
		public static float Turbulence(float heat01, float windMetresPerSecond)
		{
			float heat = Mathf.Clamp01(heat01);
			if (heat <= 0f)
			{
				return 0f;
			}
			float u = Mathf.Max(0f, windMetresPerSecond) / MixingWind;
			return Mathf.Pow(heat, 4f / 3f) / (1f + u * u);
		}

		/// <summary>
		/// How much hotter the ground's skin is than the air a metre up, K: the heat flux over what the air
		/// can carry off it (H = ρc_p·C·U·ΔT). Some 28 K on a still desert noon, a few in a breeze.
		/// </summary>
		public static float SkinExcessKelvin(float heat01, float windMetresPerSecond)
		{
			float u = Mathf.Max(ConvectiveFloor, windMetresPerSecond);
			return Mathf.Clamp01(heat01) * FullHeatFlux / (AirHeatCapacity * SkinTransfer * u);
		}

		/// <summary>
		/// The grazing angle below which the hot layer turns a ray back up, rad: the inferior mirage, the sky's
		/// "water" on a hot road. A ray bent by a total index change Δn curls back from incidence √(2Δn) — about
		/// 0.4° over a desert, a few pixels on a screen, which is why a real mirage is a sliver at the horizon.
		/// </summary>
		public static float MirageAngle(float skinExcessKelvin)
		{
			return Mathf.Sqrt(2f * IndexPerKelvin * Mathf.Max(0f, skinExcessKelvin));
		}

		/// <summary>
		/// How bare and dry a biome's ground is, 0..1: 1 where its ground is loose (sand, dust, regolith — the
		/// wind lifts it) or its climate is desert-dry, 0 where it is wet enough to be green all over. From the
		/// climate the biome is chosen in, so a salt flat and a dune sea read alike without any authoring.
		/// </summary>
		public static float GroundDryness(bool looseGround, float minHumidity, float maxHumidity)
		{
			if (looseGround)
			{
				return 1f;
			}
			// The climate's humidity runs −1 (desert) .. 1 (rainforest); its middle for this biome.
			float middle = 0.5f * (Mathf.Clamp(minHumidity, -1f, 1f) + Mathf.Clamp(maxHumidity, -1f, 1f));
			return Mathf.Clamp01((0.3f - middle) / 1.0f);
		}

		/// <summary>The same for a biome; 0.4 (a temperate mix) when none is known.</summary>
		public static float GroundDryness(BiomeTemplate biome)
		{
			return biome == null ? 0.4f : GroundDryness(biome.LooseGround != null, biome.MinHumidity, biome.MaxHumidity);
		}

		/// <summary>
		/// The dryness the surface layer actually works with: the ground's, less when the air itself is humid —
		/// humid air means damp soil and transpiring plants, which spend the sun on evaporation.
		/// </summary>
		public static float EffectiveDryness(float groundDryness, float airHumidity)
		{
			return Mathf.Clamp01(groundDryness) * Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(airHumidity));
		}

		/// <summary>The shimmer's inputs for one frame, worked out once (<see cref="Current"/>).</summary>
		public struct State
		{
			/// <summary>Sensible heat flux 0..1 (<see cref="SensibleHeat"/>).</summary>
			public float Heat;
			/// <summary>Cn² against a still desert noon's, 0..1 (<see cref="Turbulence"/>).</summary>
			public float Turbulence;
			/// <summary>Skin over air, K (<see cref="SkinExcessKelvin"/>).</summary>
			public float SkinExcess;
			/// <summary>The wind near the ground, m/s.</summary>
			public float Wind;
			/// <summary>The wind's direction on the ground, world x, z.</summary>
			public Vector2 WindDirection;
		}

		private static int stateFrame = -1;
		private static State state;

		/// <summary>
		/// This frame's shimmer air, from the weather last presented: the sun from the sky, the cloud and wind
		/// shown, the biome, the air's humidity and the ground's cover under the viewer. All zero with no weather.
		/// </summary>
		public static State Current
		{
			get
			{
				if (stateFrame == Time.frameCount)
				{
					return state;
				}
				stateFrame = Time.frameCount;
				state = Compute();
				return state;
			}
		}

		private static State Compute()
		{
			WeatherPresentation presentation = WeatherPresentation.Instance;
			SkySystem sky = SkySystem.Instance;
			if (presentation == null || !presentation.HasContext || sky == null || sky.State == null)
			{
				return default;
			}
			WeatherContext context = presentation.Context;
			WeatherFrame frame = presentation.Shown;
			float wind = frame[WeatherChannel.WindSpeed] * 30f * (1f + 0.3f * frame[WeatherChannel.WindGust]);
			// Frozen ground (a surface below freezing) holds its sun in frost and thaw, not in the air.
			float frozen = context.Sample.Column.SurfaceKelvin > 0f && context.Sample.Column.SurfaceKelvin < 270f ? 0.5f : 1f;
			float dryness = EffectiveDryness(GroundDryness(context.Sample.Biome), context.Sample.Air.Humidity);
			float heat = frozen * SensibleHeat(sky.State.SunAltitude, frame[WeatherChannel.CloudCover], frame[WeatherChannel.CloudDensity],
				dryness, context.Cover.Wet, context.Cover.Snow);
			return new State
			{
				Heat = heat,
				Turbulence = Turbulence(heat, wind),
				SkinExcess = SkinExcessKelvin(heat, wind),
				Wind = wind,
				WindDirection = WeatherShaderGlobals.WindDirection(frame[WeatherChannel.WindHeading]),
			};
		}
	}
}
