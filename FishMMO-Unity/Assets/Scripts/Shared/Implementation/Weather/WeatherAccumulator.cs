using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Combines any number of weighted layer frames by each channel's <see cref="WeatherBlendRule"/>.
	/// </summary>
	/// <remarks>
	/// The weight is how much of a layer applies here — a storm cell's falloff, a transition's
	/// progress — and scales the layer's values before they are combined. Order does not matter.
	/// </remarks>
	public struct WeatherAccumulator
	{
		private WeatherFrame max;
		private WeatherFrame keep;        // Π(1 − x) for SoftSum channels, stored as (1 − Π) so default(=0) means "nothing yet"
		private WeatherFrame weighted;    // Σ value × precipitation weight
		private WeatherFrame added;
		private float precipitationWeight;
		private float windX, windZ;
		private bool any;

		/// <summary>True once anything has been added.</summary>
		public bool HasAny => any;

		public void Add(in WeatherFrame layer, float weight)
		{
			weight = Mathf.Clamp01(weight);
			if (weight <= 0f)
			{
				return;
			}
			any = true;
			float precipitation = Mathf.Clamp01(layer[WeatherChannel.Precipitation] * weight);
			for (int i = 0; i < WeatherChannels.Count; i++)
			{
				float value = layer[i];
				switch (WeatherChannels.RuleOf((WeatherChannel)i))
				{
					case WeatherBlendRule.Max:
						max[i] = Mathf.Max(max[i], value * weight);
						break;
					case WeatherBlendRule.SoftSum:
						// keep holds 1 − Π(1 − x); fold one more factor in.
						keep[i] = 1f - (1f - keep[i]) * (1f - Mathf.Clamp01(value * weight));
						break;
					case WeatherBlendRule.PrecipitationWeighted:
						weighted[i] += value * precipitation;
						break;
					case WeatherBlendRule.Add:
						added[i] += value * weight;
						break;
				}
			}
			precipitationWeight += precipitation;
			float heading = layer[WeatherChannel.WindHeading] * Mathf.Deg2Rad;
			float speed = layer[WeatherChannel.WindSpeed] * weight;
			windX += Mathf.Sin(heading) * speed;
			windZ += Mathf.Cos(heading) * speed;
		}

		/// <summary>The combined frame. Type weights are normalised; surface rates are derived.</summary>
		public WeatherFrame Resolve()
		{
			var result = new WeatherFrame();
			for (int i = 0; i < WeatherChannels.Count; i++)
			{
				switch (WeatherChannels.RuleOf((WeatherChannel)i))
				{
					case WeatherBlendRule.Max:
						result[i] = max[i];
						break;
					case WeatherBlendRule.SoftSum:
						result[i] = Mathf.Clamp01(keep[i]);
						break;
					case WeatherBlendRule.PrecipitationWeighted:
						result[i] = precipitationWeight > 1e-6f ? weighted[i] / precipitationWeight : 0f;
						break;
					case WeatherBlendRule.Add:
						result[i] = Mathf.Clamp(added[i], -1f, 1f);
						break;
				}
			}
			result[WeatherChannel.WindSpeed] = Mathf.Clamp01(Mathf.Sqrt(windX * windX + windZ * windZ));
			float heading = Mathf.Atan2(windX, windZ) * Mathf.Rad2Deg;
			result[WeatherChannel.WindHeading] = heading < 0f ? heading + 360f : heading;
			result.NormalizeTypes(keepPrecipitationWhenEmpty: false);
			result.DeriveSurfaceRates();
			return result;
		}
	}
}
