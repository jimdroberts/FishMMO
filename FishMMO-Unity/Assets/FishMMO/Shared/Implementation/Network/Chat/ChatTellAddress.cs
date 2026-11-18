using System.Text;

namespace FishMMO.Shared
{
	/// <summary>
	/// How a whisper names its target: a single word, or a name in double quotes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Character names may hold single spaces ("Aragorn of Arnor"), and a tell used to take its
	/// target as the first space-delimited word: <c>/tell Aragorn of Arnor hello</c> went to
	/// "Aragorn" — or, when nobody has that name, answered "Aragorn is offline" — and a name with
	/// a space could not be whispered at all. A quoted name is now the target whole:
	/// <c>/tell "Aragorn of Arnor" hello</c>. An unquoted target is still one word, exactly as
	/// before, so every single-word tell a player already types is unchanged.
	/// </para>
	/// <para>
	/// Unquoted text is never matched against longer names. Guessing that "Aragorn of Arnor hello"
	/// meant the longest name some online character happens to have would decide who receives a
	/// private message by who is logged in; a quote is an explicit rule the sender controls.
	/// </para>
	/// <para>
	/// <b>The same address is the persisted row's.</b> A whisper is stored as its address, one
	/// space, then the body (<see cref="FormatLine"/>), and the target's own scene server finds it
	/// by that address — <c>ChatService.BuildPumpFilterSql</c> extracts it in SQL with the same
	/// rule (the quoted name when the row starts with a quote, otherwise the first word), and
	/// <c>ChatSystem.PumpKeyOf</c> and the tell handler parse it here. A single-word name is
	/// written unquoted, so its rows are byte-for-byte what they always were.
	/// </para>
	/// <para>
	/// Pure and free of every project dependency, like <see cref="ChatSanitizer"/>, so the rule
	/// can be pinned by a test without a server.
	/// </para>
	/// </remarks>
	public static class ChatTellAddress
	{
		/// <summary>The character that opens and closes a quoted name.</summary>
		public const char Quote = '"';

		/// <summary>
		/// Splits a whisper into its target and its body.
		/// </summary>
		/// <param name="text">The text after the tell command: <c>Bob hello</c> or <c>"First Last" hello</c>.</param>
		/// <param name="targetName">The target: the quoted name with its spacing normalised, or the first word.</param>
		/// <param name="body">The message, trimmed.</param>
		/// <returns>
		/// True when there is a target and a non-empty body. A quote that is never closed, an empty
		/// quoted name and a tell with nothing to say are all refused, as a tell with no body always was.
		/// </returns>
		public static bool TryParse(string text, out string targetName, out string body)
		{
			if (!TrySplit(text, out targetName, out body) || body.Length < 1)
			{
				targetName = string.Empty;
				body = string.Empty;
				return false;
			}
			return true;
		}

		/// <summary>
		/// Reads only the address at the start of a whisper, whether or not a body follows.
		/// </summary>
		/// <param name="text">A whisper, typed or persisted.</param>
		/// <param name="targetName">The target name, or empty.</param>
		/// <returns>True when an address could be read.</returns>
		/// <remarks>
		/// For the pump's relevance key, which must agree with the SQL: the SQL reads the address
		/// and never looks at the body.
		/// </remarks>
		public static bool TryParseAddress(string text, out string targetName)
		{
			return TrySplit(text, out targetName, out _);
		}

		/// <summary>
		/// The address to write for a target: the name itself when it is one word, else quoted.
		/// </summary>
		/// <param name="targetName">A character name (letters and single spaces).</param>
		/// <returns>The address.</returns>
		public static string Format(string targetName)
		{
			string name = NormalizeName(targetName);
			return name.IndexOf(' ') < 0 ? name : Quote + name + Quote;
		}

		/// <summary>
		/// A whisper as it is persisted and parsed: the address, a space, then the body.
		/// </summary>
		/// <param name="targetName">The target.</param>
		/// <param name="body">The message.</param>
		/// <returns>The line.</returns>
		public static string FormatLine(string targetName, string body)
		{
			return Format(targetName) + " " + (body ?? string.Empty);
		}

		/// <summary>
		/// The text to pre-fill a chat input with to whisper a character, ready for the message.
		/// </summary>
		/// <param name="targetName">The target.</param>
		/// <returns><c>/tell Bob </c> or <c>/tell "First Last" </c>.</returns>
		public static string FormatCommand(string targetName)
		{
			return "/tell " + Format(targetName) + " ";
		}

		/// <summary>
		/// Trims a name and collapses every run of whitespace inside it to one space.
		/// </summary>
		/// <remarks>
		/// Character names hold single spaces only, so <c>"First  Last"</c> typed with two can only
		/// mean one character, and the persisted address is always the normalised name.
		/// </remarks>
		/// <param name="name">The name as typed.</param>
		/// <returns>The normalised name, or empty.</returns>
		public static string NormalizeName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return string.Empty;
			}

			string trimmed = name.Trim();
			bool clean = true;
			for (int i = 1; i < trimmed.Length; ++i)
			{
				if (char.IsWhiteSpace(trimmed[i]) && (trimmed[i] != ' ' || char.IsWhiteSpace(trimmed[i - 1])))
				{
					clean = false;
					break;
				}
			}
			if (clean)
			{
				return trimmed;
			}

			StringBuilder builder = new StringBuilder(trimmed.Length);
			bool previousWasSpace = false;
			for (int i = 0; i < trimmed.Length; ++i)
			{
				char c = trimmed[i];
				if (char.IsWhiteSpace(c))
				{
					if (!previousWasSpace)
					{
						builder.Append(' ');
					}
					previousWasSpace = true;
					continue;
				}
				builder.Append(c);
				previousWasSpace = false;
			}
			return builder.ToString();
		}

		/// <summary>
		/// Reads the address and whatever follows it.
		/// </summary>
		private static bool TrySplit(string text, out string targetName, out string rest)
		{
			targetName = string.Empty;
			rest = string.Empty;
			if (string.IsNullOrEmpty(text))
			{
				return false;
			}

			int start = 0;
			while (start < text.Length && char.IsWhiteSpace(text[start]))
			{
				++start;
			}
			if (start >= text.Length)
			{
				return false;
			}

			if (text[start] == Quote)
			{
				int close = text.IndexOf(Quote, start + 1);
				if (close < 0)
				{
					return false;
				}
				targetName = NormalizeName(text.Substring(start + 1, close - start - 1));
				if (targetName.Length < 1)
				{
					return false;
				}
				rest = text.Substring(close + 1).Trim();
				return true;
			}

			int space = text.IndexOf(' ', start);
			if (space < 0)
			{
				targetName = text.Substring(start);
				return true;
			}
			targetName = text.Substring(start, space - start);
			rest = text.Substring(space + 1).Trim();
			return true;
		}
	}
}
