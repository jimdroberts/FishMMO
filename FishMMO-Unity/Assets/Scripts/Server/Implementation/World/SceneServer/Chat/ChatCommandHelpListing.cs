using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Auth.Core;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The text <c>/help</c> answers with, built from the command registry and the caller's access level.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Visibility is the gate's own rule.</b> A command is listed only when
	/// <see cref="ChatHelper.TryParseCommand"/> would run it for this caller: above
	/// <see cref="AccessLevel.Banned"/> and at or above the command's registered level. The level is
	/// the caller's server-loaded <see cref="Shared.Core.IPlayerCharacter.AccessLevel"/>; nothing the
	/// client sends reaches this. A player therefore never sees <c>/gm</c> or <c>/admin</c>, and a
	/// game master never sees <c>/admin</c>.
	/// </para>
	/// <para>
	/// <b>Sub-commands are not listed here.</b> <c>/gm</c> and <c>/admin</c> are one entry each whose
	/// summary points at their own <c>help</c>, which is derived from their tables and is itself
	/// behind their access gate. Nothing in this listing could leak a sub-command name.
	/// </para>
	/// <para>
	/// <b>No oracle.</b> <c>/help &lt;command&gt;</c> answers a command the caller may not use with
	/// exactly the line it answers a command that does not exist, and echoes nothing typed.
	/// </para>
	/// <para>
	/// Pure: nothing here touches server state, so the rules can be tested against any registry.
	/// </para>
	/// </remarks>
	public static class ChatCommandHelpListing
	{
		/// <summary>
		/// Longest line composed, below <see cref="ChatBroadcast.MaxTextLength"/>; the client discards
		/// a longer line outright.
		/// </summary>
		public const int LineLength = 120;

		/// <summary>The first line of the full listing.</summary>
		public const string Header = "Commands you can use (/help <command> describes one):";

		/// <summary>The answer to a topic the caller cannot use, whether or not it exists.</summary>
		public const string UnknownCommand = "Unknown command. /help lists the commands you can use.";

		/// <summary>Longest category name shown before a listing line.</summary>
		public const int MaxCategoryLength = 24;

		/// <summary>Category for registered commands nobody described.</summary>
		public const string UndescribedCategory = "Other";

		/// <summary>The category listed first; the rest follow by access level, then name.</summary>
		public const string ChatCategory = "Chat";

		/// <summary>One listed command: every word that runs it, and what it says about itself.</summary>
		public sealed class Entry
		{
			/// <summary>The main word first, then its aliases, each with its slash.</summary>
			public IReadOnlyList<string> Words;

			/// <summary>The group it is listed under.</summary>
			public string Category;

			/// <summary>Argument text, possibly empty.</summary>
			public string Arguments;

			/// <summary>One line describing it, possibly empty.</summary>
			public string Summary;

			/// <summary>The level it is registered at.</summary>
			public AccessLevel Level;

			/// <summary>The words joined by <c>|</c>, then the arguments.</summary>
			public string Usage => string.Join("|", Words) + (string.IsNullOrWhiteSpace(Arguments) ? string.Empty : " " + Arguments.Trim());
		}

		/// <summary>True when a caller at <paramref name="caller"/> may run a command registered at <paramref name="required"/>.</summary>
		/// <remarks>The same test as <see cref="ChatHelper.TryParseCommand"/>: nothing is usable from Banned.</remarks>
		public static bool CanUse(AccessLevel caller, AccessLevel required)
		{
			return caller > AccessLevel.Banned && caller >= required;
		}

		/// <summary>Builds the <c>/help</c> reply from the live <see cref="ChatHelper"/> registry.</summary>
		/// <param name="caller">The caller's server-loaded access level.</param>
		/// <param name="topic">The text after <c>/help</c>; empty for the full listing.</param>
		public static IReadOnlyList<string> Build(AccessLevel caller, string topic)
		{
			return Build(caller, topic, ChatHelper.Commands, ChatHelper.CommandHelp, ChatHelper.ChannelCommandMap, ChatHelper.ChannelCommandHelp);
		}

		/// <summary>Builds the <c>/help</c> reply from the registry given.</summary>
		/// <param name="caller">The caller's server-loaded access level.</param>
		/// <param name="topic">The text after <c>/help</c>; empty for the full listing.</param>
		/// <param name="commands">Registered slash commands and their levels.</param>
		/// <param name="help">Help text for slash commands, keyed by main word.</param>
		/// <param name="channelWords">Channel commands and the words that select each.</param>
		/// <param name="channelHelp">Help text for channel commands.</param>
		/// <returns>Lines, each no longer than <see cref="LineLength"/>.</returns>
		public static IReadOnlyList<string> Build(AccessLevel caller, string topic,
			IReadOnlyDictionary<string, ChatCommandRegistration> commands,
			IReadOnlyDictionary<string, ChatCommandHelp> help,
			IReadOnlyDictionary<ChatChannel, List<string>> channelWords,
			IReadOnlyDictionary<ChatChannel, ChatCommandHelp> channelHelp)
		{
			List<Entry> entries = VisibleEntries(caller, commands, help, channelWords, channelHelp);

			string word = OperatorCommandParsing.SplitFirstWord(topic, out _);
			if (word.Length > 0)
			{
				return Describe(entries, word);
			}

			var lines = new List<string>();
			if (entries.Count == 0)
			{
				return lines;
			}
			lines.Add(Header);

			IEnumerable<IGrouping<string, Entry>> groups = entries
				.GroupBy(e => e.Category)
				.OrderBy(g => g.Key == ChatCategory ? 0 : 1)
				.ThenBy(g => g.Key == UndescribedCategory ? 1 : 0)
				.ThenBy(g => g.Min(e => e.Level))
				.ThenBy(g => g.Key, StringComparer.Ordinal);
			foreach (IGrouping<string, Entry> group in groups)
			{
				/* The category is free text from a registration site. PackLines keeps its prefix whole,
				 * so an over-long one would push every line past the limit and the client would drop them. */
				string prefix = OperatorCommandParsing.Truncate(group.Key, MaxCategoryLength) + ": ";
				lines.AddRange(OperatorCommandParsing.PackLines(group.Select(e => e.Usage), prefix, ", ", LineLength));
			}
			return lines;
		}

		/// <summary>
		/// Every command the caller may run: channel commands first, then described slash commands,
		/// then any registered word nobody described.
		/// </summary>
		public static List<Entry> VisibleEntries(AccessLevel caller,
			IReadOnlyDictionary<string, ChatCommandRegistration> commands,
			IReadOnlyDictionary<string, ChatCommandHelp> help,
			IReadOnlyDictionary<ChatChannel, List<string>> channelWords,
			IReadOnlyDictionary<ChatChannel, ChatCommandHelp> channelHelp)
		{
			var entries = new List<Entry>();
			if (!CanUse(caller, AccessLevel.Player))
			{
				return entries;
			}

			var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			if (channelWords != null)
			{
				foreach (KeyValuePair<ChatChannel, List<string>> pair in channelWords)
				{
					List<string> words = (pair.Value ?? new List<string>()).Where(w => !string.IsNullOrEmpty(w) && claimed.Add(w)).ToList();
					if (words.Count == 0)
					{
						continue;
					}
					ChatCommandHelp described = null;
					channelHelp?.TryGetValue(pair.Key, out described);
					entries.Add(new Entry()
					{
						Words = words,
						Category = ChatCategory,
						Arguments = described?.Arguments ?? string.Empty,
						Summary = described?.Summary ?? string.Empty,
						Level = AccessLevel.Player,
					});
				}
			}

			if (commands == null)
			{
				return entries;
			}

			if (help != null)
			{
				foreach (KeyValuePair<string, ChatCommandHelp> pair in help.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
				{
					if (pair.Value == null ||
						claimed.Contains(pair.Key) ||
						!commands.TryGetValue(pair.Key, out ChatCommandRegistration registration) ||
						!CanUse(caller, registration.MinimumAccessLevel))
					{
						continue;
					}

					var words = new List<string>() { pair.Key };
					claimed.Add(pair.Key);
					foreach (string alias in pair.Value.Aliases ?? Array.Empty<string>())
					{
						// An alias is listed only when it is itself registered and runnable by this caller.
						if (!string.IsNullOrEmpty(alias) &&
							commands.TryGetValue(alias, out ChatCommandRegistration aliasRegistration) &&
							CanUse(caller, aliasRegistration.MinimumAccessLevel) &&
							claimed.Add(alias))
						{
							words.Add(alias);
						}
					}

					entries.Add(new Entry()
					{
						Words = words,
						Category = string.IsNullOrWhiteSpace(pair.Value.Category) ? UndescribedCategory : pair.Value.Category,
						Arguments = pair.Value.Arguments ?? string.Empty,
						Summary = pair.Value.Summary ?? string.Empty,
						Level = registration.MinimumAccessLevel,
					});
				}
			}

			foreach (KeyValuePair<string, ChatCommandRegistration> pair in commands.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
			{
				if (claimed.Contains(pair.Key) || !CanUse(caller, pair.Value.MinimumAccessLevel))
				{
					continue;
				}
				claimed.Add(pair.Key);
				entries.Add(new Entry()
				{
					Words = new[] { pair.Key },
					Category = UndescribedCategory,
					Arguments = string.Empty,
					Summary = string.Empty,
					Level = pair.Value.MinimumAccessLevel,
				});
			}

			return entries;
		}

		/// <summary>Answers <c>/help &lt;command&gt;</c> from the caller's visible entries only.</summary>
		private static IReadOnlyList<string> Describe(List<Entry> entries, string word)
		{
			if (!word.StartsWith("/", StringComparison.Ordinal))
			{
				word = "/" + word;
			}

			Entry entry = entries.FirstOrDefault(e => e.Words.Any(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase)));
			if (entry == null)
			{
				return new[] { UnknownCommand };
			}

			var lines = new List<string>() { OperatorCommandParsing.Truncate("Usage: " + entry.Usage, LineLength) };
			if (!string.IsNullOrWhiteSpace(entry.Summary))
			{
				lines.Add(OperatorCommandParsing.Truncate(entry.Summary.Trim(), LineLength));
			}
			return lines;
		}
	}
}
