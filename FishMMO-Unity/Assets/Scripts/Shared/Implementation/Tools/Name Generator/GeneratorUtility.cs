using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Shared helper methods used by the builders and the name-generation
	/// facade. All methods are pure and thread-safe unless noted.
	/// </summary>
	internal static class GeneratorUtility
	{
		/// <summary>Pick a uniformly random element from a non-empty array.</summary>
		public static string Pick(string[] arr, DeterministicRNG rng) =>
			arr[rng.Next(arr.Length)];

		/// <summary>
		/// Weighted pick from a non-empty <paramref name="entries"/> array.
		/// If every weight is <c>&lt;= 0</c>, falls back to a uniform pick
		/// rather than throwing. Throws only when <paramref name="entries"/>
		/// itself is null or empty.
		/// </summary>
		public static string PickWeighted((string item, int weight)[] entries, DeterministicRNG rng)
		{
			if (entries == null || entries.Length == 0)
				throw new ArgumentException("entries must not be empty", nameof(entries));
			int totalWeight = 0;
			foreach (var e in entries) totalWeight += e.weight;
			// Fallback: if caller passed all-zero weights, treat as uniform.
			if (totalWeight <= 0) return entries[rng.Next(entries.Length)].item;
			int roll = rng.Next(totalWeight);
			int cumulative = 0;
			foreach (var e in entries)
			{
				cumulative += e.weight;
				if (roll < cumulative) return e.item;
			}
			return entries[entries.Length - 1].item;
		}

		/// <summary>
		/// Picks from <paramref name="preferred"/> with the given chance, else from
		/// <paramref name="fallback"/>; either may be empty. A climate variant's adjectives lead a
		/// name this way without the biome's own vocabulary disappearing.
		/// </summary>
		public static string PickFlavoured(string[] preferred, string[] fallback, double preferChance, DeterministicRNG rng)
		{
			bool hasPreferred = preferred != null && preferred.Length > 0;
			bool hasFallback = fallback != null && fallback.Length > 0;
			if (hasPreferred && (!hasFallback || rng.NextDouble() < preferChance))
			{
				return Pick(preferred, rng);
			}
			return hasFallback ? Pick(fallback, rng) : "";
		}

		/// <summary>Uppercase the first character; leaves other characters as-is.</summary>
		public static string Capitalize(string s) =>
			string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

		// Bounded cache for NormalizeRace. Names are a small closed set in
		// practice (races, biomes, cultures) so a simple upper bound avoids
		// unbounded growth from user-provided strings.
		private const int NormalizeCacheMax = 512;
		private static readonly ConcurrentDictionary<string, string> _normalizeCache
			= new(StringComparer.Ordinal);

		/// <summary>
		/// Normalize a race / biome / culture key: lowercase, letters only.
		/// <para>All non-letter characters are stripped, so
		/// <c>"Half-Elf"</c> and <c>"half elf"</c> both normalize to
		/// <c>"halfelf"</c>. Callers must be aware of this coupling —
		/// keys in the registries are authored in this normalized form.</para>
		/// </summary>
		public static string NormalizeRace(string race)
		{
			if (string.IsNullOrEmpty(race)) return string.Empty;
			if (_normalizeCache.TryGetValue(race, out var cached)) return cached;

			var sb = new StringBuilder(race.Length);
			for (int i = 0; i < race.Length; i++)
			{
				char c = race[i];
				if (c >= 'A' && c <= 'Z') sb.Append((char)(c + 32));
				else if (c >= 'a' && c <= 'z') sb.Append(c);
				else if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
			}
			var result = sb.ToString();
			// Cap cache size — drop on overflow rather than evict per-key.
			if (_normalizeCache.Count < NormalizeCacheMax)
				_normalizeCache.TryAdd(race, result);
			return result;
		}

		// Compiled regexes — avoid re-parsing the pattern on every call.
		private static readonly Regex _rxRepeatedVowel = new(
			@"([aeiou])\1{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex _rxRepeatedConsonant = new(
			@"([^aeiou])\1{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex _rxHarshCluster = new(
			@"([^aeiouAEIOU]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		/// <summary>
		/// Phonetic smoothing: collapses 3+ identical chars to 2 and breaks
		/// 4-consonant clusters with a short vowel. The case of the surviving
		/// letter follows the <i>first</i> character of the matched run, so
		/// <c>"AAa"</c> and <c>"aAA"</c> both collapse to <c>"Aa"</c> and
		/// <c>"aA"</c> respectively (same length, first-wins) — no more
		/// asymmetry based on which case the regex engine captured.
		/// </summary>
		public static string Smooth(string input)
		{
			if (string.IsNullOrEmpty(input)) return input;

			// Collapse triple+ repeated vowels. Callback emits two copies of
			// the *first* matched character to eliminate the previous
			// "$1$1 preserves captured-case" asymmetry (issue 1.5).
			input = _rxRepeatedVowel.Replace(input, m =>
			{
				char first = m.Value[0];
				char second = m.Value[1];
				return new string(new[] { first, second });
			});

			input = _rxRepeatedConsonant.Replace(input, m =>
			{
				char first = m.Value[0];
				char second = m.Value[1];
				return new string(new[] { first, second });
			});

			// Break harsh 4+ consonant clusters by inserting a short vowel.
			input = _rxHarshCluster.Replace(input, m =>
				m.Value.Substring(0, 2) + "a" + m.Value.Substring(2));

			return input;
		}
	}
}
