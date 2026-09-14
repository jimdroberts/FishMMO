using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// One argument of a staff console command, as read from <see cref="StaffCommandEntry.Arguments"/>.
	/// </summary>
	public sealed class StaffArgument
	{
		/// <summary>The label the server gave the argument.</summary>
		public string Label;

		/// <summary>What shape of value the argument takes.</summary>
		public StaffArgumentKind Kind;

		/// <summary>The allowed words for a <see cref="StaffArgumentKind.Choice"/>; empty otherwise.</summary>
		public IReadOnlyList<string> Choices = Array.Empty<string>();

		/// <summary>True when the argument may be left out.</summary>
		public bool Optional;
	}

	/// <summary>
	/// Reads a staff command's argument spec and turns filled-in values into the chat line the
	/// console sends.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Pure and static so it can be tested without a panel. It knows the GRAMMAR of the spec, which
	/// is part of the wire contract, and nothing about any particular command: every command, label
	/// and choice word comes from the catalogue the server sends at runtime.
	/// </para>
	/// <para>
	/// The grammar (see <see cref="StaffCommandEntry"/>): entries separated by <c>;</c>, each
	/// <c>label:Kind</c>, optionally followed by <c>=a,b,c</c> for a Choice and by a trailing
	/// <c>?</c> when the argument may be left out. A Text argument consumes the rest of the line, so
	/// it must be last.
	/// </para>
	/// <para>
	/// A blank optional argument is omitted from the line, and a filled argument after it is still
	/// sent: the server disambiguates optional arguments by shape (for
	/// <c>character:Character?;amount:Integer</c> a leading number means no character was given).
	/// Every non-optional argument is still required.
	/// </para>
	/// </remarks>
	public static class StaffCommandLine
	{
		/// <summary>Separates one argument entry from the next.</summary>
		public const char EntrySeparator = ';';

		/// <summary>Separates an argument's label from its kind.</summary>
		public const char KindSeparator = ':';

		/// <summary>Introduces a Choice argument's word list.</summary>
		public const char ChoiceMarker = '=';

		/// <summary>Separates the words of a Choice list.</summary>
		public const char ChoiceSeparator = ',';

		/// <summary>Marks an argument as optional when it ends the entry.</summary>
		public const char OptionalMarker = '?';

		/// <summary>The duration shape the contract documents: digits, then an optional unit.</summary>
		private static readonly Regex DurationPattern = new Regex(@"^\d+[smhdw]?$", RegexOptions.CultureInvariant);

		/// <summary>
		/// Parses an argument spec.
		/// </summary>
		/// <param name="spec">The spec string. Null or blank means the command takes no arguments.</param>
		/// <param name="arguments">The parsed arguments, in line order. Never null.</param>
		/// <param name="error">Why the spec could not be read, or null.</param>
		/// <returns>True when the whole spec was read.</returns>
		public static bool TryParseSpec(string spec, out List<StaffArgument> arguments, out string error)
		{
			arguments = new List<StaffArgument>();
			error = null;

			if (string.IsNullOrWhiteSpace(spec))
			{
				return true;
			}

			string[] entries = spec.Split(EntrySeparator);
			for (int i = 0; i < entries.Length; ++i)
			{
				string entry = entries[i].Trim();

				// A trailing separator leaves an empty entry; it describes nothing, so it is skipped.
				if (entry.Length == 0)
				{
					continue;
				}

				int position = arguments.Count + 1;

				if (arguments.Count > 0 && arguments[arguments.Count - 1].Kind == StaffArgumentKind.Text)
				{
					error = $"Argument {position} follows a free-text argument, which must be last.";
					return false;
				}

				bool optional = false;
				if (entry[entry.Length - 1] == OptionalMarker)
				{
					optional = true;
					entry = entry.Substring(0, entry.Length - 1).TrimEnd();
				}

				int colon = entry.IndexOf(KindSeparator);
				if (colon < 0)
				{
					error = $"Argument {position} has no kind.";
					return false;
				}

				string label = entry.Substring(0, colon).Trim();
				if (label.Length == 0)
				{
					error = $"Argument {position} has no label.";
					return false;
				}

				string rest = entry.Substring(colon + 1).Trim();
				string kindName = rest;
				string choiceList = null;

				int marker = rest.IndexOf(ChoiceMarker);
				if (marker >= 0)
				{
					kindName = rest.Substring(0, marker).Trim();
					choiceList = rest.Substring(marker + 1);
				}

				if (!TryParseKind(kindName, out StaffArgumentKind kind))
				{
					error = $"Argument {position} ({label}) has an unknown kind.";
					return false;
				}

				List<string> choices = new List<string>();
				if (choiceList != null)
				{
					if (kind != StaffArgumentKind.Choice)
					{
						error = $"Argument {position} ({label}) lists words but is not a choice.";
						return false;
					}

					foreach (string raw in choiceList.Split(ChoiceSeparator))
					{
						string word = raw.Trim();
						if (word.Length == 0)
						{
							continue;
						}
						if (ContainsWhitespace(word))
						{
							error = $"Argument {position} ({label}) has a choice with a space in it.";
							return false;
						}
						if (!ContainsIgnoreCase(choices, word))
						{
							choices.Add(word);
						}
					}
				}

				if (kind == StaffArgumentKind.Choice && choices.Count == 0)
				{
					error = $"Argument {position} ({label}) is a choice with nothing to choose from.";
					return false;
				}

				arguments.Add(new StaffArgument()
				{
					Label = label,
					Kind = kind,
					Choices = choices,
					Optional = optional,
				});
			}

			return true;
		}

		/// <summary>
		/// Reads a kind name.
		/// </summary>
		/// <remarks>
		/// Numeric names are refused before <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
		/// sees them: it accepts any integer string and hands back an undefined enum value, which
		/// <see cref="Enum.IsDefined"/> would then have to catch.
		/// </remarks>
		private static bool TryParseKind(string name, out StaffArgumentKind kind)
		{
			kind = default;

			if (string.IsNullOrEmpty(name) || !char.IsLetter(name[0]))
			{
				return false;
			}

			return Enum.TryParse(name, true, out kind) && Enum.IsDefined(typeof(StaffArgumentKind), kind);
		}

		/// <summary>
		/// Checks one value against its argument.
		/// </summary>
		/// <param name="argument">The argument the value is for.</param>
		/// <param name="raw">What was entered. Null is blank.</param>
		/// <param name="value">The value as it goes on the line: trimmed, and a choice in its listed spelling. Empty when blank.</param>
		/// <param name="error">Why the value was refused, or null.</param>
		/// <returns>True when the value may be sent. A blank optional is acceptable and is left off the line.</returns>
		public static bool TryValidateValue(StaffArgument argument, string raw, out string value, out string error)
		{
			value = (raw ?? string.Empty).Trim();
			error = null;

			if (argument == null)
			{
				error = "There is no such argument.";
				return false;
			}

			if (value.Length == 0)
			{
				if (argument.Optional)
				{
					return true;
				}
				error = $"{argument.Label} is required.";
				return false;
			}

			if (argument.Kind == StaffArgumentKind.Text)
			{
				if (value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
				{
					error = $"{argument.Label} must be on one line.";
					return false;
				}
				return true;
			}

			if (ContainsWhitespace(value))
			{
				error = $"{argument.Label} must be a single word.";
				return false;
			}

			switch (argument.Kind)
			{
				case StaffArgumentKind.Integer:
					if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
					{
						error = $"{argument.Label} must be a whole number.";
						return false;
					}
					break;

				case StaffArgumentKind.Number:
					if (!double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
							CultureInfo.InvariantCulture, out double number) ||
						double.IsNaN(number) || double.IsInfinity(number))
					{
						error = $"{argument.Label} must be a number.";
						return false;
					}
					break;

				case StaffArgumentKind.Ticket:
					if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long ticket) || ticket <= 0)
					{
						error = $"{argument.Label} must be a ticket number.";
						return false;
					}
					break;

				case StaffArgumentKind.Duration:
					if (!DurationPattern.IsMatch(value))
					{
						error = $"{argument.Label} must be a duration such as 30m, 2h or 7d.";
						return false;
					}
					break;

				case StaffArgumentKind.Choice:
					string match = FindIgnoreCase(argument.Choices, value);
					if (match == null)
					{
						error = $"{argument.Label} must be one of: {string.Join(", ", argument.Choices)}.";
						return false;
					}
					value = match;
					break;
			}

			return true;
		}

		/// <summary>
		/// Composes the chat line for a command.
		/// </summary>
		/// <param name="entry">The command.</param>
		/// <param name="arguments">Its parsed arguments.</param>
		/// <param name="values">One entered value per argument, in order. Missing entries are blank.</param>
		/// <param name="line">
		/// The line, whenever every value was acceptable — including when it is too long to send, so
		/// the console can show what it would have been.
		/// </param>
		/// <param name="error">Why the line may not be sent, or null.</param>
		/// <returns>True when <paramref name="line"/> may be sent as it is.</returns>
		public static bool TryCompose(StaffCommandEntry entry, IReadOnlyList<StaffArgument> arguments,
			IReadOnlyList<string> values, out string line, out string error)
		{
			line = null;
			error = null;

			string command = (entry.Command ?? string.Empty).Trim();
			if (command.Length == 0)
			{
				error = "This command has no name.";
				return false;
			}

			StringBuilder builder = new StringBuilder(command);

			string name = (entry.Name ?? string.Empty).Trim();
			if (name.Length > 0)
			{
				builder.Append(' ').Append(name);
			}

			int count = arguments != null ? arguments.Count : 0;
			for (int i = 0; i < count; ++i)
			{
				StaffArgument argument = arguments[i];
				string raw = values != null && i < values.Count ? values[i] : null;

				if (!TryValidateValue(argument, raw, out string value, out error))
				{
					return false;
				}

				/* A blank optional argument is left out entirely: no placeholder, and no refusal when
				 * a later argument is filled. The server tells the arguments apart by their shape — a
				 * leading number where an optional character name may stand means no name was given —
				 * so the line carries exactly the values that were entered, in spec order. */
				if (value.Length == 0)
				{
					continue;
				}

				builder.Append(' ').Append(value);
			}

			line = builder.ToString();

			if (line.Length > ChatBroadcast.MaxTextLength)
			{
				error = $"The command is {line.Length} characters long; a chat line may be at most {ChatBroadcast.MaxTextLength}.";
				return false;
			}

			return true;
		}

		/// <summary>True when <paramref name="text"/> contains any whitespace character.</summary>
		private static bool ContainsWhitespace(string text)
		{
			for (int i = 0; i < text.Length; ++i)
			{
				if (char.IsWhiteSpace(text[i]))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>True when <paramref name="list"/> holds <paramref name="word"/>, ignoring case.</summary>
		private static bool ContainsIgnoreCase(IReadOnlyList<string> list, string word)
		{
			return FindIgnoreCase(list, word) != null;
		}

		/// <summary>The listed spelling of <paramref name="word"/>, or null when it is not listed.</summary>
		private static string FindIgnoreCase(IReadOnlyList<string> list, string word)
		{
			if (list == null)
			{
				return null;
			}
			for (int i = 0; i < list.Count; ++i)
			{
				if (string.Equals(list[i], word, StringComparison.OrdinalIgnoreCase))
				{
					return list[i];
				}
			}
			return null;
		}
	}
}
