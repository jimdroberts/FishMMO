using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Database;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The machinery shared by the <c>/gm</c> and <c>/admin</c> command sets: the command table,
	/// its dispatcher, its help, and the reply and target helpers every handler uses.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One registration per set, and one table per registration.</b> Each set is registered
	/// once with <see cref="ChatHelper"/> at its access level, so one access check — and the audit
	/// hook at that check — covers every sub-command. The table is what the dispatcher runs, what
	/// <c>help</c> prints, and what the staff console's catalogue is built from, so a command
	/// cannot exist in one of those and be missing or described differently in another.
	/// </para>
	/// <para>
	/// <b>Nothing in this file changes game state.</b> It is deliberately neutral ground: the game
	/// master files may call anything here, and a test pins that they call nothing declared in the
	/// administrator files. A helper that grants, heals, or edits a character belongs in an
	/// administrator file, where that pin keeps it out of a game master's reach.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		/// <summary>
		/// Longest line the helpers compose, below <see cref="ChatBroadcast.MaxTextLength"/> so a
		/// line exactly at the cap is never cut by a later prefix.
		/// </summary>
		private const int OperatorLineLength = 120;

		/// <summary>One operator sub-command: what it is called, how it is described, and what runs.</summary>
		private sealed class OperatorCommand
		{
			/// <summary>The word typed after the set's command.</summary>
			public string Name;

			/// <summary>Other words accepted for it. Not shown in the console.</summary>
			public string[] Aliases = Array.Empty<string>();

			/// <summary>Group for help and the console.</summary>
			public string Category;

			/// <summary>One line saying what it does.</summary>
			public string Summary;

			/// <summary>Argument spec, in the grammar documented on <see cref="StaffCommandEntry"/>.</summary>
			public string Arguments = string.Empty;

			/// <summary>The first argument is a character, so the console may offer this on a roster row.</summary>
			public bool RosterAction;

			/// <summary>The first argument is a ticket number, so the console may offer this on a ticket.</summary>
			public bool TicketAction;

			/// <summary>The console confirms before sending.</summary>
			public bool Destructive;

			/// <summary>The handler: the caller and the argument text after the sub-command word.</summary>
			public Action<IPlayerCharacter, string> Run;
		}

		/// <summary>A registered command and the sub-commands it dispatches to.</summary>
		private sealed class OperatorCommandSet
		{
			/// <summary>The registered command, including its slash.</summary>
			public readonly string Command;

			/// <summary>The access level it is registered at.</summary>
			public readonly AccessLevel Level;

			/// <summary>Every sub-command, in presentation order.</summary>
			public readonly IReadOnlyList<OperatorCommand> Commands;

			private readonly Dictionary<string, OperatorCommand> byWord =
				new Dictionary<string, OperatorCommand>(StringComparer.OrdinalIgnoreCase);

			/// <summary>Builds the set, refusing a word claimed twice.</summary>
			/// <remarks>
			/// Throws rather than letting the later entry win. Two sub-commands answering to one word
			/// means one of them can never be run, which is a defect worth failing server start on
			/// rather than one an operator discovers by typing it.
			/// </remarks>
			public OperatorCommandSet(string command, AccessLevel level, IEnumerable<OperatorCommand> commands)
			{
				Command = command;
				Level = level;
				var list = new List<OperatorCommand>();
				foreach (OperatorCommand entry in commands)
				{
					if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || entry.Run == null)
					{
						throw new InvalidOperationException($"{command}: a sub-command has no name or no handler.");
					}
					Claim(entry.Name, entry);
					foreach (string alias in entry.Aliases ?? Array.Empty<string>())
					{
						Claim(alias, entry);
					}
					list.Add(entry);
				}
				Commands = list;
			}

			/// <summary>Resolves a typed word to its sub-command.</summary>
			public bool TryGet(string word, out OperatorCommand command)
			{
				return byWord.TryGetValue(word ?? string.Empty, out command);
			}

			private void Claim(string word, OperatorCommand entry)
			{
				if (word.Equals("help", StringComparison.OrdinalIgnoreCase) || byWord.ContainsKey(word))
				{
					throw new InvalidOperationException($"{Command}: '{word}' is claimed twice.");
				}
				byWord.Add(word, entry);
			}
		}

		/// <summary>The <c>/admin</c> set. Built at registration.</summary>
		private OperatorCommandSet adminCommands;

		/// <summary>The <c>/gm</c> set. Built at registration.</summary>
		private OperatorCommandSet gameMasterCommands;

		/// <summary>
		/// Runs the sub-command named by the first word of <paramref name="msg"/>.
		/// </summary>
		/// <remarks>
		/// Access has already been checked by <see cref="ChatHelper.TryParseCommand"/> against the
		/// set's registration, and the command has already been audited there.
		/// </remarks>
		/// <returns>Always true: an operator command is consumed and never echoed to chat.</returns>
		private bool DispatchOperatorCommand(OperatorCommandSet set, IPlayerCharacter character, ChatBroadcast msg)
		{
			if (character == null || set == null)
			{
				return true;
			}

			string word = OperatorCommandParsing.SplitFirstWord(msg.Text, out string arguments);
			if (word.Length == 0 || word.Equals("help", StringComparison.OrdinalIgnoreCase))
			{
				ReplyOperatorHelp(set, character, arguments);
				return true;
			}

			if (!set.TryGet(word, out OperatorCommand command))
			{
				Reply(character, $"'{OperatorCommandParsing.Truncate(word, 24)}' is not a {set.Command} command. {set.Command} help lists them.");
				return true;
			}

			try
			{
				command.Run(character, arguments);
			}
			catch (Exception ex)
			{
				/* A handler that throws has already been recorded by the gate. Answering here is
				 * what stops the operator from running it again to find out whether it worked. */
				Log.Error("SceneServerSystem", $"{set.Command} {command.Name} threw for '{character.Account}': {ex}");
				Reply(character, "The command failed. See the server log.");
			}
			return true;
		}

		/// <summary>
		/// Answers <c>help</c>: every category, one category, or one command's usage.
		/// </summary>
		private void ReplyOperatorHelp(OperatorCommandSet set, IPlayerCharacter character, string topic)
		{
			topic = (topic ?? string.Empty).Trim();
			if (topic.Length > 0)
			{
				if (set.TryGet(topic, out OperatorCommand command))
				{
					ReplyUsage(character, set, command);
					Reply(character, command.Summary);
					return;
				}

				var named = set.Commands
					.Where(c => string.Equals(c.Category, topic, StringComparison.OrdinalIgnoreCase))
					.ToList();
				if (named.Count > 0)
				{
					ReplyLines(character, OperatorCommandParsing.PackLines(
						named.Select(c => c.Name), named[0].Category + ": ", ", ", OperatorLineLength));
					return;
				}

				Reply(character, $"'{OperatorCommandParsing.Truncate(topic, 24)}' is not a {set.Command} command or category.");
				return;
			}

			foreach (IGrouping<string, OperatorCommand> group in set.Commands.GroupBy(c => c.Category))
			{
				ReplyLines(character, OperatorCommandParsing.PackLines(
					group.Select(c => c.Name), group.Key + ": ", ", ", OperatorLineLength));
			}
			Reply(character, $"{set.Command} help <command> shows how to use one.");
		}

		/// <summary>Replies with a command's usage line, derived from its spec.</summary>
		private void ReplyUsage(IPlayerCharacter character, OperatorCommandSet set, OperatorCommand command)
		{
			Reply(character, "Usage: " + OperatorCommandParsing.FormatUsage(set.Command, command.Name, command.Arguments));
		}

		/// <summary>Replies with the usage of a sub-command of <paramref name="set"/> by name.</summary>
		private void ReplyUsage(IPlayerCharacter character, OperatorCommandSet set, string name)
		{
			if (set != null && set.TryGet(name, out OperatorCommand command))
			{
				ReplyUsage(character, set, command);
			}
		}

		#region Replies

		/// <summary>Sends a system-channel line to a character. Main thread only.</summary>
		/// <remarks>
		/// Clamped to <see cref="ChatBroadcast.MaxTextLength"/>. Every line here is written to fit,
		/// but some carry text from the database or from another player, and the client discards a
		/// line over the limit outright — which reads to the operator as no answer at all.
		/// </remarks>
		private void Reply(IPlayerCharacter character, string text)
		{
			NetworkConnection conn = character?.Owner;
			if (string.IsNullOrEmpty(text) || conn == null || !conn.IsActive || Server?.NetworkWrapper == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new ChatBroadcast()
			{
				Channel = ChatChannel.System,
				Text = OperatorCommandParsing.Truncate(text, ChatBroadcast.MaxTextLength),
			}, true, FishNet.Transporting.Channel.Reliable);
		}

		/// <summary>Sends several system-channel lines to a character. Main thread only.</summary>
		private void ReplyLines(IPlayerCharacter character, IEnumerable<string> lines)
		{
			foreach (string line in lines)
			{
				Reply(character, line);
			}
		}

		/// <summary>
		/// Sends a system-channel line to a character resolved by id.
		/// </summary>
		/// <remarks>
		/// Resolved by id rather than by holding the character reference across the await: the
		/// operator may have logged out, changed scene server or been despawned while the database
		/// work ran, and the object would then be a stale reference to a pooled instance now
		/// belonging to somebody else.
		/// </remarks>
		private void ReplyByCharacterID(long characterID, string text)
		{
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping) ||
				!charMapping.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character))
			{
				return;
			}
			Reply(character, text);
		}

		/// <summary>
		/// Runs an operator's database work off the main thread and answers with its single line.
		/// </summary>
		private void RunOperatorAction(IPlayerCharacter character, Func<Task<string>> action)
		{
			RunOperatorLines(character, async () => new[] { await action() });
		}

		/// <summary>
		/// Runs an operator's database work off the main thread and answers with every line it returns.
		/// </summary>
		/// <remarks>
		/// Every path answers. An operator command that appears to do nothing is worse than one that
		/// fails loudly — they will run it again, and for a ban or a shutdown that means a second
		/// write replacing the first. The replies are marshalled back to the main thread because the
		/// broadcast is a network call.
		/// </remarks>
		private void RunOperatorLines(IPlayerCharacter character, Func<Task<IReadOnlyList<string>>> action)
		{
			long characterID = character.ID;

			if (!TryEnqueueAsyncWork(async () =>
			{
				IReadOnlyList<string> lines;
				try
				{
					lines = await action();
				}
				catch (Exception ex)
				{
					await Log.Error("SceneServerSystem", $"Operator command failed: {ex}");
					lines = new[] { "The command failed. See the server log." };
				}

				IReadOnlyList<string> finalLines = lines ?? Array.Empty<string>();
				TryEnqueueMainThread(() =>
				{
					for (int i = 0; i < finalLines.Count; ++i)
					{
						ReplyByCharacterID(characterID, finalLines[i]);
					}
				});
			}, characterID))
			{
				Reply(character, "The server is busy and could not run that command. Try again in a moment.");
			}
		}

		/// <summary>
		/// The answer for a database lookup that came back without the row it asked for.
		/// </summary>
		/// <remarks>
		/// "No such thing" only when the database said so — not found, or a name that could not
		/// name anything. Every lookup used to answer "no account named X" for any failure at all,
		/// so an operator whose command met a database fault was told the account did not exist,
		/// and went looking for a typo that was not there.
		/// </remarks>
		/// <param name="result">The failed lookup, or a successful one that carried nothing.</param>
		/// <param name="notFound">The answer when there genuinely is no such row.</param>
		/// <param name="what">What was being read, for the fault answer.</param>
		private static string DescribeLookupFailure<T>(DatabaseResult<T> result, string notFound, string what)
		{
			if (result.IsSuccess ||
				result.ErrorCode == DatabaseErrorCodes.NotFound ||
				result.ErrorCode == DatabaseErrorCodes.ValidationError)
			{
				return notFound;
			}
			return $"Could not read {what}: [{result.ErrorCode}] {result.ErrorMessage}";
		}

		#endregion

		#region Targets

		/// <summary>Resolves this scene server's online-character mapping.</summary>
		private bool TryGetOnlineCharacters(out ICharacterMappingData<NetworkConnection> mapping)
		{
			return Server.DataContainerRegistry.TryGet(out mapping) && mapping != null;
		}

		/// <summary>
		/// Resolves a character held by this scene server by name, answering the caller when it cannot.
		/// </summary>
		/// <remarks>
		/// Only characters this scene server holds. An operator on one scene server cannot act on a
		/// player held by another, and saying so is better than a lookup that reaches across
		/// processes and acts on a character whose authoritative state lives elsewhere.
		/// </remarks>
		/// <param name="character">The operator.</param>
		/// <param name="arguments">Text whose first word is the name.</param>
		/// <param name="target">The resolved character.</param>
		private bool TryResolveTarget(IPlayerCharacter character, string arguments, out IPlayerCharacter target,
			StaffTargetRank rank = StaffTargetRank.RequireOutranks)
		{
			target = null;

			string name = OperatorCommandParsing.SplitFirstWord(arguments, out _);
			if (name.Length == 0)
			{
				Reply(character, "Name a character.");
				return false;
			}
			if (!TryGetOnlineCharacters(out var mapping))
			{
				Reply(character, "The character mapping is unavailable.");
				return false;
			}

			if (!mapping.CharactersByLowerCaseName.TryGetValue(name.ToLowerInvariant(), out target) || target == null)
			{
				Reply(character, $"'{OperatorCommandParsing.Truncate(name, 32)}' is not on this scene server.");
				return false;
			}

			/* The rank gate lives HERE so a new command inherits it.
			 *
			 * Outranks existed but had exactly one caller — kick. Every other staff action on a live
			 * character went ungated, so a compromised GameMaster could summon and teleport the Admin
			 * investigating them, and an Admin could kill, immobilise or rewrite the currency and
			 * attributes of a peer Admin. That is precisely the case Outranks' own remarks describe:
			 * "a compromised account removing, muting or banning the colleagues who would notice".
			 *
			 * Pasting the check into ten methods would have left the eleventh to remember it, so it
			 * goes in the one place every character-targeting command already passes through. The
			 * opt-out is an enum rather than a bool: SkipOutranks has to be typed out, which makes it
			 * greppable and makes it read wrong on a command that changes the target's state.
			 *
			 * Acting on yourself is always allowed — an operator unsticking or inspecting their own
			 * character is not an escalation, and kick already made that allowance explicitly. */
			if (rank == StaffTargetRank.RequireOutranks &&
				target.ID != character.ID &&
				!Outranks(character, target.AccessLevel))
			{
				Reply(character, "That character is at or above your access level.");
				target = null;
				return false;
			}

			return true;
		}

		/// <summary>
		/// Resolves an optional leading character name: nothing or a number means the caller.
		/// </summary>
		/// <remarks>See <see cref="OperatorCommandParsing.TrySplitLeadingCharacter"/> for why the
		/// rule is about the word's shape and never falls back to the caller on a missing name.</remarks>
		/// <param name="character">The operator.</param>
		/// <param name="arguments">The command's argument text.</param>
		/// <param name="target">The caller, or the named character.</param>
		/// <param name="rest">The arguments after any name.</param>
		/// <param name="rank">Passed through to <see cref="TryResolveTarget"/>. Only consulted when a
		/// name was actually given — the no-name case resolves to the caller, and an operator acting
		/// on their own character is never an escalation.</param>
		private bool TryResolveOptionalTarget(IPlayerCharacter character, string arguments, out IPlayerCharacter target, out string rest,
			StaffTargetRank rank = StaffTargetRank.RequireOutranks)
		{
			if (!OperatorCommandParsing.TrySplitLeadingCharacter(arguments, out string name, out rest))
			{
				target = character;
				return true;
			}
			return TryResolveTarget(character, name, out target, rank);
		}

		/// <summary>Whether a resolved staff target must be outranked by the operator.</summary>
		/// <remarks>
		/// An enum rather than a bool because the opt-out is the dangerous half, and a bare
		/// <c>false</c> at a call site carries no reason with it. <c>SkipOutranks</c> is correct only
		/// for commands that READ the target, or that move the OPERATOR — never for one that changes
		/// the target's state.
		/// </remarks>
		private enum StaffTargetRank
		{
			/// <summary>Refuse a target at or above the operator's level. The default.</summary>
			RequireOutranks = 0,

			/// <summary>Allow any target. Read-only commands, and commands that move the operator.</summary>
			SkipOutranks,
		}

		/// <summary>
		/// True when <paramref name="actor"/> may act on somebody at <paramref name="targetLevel"/>.
		/// </summary>
		/// <remarks>
		/// Strictly below. Two operators at one level acting on each other is the harmless version;
		/// the damaging one is a compromised account removing, muting or banning the colleagues who
		/// would notice.
		/// </remarks>
		private static bool Outranks(IPlayerCharacter actor, AccessLevel targetLevel)
		{
			return actor != null && targetLevel < actor.AccessLevel;
		}

		#endregion

		#region Descriptions

		/// <summary>Describes how long ago something happened, in one short phrase.</summary>
		private static string DescribeSince(DateTime utc)
		{
			if (utc == default || utc.Year < 2000)
			{
				return "never";
			}
			TimeSpan age = DateTime.UtcNow - utc;
			if (age < TimeSpan.Zero)
			{
				return "just now";
			}
			return age.TotalMinutes < 1 ? "just now" : OperatorCommandParsing.DescribeDuration(age) + " ago";
		}

		/// <summary>Describes a chat mute held on a character in memory.</summary>
		private static string DescribeChatMute(long mutedUntilTicks)
		{
			if (mutedUntilTicks == long.MaxValue)
			{
				return "muted with no end";
			}
			long now = DateTime.UtcNow.Ticks;
			return mutedUntilTicks > now
				? "muted for " + OperatorCommandParsing.DescribeDuration(TimeSpan.FromTicks(mutedUntilTicks - now))
				: "not muted";
		}

		#endregion
	}
}
