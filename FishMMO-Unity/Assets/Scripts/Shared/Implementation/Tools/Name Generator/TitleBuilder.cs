namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Grammar-based title builder.
	///
	/// <para>A title is assembled by picking a category (honorific / epithet /
	/// rank / legend) and filling its slots from a mix of race-specific data
	/// (the race's titles and places) and the universal grammar in
	/// <see cref="NameGrammar"/> (deeds, objects, ordinals, outcomes, qualifiers).</para>
	///
	/// <para>Output examples: <c>Sir</c> · <c>Sir, Third of his Name</c> ·
	/// <c>Dame of Ashford</c> · <c>the Ironwilled</c> ·
	/// <c>Knight of the Realm, the Dragon-touched</c> ·
	/// <c>Who Sealed the Dark Gate at the Battle of Hollow Hill</c>.</para>
	/// </summary>
	public static class TitleBuilder
	{
		// Each roll threshold is named so its intent is visible at the call site.

		/// <summary>Chance to append a second clause to a completed title.</summary>
		private const double CompoundTitleChance = 0.18;

		private const double HonorificPlaceChance = 0.30;
		private const double HonorificOrdinalChance = 0.18;
		private const double HonorificStackChance = 0.12;

		private const double EpithetComposeChance = 0.35;
		private const double EpithetPlaceChance = 0.28;

		private const double RankPlaceChance = 0.25;
		private const double RankOrdinalChance = 0.12;

		private const double LegendAuthoredChance = 0.55;
		/// <summary>Chance to prefer the race's own places when composing a legend's location.</summary>
		private const double LegendRacePlaceBias = 0.70;

		/// <summary>Each meaning keyword that matches gets this chance to pin the category.</summary>
		private const double MeaningBiasChance = 0.50;

		/// <summary>Chance a runtime-injected title is used over a procedural one when both exist.</summary>
		private const double InjectedTitleChance = 0.65;

		// Weighted category selection (cumulative); the remainder is legend.
		private const double CategoryEpithetCdf = 0.40;
		private const double CategoryHonorificCdf = 0.70;
		private const double CategoryRankCdf = 0.90;

		public static (string title, string category) Build(
			string race, TitleType titleType, string meaning, DeterministicRNG rng)
		{
			if (!RaceRegistry.TryGetTitles(race, out RaceTitles titles))
			{
				return ("", "");
			}

			string category = titleType == TitleType.Any
				? PickMeaningAwareCategory(meaning, rng)
				: titleType.ToString().ToLower();

			// Injected titles win most of the time; the remainder keeps procedural
			// titles in play so the pool is never exclusive once one is registered.
			string injected = RuntimeInjection.TryPickTitle(race, category, rng);
			if (injected != null && rng.NextDouble() < InjectedTitleChance)
			{
				return (injected, category);
			}

			string title = category switch
			{
				"honorific" => BuildHonorific(race, titles, rng),
				"epithet" => BuildEpithet(race, titles, rng),
				"rank" => BuildRank(race, titles, rng),
				"legend" => BuildLegend(race, titles, rng),
				_ => "",
			};

			if (!string.IsNullOrEmpty(title) && rng.NextDouble() < CompoundTitleChance)
			{
				string extra = BuildSecondary(race, titles, category, rng);
				if (!string.IsNullOrEmpty(extra))
				{
					title = title + ", " + extra;
				}
			}

			return (title, category);
		}

		// ── Category builders ──────────────────────────────────────────

		private static string BuildHonorific(string race, RaceTitles t, DeterministicRNG rng)
		{
			if (t.Honorific == null || t.Honorific.Length == 0)
			{
				return "";
			}
			string baseTitle = GeneratorUtility.Pick(t.Honorific, rng);

			// "Lord of Ashford"
			if (rng.NextDouble() < HonorificPlaceChance && RaceRegistry.TryGetPlaces(race, out string[] places))
			{
				return $"{baseTitle} of {GeneratorUtility.Pick(places, rng)}";
			}

			// "Lord, Third of his Name"
			if (rng.NextDouble() < HonorificOrdinalChance
				&& NameGrammar.Ordinals.Length > 0 && NameGrammar.PossessivePronouns.Length > 0)
			{
				string ordinal = GeneratorUtility.Pick(NameGrammar.Ordinals, rng);
				string pronoun = GeneratorUtility.Pick(NameGrammar.PossessivePronouns, rng);
				return $"{baseTitle}, {ordinal} of {pronoun} Name";
			}

			// "Lord and Commander"
			if (t.Honorific.Length > 1 && rng.NextDouble() < HonorificStackChance)
			{
				string second = GeneratorUtility.Pick(t.Honorific, rng);
				if (second != baseTitle)
				{
					return $"{baseTitle} and {second}";
				}
			}

			return baseTitle;
		}

		private static string BuildEpithet(string race, RaceTitles t, DeterministicRNG rng)
		{
			if (t.Epithet == null || t.Epithet.Length == 0)
			{
				return "";
			}
			string epithet = GeneratorUtility.Pick(t.Epithet, rng);

			// "the Iron-wrought"
			if (rng.NextDouble() < EpithetComposeChance
				&& NameGrammar.ComposedAdjectives.Length > 0 && NameGrammar.ComposedQualifiers.Length > 0)
			{
				string adjective = GeneratorUtility.Pick(NameGrammar.ComposedAdjectives, rng);
				string qualifier = GeneratorUtility.Pick(NameGrammar.ComposedQualifiers, rng);
				epithet = $"the {adjective}-{qualifier}";
			}

			// "the Unyielding of Karak"
			if (rng.NextDouble() < EpithetPlaceChance
				&& RaceRegistry.TryGetPlaces(race, out string[] places)
				&& NameGrammar.PlaceEpithetPatterns.Length > 0)
			{
				string place = GeneratorUtility.Pick(places, rng);
				string pattern = GeneratorUtility.Pick(NameGrammar.PlaceEpithetPatterns, rng);
				return $"{epithet} {string.Format(pattern, place)}";
			}

			return epithet;
		}

		private static string BuildRank(string race, RaceTitles t, DeterministicRNG rng)
		{
			if (t.Rank == null || t.Rank.Length == 0)
			{
				return "";
			}
			string rank = GeneratorUtility.Pick(t.Rank, rng);

			// "Warden of the Silverwood" — skipped when the rank already carries an "of".
			if (rng.NextDouble() < RankPlaceChance
				&& RaceRegistry.TryGetPlaces(race, out string[] places)
				&& !rank.Contains(" of "))
			{
				return $"{rank} of {GeneratorUtility.Pick(places, rng)}";
			}

			// "Third Warden"
			if (rng.NextDouble() < RankOrdinalChance && NameGrammar.Ordinals.Length > 0)
			{
				return $"{GeneratorUtility.Pick(NameGrammar.Ordinals, rng)} {rank}";
			}

			return rank;
		}

		private static string BuildLegend(string race, RaceTitles t, DeterministicRNG rng)
		{
			if (t.Legend != null && t.Legend.Length > 0 && rng.NextDouble() < LegendAuthoredChance)
			{
				return GeneratorUtility.Pick(t.Legend, rng);
			}
			return ComposeLegend(race, rng);
		}

		private static string ComposeLegend(string race, DeterministicRNG rng)
		{
			if (NameGrammar.DeedVerbs.Length == 0 || NameGrammar.DeedObjects.Length == 0)
			{
				return "";
			}

			string deed = GeneratorUtility.Pick(NameGrammar.DeedVerbs, rng);
			string obj = GeneratorUtility.Pick(NameGrammar.DeedObjects, rng);

			string place = null;
			if (RaceRegistry.TryGetPlaces(race, out string[] places) && rng.NextDouble() < LegendRacePlaceBias)
			{
				place = GeneratorUtility.Pick(places, rng);
			}
			else if (NameGrammar.UniversalPlaces.Length > 0)
			{
				place = GeneratorUtility.Pick(NameGrammar.UniversalPlaces, rng);
			}

			double roll = rng.NextDouble();
			if (roll < 0.20 || place == null)
			{
				return $"Who {deed} {obj}";
			}
			if (roll < 0.50)
			{
				return $"Who {deed} {obj} at {place}";
			}
			if (roll < 0.70 && NameGrammar.EraQualifiers.Length > 0)
			{
				return $"Who {deed} {obj} in the {GeneratorUtility.Pick(NameGrammar.EraQualifiers, rng)}";
			}
			if (roll < 0.85 && NameGrammar.Outcomes.Length > 0)
			{
				return $"Who {deed} {obj} and {GeneratorUtility.Pick(NameGrammar.Outcomes, rng)}";
			}
			if (NameGrammar.BattleQualifiers.Length > 0)
			{
				return $"Who {deed} {obj} at the {GeneratorUtility.Pick(NameGrammar.BattleQualifiers, rng)} of {place}";
			}
			return $"Who {deed} {obj} at {place}";
		}

		private static string BuildSecondary(string race, RaceTitles t, string primaryCategory, DeterministicRNG rng)
		{
			// A different category from the primary, for variety.
			string pick = primaryCategory switch
			{
				"honorific" => rng.NextDouble() < 0.6 ? "epithet" : "rank",
				"rank" => rng.NextDouble() < 0.6 ? "epithet" : "legend",
				"epithet" => rng.NextDouble() < 0.5 ? "rank" : "legend",
				"legend" => "epithet",
				_ => "epithet",
			};

			return pick switch
			{
				"honorific" => BuildHonorific(race, t, rng),
				"epithet" => BuildEpithet(race, t, rng),
				"rank" => BuildRank(race, t, rng),
				"legend" => LowerFirstWord(BuildLegend(race, t, rng)),
				_ => "",
			};
		}

		private static string LowerFirstWord(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				return s;
			}
			return s.StartsWith("Who ") ? "who " + s.Substring(4) : s;
		}

		// ── Category selection ─────────────────────────────────────────

		private static string PickMeaningAwareCategory(string meaning, DeterministicRNG rng)
		{
			string lower = (meaning ?? "").ToLower();
			var bias = NameGrammar.MeaningTitleBias;
			for (int i = 0; i < bias.Count; i++)
			{
				if (lower.Contains(bias[i].Key.ToLower()) && rng.NextDouble() < MeaningBiasChance)
				{
					return bias[i].Value;
				}
			}
			return PickWeightedCategory(rng);
		}

		private static string PickWeightedCategory(DeterministicRNG rng)
		{
			double roll = rng.NextDouble();
			if (roll < CategoryEpithetCdf) return "epithet";
			if (roll < CategoryHonorificCdf) return "honorific";
			if (roll < CategoryRankCdf) return "rank";
			return "legend";
		}
	}
}
