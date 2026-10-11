using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Chooses the biome for a set of conditions from the registered templates' data — the
	/// replacement for WorldEditor's hand-written decision tree. A biome is eligible when its
	/// elevation tier matches, or when it is an any-elevation biome (tier
	/// <see cref="AnyElevationTier"/>) whose height band holds the point; among eligible biomes,
	/// one whose climate envelope contains the reading beats one whose envelope does not, the more
	/// central reading wins, and the <see cref="BiomeTemplate.SelectionWeight"/> scales the score.
	/// Ties break on key order, so the result is deterministic for every peer that holds the same
	/// templates.
	/// </summary>
	public static class BiomeResolver
	{
		/// <summary>
		/// The tier of a biome that can appear at any elevation: channels, fractures, anything that
		/// cuts ground of every height rather than belonging to one landform band.
		/// </summary>
		public const int AnyElevationTier = 9;

		/// <summary>The biome for a height and climate reading, or null when no selectable biome is registered.</summary>
		public static BiomeTemplate Select(float height, ClimateSample sample)
		{
			return Select(height, sample, BiomeWorldConditions.Earthlike);
		}

		/// <summary>The same, on a world that is not the home world. A sample whose seasons are known is held to each biome's warmest-season range too.</summary>
		public static BiomeTemplate Select(float height, ClimateSample sample, in BiomeWorldConditions world)
		{
			return SelectCore(height, sample, world);
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
			return Select(height, temperature, humidity, elevationTier, BiomeWorldConditions.Earthlike);
		}

		/// <summary>
		/// The biome that best fits this spot on this world.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <paramref name="world"/> is the physical filter and is applied BEFORE the climate score,
		/// because no amount of fitting the temperature makes a jungle possible in vacuum. The
		/// climate envelope then decides among what is left.
		/// </para>
		/// <para>
		/// This is what makes a body's biomes fall out of its orbit rather than out of a list:
		/// distance from the star sets the temperature, the atmosphere and surface water set what
		/// can live there, and the biomes select themselves.
		/// </para>
		/// </remarks>
		public static BiomeTemplate Select(float height, float temperature, float humidity, int elevationTier, in BiomeWorldConditions world)
		{
			return SelectCore(height, new ClimateSample { Temperature = temperature, Humidity = humidity, ElevationTier = elevationTier }, world);
		}

		/// <summary>
		/// The one selection: <paramref name="sample"/>'s tier, temperature and humidity, and its warmest
		/// season when <see cref="ClimateSample.SeasonKnown"/>.
		/// </summary>
		/// <remarks>
		/// <b>The summer is part of the envelope (2026-10-10).</b> The yearly temperature alone cannot
		/// tell an ice cap from a taiga: a scene at 72° south on Arthis read −15 °C, inside Peat Bog's
		/// and Taiga's envelopes, and was painted a bog with trees beside its own sea ice, under a
		/// summer of −6 °C that thaws nothing. A biome whose warmest-season range the sample falls
		/// outside scores as outside its envelope, exactly as a temperature or humidity miss does.
		/// </remarks>
		private static BiomeTemplate SelectCore(float height, in ClimateSample sample, in BiomeWorldConditions world)
		{
			int elevationTier = sample.ElevationTier;
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
					if (!world.Allows(biome))
					{
						continue;
					}
					if (!Eligible(biome, height, elevationTier, pass))
					{
						continue;
					}
					float score = Score(biome, sample);
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
		/// Pass 0: the tier matches, or the biome is an any-elevation one whose height band holds the
		/// point. Pass 1: the biome's height band contains the height (hand-tuned bands that claim
		/// ground below their tier). Pass 2: anything selectable, so a sparse biome set still answers.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Any-elevation biomes compete in every tier (2026-10-02).</b> They used to be offered
		/// only in pass 1, so on a world with a biome for every tier they could never win however
		/// well they fit — Io, the most tidally flexed body there is, drew tidal fractures only on
		/// the 2% of its ground no other biome claimed — and on a world without one they won the
		/// whole tier whatever its climate. Now they stand beside each tier's own biomes and win on
		/// climate fit and weight like any of them: a fracture that fits beats a mountain biome whose
		/// envelope the cold moon falls outside, and loses to a plain that fits better or weighs more.
		/// Their weights are tuned for that (<c>BiomeSpecTable</c>), below the native biomes they
		/// share a reading with, so they take the ground no native fits rather than flood a world.
		/// </para>
		/// <para>
		/// <b>Their height band is read.</b> A band of 0–1, the default, is every height; a narrower
		/// one keeps a channel off the summits. It used to be ignored, so a tier-9 band said nothing.
		/// </para>
		/// <para>
		/// Two integer compares and one band test per biome: the resolver stays allocation-free and
		/// runs per character per second and some two million times a globe bake.
		/// </para>
		/// </remarks>
		private static bool Eligible(BiomeTemplate biome, float height, int elevationTier, int pass)
		{
			switch (pass)
			{
				case 0: return biome.ElevationTier == elevationTier || (biome.ElevationTier == AnyElevationTier && biome.ContainsHeight(height));
				case 1: return biome.ContainsHeight(height);
				default: return true;
			}
		}

		/// <summary>Inside the envelope: 1 + centrality, scaled by weight. Outside: falls off with distance, capped below any inside score.</summary>
		private static float Score(BiomeTemplate biome, in ClimateSample sample)
		{
			float weight = Mathf.Max(0.0001f, biome.SelectionWeight);
			if (biome.ContainsClimate(sample))
			{
				return weight * (1f + biome.ClimateCentrality(sample.Temperature, sample.Humidity));
			}
			// Two units is the farthest any reading can be from any envelope (a summer miss adds to it, the cap holds).
			float closeness = 1f - Mathf.Clamp01(biome.ClimateDistance(sample) / 2f);
			return weight * closeness * 0.5f;
		}

		/// <summary>
		/// Every selectable biome that competes in a tier — its own and the any-elevation ones — for
		/// tools that list what a height could become.
		/// </summary>
		/// <remarks>Any-elevation biomes are listed whatever their height band, since a tier spans many heights.</remarks>
		public static List<BiomeTemplate> CandidatesForTier(int elevationTier)
		{
			var result = new List<BiomeTemplate>();
			IReadOnlyList<BiomeTemplate> candidates = BiomeRegistry.Selectable;
			for (int i = 0; i < candidates.Count; i++)
			{
				if (candidates[i].ElevationTier == elevationTier || candidates[i].ElevationTier == AnyElevationTier)
				{
					result.Add(candidates[i]);
				}
			}
			return result;
		}
	}
}
