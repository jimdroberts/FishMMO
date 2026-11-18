using System;
using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// The plural and adjective forms of a race's name that place names use: "Cave of Orcs", "Barrow of
	/// the Dwarves", "Goblin Warren", "Elven Ruins".
	/// </summary>
	/// <remarks>
	/// <para>
	/// The authored <see cref="RaceNamingData.Plural"/> and <see cref="RaceNamingData.Adjective"/> win.
	/// When they are empty the forms are derived here, so place names work before the fill tool has
	/// touched the ~285 race assets, and a race added later names correctly without anyone remembering
	/// the fields. The fill tool writes exactly what these rules produce, so a filled field is a place
	/// for a designer to override, never a second opinion.
	/// </para>
	/// <para>
	/// Only the last word is inflected ("Stone Golem" → "Stone Golems", "Half-Elf" → "Half-Elves"), and
	/// collective names — -folk, -kin, -born, -spawn, the Dead — do not change.
	/// </para>
	/// </remarks>
	public static class RaceNaming
	{
		/// <summary>Whole-word irregular plurals, matched case-insensitively on the last word.</summary>
		private static readonly Dictionary<string, string> IrregularPlurals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Elf"] = "Elves",
			["Dwarf"] = "Dwarves",
			["Wolf"] = "Wolves",
			["Werewolf"] = "Werewolves",
			["Fish"] = "Fish",
			["Mouse"] = "Mice",
			["Louse"] = "Lice",
			["Goose"] = "Geese",
			["Man"] = "Men",
			["Woman"] = "Women",
			["Human"] = "Humans",
			["Ox"] = "Oxen",
			["Elk"] = "Elk",
			["Deer"] = "Deer",
			["Sheep"] = "Sheep",
			["Fae"] = "Fae",
			["Dead"] = "Dead",
			["Undead"] = "Undead",
			["Duergar"] = "Duergar",
			["Cyclops"] = "Cyclopes",
			["Succubus"] = "Succubi",
			["Seraph"] = "Seraphim",
			["Foot"] = "Feet",
			["Tooth"] = "Teeth",
		};

		/// <summary>Endings of collective names that are their own plural ("the Ratfolk", "the Hellborn").</summary>
		private static readonly string[] CollectiveEndings = { "folk", "kin", "born", "spawn", "blood", "dead" };

		/// <summary>Whole-word adjective forms where the bare name reads wrongly ("Elf Ruins").</summary>
		private static readonly Dictionary<string, string> IrregularAdjectives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Elf"] = "Elven",
			["Dwarf"] = "Dwarven",
			["Gnome"] = "Gnomish",
			["Fae"] = "Faerie",
		};

		/// <summary>The plural a place name uses for this race: the authored one, else the derived one.</summary>
		public static string PluralOf(RaceTemplate race)
		{
			if (race == null)
			{
				return "";
			}
			string authored = race.Naming?.Plural;
			return string.IsNullOrWhiteSpace(authored) ? DerivePlural(race.Name) : authored.Trim();
		}

		/// <summary>The plural for a race key or name; a registered race uses its authored form.</summary>
		public static string PluralOf(string race)
		{
			return RaceRegistry.TryGet(race, out RaceTemplate template) ? PluralOf(template) : DerivePlural(race);
		}

		/// <summary>The describing form a place name uses for this race: the authored one, else the derived one.</summary>
		public static string AdjectiveOf(RaceTemplate race)
		{
			if (race == null)
			{
				return "";
			}
			string authored = race.Naming?.Adjective;
			return string.IsNullOrWhiteSpace(authored) ? DeriveAdjective(race.Name) : authored.Trim();
		}

		/// <summary>The describing form for a race key or name; a registered race uses its authored form.</summary>
		public static string AdjectiveOf(string race)
		{
			return RaceRegistry.TryGet(race, out RaceTemplate template) ? AdjectiveOf(template) : DeriveAdjective(race);
		}

		/// <summary>English plural of a race name, inflecting only its last word.</summary>
		public static string DerivePlural(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return "";
			}
			SplitLastWord(name.Trim(), out string head, out string last);
			return head + PluralWord(last);
		}

		/// <summary>Adjective form of a race name: the name itself, except for the few fixed forms.</summary>
		public static string DeriveAdjective(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return "";
			}
			SplitLastWord(name.Trim(), out string head, out string last);
			return head + (IrregularAdjectives.TryGetValue(last, out string form) ? form : last);
		}

		/// <summary>"Dark Elf" → ("Dark ", "Elf"); "Half-Elf" → ("Half-", "Elf"); "Orc" → ("", "Orc").</summary>
		private static void SplitLastWord(string name, out string head, out string last)
		{
			int cut = Math.Max(name.LastIndexOf(' '), name.LastIndexOf('-'));
			head = cut >= 0 ? name.Substring(0, cut + 1) : "";
			last = cut >= 0 ? name.Substring(cut + 1) : name;
		}

		private static string PluralWord(string word)
		{
			if (word.Length == 0)
			{
				return word;
			}
			if (IrregularPlurals.TryGetValue(word, out string irregular))
			{
				return MatchCase(word, irregular);
			}
			string lower = word.ToLowerInvariant();
			for (int i = 0; i < CollectiveEndings.Length; i++)
			{
				if (lower.EndsWith(CollectiveEndings[i], StringComparison.Ordinal))
				{
					return word;
				}
			}
			// Compounds ending in an irregular word: Werewolf, Beastman, Spider-mouse.
			if (lower.EndsWith("wolf", StringComparison.Ordinal) || lower.EndsWith("elf", StringComparison.Ordinal)
				|| lower.EndsWith("dwarf", StringComparison.Ordinal))
			{
				return word.Substring(0, word.Length - 1) + "ves";
			}
			if (lower.EndsWith("man", StringComparison.Ordinal) && lower != "human" && lower != "shaman" && lower != "talisman")
			{
				return word.Substring(0, word.Length - 3) + "men";
			}
			if (lower.EndsWith("mouse", StringComparison.Ordinal))
			{
				return word.Substring(0, word.Length - 5) + "mice";
			}
			if (lower.EndsWith("fish", StringComparison.Ordinal))
			{
				return word;
			}
			if (lower.EndsWith("s", StringComparison.Ordinal) || lower.EndsWith("x", StringComparison.Ordinal)
				|| lower.EndsWith("z", StringComparison.Ordinal) || lower.EndsWith("ch", StringComparison.Ordinal)
				|| lower.EndsWith("sh", StringComparison.Ordinal))
			{
				return word + "es";
			}
			if (lower.Length >= 2 && lower[lower.Length - 1] == 'y' && !IsVowel(lower[lower.Length - 2]))
			{
				return word.Substring(0, word.Length - 1) + "ies";
			}
			return word + "s";
		}

		private static bool IsVowel(char c) => c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u';

		/// <summary>Keeps a lowercase source lowercase ("elf" → "elves"); otherwise the table's capitalised form.</summary>
		private static string MatchCase(string source, string form)
		{
			return source.Length > 0 && char.IsLower(source[0]) ? form.ToLowerInvariant() : form;
		}
	}
}
