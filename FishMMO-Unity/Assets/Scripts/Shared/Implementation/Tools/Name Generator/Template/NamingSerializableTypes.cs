using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// One coda syllable and how often it should be drawn relative to the others.
	/// </summary>
	[Serializable]
	public class WeightedCoda
	{
		public string Item;
		public int Weight = 1;
	}

	/// <summary>
	/// A string-to-string table row. Unity cannot serialize a dictionary, so the
	/// naming templates author their lookup tables as ordered lists of these;
	/// order matters for the tables that are scanned for a prefix or suffix match.
	/// </summary>
	[Serializable]
	public class StringMapping
	{
		public string Key;
		public string Value;

		public StringMapping() { }

		public StringMapping(string key, string value)
		{
			Key = key;
			Value = value;
		}
	}

	/// <summary>
	/// A string-to-list table row, e.g. a POI type and the suffixes it can take.
	/// </summary>
	[Serializable]
	public class StringListMapping
	{
		public string Key;
		public string[] Values;

		public StringListMapping() { }

		public StringListMapping(string key, string[] values)
		{
			Key = key;
			Values = values;
		}
	}

	/// <summary>
	/// Inspector-editable phonology for a race or one of its cultures. Converted
	/// to the runtime <see cref="RacePhonology"/> once, when the template loads.
	/// </summary>
	[Serializable]
	public class SerializableRacePhonology
	{
		[Tooltip("Syllable openings a name can start with.")]
		public string[] Onsets;
		[Tooltip("Vowel clusters placed after the onset.")]
		public string[] Nuclei;
		[Tooltip("Syllable endings a name can finish with.")]
		public string[] Codas;
		[Tooltip("Optional middle syllables used by longer names.")]
		public string[] Middles;
		[Tooltip("Fewest syllables a name may have.")]
		public int SyllMin = 2;
		[Tooltip("Most syllables a name may have.")]
		public int SyllMax = 3;
		[Tooltip("Endings appended to feminine names.")]
		public string[] FeminineSuffixes;
		[Tooltip("Endings appended to masculine names.")]
		public string[] MasculineSuffixes;
		[TextArea]
		public string Description;
		public string[] Tags;
		[Tooltip("When non-empty these replace Codas with a weighted draw.")]
		public WeightedCoda[] WeightedCodas;

		public RacePhonology ToRuntime()
		{
			var runtime = new RacePhonology
			{
				Onsets = Onsets ?? Array.Empty<string>(),
				Nuclei = Nuclei ?? Array.Empty<string>(),
				Codas = Codas ?? Array.Empty<string>(),
				Middles = Middles ?? Array.Empty<string>(),
				SyllMin = SyllMin,
				SyllMax = SyllMax,
				FeminineSuffixes = FeminineSuffixes ?? Array.Empty<string>(),
				MasculineSuffixes = MasculineSuffixes ?? Array.Empty<string>(),
				Description = Description,
				Tags = Tags ?? Array.Empty<string>(),
			};
			if (WeightedCodas != null && WeightedCodas.Length > 0)
			{
				var weighted = new (string item, int weight)[WeightedCodas.Length];
				for (int i = 0; i < WeightedCodas.Length; i++)
				{
					weighted[i] = (WeightedCodas[i].Item ?? "", WeightedCodas[i].Weight);
				}
				runtime.WeightedCodas = weighted;
			}
			return runtime;
		}

		public static SerializableRacePhonology From(RacePhonology runtime)
		{
			var serializable = new SerializableRacePhonology
			{
				Onsets = runtime.Onsets,
				Nuclei = runtime.Nuclei,
				Codas = runtime.Codas,
				Middles = runtime.Middles,
				SyllMin = runtime.SyllMin,
				SyllMax = runtime.SyllMax,
				FeminineSuffixes = runtime.FeminineSuffixes,
				MasculineSuffixes = runtime.MasculineSuffixes,
				Description = runtime.Description,
				Tags = runtime.Tags,
			};
			if (runtime.WeightedCodas != null && runtime.WeightedCodas.Length > 0)
			{
				serializable.WeightedCodas = new WeightedCoda[runtime.WeightedCodas.Length];
				for (int i = 0; i < runtime.WeightedCodas.Length; i++)
				{
					serializable.WeightedCodas[i] = new WeightedCoda
					{
						Item = runtime.WeightedCodas[i].item,
						Weight = runtime.WeightedCodas[i].weight,
					};
				}
			}
			return serializable;
		}

		/// <summary>True when the phonology can build at least a one-syllable name.</summary>
		public bool IsUsable()
		{
			return Onsets != null && Onsets.Length > 0
				&& ((Codas != null && Codas.Length > 0) || (WeightedCodas != null && WeightedCodas.Length > 0));
		}
	}

	/// <summary>A cultural variant of a race: its own phonology under the race's titles and places.</summary>
	[Serializable]
	public class SerializableCultureVariant
	{
		[Tooltip("Lowercase key used to request this culture, e.g. 'nordic'.")]
		public string CultureKey;
		public SerializableRacePhonology Phonology = new();
	}

	/// <summary>Title vocabulary for one race, by category.</summary>
	[Serializable]
	public class SerializableRaceTitles
	{
		[Tooltip("Honorifics that fit any gender: Captain, Elder, Chieftain.")]
		public string[] Honorific;
		[Tooltip("Honorifics for masculine names: Sir, Lord, Clanfather.")]
		public string[] HonorificMasculine;
		[Tooltip("Honorifics for feminine names: Dame, Lady, Clanmother.")]
		public string[] HonorificFeminine;
		public string[] Epithet;
		public string[] Rank;
		public string[] Legend;
		[Tooltip("Trades and callings of this race: Runesmith, Brewer. When empty, the shared pools and then the grammar's generic occupations fill in, unless AllowGenericOccupations is off.")]
		public string[] Occupational;

		public RaceTitles ToRuntime() => new()
		{
			Honorific = Honorific ?? Array.Empty<string>(),
			HonorificMasculine = HonorificMasculine ?? Array.Empty<string>(),
			HonorificFeminine = HonorificFeminine ?? Array.Empty<string>(),
			Epithet = Epithet ?? Array.Empty<string>(),
			Rank = Rank ?? Array.Empty<string>(),
			Legend = Legend ?? Array.Empty<string>(),
			Occupational = Occupational ?? Array.Empty<string>(),
		};

		public static SerializableRaceTitles From(RaceTitles runtime) => new()
		{
			Honorific = runtime.Honorific,
			HonorificMasculine = runtime.HonorificMasculine,
			HonorificFeminine = runtime.HonorificFeminine,
			Epithet = runtime.Epithet,
			Rank = runtime.Rank,
			Legend = runtime.Legend,
			Occupational = runtime.Occupational,
		};
	}

	/// <summary>
	/// One way of composing a title, authored in the grammar asset. The pattern
	/// names its slots in braces — <c>{honorific:place} of {place}</c> — and the
	/// builder fills them from the race's tables and the grammar; a template
	/// whose slots cannot all be filled for a given race is simply not used.
	/// </summary>
	[Serializable]
	public class TitleTemplate
	{
		public TitleType Category = TitleType.Honorific;
		[Tooltip("Register this composition belongs to; Any fits every register.")]
		public TitleRegister Register = TitleRegister.Any;
		[Tooltip("Slots: {honorific} {honorific:place} {honorific:ordinal} {epithet} {rank} {rank:noplace} {legend} {occupation} {profession} {place} {place:race} {universalplace} {ordinal} {pronoun} {deed} {object} {era} {battle} {outcome} {adjective} {qualifier}.")]
		public string Pattern;
		[Tooltip("Relative chance among the usable templates of the same category.")]
		public int Weight = 10;
	}

	/// <summary>City-name endings for one race, by city type.</summary>
	[Serializable]
	public class SerializableRaceCitySuffixes
	{
		public string[] Capital;
		public string[] Fortress;
		public string[] Village;
		public string[] Port;
		public string[] Sacred;
		public string[] Ruin;

		public RaceCitySuffixes ToRuntime() => new()
		{
			Capital = Capital ?? Array.Empty<string>(),
			Fortress = Fortress ?? Array.Empty<string>(),
			Village = Village ?? Array.Empty<string>(),
			Port = Port ?? Array.Empty<string>(),
			Sacred = Sacred ?? Array.Empty<string>(),
			Ruin = Ruin ?? Array.Empty<string>(),
		};

		public static SerializableRaceCitySuffixes From(RaceCitySuffixes runtime) => new()
		{
			Capital = runtime.Capital,
			Fortress = runtime.Fortress,
			Village = runtime.Village,
			Port = runtime.Port,
			Sacred = runtime.Sacred,
			Ruin = runtime.Ruin,
		};
	}

	/// <summary>Inspector-editable phonology and vocabulary for a biome.</summary>
	[Serializable]
	public class SerializableBiomePhonology
	{
		public string[] Onsets;
		public string[] Nuclei;
		public string[] Codas;
		public string[] Middles;
		public int SyllMin = 2;
		public int SyllMax = 3;
		[Tooltip("Endings for dungeon names, e.g. 'Cathedral', 'Vault'.")]
		public string[] DungeonSuffixes;
		[Tooltip("Leading words for dungeon names, e.g. 'The Frozen'.")]
		public string[] DungeonPrefixes;
		[Tooltip("Endings for points of interest when no type-specific suffix is drawn.")]
		public string[] POISuffixes;
		[Tooltip("Descriptive words this biome lends to city and POI names.")]
		public string[] Adjectives;
		[TextArea]
		public string Description;

		public BiomePhonology ToRuntime() => new()
		{
			Onsets = Onsets ?? Array.Empty<string>(),
			Nuclei = Nuclei ?? Array.Empty<string>(),
			Codas = Codas ?? Array.Empty<string>(),
			Middles = Middles ?? Array.Empty<string>(),
			SyllMin = SyllMin,
			SyllMax = SyllMax,
			DungeonSuffixes = DungeonSuffixes ?? Array.Empty<string>(),
			DungeonPrefixes = DungeonPrefixes ?? Array.Empty<string>(),
			POISuffixes = POISuffixes ?? Array.Empty<string>(),
			Adjectives = Adjectives ?? Array.Empty<string>(),
			Description = Description,
		};

		public static SerializableBiomePhonology From(BiomePhonology runtime) => new()
		{
			Onsets = runtime.Onsets,
			Nuclei = runtime.Nuclei,
			Codas = runtime.Codas,
			Middles = runtime.Middles,
			SyllMin = runtime.SyllMin,
			SyllMax = runtime.SyllMax,
			DungeonSuffixes = runtime.DungeonSuffixes,
			DungeonPrefixes = runtime.DungeonPrefixes,
			POISuffixes = runtime.POISuffixes,
			Adjectives = runtime.Adjectives,
			Description = runtime.Description,
		};

		/// <summary>True when the phonology can build at least a one-syllable root.</summary>
		public bool IsUsable()
		{
			return Onsets != null && Onsets.Length > 0 && Codas != null && Codas.Length > 0;
		}
	}

	/// <summary>Helpers that turn the serialized table rows into runtime lookups.</summary>
	public static class NamingTableUtility
	{
		/// <summary>Exact-match lookup; later rows with a repeated key win.</summary>
		public static Dictionary<string, string> ToDictionary(List<StringMapping> rows, StringComparer comparer)
		{
			var result = new Dictionary<string, string>(comparer);
			if (rows == null)
			{
				return result;
			}
			for (int i = 0; i < rows.Count; i++)
			{
				StringMapping row = rows[i];
				if (row != null && !string.IsNullOrEmpty(row.Key))
				{
					result[row.Key] = row.Value ?? "";
				}
			}
			return result;
		}

		/// <summary>Exact-match lookup for list-valued rows; later rows with a repeated key win.</summary>
		public static Dictionary<string, string[]> ToListDictionary(List<StringListMapping> rows, StringComparer comparer)
		{
			var result = new Dictionary<string, string[]>(comparer);
			if (rows == null)
			{
				return result;
			}
			for (int i = 0; i < rows.Count; i++)
			{
				StringListMapping row = rows[i];
				if (row != null && !string.IsNullOrEmpty(row.Key))
				{
					result[row.Key] = row.Values ?? Array.Empty<string>();
				}
			}
			return result;
		}

		/// <summary>Ordered copy with null and keyless rows removed, for prefix/suffix scans.</summary>
		public static List<StringMapping> ToOrdered(List<StringMapping> rows)
		{
			var result = new List<StringMapping>(rows?.Count ?? 0);
			if (rows == null)
			{
				return result;
			}
			for (int i = 0; i < rows.Count; i++)
			{
				StringMapping row = rows[i];
				if (row != null && !string.IsNullOrEmpty(row.Key))
				{
					result.Add(row);
				}
			}
			return result;
		}
	}
}
