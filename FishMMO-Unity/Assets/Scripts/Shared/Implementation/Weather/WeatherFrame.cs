using System;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// One value per <see cref="WeatherChannel"/>. A value type with named fields so sampling
	/// weather for every player every second allocates nothing.
	/// </summary>
	[Serializable]
	public struct WeatherFrame
	{
		private float c0, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11, c12, c13, c14, c15, c16, c17, c18, c19, c20, c21, c22, c23;

		public float this[WeatherChannel channel]
		{
			get => this[(int)channel];
			set => this[(int)channel] = value;
		}

		public float this[int index]
		{
			get
			{
				switch (index)
				{
					case 0: return c0; case 1: return c1; case 2: return c2; case 3: return c3;
					case 4: return c4; case 5: return c5; case 6: return c6; case 7: return c7;
					case 8: return c8; case 9: return c9; case 10: return c10; case 11: return c11;
					case 12: return c12; case 13: return c13; case 14: return c14; case 15: return c15;
					case 16: return c16; case 17: return c17; case 18: return c18; case 19: return c19;
					case 20: return c20; case 21: return c21; case 22: return c22; case 23: return c23;
					default: throw new ArgumentOutOfRangeException(nameof(index));
				}
			}
			set
			{
				switch (index)
				{
					case 0: c0 = value; break; case 1: c1 = value; break; case 2: c2 = value; break; case 3: c3 = value; break;
					case 4: c4 = value; break; case 5: c5 = value; break; case 6: c6 = value; break; case 7: c7 = value; break;
					case 8: c8 = value; break; case 9: c9 = value; break; case 10: c10 = value; break; case 11: c11 = value; break;
					case 12: c12 = value; break; case 13: c13 = value; break; case 14: c14 = value; break; case 15: c15 = value; break;
					case 16: c16 = value; break; case 17: c17 = value; break; case 18: c18 = value; break; case 19: c19 = value; break;
					case 20: c20 = value; break; case 21: c21 = value; break; case 22: c22 = value; break; case 23: c23 = value; break;
					default: throw new ArgumentOutOfRangeException(nameof(index));
				}
			}
		}

		/// <summary>A calm, clear sky.</summary>
		public static WeatherFrame Clear => default;

		/// <summary>What mostly falls, or None below a trace.</summary>
		public PrecipitationKind DominantPrecipitation
		{
			get
			{
				if (this[WeatherChannel.Precipitation] < 0.02f)
				{
					return PrecipitationKind.None;
				}
				float rain = this[WeatherChannel.RainWeight], snow = this[WeatherChannel.SnowWeight];
				float hail = this[WeatherChannel.HailWeight], ash = this[WeatherChannel.AshWeight], sand = this[WeatherChannel.SandWeight];
				float best = Mathf.Max(rain, Mathf.Max(snow, Mathf.Max(hail, Mathf.Max(ash, sand))));
				if (best <= 0f)
				{
					return PrecipitationKind.None;
				}
				if (rain > 0.2f && snow > 0.2f)
				{
					return PrecipitationKind.Sleet;
				}
				if (best == rain) return PrecipitationKind.Rain;
				if (best == snow) return PrecipitationKind.Snow;
				if (best == hail) return PrecipitationKind.Hail;
				if (best == ash) return PrecipitationKind.Ash;
				return PrecipitationKind.Sand;
			}
		}

		/// <summary>How much of a storm this is, 0..1: the worst of rain, wind, hail and lightning.</summary>
		public float StormSeverity
		{
			get
			{
				float precip = this[WeatherChannel.Precipitation];
				float hail = precip * this[WeatherChannel.HailWeight];
				float wind = Mathf.Max(this[WeatherChannel.WindSpeed], this[WeatherChannel.WindGust] * 0.8f);
				float sand = precip * this[WeatherChannel.SandWeight] * Mathf.Max(0.5f, wind);
				return Mathf.Clamp01(Mathf.Max(precip * 0.8f, Mathf.Max(wind * 0.9f, Mathf.Max(hail * 1.5f, Mathf.Max(this[WeatherChannel.LightningRate], sand)))));
			}
		}

		/// <summary>Wind as a horizontal vector in world space, 0..1 per axis.</summary>
		public Vector2 WindVector
		{
			get
			{
				float heading = this[WeatherChannel.WindHeading] * Mathf.Deg2Rad;
				float speed = this[WeatherChannel.WindSpeed];
				return new Vector2(Mathf.Sin(heading) * speed, Mathf.Cos(heading) * speed);
			}
		}

		/// <summary>
		/// Re-types rain and snow for the local temperature: the same storm falls as rain in a warm
		/// valley and as snow on a cold peak, with sleet between.
		/// </summary>
		public void RetypeForTemperature(float temperature)
		{
			float rain = this[WeatherChannel.RainWeight];
			float snow = this[WeatherChannel.SnowWeight];
			float water = rain + snow;
			if (water <= 0f)
			{
				return;
			}
			float frozen = Mathf.InverseLerp(0.05f, -0.15f, temperature);
			this[WeatherChannel.RainWeight] = water * (1f - frozen);
			this[WeatherChannel.SnowWeight] = water * frozen;
		}

		/// <summary>Zeroes the precipitation kinds in a mask and renormalises what remains.</summary>
		public void Suppress(WeatherKindMask mask)
		{
			if ((mask & WeatherKindMask.Rain) != 0) this[WeatherChannel.RainWeight] = 0f;
			if ((mask & WeatherKindMask.Snow) != 0) this[WeatherChannel.SnowWeight] = 0f;
			if ((mask & WeatherKindMask.Hail) != 0) this[WeatherChannel.HailWeight] = 0f;
			if ((mask & WeatherKindMask.Ash) != 0) this[WeatherChannel.AshWeight] = 0f;
			if ((mask & WeatherKindMask.Sand) != 0) this[WeatherChannel.SandWeight] = 0f;
			if ((mask & WeatherKindMask.Clouds) != 0) { this[WeatherChannel.CloudCover] = 0f; this[WeatherChannel.CloudDensity] = 0f; }
			if ((mask & WeatherKindMask.Wind) != 0) { this[WeatherChannel.WindSpeed] = 0f; this[WeatherChannel.WindGust] = 0f; }
			if ((mask & WeatherKindMask.Fog) != 0) { this[WeatherChannel.FogDensity] = 0f; this[WeatherChannel.VolumetricFog] = 0f; }
			if ((mask & WeatherKindMask.Lightning) != 0) this[WeatherChannel.LightningRate] = 0f;
			if ((mask & WeatherKindMask.Aurora) != 0) this[WeatherChannel.Aurora] = 0f;
			NormalizeTypes(keepPrecipitationWhenEmpty: false);
		}

		/// <summary>
		/// Makes the type weights sum to 1. With none left, precipitation stops (unless asked not to).
		/// </summary>
		public void NormalizeTypes(bool keepPrecipitationWhenEmpty)
		{
			float sum = 0f;
			for (int c = (int)WeatherChannel.RainWeight; c <= (int)WeatherChannel.SandWeight; c++)
			{
				sum += Mathf.Max(0f, this[c]);
			}
			if (sum <= 1e-6f)
			{
				if (!keepPrecipitationWhenEmpty)
				{
					this[WeatherChannel.Precipitation] = 0f;
				}
				return;
			}
			for (int c = (int)WeatherChannel.RainWeight; c <= (int)WeatherChannel.SandWeight; c++)
			{
				this[c] = Mathf.Max(0f, this[c]) / sum;
			}
		}

		/// <summary>
		/// Fills the surface channels from what is falling, so a template only has to author the
		/// sky. An authored value that is higher is kept.
		/// </summary>
		public void DeriveSurfaceRates()
		{
			float p = this[WeatherChannel.Precipitation];
			this[WeatherChannel.WetnessTarget] = Mathf.Max(this[WeatherChannel.WetnessTarget], p * (this[WeatherChannel.RainWeight] + this[WeatherChannel.HailWeight] * 0.5f));
			this[WeatherChannel.SnowCoverRate] = Mathf.Max(this[WeatherChannel.SnowCoverRate], p * this[WeatherChannel.SnowWeight]);
			this[WeatherChannel.AshCoverRate] = Mathf.Max(this[WeatherChannel.AshCoverRate], p * this[WeatherChannel.AshWeight]);
			this[WeatherChannel.SandCoverRate] = Mathf.Max(this[WeatherChannel.SandCoverRate], p * this[WeatherChannel.SandWeight]);
		}

		/// <summary>Moves every channel toward another frame by <paramref name="t"/>.</summary>
		public static WeatherFrame Lerp(in WeatherFrame a, in WeatherFrame b, float t)
		{
			var result = new WeatherFrame();
			for (int i = 0; i < WeatherChannels.Count; i++)
			{
				result[i] = i == (int)WeatherChannel.WindHeading
					? Mathf.LerpAngle(a[i], b[i], t)
					: Mathf.Lerp(a[i], b[i], t);
			}
			return result;
		}

		public override string ToString()
		{
			return $"precip {this[WeatherChannel.Precipitation]:0.00} ({DominantPrecipitation}) cloud {this[WeatherChannel.CloudCover]:0.00} " +
				$"wind {this[WeatherChannel.WindSpeed]:0.00}@{this[WeatherChannel.WindHeading]:0} fog {this[WeatherChannel.FogDensity]:0.00} " +
				$"storm {StormSeverity:0.00}";
		}
	}
}
