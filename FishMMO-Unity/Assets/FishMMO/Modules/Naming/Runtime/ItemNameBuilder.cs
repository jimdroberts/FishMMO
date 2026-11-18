using System;
using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Builds legendary item names from race phonology and the item vocabulary
	/// in <see cref="NameGrammar"/>.
	/// Patterns:
	///   "[The] [Epithet] [Root][TypeSuffix]"      — e.g. "The Ashen Gorveil"
	///   "[Root][TypeSuffix], [Epithet]"            — e.g. "Thralvane, the Unbroken"
	///   "[TypeNoun] of [Root]"                     — e.g. "Blade of Krimuur"
	/// </summary>
	internal static class ItemNameBuilder
	{
		/// <summary>Chance pattern A gets a legendary prefix.</summary>
		private const double LegendaryPrefixChance = 0.40;
		/// <summary>Chance pattern C gets an epithet tail.</summary>
		private const double EpithetTailChance = 0.30;
		/// <summary>Chance a root syllable is preceded by a middle.</summary>
		private const double MiddleChance = 0.50;
		/// <summary>Chance a root ends in a coda.</summary>
		private const double CodaChance = 0.40;

		/// <summary>Build a legendary item name from race phonology.</summary>
		public static (string name, string meaning, List<string> fragments) Build(
			RacePhonology ph, ItemType itemType, DeterministicRNG rng,
			LibraryContext library = null)
		{
			string typeKey = itemType == ItemType.Any ? PickRandomType(rng) : itemType.ToString().ToLower();

			// A library with matching names is used exclusively — no procedural fallback.
			if (library != null && library.HasAny)
			{
				return BuildLibrarySeeded(ph, typeKey, rng, library);
			}

			return BuildProcedural(ph, typeKey, rng);
		}

		// ── Procedural ─────────────────────────────────────────────────

		private static (string name, string meaning, List<string> fragments) BuildProcedural(
			RacePhonology ph, string typeKey, DeterministicRNG rng)
		{
			var (root, rootFragments) = BuildRoot(ph, rng);
			var fragments = new List<string>(rootFragments);

			double pattern = rng.NextDouble();

			string name;
			if (pattern < 0.35)
			{
				// Pattern A: [Root][TypeSuffix] — "Gorvane", "Thralrender"
				string suffix = PickTypeSuffix(typeKey, rng);
				fragments.Add(suffix);
				name = GeneratorUtility.Capitalize(GeneratorUtility.Smooth(root + suffix));

				if (NameGrammar.ItemLegendaryPrefixes.Length > 0 && rng.NextDouble() < LegendaryPrefixChance)
				{
					string prefix = GeneratorUtility.Pick(NameGrammar.ItemLegendaryPrefixes, rng);
					fragments.Insert(0, prefix);
					name = $"{prefix} {name}";
				}
			}
			else if (pattern < 0.65)
			{
				// Pattern B: [Root][Suffix], [Epithet] — "Gorvane, the Unbroken"
				string suffix = PickTypeSuffix(typeKey, rng);
				fragments.Add(suffix);
				string coreName = GeneratorUtility.Capitalize(GeneratorUtility.Smooth(root + suffix));
				string epithet = PickEpithet(rng);
				if (string.IsNullOrEmpty(epithet))
				{
					name = coreName;
				}
				else
				{
					fragments.Add(epithet);
					name = $"{coreName}, {epithet}";
				}
			}
			else
			{
				// Pattern C: [TypeNoun] of [Root] — "Blade of Gorvane"
				string noun = GeneratorUtility.Pick(GetTypeNouns(typeKey), rng);
				fragments.Insert(0, noun);
				fragments.Add("of");
				string coreName = GeneratorUtility.Capitalize(GeneratorUtility.Smooth(root));
				name = $"{noun} of {coreName}";

				if (rng.NextDouble() < EpithetTailChance)
				{
					string epithet = PickEpithet(rng);
					if (!string.IsNullOrEmpty(epithet))
					{
						fragments.Add(epithet);
						name = $"{name}, {epithet}";
					}
				}
			}

			string meaning = DeriveMeaning(root, typeKey, ph);
			return (name, meaning, fragments);
		}

		// ── Internals ──────────────────────────────────────────────────

		private static (string root, List<string> rootFragments) BuildRoot(RacePhonology ph, DeterministicRNG rng)
		{
			int syllables = rng.Next(ph.SyllMin, ph.SyllMax + 1);
			syllables = Math.Max(1, Math.Min(syllables, 3));

			string onset = GeneratorUtility.Pick(ph.Onsets, rng);
			var fragments = new List<string> { onset };
			string raw = onset;

			for (int s = 1; s < syllables; s++)
			{
				if (ph.Middles != null && ph.Middles.Length > 0 && rng.NextDouble() < MiddleChance)
				{
					string mid = GeneratorUtility.Pick(ph.Middles, rng);
					raw += mid;
					fragments.Add(mid);
				}
				string nucleus = GeneratorUtility.Pick(ph.Nuclei, rng);
				raw += nucleus;
				fragments.Add(nucleus);
			}

			if (ph.Codas != null && ph.Codas.Length > 0 && rng.NextDouble() < CodaChance)
			{
				string coda = GeneratorUtility.Pick(ph.Codas, rng);
				raw += coda;
				fragments.Add(coda);
			}

			return (raw, fragments);
		}

		private static string[] GetTypeNouns(string typeKey)
		{
			if (NameGrammar.ItemTypeNouns.TryGetValue(typeKey, out string[] nouns) && nouns.Length > 0)
			{
				return nouns;
			}
			return NameGrammar.ItemGenericNouns.Length > 0
				? NameGrammar.ItemGenericNouns
				: new[] { GeneratorUtility.Capitalize(typeKey) };
		}

		private static string PickTypeSuffix(string typeKey, DeterministicRNG rng)
		{
			if (NameGrammar.ItemTypeSuffixes.TryGetValue(typeKey, out string[] suffixes) && suffixes.Length > 0)
			{
				return GeneratorUtility.Pick(suffixes, rng);
			}
			if (NameGrammar.ItemTypeSuffixes.TryGetValue("weapon", out string[] weapon) && weapon.Length > 0)
			{
				return GeneratorUtility.Pick(weapon, rng);
			}
			return typeKey;
		}

		private static string PickEpithet(DeterministicRNG rng)
		{
			return NameGrammar.ItemEpithets.Length > 0
				? GeneratorUtility.Pick(NameGrammar.ItemEpithets, rng)
				: "";
		}

		private static string PickRandomType(DeterministicRNG rng)
		{
			double roll = rng.NextDouble();
			if (roll < 0.30) return "weapon";
			if (roll < 0.50) return "armor";
			if (roll < 0.70) return "artifact";
			if (roll < 0.85) return "relic";
			return "trinket";
		}

		private static string DeriveMeaning(string root, string typeKey, RacePhonology ph)
		{
			var parts = new List<string>();

			string onsetMeaning = NameGrammar.MatchPrefix(NameGrammar.BiomeMeaningOnsets, root);
			if (onsetMeaning != null)
			{
				parts.Add(onsetMeaning);
			}

			parts.Add(typeKey);

			if (ph.Description != null)
			{
				string description = ph.Description.Split('—')[0].Trim().ToLower();
				if (description.Length > 0 && description.Length < 40)
				{
					parts.Add(description);
				}
			}

			return string.Join(" ", parts).Trim();
		}

		// ── Library-seeded patterns ────────────────────────────────────

		private static (string name, string meaning, List<string> fragments) BuildLibrarySeeded(
			RacePhonology ph, string typeKey, DeterministicRNG rng, LibraryContext lib)
		{
			// Characters weigh more: legendary items most often reference heroes and deities.
			var pool = new List<(string entityName, string source, int weight)>();

			if (lib.CharacterNames != null)
			{
				for (int i = 0; i < lib.CharacterNames.Count; i++)
				{
					pool.Add((lib.CharacterNames[i], "Character", 3));
				}
			}
			if (lib.CityNames != null)
			{
				for (int i = 0; i < lib.CityNames.Count; i++)
				{
					pool.Add((lib.CityNames[i], "City", 2));
				}
			}
			if (lib.DungeonNames != null)
			{
				for (int i = 0; i < lib.DungeonNames.Count; i++)
				{
					pool.Add((lib.DungeonNames[i], "Dungeon", 1));
				}
			}
			if (lib.POINames != null)
			{
				for (int i = 0; i < lib.POINames.Count; i++)
				{
					pool.Add((lib.POINames[i], "POI", 1));
				}
			}

			if (pool.Count == 0)
			{
				return BuildProcedural(ph, typeKey, rng);
			}

			int totalWeight = 0;
			for (int i = 0; i < pool.Count; i++)
			{
				totalWeight += pool[i].weight;
			}
			int pick = rng.Next(totalWeight);
			int accum = 0;
			var chosen = pool[0];
			for (int i = 0; i < pool.Count; i++)
			{
				accum += pool[i].weight;
				if (pick < accum)
				{
					chosen = pool[i];
					break;
				}
			}

			string entityName = chosen.entityName;
			string shortName = StripEpithet(entityName);

			var fragments = new List<string>();
			string name;
			string meaning;

			double pattern = rng.NextDouble();
			bool isPlace = chosen.source != "Character";
			string[] placeRelations = NameGrammar.ItemPlaceRelations.Length > 0
				? NameGrammar.ItemPlaceRelations
				: new[] { "of" };
			string[] heroRelations = NameGrammar.ItemHeroRelations.Length > 0
				? NameGrammar.ItemHeroRelations
				: new[] { "'s" };

			if (pattern < 0.35)
			{
				// "[TypeNoun] of [EntityName]" — "Blade of Aelion"
				string noun = GeneratorUtility.Pick(GetTypeNouns(typeKey), rng);
				string rel = isPlace ? GeneratorUtility.Pick(placeRelations, rng) : "of";
				fragments.Add(noun);
				fragments.Add(rel);
				fragments.Add(shortName);
				name = $"{noun} {rel} {shortName}";
				meaning = $"legendary {typeKey} linked to {entityName}";
			}
			else if (pattern < 0.60)
			{
				// "[EntityName]'s [TypeNoun]" — "Aelion's Blade"
				string noun = GeneratorUtility.Pick(GetTypeNouns(typeKey), rng);
				if (isPlace)
				{
					string rel = GeneratorUtility.Pick(placeRelations, rng) + " " + shortName;
					fragments.Add(noun);
					fragments.Add(rel);
					name = $"{noun} {rel}";
				}
				else
				{
					string rel = shortName + GeneratorUtility.Pick(heroRelations, rng);
					fragments.Add(rel);
					fragments.Add(noun);
					name = $"{rel} {noun}";
				}
				meaning = $"{typeKey} bound to {entityName}";
			}
			else if (pattern < 0.80)
			{
				// "[LegendaryPrefix] [Root][Suffix] of [EntityName]"
				var (root, rootFragments) = BuildRoot(ph, rng);
				string suffix = PickTypeSuffix(typeKey, rng);
				string coreName = GeneratorUtility.Capitalize(GeneratorUtility.Smooth(root + suffix));
				string prefix = NameGrammar.ItemLegendaryPrefixes.Length > 0
					? GeneratorUtility.Pick(NameGrammar.ItemLegendaryPrefixes, rng)
					: "The";
				string rel = isPlace ? GeneratorUtility.Pick(placeRelations, rng) : "of";
				fragments.Add(prefix);
				fragments.AddRange(rootFragments);
				fragments.Add(suffix);
				fragments.Add(rel);
				fragments.Add(shortName);
				name = $"{prefix} {coreName}, {rel} {shortName}";
				meaning = $"{typeKey} forged in connection to {entityName}";
			}
			else
			{
				// "[Root][Suffix], [Epithet] of [EntityName]"
				var (root, rootFragments) = BuildRoot(ph, rng);
				string suffix = PickTypeSuffix(typeKey, rng);
				string coreName = GeneratorUtility.Capitalize(GeneratorUtility.Smooth(root + suffix));
				string epithet = PickEpithet(rng);
				string rel = isPlace ? GeneratorUtility.Pick(placeRelations, rng) : "of";
				fragments.AddRange(rootFragments);
				fragments.Add(suffix);
				if (!string.IsNullOrEmpty(epithet))
				{
					fragments.Add(epithet);
				}
				fragments.Add(rel);
				fragments.Add(shortName);
				name = string.IsNullOrEmpty(epithet)
					? $"{coreName}, {rel} {shortName}"
					: $"{coreName}, {epithet} {rel} {shortName}";
				meaning = $"{typeKey} steeped in the legend of {entityName}";
			}

			return (name, meaning, fragments);
		}

		/// <summary>Strips ", the Epithet" or " (note)" from a name, leaving the root name.</summary>
		private static string StripEpithet(string fullName)
		{
			if (string.IsNullOrEmpty(fullName))
			{
				return fullName;
			}
			int comma = fullName.IndexOf(',');
			if (comma > 0)
			{
				return fullName.Substring(0, comma).Trim();
			}
			int paren = fullName.IndexOf('(');
			if (paren > 0)
			{
				return fullName.Substring(0, paren).Trim();
			}
			return fullName;
		}
	}
}
