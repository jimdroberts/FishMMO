using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Builds city names from race phonology and city-type suffixes. When a
	/// biome is supplied, part of the prefix and suffix vocabulary is drawn from
	/// its adjectives and dungeon suffixes, so "mangrove-port" human cities read
	/// differently from "alpine-port" ones.
	/// </summary>
	internal static class CityNameBuilder
	{
		/// <summary>Chance a biome dungeon suffix replaces the race's city suffix.</summary>
		private const double BiomeSuffixChance = 0.22;
		/// <summary>Chance of a leading word when a biome is supplied / when it is not.</summary>
		private const double BiomePrefixChance = 0.30;
		private const double PlainPrefixChance = 0.20;
		/// <summary>When a biome is supplied, chance the prefix is one of its adjectives rather than a city prefix.</summary>
		private const double BiomeAdjectiveChance = 0.60;
		/// <summary>Chance a two-syllable root grows a middle syllable.</summary>
		private const double MiddleChance = 0.40;

		public static (string name, string meaning, List<string> fragments) Build(
			RacePhonology ph, string race, CityType cityType, string biome, DeterministicRNG rng)
		{
			string typeKey = cityType == CityType.Any
				? PickRandomCityType(rng)
				: cityType.ToString().ToLower();

			BiomePhonology biomePh = string.IsNullOrEmpty(biome) ? null : BiomeRegistry.ResolvePhonology(biome);

			string suffix;
			if (biomePh != null && biomePh.DungeonSuffixes != null &&
				biomePh.DungeonSuffixes.Length > 0 && rng.NextDouble() < BiomeSuffixChance)
			{
				suffix = GeneratorUtility.Pick(biomePh.DungeonSuffixes, rng)
					.ToLower().Replace("-", "").Replace(" ", "");
			}
			else
			{
				suffix = PickCitySuffix(race, typeKey, rng);
			}

			var (root, rootFragments) = BuildRoot(ph, rng);

			var fragments = new List<string>(rootFragments);
			fragments.Add(suffix);

			string prefix = "";
			double prefixChance = biomePh != null ? BiomePrefixChance : PlainPrefixChance;
			if (rng.NextDouble() < prefixChance)
			{
				if (biomePh != null && biomePh.Adjectives != null &&
					biomePh.Adjectives.Length > 0 && rng.NextDouble() < BiomeAdjectiveChance)
				{
					prefix = GeneratorUtility.Pick(biomePh.Adjectives, rng);
				}
				else if (NameGrammar.CityPrefixes.Length > 0)
				{
					prefix = GeneratorUtility.Pick(NameGrammar.CityPrefixes, rng);
				}
				if (!string.IsNullOrEmpty(prefix))
				{
					fragments.Insert(0, prefix);
				}
			}

			string raw = GeneratorUtility.Smooth(root + suffix);
			string name = GeneratorUtility.Capitalize(raw);

			if (!string.IsNullOrEmpty(prefix))
			{
				name = prefix + " " + name;
			}

			string meaning = DeriveMeaning(root, suffix, prefix, typeKey, biomePh);
			return (name, meaning, fragments);
		}

		private static (string root, List<string> fragments) BuildRoot(RacePhonology ph, DeterministicRNG rng)
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

				if (ph.Middles != null && ph.Middles.Length > 0 && rng.NextDouble() < MiddleChance)
				{
					string mid = GeneratorUtility.Pick(ph.Middles, rng);
					raw += mid;
					fragments.Add(mid);
				}
			}

			return (raw, fragments);
		}

		private static string PickCitySuffix(string race, string typeKey, DeterministicRNG rng)
		{
			if (RaceRegistry.TryGetCitySuffixes(race, out RaceCitySuffixes suffixes))
			{
				string[] table = typeKey switch
				{
					"capital" => suffixes.Capital,
					"fortress" => suffixes.Fortress,
					"village" => suffixes.Village,
					"port" => suffixes.Port,
					"sacred" => suffixes.Sacred,
					"ruin" => suffixes.Ruin,
					_ => suffixes.Capital,
				};

				if (table != null && table.Length > 0)
				{
					return GeneratorUtility.Pick(table, rng);
				}
			}

			string[] fallback = NameGrammar.FallbackCitySuffixes;
			return fallback.Length > 0 ? GeneratorUtility.Pick(fallback, rng) : typeKey;
		}

		private static string PickRandomCityType(DeterministicRNG rng)
		{
			double roll = rng.NextDouble();
			if (roll < 0.25) return "capital";
			if (roll < 0.45) return "village";
			if (roll < 0.60) return "fortress";
			if (roll < 0.75) return "port";
			if (roll < 0.88) return "sacred";
			return "ruin";
		}

		private static string DeriveMeaning(string root, string suffix,
			string prefix, string typeKey, BiomePhonology biomePh)
		{
			var parts = new List<string>();

			string onsetMeaning = NameGrammar.MatchPrefix(NameGrammar.MeaningOnsets, root);
			if (onsetMeaning != null)
			{
				parts.Add(onsetMeaning);
			}

			if (NameGrammar.CitySuffixMeanings.TryGetValue(suffix, out string suffixMeaning))
			{
				parts.Add(suffixMeaning);
			}
			else
			{
				parts.Add(typeKey);
			}

			if (!string.IsNullOrEmpty(prefix))
			{
				string lower = prefix.ToLower();
				if (lower == "old" || lower == "ancient" || lower == "lost")
				{
					parts.Insert(0, "ancient");
				}
				else if (lower == "new")
				{
					parts.Insert(0, "young");
				}
				else if (lower == "great" || lower == "high")
				{
					parts.Insert(0, "grand");
				}
				else
				{
					parts.Insert(0, lower);
				}
			}

			if (biomePh != null && parts.Count < 2 && !string.IsNullOrEmpty(biomePh.Description))
			{
				string shortDescription = biomePh.Description.Split('—')[0].Trim().ToLower();
				if (!string.IsNullOrEmpty(shortDescription))
				{
					parts.Add(shortDescription);
				}
			}

			return string.Join(" ", parts).Trim();
		}
	}
}
