using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>A preset and how often the director picks it.</summary>
	[Serializable]
	public class WeightedWeatherPreset
	{
		public WeatherPreset Preset;
		[Min(0f)] public float Weight = 1f;
	}

	/// <summary>How well a preset survives over a biome: 1 thrives, 0 dies out.</summary>
	[Serializable]
	public class WeatherPresetSuitability
	{
		public WeatherPreset Preset;
		[Range(0f, 1f)] public float Suitability = 1f;
	}

	/// <summary>A biome's own take on a preset: Swamp Rain instead of Heavy Rain.</summary>
	[Serializable]
	public class WeatherPresetVariant
	{
		public WeatherPreset Preset;
		[Tooltip("Shown instead of the preset while its weather is over this biome.")]
		public WeatherPreset Variant;
	}

	/// <summary>
	/// What weather a biome has: its always-present background, the storm cells it spawns, and
	/// what it forbids.
	/// </summary>
	/// <remarks>
	/// Both the elevation-chosen biome and a painted biome area use the same profile, because
	/// <see cref="Biomes.BiomeSampler"/> already resolves a position to one biome. A biome with no
	/// profile uses the default on its <see cref="Biomes.ClimateSettings"/>.
	/// </remarks>
	[Serializable]
	public class BiomeWeatherProfile
	{
		[Tooltip("Weather that is always here: mist in a swamp, haze in a desert.")]
		public List<WeatherPresetLayer> Background = new List<WeatherPresetLayer>();
		[Tooltip("Storm cells the automatic director may start over this biome.")]
		public List<WeightedWeatherPreset> CellSpawns = new List<WeightedWeatherPreset>();
		[Tooltip("Presets that fade when they drift over this biome. Unlisted presets are fully suitable.")]
		public List<WeatherPresetSuitability> Suitability = new List<WeatherPresetSuitability>();
		[Tooltip("This biome's versions of presets: the variant's channels replace the preset's while it is over the biome.")]
		public List<WeatherPresetVariant> Variants = new List<WeatherPresetVariant>();
		[Tooltip("Kinds that never show here, whatever passes over.")]
		public WeatherKindMask Forbidden = WeatherKindMask.None;
		[Tooltip("Director target: storm cells per square kilometre.")]
		[Min(0f)] public float CellsPerSquareKm = 0.4f;

		/// <summary>True when the profile says anything at all.</summary>
		public bool IsAuthored => Background.Count > 0 || CellSpawns.Count > 0 || Suitability.Count > 0 || Variants.Count > 0 || Forbidden != WeatherKindMask.None;

		/// <summary>The biome's variant of a preset, or the preset itself.</summary>
		public WeatherPreset VariantOf(WeatherPreset preset)
		{
			if (preset == null || Variants == null)
			{
				return preset;
			}
			for (int i = 0; i < Variants.Count; i++)
			{
				WeatherPresetVariant entry = Variants[i];
				if (entry != null && entry.Preset == preset && entry.Variant != null)
				{
					return entry.Variant;
				}
			}
			return preset;
		}

		public float SuitabilityOf(WeatherPreset preset)
		{
			if (preset == null)
			{
				return 1f;
			}
			for (int i = 0; i < Suitability.Count; i++)
			{
				if (Suitability[i] != null && Suitability[i].Preset == preset)
				{
					return Suitability[i].Suitability;
				}
			}
			return 1f;
		}

		/// <summary>Adds the background layers to an accumulator.</summary>
		public void AccumulateBackground(ref WeatherAccumulator accumulator)
		{
			for (int i = 0; i < Background.Count; i++)
			{
				WeatherPresetLayer layer = Background[i];
				if (layer?.Template != null)
				{
					accumulator.Add(layer.Template.Evaluate(layer.Intensity), 1f);
				}
			}
		}

		/// <summary>Picks a cell preset by weight, or null when the biome spawns none.</summary>
		public WeatherPreset PickCellPreset(DeterministicRNG rng)
		{
			float total = 0f;
			foreach (WeightedWeatherPreset entry in CellSpawns)
			{
				if (entry?.Preset != null)
				{
					total += Mathf.Max(0f, entry.Weight);
				}
			}
			if (total <= 0f)
			{
				return null;
			}
			float roll = rng.Range(0f, total);
			foreach (WeightedWeatherPreset entry in CellSpawns)
			{
				if (entry?.Preset == null)
				{
					continue;
				}
				roll -= Mathf.Max(0f, entry.Weight);
				if (roll <= 0f)
				{
					return entry.Preset;
				}
			}
			return null;
		}
	}
}
