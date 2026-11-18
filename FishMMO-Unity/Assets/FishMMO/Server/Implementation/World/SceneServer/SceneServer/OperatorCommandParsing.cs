using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The text handling behind the in-game <c>/gm</c> and <c>/admin</c> commands.
	/// </summary>
	/// <remarks>
	/// Nothing here touches server state, so every rule an operator can trip over — what counts as
	/// a duration, when a leading word names a character, how a usage line is spelled — can be
	/// tested without standing up a scene server.
	/// </remarks>
	public static class OperatorCommandParsing
	{
		/// <summary>Longest mute or temporary ban a game master may apply.</summary>
		/// <remarks>
		/// Administrators are not held to it. A game master who needs longer than a month is asking
		/// for a decision that belongs to an administrator, who can apply it themselves.
		/// </remarks>
		public static readonly TimeSpan GameMasterMaximumDuration = TimeSpan.FromDays(30);

		/// <summary>Longest duration accepted from anybody. Bounds a typo, not a policy.</summary>
		public static readonly TimeSpan MaximumDuration = TimeSpan.FromDays(3650);

		/// <summary>The kinds an argument spec may name, matched case-insensitively.</summary>
		private static readonly Dictionary<string, StaffArgumentKind> kindsByName = BuildKinds();

		/// <summary>
		/// Splits the first space-separated word off <paramref name="text"/>.
		/// </summary>
		/// <param name="text">Text to split. Null is treated as empty.</param>
		/// <param name="rest">Everything after the first word, trimmed.</param>
		/// <returns>The first word, or empty when there is none.</returns>
		public static string SplitFirstWord(string text, out string rest)
		{
			text = (text ?? string.Empty).Trim();
			int space = text.IndexOf(' ');
			if (space < 0)
			{
				rest = string.Empty;
				return text;
			}
			rest = text.Substring(space + 1).Trim();
			return text.Substring(0, space);
		}

		/// <summary>
		/// Parses a duration such as <c>90</c>, <c>30m</c>, <c>2h</c>, <c>7d</c> or <c>1w</c>.
		/// </summary>
		/// <remarks>
		/// A bare number is minutes, because minutes are what a moderator reaches for first and
		/// "mute 10" meaning ten seconds would be a mute that has lapsed before the player reads it.
		/// Zero and negative values are refused rather than meaning "none": a mute of nothing is a
		/// typo, and treating it as permanent would be a far worse one.
		/// </remarks>
		/// <param name="text">The text to parse.</param>
		/// <param name="duration">The duration, when parsed.</param>
		/// <returns>True when a positive duration no longer than <see cref="MaximumDuration"/> was parsed.</returns>
		public static bool TryParseDuration(string text, out TimeSpan duration)
		{
			duration = TimeSpan.Zero;
			text = (text ?? string.Empty).Trim().ToLowerInvariant();
			if (text.Length == 0)
			{
				return false;
			}

			char last = text[text.Length - 1];
			bool hasUnit = !char.IsDigit(last);
			string digits = hasUnit ? text.Substring(0, text.Length - 1) : text;

			// Six digits is already far past the cap in every unit; refusing longer strings keeps
			// the arithmetic below well away from TimeSpan's own limits.
			if (digits.Length == 0 || digits.Length > 6)
			{
				return false;
			}
			for (int i = 0; i < digits.Length; ++i)
			{
				if (digits[i] < '0' || digits[i] > '9')
				{
					return false;
				}
			}

			long amount = long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
			if (amount <= 0)
			{
				return false;
			}

			switch (hasUnit ? last : 'm')
			{
				case 's': duration = TimeSpan.FromSeconds(amount); break;
				case 'm': duration = TimeSpan.FromMinutes(amount); break;
				case 'h': duration = TimeSpan.FromHours(amount); break;
				case 'd': duration = TimeSpan.FromDays(amount); break;
				case 'w': duration = TimeSpan.FromDays(amount * 7); break;
				default: return false;
			}

			if (duration > MaximumDuration)
			{
				duration = TimeSpan.Zero;
				return false;
			}
			return true;
		}

		/// <summary>True when the word asks for something with no end: <c>perm</c> or <c>permanent</c>.</summary>
		public static bool IsPermanent(string text)
		{
			text = (text ?? string.Empty).Trim();
			return text.Equals("perm", StringComparison.OrdinalIgnoreCase) ||
				text.Equals("permanent", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Describes a duration in the same short units it is typed in.</summary>
		public static string DescribeDuration(TimeSpan duration)
		{
			if (duration.TotalMinutes < 1)
			{
				return $"{(int)Math.Max(1, duration.TotalSeconds)}s";
			}
			if (duration.TotalHours < 1)
			{
				return $"{(int)duration.TotalMinutes}m";
			}
			if (duration.TotalDays < 1)
			{
				int hours = (int)duration.TotalHours;
				return duration.Minutes > 0 ? $"{hours}h {duration.Minutes}m" : $"{hours}h";
			}
			int days = (int)duration.TotalDays;
			return duration.Hours > 0 ? $"{days}d {duration.Hours}h" : $"{days}d";
		}

		/// <summary>Parses a ticket number, with or without a leading <c>#</c>.</summary>
		public static bool TryParseTicketID(string text, out long ticketID)
		{
			ticketID = 0;
			text = (text ?? string.Empty).Trim();
			if (text.StartsWith("#", StringComparison.Ordinal))
			{
				text = text.Substring(1);
			}
			return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ticketID) && ticketID > 0;
		}

		/// <summary>True when the word is a whole number, optionally signed.</summary>
		public static bool IsInteger(string word)
		{
			return long.TryParse((word ?? string.Empty).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
		}

		/// <summary>
		/// Decides whether a command's leading word names a character or is already its first real argument.
		/// </summary>
		/// <remarks>
		/// <para>
		/// For the administrator commands whose target is optional — <c>/admin setgold [character]
		/// &lt;amount&gt;</c>, <c>/admin heal [character]</c>. The rule is deliberately about the
		/// word's shape and not about whether a character by that name exists: nothing, or a number,
		/// means the caller; anything else is a character name, and if that character is not on
		/// this scene server the command says so rather than quietly acting on the caller.
		/// </para>
		/// <para>
		/// The alternative — "a name if one is online, else yourself" — turns a mistyped name into
		/// a grant to the administrator's own character, which is exactly the silent wrong-target
		/// failure an economy command must not have.
		/// </para>
		/// </remarks>
		/// <param name="arguments">The command's argument text.</param>
		/// <param name="name">The named character, or null when the caller is the target.</param>
		/// <param name="rest">The arguments after the name, or all of them when there is no name.</param>
		/// <returns>True when a character was named.</returns>
		public static bool TrySplitLeadingCharacter(string arguments, out string name, out string rest)
		{
			string first = SplitFirstWord(arguments, out string after);
			if (first.Length == 0 || IsInteger(first))
			{
				name = null;
				rest = (arguments ?? string.Empty).Trim();
				return false;
			}
			name = first;
			rest = after;
			return true;
		}

		/// <summary>One argument, as a command's spec describes it.</summary>
		public readonly struct ArgumentSpec
		{
			/// <summary>The label shown to operators.</summary>
			public readonly string Label;

			/// <summary>What sort of value it is.</summary>
			public readonly StaffArgumentKind Kind;

			/// <summary>The allowed words, for a choice.</summary>
			public readonly string[] Choices;

			/// <summary>True when the argument may be left out.</summary>
			public readonly bool Optional;

			/// <summary>Creates an argument description.</summary>
			public ArgumentSpec(string label, StaffArgumentKind kind, string[] choices, bool optional)
			{
				Label = label;
				Kind = kind;
				Choices = choices ?? Array.Empty<string>();
				Optional = optional;
			}
		}

		/// <summary>
		/// Parses an argument spec in the grammar documented on <see cref="StaffCommandEntry"/>.
		/// </summary>
		/// <param name="spec">The spec. Null or empty means no arguments.</param>
		/// <param name="arguments">The parsed arguments.</param>
		/// <param name="error">Why the spec is malformed, when it is.</param>
		/// <returns>True when the spec is well formed.</returns>
		public static bool TryParseSpec(string spec, out List<ArgumentSpec> arguments, out string error)
		{
			arguments = new List<ArgumentSpec>();
			error = null;
			if (string.IsNullOrWhiteSpace(spec))
			{
				return true;
			}

			string[] entries = spec.Split(';');
			for (int i = 0; i < entries.Length; ++i)
			{
				string entry = entries[i].Trim();
				bool optional = entry.EndsWith("?", StringComparison.Ordinal);
				if (optional)
				{
					entry = entry.Substring(0, entry.Length - 1);
				}

				int colon = entry.IndexOf(':');
				if (colon <= 0 || colon == entry.Length - 1)
				{
					error = $"entry {i + 1} '{entries[i]}' is not label:Kind";
					return false;
				}

				string label = entry.Substring(0, colon);
				string kindText = entry.Substring(colon + 1);
				string[] choices = Array.Empty<string>();
				int equals = kindText.IndexOf('=');
				if (equals >= 0)
				{
					choices = kindText.Substring(equals + 1).Split(',');
					kindText = kindText.Substring(0, equals);
				}

				if (!kindsByName.TryGetValue(kindText, out StaffArgumentKind kind))
				{
					error = $"entry {i + 1} names unknown kind '{kindText}'";
					return false;
				}
				if (kind == StaffArgumentKind.Choice && choices.Length == 0)
				{
					error = $"choice '{label}' lists no choices";
					return false;
				}
				if (kind != StaffArgumentKind.Choice && choices.Length > 0)
				{
					error = $"'{label}' lists choices but is not a Choice";
					return false;
				}
				if (kind == StaffArgumentKind.Text && i != entries.Length - 1)
				{
					error = $"text argument '{label}' is not last, but text consumes the rest of the line";
					return false;
				}

				arguments.Add(new ArgumentSpec(label, kind, choices, optional));
			}
			return true;
		}

		/// <summary>
		/// Formats a usage line, <c>/gm mute &lt;character&gt; &lt;duration&gt; [reason]</c>, from a spec.
		/// </summary>
		/// <remarks>
		/// Derived rather than written beside the spec, so the help an operator reads in chat and
		/// the form the console builds cannot describe the same command two different ways.
		/// </remarks>
		public static string FormatUsage(string command, string name, string spec)
		{
			var builder = new StringBuilder();
			builder.Append(command).Append(' ').Append(name);
			if (!TryParseSpec(spec, out List<ArgumentSpec> arguments, out _))
			{
				return builder.ToString();
			}
			foreach (ArgumentSpec argument in arguments)
			{
				builder.Append(' ').Append(argument.Optional ? '[' : '<');
				builder.Append(argument.Choices.Length > 0 ? string.Join("|", argument.Choices) : argument.Label);
				builder.Append(argument.Optional ? ']' : '>');
			}
			return builder.ToString();
		}

		/// <summary>Cuts text to a length, marking it when anything was removed.</summary>
		public static string Truncate(string text, int maxLength)
		{
			if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
			{
				return text ?? string.Empty;
			}
			if (maxLength <= 3)
			{
				return text.Substring(0, Math.Max(0, maxLength));
			}
			return text.Substring(0, maxLength - 3) + "...";
		}

		/// <summary>
		/// Packs items into lines no longer than <paramref name="maxLength"/>, joined by <paramref name="separator"/>.
		/// </summary>
		/// <remarks>
		/// An item longer than a whole line is truncated onto a line of its own rather than
		/// dropped: an operator who asked for a list must see that the entry exists.
		/// </remarks>
		public static IEnumerable<string> PackLines(IEnumerable<string> items, string prefix, string separator, int maxLength)
		{
			var line = new StringBuilder(prefix ?? string.Empty);
			int prefixLength = line.Length;
			foreach (string raw in items)
			{
				string item = Truncate(raw ?? string.Empty, Math.Max(4, maxLength - prefixLength));
				bool empty = line.Length == prefixLength;
				int needed = (empty ? 0 : separator.Length) + item.Length;
				if (!empty && line.Length + needed > maxLength)
				{
					yield return line.ToString();
					line.Clear();
					line.Append(prefix ?? string.Empty);
					empty = true;
				}
				if (!empty)
				{
					line.Append(separator);
				}
				line.Append(item);
			}
			if (line.Length > prefixLength)
			{
				yield return line.ToString();
			}
		}

		private static Dictionary<string, StaffArgumentKind> BuildKinds()
		{
			var kinds = new Dictionary<string, StaffArgumentKind>(StringComparer.OrdinalIgnoreCase);
			foreach (StaffArgumentKind kind in (StaffArgumentKind[])Enum.GetValues(typeof(StaffArgumentKind)))
			{
				kinds[kind.ToString()] = kind;
			}
			return kinds;
		}
	}
}
