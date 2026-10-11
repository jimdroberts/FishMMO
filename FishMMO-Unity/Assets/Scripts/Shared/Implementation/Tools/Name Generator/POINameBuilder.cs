using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>Everything one point-of-interest name is composed from, resolved by <see cref="NameGenerator"/>.</summary>
	internal sealed class PlaceContext
	{
		public POIType Kind;
		/// <summary>The biome's phonology and vocabulary; null for a biome-free kind (rivers, lakes, falls).</summary>
		public BiomePhonology Biome;
		public BiomeClimateVariant Variant;
		/// <summary>The race the place belongs to; null names from the biome alone and skips race slots.</summary>
		public RaceTemplate Race;
		/// <summary>Draws the composition and every slot but the root.</summary>
		public DeterministicRNG Rng;
		/// <summary>Draws the root. The same RNG as <see cref="Rng"/> unless the kind is biome-free.</summary>
		public DeterministicRNG RootRng;
		/// <summary>Names the settlement behind a {City} slot; null when settlements cannot be named here.</summary>
		public Func<string> City;
		/// <summary>Names the dungeon behind a {Dungeon} slot; null when there is no biome to name it from.</summary>
		public Func<string> Dungeon;
		/// <summary>The phonology {Founder} names are drawn from; null when there is none.</summary>
		public RacePhonology FounderPhonology;
	}

	/// <summary>
	/// Composes point-of-interest names from <see cref="PlaceNameTemplate"/>s: "Cave of Orcs", "Goblin
	/// Warren", "The Drowned Grotto", "Morrowmere", "Aldric's Rest", "Shrine of the Weeping Moon".
	/// </summary>
	/// <remarks>
	/// <para>
	/// The engine is the title builder's: the usable compositions for a kind are those whose every slot
	/// can be filled for this request, one is drawn by weight, and a few draws are tried for one that fits
	/// the length budget and does not say the same word twice. Compositions come from the grammar asset,
	/// or <see cref="PlaceNameDefaults.Templates"/> when it has none.
	/// </para>
	/// <para>
	/// Every kind always names: when nothing composes, "{Root} {Type}" does, and if even that is too long,
	/// the type word alone. A name is never empty and never longer than <see cref="PlaceNameDefaults.MaxLength"/>.
	/// </para>
	/// </remarks>
	internal static class POINameBuilder
	{
		/// <summary>Chance the biome's own type words are used over the global ones when it has any for the kind.</summary>
		private const double BiomeTypeWordChance = 0.60;
		/// <summary>Chance {Adjective} is the biome's (or its climate variant's) rather than a generic one.</summary>
		private const double BiomeAdjectiveChance = 0.70;
		/// <summary>Chance a variant's own adjective is used over the biome's when the variant has any.</summary>
		private const double VariantAdjectiveChance = 0.65;
		/// <summary>Chance {Noun} is one of the biome's landmark words rather than a generic noun.</summary>
		private const double BiomeNounChance = 0.25;
		/// <summary>Chance a two-syllable biome root grows a coda.</summary>
		private const double CodaChance = 0.30;
		/// <summary>Chance a water root grows a middle syllable ("Av-er-on").</summary>
		private const double WaterMiddleChance = 0.40;
		/// <summary>How many compositions are tried before settling for the shortest usable one.</summary>
		private const int Attempts = 8;
		/// <summary>Longest biome root that still grows a coda.</summary>
		private const int MaxRootLength = 7;
		/// <summary>Founder names longer than this are redrawn, so "{Founder}'s {Type}" fits the budget.</summary>
		private const int FounderMaxLength = 10;

		private static readonly Regex SlotPattern = new Regex(@"\{(\w+)\}", RegexOptions.Compiled);
		private static readonly Regex Spaces = new Regex(@"\s{2,}", RegexOptions.Compiled);
		private static readonly HashSet<string> SmallWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"the", "of", "and", "a", "an", "in", "at", "on",
		};

		/// <summary>Draws a kind for <see cref="POIType.Any"/> from the grammar's kind weights, else the built-in ones.</summary>
		public static POIType PickKind(DeterministicRNG rng)
		{
			Array kinds = Enum.GetValues(typeof(POIType));
			int total = 0;
			for (int i = 0; i < kinds.Length; i++)
			{
				total += WeightOf((POIType)kinds.GetValue(i));
			}
			if (total <= 0)
			{
				return POIType.Landmark;
			}
			int roll = rng.Next(total);
			for (int i = 0; i < kinds.Length; i++)
			{
				POIType kind = (POIType)kinds.GetValue(i);
				roll -= WeightOf(kind);
				if (roll < 0)
				{
					return kind;
				}
			}
			return POIType.Landmark;
		}

		private static int WeightOf(POIType kind)
		{
			if (kind == POIType.Any)
			{
				return 0;
			}
			return NameGrammar.POIKindWeights.TryGetValue(kind, out int weight) ? weight : PlaceNameDefaults.KindWeight(kind);
		}

		/// <summary>The type words a kind is called by: the grammar's row, else the built-in words. Never empty.</summary>
		public static string[] GlobalTypeWords(POIType kind)
		{
			return NameGrammar.POITypeSuffixes.TryGetValue(PlaceNameDefaults.KeyOf(kind), out string[] words) && words != null && words.Length > 0
				? words
				: PlaceNameDefaults.TypeWordsFor(kind);
		}

		/// <summary>The compositions in force: the grammar's, else the built-in set.</summary>
		public static IReadOnlyList<PlaceNameTemplate> Templates =>
			NameGrammar.PlaceNameTemplates.Count > 0 ? NameGrammar.PlaceNameTemplates : PlaceNameDefaults.Templates;

		public static (string name, string meaning, List<string> fragments) Build(PlaceContext ctx)
		{
			var state = new BuildState(ctx);
			List<PlaceNameTemplate> usable = UsableTemplates(state);

			string best = null;
			Composition bestComposition = null;
			for (int attempt = 0; attempt < Attempts && usable.Count > 0; attempt++)
			{
				PlaceNameTemplate template = PickWeighted(usable, ctx.Rng);
				Composition candidate = Fill(template.Pattern, state);
				if (candidate == null || candidate.Name.Length == 0 || RepeatsAWord(candidate.Name))
				{
					continue;
				}
				if (candidate.Name.Length <= PlaceNameDefaults.MaxLength)
				{
					best = candidate.Name;
					bestComposition = candidate;
					break;
				}
				if (best == null || candidate.Name.Length < best.Length)
				{
					best = candidate.Name;
					bestComposition = candidate;
				}
			}

			if (best == null || best.Length > PlaceNameDefaults.MaxLength)
			{
				// Nothing composed within budget: the plainest name there is, then the bare type word.
				bestComposition = Fill("{Root} {Type}", state);
				if (bestComposition == null || bestComposition.Name.Length == 0 || bestComposition.Name.Length > PlaceNameDefaults.MaxLength)
				{
					bestComposition = Fill("{Type}", state);
				}
				best = bestComposition.Name;
				if (best.Length > PlaceNameDefaults.MaxLength)
				{
					best = best.Substring(0, PlaceNameDefaults.MaxLength).TrimEnd();
				}
			}

			return (best, DeriveMeaning(state, bestComposition), bestComposition.Fragments);
		}

		/// <summary>
		/// Fills one pattern for a context, or null when a slot cannot be filled (a race slot with no race).
		/// The seam tests use to pin a composition without depending on which one the weights draw.
		/// </summary>
		internal static string FillPattern(PlaceContext ctx, string pattern)
		{
			var state = new BuildState(ctx);
			return CanFill(pattern, state) ? Fill(pattern, state)?.Name : null;
		}

		// ── Composition ────────────────────────────────────────────────

		/// <summary>One filled composition and what went into it.</summary>
		private sealed class Composition
		{
			public string Name;
			public readonly List<string> Fragments = new List<string>();
			public string Adjective;
			public bool UsedRace;
			public bool UsedRoot;
		}

		/// <summary>Per-build state: the lazily drawn root, so every {Root} in one name is the same word.</summary>
		private sealed class BuildState
		{
			public readonly PlaceContext Ctx;
			public readonly BiomePhonology RootPhonology;
			private string root;
			private string rawRoot;

			public BuildState(PlaceContext ctx)
			{
				Ctx = ctx;
				RootPhonology = ctx.Biome ?? NameGrammar.WaterPhonology ?? PlaceNameDefaults.WaterPhonology();
			}

			public bool BiomeFree => Ctx.Biome == null;

			/// <summary>The root as drawn, lower-case-ish, for fusing and meaning.</summary>
			public string RawRoot
			{
				get
				{
					EnsureRoot();
					return rawRoot;
				}
			}

			public string Root
			{
				get
				{
					EnsureRoot();
					return root;
				}
			}

			private void EnsureRoot()
			{
				if (root != null)
				{
					return;
				}
				rawRoot = BiomeFree ? BuildWaterRoot(RootPhonology, Ctx.RootRng) : BuildBiomeRoot(RootPhonology, Ctx.RootRng);
				root = GeneratorUtility.Capitalize(GeneratorUtility.Smooth(rawRoot.ToLowerInvariant()));
			}
		}

		private static List<PlaceNameTemplate> UsableTemplates(BuildState state)
		{
			IReadOnlyList<PlaceNameTemplate> source = Templates;
			var usable = new List<PlaceNameTemplate>();
			for (int i = 0; i < source.Count; i++)
			{
				PlaceNameTemplate t = source[i];
				if (t != null && !string.IsNullOrWhiteSpace(t.Pattern) && t.Weight > 0 && t.Applies(state.Ctx.Kind) && CanFill(t.Pattern, state))
				{
					usable.Add(t);
				}
			}
			return usable;
		}

		private static PlaceNameTemplate PickWeighted(List<PlaceNameTemplate> templates, DeterministicRNG rng)
		{
			int total = 0;
			for (int i = 0; i < templates.Count; i++)
			{
				total += Math.Max(1, templates[i].Weight);
			}
			int roll = rng.Next(total);
			for (int i = 0; i < templates.Count; i++)
			{
				roll -= Math.Max(1, templates[i].Weight);
				if (roll < 0)
				{
					return templates[i];
				}
			}
			return templates[templates.Count - 1];
		}

		/// <summary>"{Root}{Suffix}" written together is one fused word.</summary>
		private static string Normalise(string pattern) => pattern.Replace("{Root}{Suffix}", "{Fused}").Replace("{root}{suffix}", "{Fused}");

		private static bool CanFill(string pattern, BuildState state)
		{
			PlaceContext ctx = state.Ctx;
			foreach (Match m in SlotPattern.Matches(Normalise(pattern)))
			{
				switch (m.Groups[1].Value.ToLowerInvariant())
				{
					case "type":
					case "adjective":
					case "noun":
						break;
					case "root":
					case "fused":
					case "suffix":
						if (state.RootPhonology?.Onsets == null || state.RootPhonology.Onsets.Length == 0)
						{
							return false;
						}
						break;
					case "raceplural":
					case "raceadjective":
						if (ctx.Race == null)
						{
							return false;
						}
						break;
					case "founder":
						if (!IsUsable(ctx.FounderPhonology))
						{
							return false;
						}
						break;
					case "city":
						if (ctx.City == null)
						{
							return false;
						}
						break;
					case "dungeon":
						if (ctx.Dungeon == null)
						{
							return false;
						}
						break;
					default:
						return false;
				}
			}
			return true;
		}

		private static Composition Fill(string pattern, BuildState state)
		{
			string normalised = Normalise(pattern);
			var composition = new Composition();
			var sb = new StringBuilder(normalised.Length + 32);
			int last = 0;
			foreach (Match m in SlotPattern.Matches(normalised))
			{
				sb.Append(normalised, last, m.Index - last);
				string value = Resolve(m.Groups[1].Value.ToLowerInvariant(), state, composition);
				if (string.IsNullOrEmpty(value))
				{
					return null;
				}
				composition.Fragments.Add(value);
				sb.Append(value);
				last = m.Index + m.Length;
			}
			sb.Append(normalised, last, normalised.Length - last);
			composition.Name = Tidy(sb.ToString());
			return composition;
		}

		private static string Resolve(string slot, BuildState state, Composition composition)
		{
			PlaceContext ctx = state.Ctx;
			switch (slot)
			{
				case "type":
					return TypeWord(state);
				case "root":
					composition.UsedRoot = true;
					return state.Root;
				case "fused":
					composition.UsedRoot = true;
					return GeneratorUtility.Capitalize(GeneratorUtility.Smooth(state.RawRoot.ToLowerInvariant() + FusedSuffix(state)));
				case "suffix":
					return FusedSuffix(state);
				case "adjective":
				{
					string adjective = Adjective(state);
					composition.Adjective = adjective;
					return adjective;
				}
				case "noun":
					return Noun(state);
				case "raceplural":
					composition.UsedRace = true;
					return RaceNaming.PluralOf(ctx.Race);
				case "raceadjective":
					composition.UsedRace = true;
					return RaceNaming.AdjectiveOf(ctx.Race);
				case "founder":
					return Founder(ctx);
				case "city":
					return Safe(ctx.City);
				case "dungeon":
					return Safe(ctx.Dungeon);
				default:
					return null;
			}
		}

		private static string Safe(Func<string> factory)
		{
			try
			{
				return factory?.Invoke() ?? "";
			}
			catch (Exception e) when (e is ArgumentException || e is IndexOutOfRangeException)
			{
				// A settlement or dungeon that cannot be named here (no usable race or phonology) simply
				// does not compose; the next composition or the fallback names the place.
				return "";
			}
		}

		// ── Slot vocabularies ──────────────────────────────────────────

		private static string TypeWord(BuildState state)
		{
			PlaceContext ctx = state.Ctx;
			string key = PlaceNameDefaults.KeyOf(ctx.Kind);
			string[] global = GlobalTypeWords(ctx.Kind);
			if (!state.BiomeFree && ctx.Biome.POITypeWords != null
				&& ctx.Biome.POITypeWords.TryGetValue(key, out string[] own) && own != null && own.Length > 0
				&& ctx.Rng.NextDouble() < BiomeTypeWordChance)
			{
				return GeneratorUtility.Pick(own, ctx.Rng);
			}
			return GeneratorUtility.Pick(global, ctx.Rng);
		}

		private static string FusedSuffix(BuildState state)
		{
			PlaceContext ctx = state.Ctx;
			string[] suffixes = null;
			if (NameGrammar.PlaceFusedSuffixes.TryGetValue(PlaceNameDefaults.KeyOf(ctx.Kind), out string[] authored) && authored != null && authored.Length > 0)
			{
				suffixes = authored;
			}
			suffixes ??= PlaceNameDefaults.FusedSuffixesFor(ctx.Kind);
			if (suffixes == null || suffixes.Length == 0)
			{
				suffixes = state.RootPhonology.Codas;
			}
			if (suffixes == null || suffixes.Length == 0)
			{
				return "";
			}
			return GeneratorUtility.Pick(suffixes, ctx.Rng).ToLowerInvariant().Replace(" ", "").Replace("-", "");
		}

		private static string[] GenericAdjectives => NameGrammar.PlaceAdjectives.Length > 0 ? NameGrammar.PlaceAdjectives : PlaceNameDefaults.Adjectives;
		private static string[] GenericNouns => NameGrammar.PlaceNouns.Length > 0 ? NameGrammar.PlaceNouns : PlaceNameDefaults.Nouns;

		private static string Adjective(BuildState state)
		{
			PlaceContext ctx = state.Ctx;
			if (!state.BiomeFree)
			{
				string[] variantAdjectives = ctx.Variant?.Adjectives;
				bool any = (ctx.Biome.Adjectives != null && ctx.Biome.Adjectives.Length > 0) || (variantAdjectives != null && variantAdjectives.Length > 0);
				if (any && ctx.Rng.NextDouble() < BiomeAdjectiveChance)
				{
					string flavoured = GeneratorUtility.PickFlavoured(variantAdjectives, ctx.Biome.Adjectives, VariantAdjectiveChance, ctx.Rng);
					if (!string.IsNullOrWhiteSpace(flavoured))
					{
						return GeneratorUtility.Capitalize(flavoured.Trim());
					}
				}
			}
			return GeneratorUtility.Pick(GenericAdjectives, ctx.Rng);
		}

		/// <summary>Kinds whose {Noun} is a proper name (a ship, an inn sign), never a landform word.</summary>
		private static bool NamesWithSigns(POIType kind) =>
			kind == POIType.Wreck || kind == POIType.SunkenShip || kind == POIType.Waystation;

		private static string Noun(BuildState state)
		{
			PlaceContext ctx = state.Ctx;
			if (!state.BiomeFree && !NamesWithSigns(ctx.Kind) && ctx.Biome.POISuffixes != null && ctx.Biome.POISuffixes.Length > 0 && ctx.Rng.NextDouble() < BiomeNounChance)
			{
				return GeneratorUtility.Capitalize(GeneratorUtility.Pick(ctx.Biome.POISuffixes, ctx.Rng).Trim());
			}
			return GeneratorUtility.Pick(GenericNouns, ctx.Rng);
		}

		private static string Founder(PlaceContext ctx)
		{
			if (!IsUsable(ctx.FounderPhonology))
			{
				return "";
			}
			string shortest = null;
			for (int i = 0; i < 3; i++)
			{
				string name;
				try
				{
					(name, _, _) = NameBuilder.Build(ctx.FounderPhonology, CharacterGender.Unspecified, ctx.Rng);
				}
				catch (IndexOutOfRangeException)
				{
					// A phonology too sparse for its own syllable count; names from it never compose.
					return "";
				}
				if (string.IsNullOrEmpty(name))
				{
					continue;
				}
				if (name.Length <= FounderMaxLength)
				{
					return name;
				}
				if (shortest == null || name.Length < shortest.Length)
				{
					shortest = name;
				}
			}
			return shortest ?? "";
		}

		private static bool IsUsable(RacePhonology ph)
		{
			return ph != null && ph.Onsets != null && ph.Onsets.Length > 0 && ph.Nuclei != null && ph.Nuclei.Length > 0
				&& ((ph.Codas != null && ph.Codas.Length > 0) || (ph.WeightedCodas != null && ph.WeightedCodas.Length > 0));
		}

		// ── Roots ──────────────────────────────────────────────────────

		/// <summary>One or two syllables of the biome's phonology, as the old landmark names were built.</summary>
		private static string BuildBiomeRoot(BiomePhonology ph, DeterministicRNG rng)
		{
			int complexity = rng.Next(1, 3);
			string raw = GeneratorUtility.Pick(ph.Onsets, rng);
			if (complexity == 2 && ph.Nuclei != null && ph.Nuclei.Length > 0)
			{
				raw += GeneratorUtility.Pick(ph.Nuclei, rng);
				if (ph.Codas != null && ph.Codas.Length > 0 && rng.NextDouble() < CodaChance)
				{
					// Biome codas are often whole words ("marsh", "slough"); a root that grows past a
					// short word stops reading as a name and starts reading as two ("Gleenoomarsh Font").
					string coda = GeneratorUtility.Pick(ph.Codas, rng);
					if (raw.Length + coda.Length <= MaxRootLength)
					{
						raw += coda;
					}
				}
			}
			return raw;
		}

		/// <summary>Onset, an optional middle, then a vowel-led coda: Av-on, Tam-ar, Sev-ern, Der-ow-ent.</summary>
		private static string BuildWaterRoot(BiomePhonology ph, DeterministicRNG rng)
		{
			string raw = GeneratorUtility.Pick(ph.Onsets, rng);
			if (ph.Middles != null && ph.Middles.Length > 0 && rng.NextDouble() < WaterMiddleChance)
			{
				raw += GeneratorUtility.Pick(ph.Middles, rng);
			}
			if (ph.Codas != null && ph.Codas.Length > 0)
			{
				raw += GeneratorUtility.Pick(ph.Codas, rng);
			}
			else if (ph.Nuclei != null && ph.Nuclei.Length > 0)
			{
				raw += GeneratorUtility.Pick(ph.Nuclei, rng);
			}
			return raw;
		}

		// ── Clean-up and checks ────────────────────────────────────────

		/// <summary>Collapses spaces, keeps "of the" lower case after a slot that began with "The", capitalises the name.</summary>
		private static string Tidy(string name)
		{
			name = Spaces.Replace(name, " ").Trim();
			name = name.Replace(" of The ", " of the ").Replace(" Of The ", " of the ");
			return GeneratorUtility.Capitalize(name);
		}

		/// <summary>"Ice Cave of the Ice Moon", "Grove of the Green Grove": a word said twice reads as a mistake.</summary>
		internal static bool RepeatsAWord(string name)
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string[] words = name.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < words.Length; i++)
			{
				string w = words[i].TrimEnd('\'', 's');
				if (w.Length < 3 || SmallWords.Contains(words[i]))
				{
					continue;
				}
				if (!seen.Add(w))
				{
					return true;
				}
			}
			return false;
		}

		private static string DeriveMeaning(BuildState state, Composition composition)
		{
			var parts = new List<string>();
			if (!string.IsNullOrEmpty(composition?.Adjective))
			{
				parts.Add(composition.Adjective.ToLowerInvariant());
			}
			if (composition != null && composition.UsedRoot && !state.BiomeFree)
			{
				string onsetMeaning = NameGrammar.MatchPrefix(NameGrammar.BiomeMeaningOnsets, state.RawRoot);
				if (onsetMeaning != null)
				{
					parts.Add(onsetMeaning);
				}
			}
			parts.Add(PointOfInterestKinds.Spaced(state.Ctx.Kind.ToString()).ToLowerInvariant());
			if (composition != null && composition.UsedRace && state.Ctx.Race != null)
			{
				parts.Add("of the " + RaceNaming.PluralOf(state.Ctx.Race).ToLowerInvariant());
			}
			return string.Join(" ", parts).Trim();
		}
	}
}
