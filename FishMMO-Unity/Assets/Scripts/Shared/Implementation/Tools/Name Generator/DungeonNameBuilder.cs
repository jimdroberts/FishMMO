using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Builds dungeon names from biome phonology.
	/// Pattern: [Prefix] [Root][Suffix] — e.g. "The Frozen Krimaw" or "Gorsanctum".
	/// </summary>
	internal static class DungeonNameBuilder
	{
		/// <summary>Chance a dungeon gets a thematic prefix; dungeons almost always have one.</summary>
		private const double PrefixChance = 0.70;
		/// <summary>Chance a two-syllable root grows a middle syllable.</summary>
		private const double MiddleChance = 0.35;

		public static (string name, string meaning, List<string> fragments) Build(
			BiomePhonology ph, DeterministicRNG rng)
		{
			var (root, rootFragments) = BuildRoot(ph, rng);

			string suffix = GeneratorUtility.Pick(ph.DungeonSuffixes, rng);

			var fragments = new List<string>(rootFragments);
			fragments.Add(suffix);

			string prefix = "";
			if (ph.DungeonPrefixes != null && ph.DungeonPrefixes.Length > 0 && rng.NextDouble() < PrefixChance)
			{
				prefix = GeneratorUtility.Pick(ph.DungeonPrefixes, rng);
				fragments.Insert(0, prefix);
			}

			string raw = root + suffix.ToLower().Replace("-", "");
			raw = GeneratorUtility.Smooth(raw);
			string coreName = GeneratorUtility.Capitalize(raw);

			string name = string.IsNullOrEmpty(prefix)
				? coreName
				: $"{prefix} {coreName}";

			string meaning = DeriveMeaning(root, suffix, prefix, ph);

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

				if (ph.Middles != null && ph.Middles.Length > 0 && rng.NextDouble() < MiddleChance)
				{
					string mid = GeneratorUtility.Pick(ph.Middles, rng);
					raw += mid;
					fragments.Add(mid);
				}
			}

			return (raw, fragments);
		}

		private static string DeriveMeaning(string root, string suffix,
			string prefix, BiomePhonology ph)
		{
			var parts = new List<string>();

			if (!string.IsNullOrEmpty(prefix))
			{
				parts.Add(prefix.ToLower().TrimStart("the ".ToCharArray()));
			}

			string onsetMeaning = NameGrammar.MatchPrefix(NameGrammar.BiomeMeaningOnsets, root);
			if (onsetMeaning != null)
			{
				parts.Add(onsetMeaning);
			}

			string suffixLower = suffix.ToLower().Replace("-", "");
			IReadOnlyList<StringMapping> codas = NameGrammar.BiomeMeaningCodas;
			for (int i = 0; i < codas.Count; i++)
			{
				if (suffixLower.Contains(codas[i].Key.ToLower()))
				{
					parts.Add(codas[i].Value);
					break;
				}
			}

			if (parts.Count == 0 && !string.IsNullOrEmpty(ph.Description))
			{
				parts.Add(ph.Description.Split('—')[0].Trim().ToLower());
			}

			return string.Join(" ", parts).Trim();
		}
	}
}
