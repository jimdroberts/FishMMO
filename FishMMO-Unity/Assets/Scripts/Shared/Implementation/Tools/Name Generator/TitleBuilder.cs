using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>What the caller wants from a title: category, register, gender, role and size.</summary>
	public sealed class TitleOptions
	{
		public TitleType TitleType = TitleType.Any;
		public TitleRegister Register = TitleRegister.Any;
		public CharacterGender Gender = CharacterGender.Unspecified;
		/// <summary>What the character does ("Banker"); fills <c>{profession}</c>.</summary>
		public string Profession;
		/// <summary>Longest title allowed; 0 is unlimited.</summary>
		public int MaxLength;
		public bool AllowCompound = true;

		public bool Fits(string title) => MaxLength <= 0 || string.IsNullOrEmpty(title) || title.Length <= MaxLength;
	}

	/// <summary>
	/// Remembers recent titles so a batch does not hand the same authored legend
	/// to two characters in a row. One per generator; not shared across peers,
	/// and never consulted when a request is seeded, since a seeded title must
	/// replay exactly regardless of what was generated before it.
	/// </summary>
	public sealed class TitleMemory
	{
		private readonly Queue<string> recent = new Queue<string>();
		private readonly HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly int capacity;

		public TitleMemory(int capacity = 48)
		{
			this.capacity = Math.Max(1, capacity);
		}

		public bool Seen(string title) => !string.IsNullOrEmpty(title) && set.Contains(title);

		public void Remember(string title)
		{
			if (string.IsNullOrEmpty(title) || set.Contains(title))
			{
				return;
			}
			recent.Enqueue(title);
			set.Add(title);
			while (recent.Count > capacity)
			{
				set.Remove(recent.Dequeue());
			}
		}
	}

	/// <summary>
	/// Template-driven title builder.
	///
	/// <para>A title is a <see cref="TitleTemplate"/> from the grammar asset with its
	/// slots filled from the race's tables (honorifics, epithets, ranks, legends,
	/// occupations, places) and the grammar's universal vocabulary (deeds, eras,
	/// ordinals…). Templates whose slots cannot be filled for a race are skipped;
	/// honorifics only take a place or an ordinal when the grammar says they may;
	/// honorifics follow the character's gender; and a length budget decides
	/// whether a second clause is appended at all.</para>
	///
	/// <para>Examples: <c>Sir</c> · <c>Lady of Ashford</c> · <c>King, Third of his
	/// Name</c> · <c>the Ironwilled</c> · <c>Master Coinkeeper of Kingshold</c> ·
	/// <c>Who Sealed the Dark Gate at the Battle of Hollow Hill</c>.</para>
	/// </summary>
	public static class TitleBuilder
	{
		/// <summary>Chance to append a second clause when it fits the budget.</summary>
		private const double CompoundTitleChance = 0.18;
		/// <summary>Chance a runtime-injected title is used over a composed one when both exist.</summary>
		private const double InjectedTitleChance = 0.65;
		/// <summary>Each meaning keyword that matches gets this chance to pin the category.</summary>
		private const double MeaningBiasChance = 0.50;
		/// <summary>How many compositions are tried before settling for the shortest usable one.</summary>
		private const int Attempts = 6;

		private static readonly Regex SlotPattern = new Regex(@"\{(\w+)(?::(\w+))?\}", RegexOptions.Compiled);
		private static readonly HashSet<string> SmallWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"a", "an", "the", "of", "in", "at", "and", "on", "to", "for", "by", "with", "from", "or",
		};

		/// <summary>
		/// Compositions used when the grammar asset has none, so a bare grammar still yields titles.
		/// The same set is what the migration writes into the asset.
		/// </summary>
		public static readonly TitleTemplate[] DefaultTemplates =
		{
			T(TitleType.Honorific, TitleRegister.Civil, "{honorific}", 50),
			T(TitleType.Honorific, TitleRegister.Civil, "{honorific:place} of {place}", 20),
			T(TitleType.Honorific, TitleRegister.Civil, "{honorific:ordinal}, {ordinal} of {pronoun} Name", 8),
			T(TitleType.Honorific, TitleRegister.Civil, "{honorific} and {honorific}", 6),
			T(TitleType.Epithet, TitleRegister.Any, "{epithet}", 45),
			T(TitleType.Epithet, TitleRegister.Any, "the {adjective}-{qualifier}", 25),
			T(TitleType.Epithet, TitleRegister.Any, "{epithet} of {place}", 8),
			T(TitleType.Epithet, TitleRegister.Any, "{epithet} from {place}", 4),
			T(TitleType.Epithet, TitleRegister.Any, "{epithet}, bane of {place}", 3),
			T(TitleType.Epithet, TitleRegister.Any, "{epithet}, sworn to {place}", 3),
			T(TitleType.Epithet, TitleRegister.Any, "{epithet}, pride of {place}", 3),
			T(TitleType.Epithet, TitleRegister.Any, "{epithet}, exiled from {place}", 3),
			T(TitleType.Rank, TitleRegister.Martial, "{rank}", 50),
			T(TitleType.Rank, TitleRegister.Martial, "{rank:noplace} of {place}", 20),
			T(TitleType.Rank, TitleRegister.Martial, "{ordinal} {rank}", 10),
			T(TitleType.Legend, TitleRegister.Mythic, "{legend}", 40),
			T(TitleType.Legend, TitleRegister.Mythic, "Who {deed} {object}", 15),
			T(TitleType.Legend, TitleRegister.Mythic, "Who {deed} {object} at {place}", 15),
			T(TitleType.Legend, TitleRegister.Mythic, "Who {deed} {object} in the {era}", 10),
			T(TitleType.Legend, TitleRegister.Mythic, "Who {deed} {object} and {outcome}", 10),
			T(TitleType.Legend, TitleRegister.Mythic, "Who {deed} {object} at the {battle} of {place}", 10),
			T(TitleType.Occupation, TitleRegister.Civil, "{profession}", 30),
			T(TitleType.Occupation, TitleRegister.Civil, "Master {profession}", 20),
			T(TitleType.Occupation, TitleRegister.Civil, "{profession} of {place}", 20),
			T(TitleType.Occupation, TitleRegister.Civil, "{occupation}", 25),
			T(TitleType.Occupation, TitleRegister.Civil, "Master {occupation}", 10),
			T(TitleType.Occupation, TitleRegister.Civil, "{occupation} of {place}", 10),
			T(TitleType.Occupation, TitleRegister.Civil, "Guild {occupation}", 5),
		};

		private static TitleTemplate T(TitleType category, TitleRegister register, string pattern, int weight) =>
			new TitleTemplate { Category = category, Register = register, Pattern = pattern, Weight = weight };

		/// <summary>Builds a title for a race. Returns empty strings when the race has no titles or nothing fits.</summary>
		public static (string title, string category) Build(string race, TitleOptions options, string meaning,
			DeterministicRNG rng, TitleMemory memory = null)
		{
			options ??= new TitleOptions();
			if (options.TitleType == TitleType.None || !RaceRegistry.TryGetTitles(race, out RaceTitles titles))
			{
				return ("", "");
			}

			var ctx = new SlotContext(race, titles, options, rng);
			TitleType category = ResolveCategory(options, meaning, rng);

			// Injected titles win most of the time; the remainder keeps composed titles in play.
			string injected = RuntimeInjection.TryPickTitle(race, CategoryKey(category), rng);
			if (injected != null && rng.NextDouble() < InjectedTitleChance && options.Fits(injected))
			{
				return (injected, CategoryKey(category));
			}

			string title = Compose(ctx, category, memory);
			if (title.Length == 0)
			{
				foreach (TitleType fallback in Fallbacks(category, options.Register))
				{
					title = Compose(ctx, fallback, memory);
					if (title.Length > 0)
					{
						category = fallback;
						break;
					}
				}
			}
			if (title.Length == 0)
			{
				return ("", "");
			}

			if (options.AllowCompound && rng.NextDouble() < CompoundTitleChance)
			{
				TitleType second = SecondaryCategory(category, options.Register, rng);
				string extra = Compose(ctx, second, memory);
				if (extra.Length > 0)
				{
					string combined = title + ", " + (second == TitleType.Legend ? LowerFirstWord(extra) : extra);
					if (options.Fits(combined))
					{
						title = combined;
					}
				}
			}

			memory?.Remember(title);
			return (title, CategoryKey(category));
		}

		// ── Composition ────────────────────────────────────────────────

		private static string Compose(SlotContext ctx, TitleType category, TitleMemory memory)
		{
			List<TitleTemplate> usable = UsableTemplates(ctx, category);
			if (usable.Count == 0)
			{
				return "";
			}

			string shortest = null;
			for (int attempt = 0; attempt < Attempts; attempt++)
			{
				TitleTemplate template = PickWeighted(usable, ctx.Rng);
				string candidate = Fill(template.Pattern, ctx);
				if (candidate.Length == 0)
				{
					continue;
				}
				if (shortest == null || candidate.Length < shortest.Length)
				{
					shortest = candidate;
				}
				if (!ctx.Options.Fits(candidate))
				{
					continue;
				}
				if (memory != null && memory.Seen(candidate) && attempt < Attempts - 1)
				{
					continue;
				}
				return candidate;
			}

			// Nothing drawn fit the budget: settle for the shortest, if even that does.
			return shortest != null && ctx.Options.Fits(shortest) ? shortest : "";
		}

		private static List<TitleTemplate> UsableTemplates(SlotContext ctx, TitleType category)
		{
			IReadOnlyList<TitleTemplate> source = NameGrammar.TitleTemplates.Count > 0 ? NameGrammar.TitleTemplates : DefaultTemplates;
			var usable = new List<TitleTemplate>();
			for (int i = 0; i < source.Count; i++)
			{
				TitleTemplate t = source[i];
				if (t.Category != category)
				{
					continue;
				}
				if (ctx.Options.Register != TitleRegister.Any && t.Register != TitleRegister.Any && t.Register != ctx.Options.Register)
				{
					continue;
				}
				if (CanFill(t.Pattern, ctx))
				{
					usable.Add(t);
				}
			}
			return usable;
		}

		private static TitleTemplate PickWeighted(List<TitleTemplate> templates, DeterministicRNG rng)
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

		private static bool CanFill(string pattern, SlotContext ctx)
		{
			foreach (Match m in SlotPattern.Matches(pattern))
			{
				if (ctx.Pool(m.Groups[1].Value, m.Groups[2].Value).Count == 0)
				{
					return false;
				}
			}
			return true;
		}

		private static string Fill(string pattern, SlotContext ctx)
		{
			var sb = new StringBuilder(pattern.Length + 32);
			int last = 0;
			string previousHonorific = null;
			foreach (Match m in SlotPattern.Matches(pattern))
			{
				sb.Append(pattern, last, m.Index - last);
				string slot = m.Groups[1].Value;
				IReadOnlyList<string> pool = ctx.Pool(slot, m.Groups[2].Value);
				if (pool.Count == 0)
				{
					return "";
				}
				string value = pool[ctx.Rng.Next(pool.Count)];
				// "{honorific} and {honorific}": never the same word twice.
				if (slot == "honorific")
				{
					if (previousHonorific != null && pool.Count > 1)
					{
						for (int i = 0; i < 4 && string.Equals(value, previousHonorific, StringComparison.OrdinalIgnoreCase); i++)
						{
							value = pool[ctx.Rng.Next(pool.Count)];
						}
						if (string.Equals(value, previousHonorific, StringComparison.OrdinalIgnoreCase))
						{
							return "";
						}
					}
					previousHonorific = value;
				}
				sb.Append(value);
				last = m.Index + m.Length;
			}
			sb.Append(pattern, last, pattern.Length - last);
			return sb.ToString().Trim();
		}

		// ── Category selection ─────────────────────────────────────────

		private static TitleType ResolveCategory(TitleOptions options, string meaning, DeterministicRNG rng)
		{
			if (options.TitleType != TitleType.Any)
			{
				return options.TitleType;
			}

			double roll;
			switch (options.Register)
			{
				case TitleRegister.Civil:
					roll = rng.NextDouble();
					if (!string.IsNullOrEmpty(options.Profession) && roll < 0.55) return TitleType.Occupation;
					if (roll < 0.45) return TitleType.Honorific;
					if (roll < 0.80) return TitleType.Occupation;
					return TitleType.Epithet;
				case TitleRegister.Martial:
					roll = rng.NextDouble();
					if (roll < 0.55) return TitleType.Rank;
					if (roll < 0.85) return TitleType.Epithet;
					return TitleType.Legend;
				case TitleRegister.Mythic:
					return rng.NextDouble() < 0.60 ? TitleType.Legend : TitleType.Epithet;
			}

			string lower = (meaning ?? "").ToLower();
			IReadOnlyList<StringMapping> bias = NameGrammar.MeaningTitleBias;
			for (int i = 0; i < bias.Count; i++)
			{
				if (lower.Contains(bias[i].Key.ToLower()) && rng.NextDouble() < MeaningBiasChance)
				{
					TitleType biased = ParseCategory(bias[i].Value);
					if (biased != TitleType.Any)
					{
						return biased;
					}
				}
			}

			roll = rng.NextDouble();
			if (roll < 0.40) return TitleType.Epithet;
			if (roll < 0.70) return TitleType.Honorific;
			if (roll < 0.90) return TitleType.Rank;
			return TitleType.Legend;
		}

		private static IEnumerable<TitleType> Fallbacks(TitleType failed, TitleRegister register)
		{
			switch (register)
			{
				case TitleRegister.Civil:
					yield return TitleType.Honorific;
					yield return TitleType.Occupation;
					yield return TitleType.Epithet;
					break;
				case TitleRegister.Martial:
					yield return TitleType.Rank;
					yield return TitleType.Epithet;
					yield return TitleType.Legend;
					break;
				case TitleRegister.Mythic:
					yield return TitleType.Legend;
					yield return TitleType.Epithet;
					break;
				default:
					yield return TitleType.Epithet;
					yield return TitleType.Honorific;
					yield return TitleType.Rank;
					yield return TitleType.Legend;
					break;
			}
		}

		private static TitleType SecondaryCategory(TitleType primary, TitleRegister register, DeterministicRNG rng)
		{
			// A different category from the primary, staying inside the register.
			switch (primary)
			{
				case TitleType.Honorific:
				case TitleType.Occupation:
					return register == TitleRegister.Civil || rng.NextDouble() < 0.6 ? TitleType.Epithet : TitleType.Rank;
				case TitleType.Rank:
					return register == TitleRegister.Martial || rng.NextDouble() < 0.6 ? TitleType.Epithet : TitleType.Legend;
				case TitleType.Epithet:
					if (register == TitleRegister.Civil) return TitleType.Honorific;
					if (register == TitleRegister.Mythic) return TitleType.Legend;
					return rng.NextDouble() < 0.5 ? TitleType.Rank : TitleType.Legend;
				default:
					return TitleType.Epithet;
			}
		}

		private static string LowerFirstWord(string s)
		{
			return s.StartsWith("Who ", StringComparison.Ordinal) ? "who " + s.Substring(4) : s;
		}

		private static string CategoryKey(TitleType category) => category.ToString().ToLower();

		private static TitleType ParseCategory(string value)
		{
			switch ((value ?? "").Trim().ToLower())
			{
				case "honorific": return TitleType.Honorific;
				case "epithet": return TitleType.Epithet;
				case "rank": return TitleType.Rank;
				case "legend": return TitleType.Legend;
				case "occupation": return TitleType.Occupation;
				default: return TitleType.Any;
			}
		}

		/// <summary>
		/// "a goddess" → "a Goddess", "the iron gate" → "the Iron Gate": nouns are
		/// capitalised, articles and prepositions stay lower, so authored and
		/// composed legends read the same.
		/// </summary>
		public static string TitleCaseObject(string s)
		{
			if (string.IsNullOrWhiteSpace(s))
			{
				return s;
			}
			string[] words = s.Split(' ');
			for (int i = 0; i < words.Length; i++)
			{
				string w = words[i];
				if (w.Length == 0 || SmallWords.Contains(w))
				{
					continue;
				}
				words[i] = char.ToUpperInvariant(w[0]) + w.Substring(1);
			}
			return string.Join(" ", words);
		}

		// ── Slot pools ─────────────────────────────────────────────────

		/// <summary>The vocabulary each slot draws from, resolved once per build.</summary>
		private sealed class SlotContext
		{
			public readonly DeterministicRNG Rng;
			public readonly TitleOptions Options;
			private readonly string race;
			private readonly RaceTitles titles;
			private readonly Dictionary<string, IReadOnlyList<string>> cache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
			private static readonly string[] Empty = Array.Empty<string>();

			public SlotContext(string race, RaceTitles titles, TitleOptions options, DeterministicRNG rng)
			{
				this.race = race;
				this.titles = titles;
				Options = options;
				Rng = rng;
			}

			public IReadOnlyList<string> Pool(string slot, string variant)
			{
				string key = slot + ":" + variant;
				if (!cache.TryGetValue(key, out IReadOnlyList<string> pool))
				{
					pool = Resolve(slot, variant) ?? Empty;
					cache[key] = pool;
				}
				return pool;
			}

			private IReadOnlyList<string> Resolve(string slot, string variant)
			{
				switch (slot)
				{
					case "honorific":
					{
						List<string> pool = GenderedHonorifics();
						if (variant == "place")
						{
							return pool.FindAll(h => NameGrammar.PlaceTakingHonorifics.Contains(h));
						}
						if (variant == "ordinal")
						{
							return pool.FindAll(h => NameGrammar.OrdinalTakingHonorifics.Contains(h));
						}
						return pool;
					}
					case "epithet": return titles.Epithet;
					case "rank":
						return variant == "noplace"
							? Array.FindAll(titles.Rank ?? Empty, r => !r.Contains(" of "))
							: titles.Rank;
					case "legend": return titles.Legend;
					case "occupation":
						if (titles.Occupational != null && titles.Occupational.Length > 0)
						{
							return titles.Occupational;
						}
						// Generic trades only for races that plausibly practise them.
						return RaceRegistry.TryGet(race, out RaceTemplate raceTemplate) && raceTemplate.Naming.AllowGenericOccupations
							? NameGrammar.GenericOccupations
							: Empty;
					case "profession":
						return string.IsNullOrWhiteSpace(Options.Profession) ? Empty : new[] { Options.Profession.Trim() };
					case "place":
						if (RaceRegistry.TryGetPlaces(race, out string[] places))
						{
							return places;
						}
						return variant == "race" ? Empty : NameGrammar.UniversalPlaces;
					case "universalplace": return NameGrammar.UniversalPlaces;
					case "ordinal": return NameGrammar.Ordinals;
					case "pronoun":
						return new[] { Options.Gender == CharacterGender.Male ? "his" : Options.Gender == CharacterGender.Female ? "her" : "their" };
					case "deed": return NameGrammar.DeedVerbs;
					case "object": return Array.ConvertAll(NameGrammar.DeedObjects, TitleCaseObject);
					case "era": return NameGrammar.EraQualifiers;
					case "battle": return NameGrammar.BattleQualifiers;
					case "outcome": return NameGrammar.Outcomes;
					case "adjective": return NameGrammar.ComposedAdjectives;
					case "qualifier": return NameGrammar.ComposedQualifiers;
					default: return Empty;
				}
			}

			/// <summary>Neutral honorifics plus the gendered set; a gender with no set of its own gets only the neutral ones, and an unspecified gender never gets a gendered word.</summary>
			private List<string> GenderedHonorifics()
			{
				var pool = new List<string>(titles.Honorific ?? Empty);
				string[] gendered = Options.Gender == CharacterGender.Male ? titles.HonorificMasculine
					: Options.Gender == CharacterGender.Female ? titles.HonorificFeminine
					: null;
				if (gendered != null)
				{
					pool.AddRange(gendered);
				}
				return pool;
			}
		}
	}
}
