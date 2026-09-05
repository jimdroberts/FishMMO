using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Chooses the biome for a set of conditions from the registered templates' data — the
	/// replacement for WorldEditor's hand-written decision tree. A biome is eligible when its
	/// elevation tier matches; among eligible biomes, one whose climate envelope contains the
	/// reading beats one whose envelope does not, the more central reading wins, and the
	/// <see cref="BiomeTemplate.SelectionWeight"/> scales the score. Ties break on key order,
	/// so the result is deterministic for every peer that holds the same templates.
	/// </summary>
	public static class BiomeResolver
	{
		/// <summary>The biome for a height and climate reading, or null when no selectable biome is registered.</summary>
		public static BiomeTemplate Select(float height, ClimateSample sample)
		{
			return Select(height, sample.Temperature, sample.Humidity, sample.ElevationTier);
		}

		/// <summary>The biome for a height, temperature and humidity under a climate's tier boundaries.</summary>
		public static BiomeTemplate Select(float height, float temperature, float humidity, ClimateSettings climate)
		{
			int tier = climate != null ? climate.TierForHeight(height) : ClimateSettings.TierForHeight(height, null);
			return Select(height, temperature, humidity, tier);
		}

		/// <summary>The biome for a height, temperature, humidity and already-resolved elevation tier.</summary>
		public static BiomeTemplate Select(float height, float temperature, float humidity, int elevationTier)
		{
			IReadOnlyList<BiomeTemplate> candidates = BiomeRegistry.Selectable;
			if (candidates.Count == 0)
			{
				return null;
			}

			BiomeTemplate best = null;
			float bestScore = float.NegativeInfinity;
			int pass = 0;
			while (best == null && pass < 3)
			{
				for (int i = 0; i < candidates.Count; i++)
				{
					BiomeTemplate biome = candidates[i];
					if (!Eligible(biome, height, elevationTier, pass))
					{
						continue;
					}
					float score = Score(biome, temperature, humidity);
					if (score > bestScore)
					{
						bestScore = score;
						best = biome;
					}
				}
				pass++;
			}
			return best;
		}

		/// <summary>
		/// Pass 0: tier matches. Pass 1: the biome's height band contains the height (tier 9
		/// "anywhere" biomes and hand-tuned bands). Pass 2: anything selectable, so a sparse
		/// biome set still answers.
		/// </summary>
		private static bool Eligible(BiomeTemplate biome, float height, int elevationTier, int pass)
		{
			switch (pass)
			{
				case 0: return biome.ElevationTier == elevationTier;
				case 1: return biome.ElevationTier == 9 || biome.ContainsHeight(height);
				default: return true;
			}
		}

		/// <summary>Inside the envelope: 1 + centrality, scaled by weight. Outside: falls off with distance, capped below any inside score.</summary>
		private static float Score(BiomeTemplate biome, float temperature, float humidity)
		{
			float weight = Mathf.Max(0.0001f, biome.SelectionWeight);
			if (biome.ContainsClimate(temperature, humidity))
			{
				return weight * (1f + biome.ClimateCentrality(temperature, humidity));
			}
			// Two units is the farthest any reading can be from any envelope.
			float closeness = 1f - Mathf.Clamp01(biome.ClimateDistance(temperature, humidity) / 2f);
			return weight * closeness * 0.5f;
		}

		/// <summary>Every selectable biome whose tier matches, for tools that list what a height could become.</summary>
		public static List<BiomeTemplate> CandidatesForTier(int elevationTier)
		{
			var result = new List<BiomeTemplate>();
			IReadOnlyList<BiomeTemplate> candidates = BiomeRegistry.Selectable;
			for (int i = 0; i < candidates.Count; i++)
			{
				if (candidates[i].ElevationTier == elevationTier)
				{
					result.Add(candidates[i]);
				}
			}
			return result;
		}
	}
}
