using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>How one channel responds to a layer's intensity.</summary>
	[Serializable]
	public class WeatherChannelCurve
	{
		public WeatherChannel Channel;
		[Tooltip("X: layer intensity 0..1. Y: the channel's value. Empty is a straight line from 0 to 1.")]
		public AnimationCurve Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
		[Tooltip("Multiplies the curve. Use it for signed or scaled channels such as temperature or wind heading.")]
		public float Scale = 1f;

		public float Evaluate(float intensity)
		{
			float y = Curve != null && Curve.length > 0 ? Curve.Evaluate(intensity) : intensity;
			return y * Scale;
		}
	}

	/// <summary>
	/// One basic kind of weather — rain, snow, wind, fog… — as curves from intensity to channels.
	/// </summary>
	/// <remarks>
	/// Intensity is a single 0..1 scalar. "Sprinkle", "light", "medium" and "heavy" rain are just
	/// points on it, and the curves decide what each point means for drop size, streak length and
	/// how fast things get wet. Presets combine layers; they never introduce channels of their own.
	/// </remarks>
	[CreateAssetMenu(fileName = "New Weather Layer", menuName = "FishMMO/Weather/Layer Template", order = 1)]
	public class WeatherLayerTemplate : CachedScriptableObject<WeatherLayerTemplate>, ICachedObject
	{
		public WeatherLayerKind Kind;

		/// <summary>
		/// What this layer is made of. Null is the kind's own default — water rain, water snow,
		/// volcanic ash.
		/// </summary>
		/// <remarks>
		/// The substance never changes how the layer blends: <see cref="Kind"/> decides that, and
		/// the mix keeps its fixed five. It decides what the stuff IS — the colour, the sound, the
		/// cover it leaves, what it melts at and whether it can be breathed. Nitrogen snow is
		/// <see cref="WeatherLayerKind.Snow"/> with a substance; cryovolcanic tephra is
		/// <see cref="WeatherLayerKind.Ash"/> with one.
		/// </remarks>
		[Tooltip("What this layer is made of. Empty means the kind's default: water rain, water snow, volcanic ash.")]
		public WeatherSubstance Substance;

		public List<WeatherChannelCurve> Channels = new List<WeatherChannelCurve>();
		[Tooltip("Coldest local temperature this layer can happen at.")]
		[Range(-1f, 1f)] public float MinTemperature = -1f;
		[Tooltip("Warmest local temperature this layer can happen at.")]
		[Range(-1f, 1f)] public float MaxTemperature = 1f;
		[Min(0f)] public float DefaultTransitionSeconds = 30f;

		/// <summary>The layer's frame at an intensity.</summary>
		public WeatherFrame Evaluate(float intensity)
		{
			intensity = Mathf.Clamp01(intensity);
			var frame = new WeatherFrame();
			bool wroteType = false;
			for (int i = 0; i < Channels.Count; i++)
			{
				WeatherChannelCurve curve = Channels[i];
				if (curve == null)
				{
					continue;
				}
				frame[curve.Channel] = curve.Evaluate(intensity);
				wroteType |= WeatherChannels.IsPrecipitationType(curve.Channel);
			}
			// A precipitating layer that names no type falls as its own kind.
			WeatherChannel? own = WeatherChannels.TypeChannelOf(Kind);
			if (own.HasValue && !wroteType)
			{
				frame[own.Value] = 1f;
			}
			return frame;
		}

		/// <summary>True when the layer can happen at a temperature.</summary>
		public bool AllowsTemperature(float temperature) => temperature >= MinTemperature && temperature <= MaxTemperature;
	}
}
