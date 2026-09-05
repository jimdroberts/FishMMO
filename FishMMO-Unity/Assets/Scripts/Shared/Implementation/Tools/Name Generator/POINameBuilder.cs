using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Builds point-of-interest names from biome phonology.
	/// Pattern: [Adjective] [Root] [TypeSuffix] — e.g. "Frozen Kelbrin Spring".
	/// </summary>
	internal static class POINameBuilder
	{
		/// <summary>Chance a biome adjective leads the name.</summary>
		private const double AdjectiveChance = 0.45;
		/// <summary>Chance the type-specific suffix table is used over the biome's own POI suffixes.</summary>
		private const double TypeSuffixChance = 0.60;
		/// <summary>Chance a two-syllable root grows a coda.</summary>
		private const double CodaChance = 0.30;

		public static (string name, string meaning, List<string> fragments) Build(
			BiomePhonology ph, POIType poiType, DeterministicRNG rng)
		{
			string typeKey = poiType == POIType.Any
				? PickRandomPOIType(rng)
				: poiType.ToString().ToLower();

			var (root, rootFragments) = BuildRoot(ph, rng);

			var fragments = new List<string>(rootFragments);

			string typeSuffix = PickTypeSuffix(typeKey, ph, rng);
			fragments.Add(typeSuffix);

			string adjective = "";
			if (ph.Adjectives != null && ph.Adjectives.Length > 0 && rng.NextDouble() < AdjectiveChance)
			{
				adjective = GeneratorUtility.Pick(ph.Adjectives, rng);
				fragments.Insert(0, adjective);
			}

			string raw = GeneratorUtility.Smooth(root);
			string coreName = GeneratorUtility.Capitalize(raw);

			var nameParts = new List<string>();
			if (!string.IsNullOrEmpty(adjective))
			{
				nameParts.Add(adjective);
			}
			nameParts.Add(coreName);
			nameParts.Add(typeSuffix);

			string name = string.Join(" ", nameParts);
			string meaning = DeriveMeaning(root, adjective, typeKey);

			return (name, meaning, fragments);
		}

		private static (string root, List<string> fragments) BuildRoot(
			BiomePhonology ph, DeterministicRNG rng)
		{
			int complexity = rng.Next(1, 3);
			string onset = GeneratorUtility.Pick(ph.Onsets, rng);

			var fragments = new List<string>();
			string raw;

			if (complexity == 1)
			{
				raw = onset;
				fragments.Add(onset);
			}
			else
			{
				string nucleus = GeneratorUtility.Pick(ph.Nuclei, rng);
				raw = onset + nucleus;
				fragments.Add(onset);
				fragments.Add(nucleus);

				if (ph.Codas != null && ph.Codas.Length > 0 && rng.NextDouble() < CodaChance)
				{
					string coda = GeneratorUtility.Pick(ph.Codas, rng);
					raw += coda;
					fragments.Add(coda);
				}
			}

			return (raw, fragments);
		}

		private static string PickTypeSuffix(string typeKey, BiomePhonology ph, DeterministicRNG rng)
		{
			if (NameGrammar.POITypeSuffixes.TryGetValue(typeKey, out string[] typeSuffixes)
				&& typeSuffixes.Length > 0
				&& rng.NextDouble() < TypeSuffixChance)
			{
				return GeneratorUtility.Pick(typeSuffixes, rng);
			}

			if (ph.POISuffixes != null && ph.POISuffixes.Length > 0)
			{
				return GeneratorUtility.Pick(ph.POISuffixes, rng);
			}

			// Neither table has an entry: fall back to the type name itself.
			return GeneratorUtility.Capitalize(typeKey);
		}

		private static string PickRandomPOIType(DeterministicRNG rng)
		{
			double roll = rng.NextDouble();
			if (roll < 0.15) return "landmark";
			if (roll < 0.25) return "camp";
			if (roll < 0.35) return "shrine";
			if (roll < 0.45) return "tower";
			if (roll < 0.55) return "bridge";
			if (roll < 0.65) return "clearing";
			if (roll < 0.75) return "spring";
			if (roll < 0.85) return "cave";
			if (roll < 0.93) return "monument";
			return "wreck";
		}

		private static string DeriveMeaning(string root, string adjective, string typeKey)
		{
			var parts = new List<string>();

			if (!string.IsNullOrEmpty(adjective))
			{
				parts.Add(adjective.ToLower());
			}

			string onsetMeaning = NameGrammar.MatchPrefix(NameGrammar.BiomeMeaningOnsets, root);
			if (onsetMeaning != null)
			{
				parts.Add(onsetMeaning);
			}

			parts.Add(typeKey);

			return string.Join(" ", parts).Trim();
		}
	}
}
