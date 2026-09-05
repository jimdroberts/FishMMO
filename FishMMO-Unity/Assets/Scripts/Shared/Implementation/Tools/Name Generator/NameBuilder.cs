using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>Builds a given name from a <see cref="RacePhonology"/> and derives its meaning from the fragments used.</summary>
	internal static class NameBuilder
	{
		/// <summary>Chance that a gendered suffix is appended when the phonology has any.</summary>
		private const double GenderSuffixChance = 0.6;

		public static (string name, string meaning, List<string> fragments) Build(
			RacePhonology ph, CharacterGender gender, DeterministicRNG rng)
		{
			int complexity = rng.Next(ph.SyllMin, ph.SyllMax + 1);
			string onset = GeneratorUtility.Pick(ph.Onsets, rng);
			string nucleus = GeneratorUtility.Pick(ph.Nuclei, rng);
			string coda = ph.WeightedCodas != null && ph.WeightedCodas.Length > 0
				? GeneratorUtility.PickWeighted(ph.WeightedCodas, rng)
				: GeneratorUtility.Pick(ph.Codas, rng);

			var fragments = new List<string>();
			string raw;

			switch (complexity)
			{
				case 1:
					raw = onset + coda;
					fragments.Add(onset);
					fragments.Add(coda);
					break;
				case 2:
					raw = onset + nucleus + coda;
					fragments.Add(onset);
					fragments.Add(nucleus);
					fragments.Add(coda);
					break;
				case 3:
				{
					string mid = GeneratorUtility.Pick(ph.Middles, rng);
					raw = onset + nucleus + mid + coda;
					fragments.Add(onset);
					fragments.Add(nucleus);
					fragments.Add(mid);
					fragments.Add(coda);
					break;
				}
				default:
				{
					string mid1 = GeneratorUtility.Pick(ph.Middles, rng);
					string mid2 = GeneratorUtility.Pick(ph.Middles, rng);
					raw = onset + nucleus + mid1 + mid2 + coda;
					fragments.Add(onset);
					fragments.Add(nucleus);
					fragments.Add(mid1);
					fragments.Add(mid2);
					fragments.Add(coda);
					break;
				}
			}

			if (gender != CharacterGender.Unspecified)
			{
				string[] suffixes = gender == CharacterGender.Female
					? ph.FeminineSuffixes
					: ph.MasculineSuffixes;

				if (suffixes != null && suffixes.Length > 0 && rng.NextDouble() < GenderSuffixChance)
				{
					// Trim trailing letters back to the last vowel past the midpoint so the suffix blends.
					int lastVowel = -1;
					for (int i = raw.Length - 1; i >= 0; i--)
					{
						if ("aeiouAEIOU".IndexOf(raw[i]) >= 0)
						{
							lastVowel = i;
							break;
						}
					}
					if (lastVowel >= 0 && lastVowel > raw.Length * 0.5)
					{
						raw = raw.Substring(0, lastVowel + 1);
					}

					string suffix = GeneratorUtility.Pick(suffixes, rng);
					raw += suffix;
					fragments.Add(suffix);
				}
			}

			raw = GeneratorUtility.Smooth(raw);

			string name = GeneratorUtility.Capitalize(raw);
			string meaning = DeriveMeaning(name, fragments);
			return (name, meaning, fragments);
		}

		private static string DeriveMeaning(string name, List<string> fragments)
		{
			var parts = new List<string>();

			string onsetMeaning = NameGrammar.MatchPrefix(NameGrammar.MeaningOnsets, name);
			if (onsetMeaning != null)
			{
				parts.Add(onsetMeaning);
			}

			if (fragments.Count >= 4)
			{
				for (int i = 1; i < fragments.Count - 1; i++)
				{
					if (NameGrammar.MeaningMiddles.TryGetValue(fragments[i], out string middleMeaning))
					{
						parts.Add(middleMeaning);
						break;
					}
				}
			}

			string codaMeaning = NameGrammar.MatchSuffix(NameGrammar.MeaningCodas, name);
			if (codaMeaning != null)
			{
				parts.Add(codaMeaning);
			}

			return string.Join(" ", parts).Trim();
		}
	}
}
