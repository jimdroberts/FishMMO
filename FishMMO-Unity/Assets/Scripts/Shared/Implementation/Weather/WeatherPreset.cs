using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>A layer and how strongly it applies inside a preset.</summary>
	[Serializable]
	public class WeatherPresetLayer
	{
		public WeatherLayerTemplate Template;
		[Range(0f, 1f)] public float Intensity = 1f;
	}

	/// <summary>
	/// A named combination of layers: a Blizzard is Snow 1.0 + Wind 0.85 + Clouds 0.95 + Fog 0.6.
	/// </summary>
	[CreateAssetMenu(fileName = "New Weather Preset", menuName = "FishMMO/Weather/Preset", order = 2)]
	public class WeatherPreset : CachedScriptableObject<WeatherPreset>, ICachedObject
	{
		public string DisplayName;
		public List<WeatherPresetLayer> Layers = new List<WeatherPresetLayer>();
		[Min(0f)] public float TransitionSeconds = 45f;
		[Tooltip("How long the automatic director keeps a storm cell of this preset alive, in minutes.")]
		public Vector2 DurationMinutes = new Vector2(10f, 25f);
		[Tooltip("Storm cell radius range in metres.")]
		public Vector2 CellRadiusMeters = new Vector2(150f, 600f);

		[NonSerialized] private bool cached;
		[NonSerialized] private WeatherFrame cachedFull;

		public string ResolvedName => string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName;

		/// <summary>The preset's frame with every layer at its authored intensity × <paramref name="scale"/>.</summary>
		public WeatherFrame Evaluate(float scale = 1f)
		{
			if (Mathf.Approximately(scale, 1f) && cached)
			{
				return cachedFull;
			}
			var accumulator = new WeatherAccumulator();
			for (int i = 0; i < Layers.Count; i++)
			{
				WeatherPresetLayer layer = Layers[i];
				if (layer?.Template == null)
				{
					continue;
				}
				accumulator.Add(layer.Template.Evaluate(Mathf.Clamp01(layer.Intensity * scale)), 1f);
			}
			WeatherFrame frame = accumulator.Resolve();
			if (Mathf.Approximately(scale, 1f))
			{
				cachedFull = frame;
				cached = true;
			}
			return frame;
		}

		/// <summary>The kinds this preset contains.</summary>
		public WeatherKindMask Kinds
		{
			get
			{
				WeatherKindMask mask = WeatherKindMask.None;
				foreach (WeatherPresetLayer layer in Layers)
				{
					if (layer?.Template != null)
					{
						mask |= WeatherChannels.MaskOf(layer.Template.Kind);
					}
				}
				return mask;
			}
		}

		/// <summary>Call after editing layers at runtime.</summary>
		public void InvalidateCache() => cached = false;

		private void OnValidate() => cached = false;
	}
}
