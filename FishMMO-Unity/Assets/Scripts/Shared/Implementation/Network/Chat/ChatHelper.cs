using System;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Auth.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Delegate for chat commands.
	/// </summary>
	/// <remarks>
	/// The return value is currently ignored by <see cref="ChatHelper.TryParseCommand"/>: a
	/// slash command is consumed and never written to the chat log, whatever it returns. It was
	/// documented as "true if the chat message should be written to the database", which was
	/// never honoured — persisting a command would need a channel and a sanitized body it does
	/// not have. Kept in the signature so every existing command compiles unchanged.
	/// </remarks>
	public delegate bool ChatCommand(IPlayerCharacter character, ChatBroadcast msg);

	/// <summary>
	/// A registered slash command and the minimum access level allowed to run it.
	/// </summary>
	public struct ChatCommandRegistration
	{
		/// <summary>Handler invoked when an authorised character runs the command.</summary>
		public ChatCommand Func;

		/// <summary>
		/// Lowest <see cref="AccessLevel"/> permitted to run this command.
		/// </summary>
		/// <remarks>
		/// Enforced server-side in <see cref="ChatHelper.TryParseCommand"/> against the
		/// character's own <see cref="IPlayerCharacter.AccessLevel"/>, which is loaded from the
		/// character row — never from anything the client sends. The client does receive its own
		/// access level in the spawn payload, but only so a UI can hide what it cannot use;
		/// nothing on the client is trusted here.
		/// </remarks>
		public AccessLevel MinimumAccessLevel;
	}

	/// <summary>
	/// Struct containing details for a chat command, including the channel and the command function.
	/// </summary>
	public struct ChatCommandDetails
	{
		/// <summary>Chat channel associated with the command.</summary>
		public ChatChannel Channel;
		/// <summary>Function to execute for the command.</summary>
		public ChatCommand Func;
	}

	/// <summary>
	/// Static helper class for chat-related functionality, including command parsing, channel mapping, and message sanitization.
	/// </summary>
	public static class ChatHelper
	{
		/// <summary>Prefix prepended to all chat error/control codes to prevent collision with player messages.</summary>
		private const string ChatCodePrefix = "FISHMMO_";

		/// <summary>Error code for when the target is already in a guild.</summary>
		public const string GUILD_ERROR_TARGET_IN_GUILD = ChatCodePrefix + "GUILD_ERROR_TARGET_IN_GUILD";
		/// <summary>Error code for when the target is already in a party.</summary>
		public const string PARTY_ERROR_TARGET_IN_PARTY = ChatCodePrefix + "PARTY_ERROR_TARGET_IN_PARTY";
		/// <summary>Code for relayed tell messages.</summary>
		public const string TELL_RELAYED = ChatCodePrefix + "TELL_RELAYED";
		/// <summary>Error code for sending a tell message to oneself.</summary>
		public const string TELL_ERROR_MESSAGE_SELF = ChatCodePrefix + "TELL_ERROR_MESSAGE_SELF";
		/// <summary>Error code for when the target is offline.</summary>
		public const string TARGET_OFFLINE = ChatCodePrefix + "TARGET_OFFLINE";

		/* The Unity Rich Text tag patterns that used to live here now live in
		 * ChatSanitizer.CombinedRichTextPattern.
		 *
		 * They moved so the sanitiser could be compiled and tested on its own. This class pulls in
		 * FishMMO.Logging, IPlayerCharacter and AccessLevel, none of which a text filter needs, and
		 * all of which have to be stood up before a test can call a single method on it. The filter
		 * is security code and is cheap to test; nothing about it should be hard to reach. */


		private static bool initialized = false;

		/// <summary>
		/// Registered slash commands, keyed by command word without the leading slash.
		/// </summary>
		/// <remarks>
		/// Case-insensitive. Players type commands by hand and <c>/LeaveInstance</c> is the same
		/// intent as <c>/leaveinstance</c>; an ordinal comparer silently treated the first as
		/// ordinary chat and broadcast it to the channel.
		/// </remarks>
		public static Dictionary<string, ChatCommandRegistration> Commands { get; private set; }

		/// <summary>
		/// Dictionary mapping chat channels to their command details.
		/// </summary>
		public static Dictionary<ChatChannel, ChatCommandDetails> ChatChannelCommands { get; private set; }

		/// <summary>
		/// Dictionary mapping command strings to chat command details.
		/// </summary>
		public static Dictionary<string, ChatCommandDetails> CommandChannelMap { get; } =
			new Dictionary<string, ChatCommandDetails>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Dictionary mapping chat channels to their supported command strings.
		/// </summary>
		public static Dictionary<ChatChannel, List<string>> ChannelCommandMap { get; } = new Dictionary<ChatChannel, List<string>>()
	   {
		   { ChatChannel.World, new List<string>() { "/w", "/world", } },
		   { ChatChannel.Region, new List<string>() { "/r", "/region", } },
		   { ChatChannel.Party, new List<string>() { "/p", "/party", } },
		   { ChatChannel.Guild, new List<string>() { "/g", "/guild", } },
		   { ChatChannel.Tell, new List<string>() { "/tell", } },
		   { ChatChannel.Trade, new List<string>() { "/t", "/trade", } },
		   { ChatChannel.Say, new List<string>() { "/s", "/say", } },
	   };

		/// <summary>
		/// Static constructor initializes command dictionaries.
		/// </summary>
		static ChatHelper()
		{
			Commands = new Dictionary<string, ChatCommandRegistration>(StringComparer.OrdinalIgnoreCase);
			ChatChannelCommands = new Dictionary<ChatChannel, ChatCommandDetails>();
		}

		/// <summary>
		/// Raised when a character runs a command it does not have the access level for.
		/// </summary>
		/// <remarks>
		/// A refused privileged command is a security event, and this class is engine-agnostic
		/// shared code with no server context to log it against. The server subscribes and
		/// records who tried what.
		/// </remarks>
		public static event Action<IPlayerCharacter, string, AccessLevel> OnCommandRefused;

		/// <summary>
		/// Initializes chat channel commands once, mapping each channel to its command function.
		/// </summary>
		/// <param name="onGetChannelCommand">Function to get the command delegate for each channel.</param>
		public static void InitializeOnce(Func<ChatChannel, ChatCommand> onGetChannelCommand)
		{
			if (initialized) return;
			initialized = true;

			foreach (KeyValuePair<ChatChannel, List<string>> pair in ChatHelper.ChannelCommandMap)
			{
				AddChatCommandDetails(pair.Value, new ChatCommandDetails()
				{
					Channel = pair.Key,
					Func = onGetChannelCommand?.Invoke(pair.Key),
				});
			}
		}

		/// <summary>
		/// Registers slash commands runnable by any player.
		/// </summary>
		/// <param name="commands">Dictionary of command strings and their delegates.</param>
		public static void AddCommands(Dictionary<string, ChatCommand> commands)
		{
			AddCommands(commands, AccessLevel.Player);
		}

		/// <summary>
		/// Registers slash commands that require at least <paramref name="minimumAccessLevel"/>.
		/// </summary>
		/// <remarks>
		/// The level is attached at registration rather than checked inside each handler, so a
		/// command cannot be added without one being considered. <see cref="TryParseCommand"/>
		/// is the single place the check happens.
		/// </remarks>
		/// <param name="commands">Dictionary of command strings and their delegates.</param>
		/// <param name="minimumAccessLevel">Lowest access level permitted to run them.</param>
		public static void AddCommands(Dictionary<string, ChatCommand> commands, AccessLevel minimumAccessLevel)
		{
			if (commands == null)
				return;

			foreach (KeyValuePair<string, ChatCommand> pair in commands)
			{
				Log.Debug("ChatHelper", $"Added Command[{pair.Key}] (min access {minimumAccessLevel})");
				Commands[pair.Key] = new ChatCommandRegistration()
				{
					Func = pair.Value,
					MinimumAccessLevel = minimumAccessLevel,
				};
			}
		}

		/// <summary>
		/// Unregisters slash commands.
		/// </summary>
		/// <remarks>
		/// Every system that registers must remove on teardown. <see cref="Commands"/> is static
		/// and holds delegates bound to <c>ScriptableObject</c> server behaviours, which survive
		/// a play-session restart in the editor while the objects they point at do not — so a
		/// command left behind either runs against a destroyed instance or, worse, against the
		/// previous session's state.
		/// </remarks>
		/// <param name="commands">Command strings to remove.</param>
		public static void RemoveCommands(IEnumerable<string> commands)
		{
			if (commands == null)
				return;

			foreach (string command in commands)
			{
				Commands.Remove(command);
			}
		}

		/// <summary>
		/// Adds chat command details for a list of command strings, mapping them to a channel and function.
		/// </summary>
		/// <param name="commands">List of command strings.</param>
		/// <param name="details">Details containing channel and function.</param>
		internal static void AddChatCommandDetails(List<string> commands, ChatCommandDetails details)
		{
			foreach (string command in commands)
			{
				Log.Debug("ChatHelper", $"Added Chat Command[" + command + "]");
				ChatChannelCommands[details.Channel] = details;
				// Assignment rather than Add: Add throws on a duplicate key, which turns a
				// double registration into a startup crash instead of a harmless overwrite.
				CommandChannelMap[command] = details;
			}
		}

		/// <summary>
		/// Clears the channel-command registration so <see cref="InitializeOnce"/> will run again.
		/// </summary>
		/// <remarks>
		/// <see cref="initialized"/> is static and was never reset, which is only invisible while
		/// the process is also the lifetime of the registration. In the editor with domain reload
		/// disabled it is not: the second play session skipped InitializeOnce entirely and kept
		/// the first session's delegates, which are bound to <c>ScriptableObject</c> instances
		/// that no longer belong to the running server. Called from the chat system's teardown.
		/// </remarks>
		public static void ResetChannelCommands()
		{
			initialized = false;
			ChatChannelCommands.Clear();
			CommandChannelMap.Clear();
		}

		/// <summary>
		/// Gets the chat command delegate for a given chat channel.
		/// </summary>
		/// <param name="channel">Chat channel to parse.</param>
		/// <returns>ChatCommand delegate if found, otherwise null.</returns>
		public static ChatCommand ParseChatChannel(ChatChannel channel)
		{
			ChatCommand command = null;
			if (ChatHelper.ChatChannelCommands.TryGetValue(channel, out ChatCommandDetails sayCommand))
			{
				command = sayCommand.Func;
			}
			return command;
		}

		/// <summary>
		/// Attempts to run a registered slash command.
		/// </summary>
		/// <remarks>
		/// A command the sender is not allowed to run is <em>consumed</em> rather than rejected
		/// back into the chat pipeline. Two reasons, both mattering:
		/// <list type="bullet">
		/// <item><description>
		/// Falling through would broadcast the refused text to whatever channel the player is
		/// on. "<c>/admin shutdown 60</c>" appearing in world chat is worse than the command
		/// running.
		/// </description></item>
		/// <item><description>
		/// Returning false for a command that exists but is not permitted, and false for one
		/// that does not exist, are indistinguishable to the caller — which is the point. An
		/// unprivileged player probing for command names learns nothing from the response.
		/// </description></item>
		/// </list>
		/// The attempt is reported through <see cref="OnCommandRefused"/> so the server can log
		/// it; privileged commands being tried by unprivileged accounts is worth seeing.
		/// </remarks>
		/// <param name="cmd">Command string to parse, without the leading slash.</param>
		/// <param name="sender">Sender character. Its access level is authoritative.</param>
		/// <param name="msg">Chat message broadcast.</param>
		/// <returns>True when the command was recognised, whether or not it was permitted.</returns>
		public static bool TryParseCommand(string cmd, IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (string.IsNullOrEmpty(cmd) ||
				!ChatHelper.Commands.TryGetValue(cmd, out ChatCommandRegistration registration))
			{
				return false;
			}

			if (sender == null)
			{
				return true;
			}

			/* Banned is zero, so a plain >= against a Player-level command would let a banned
			 * character run everything a player can. Nothing may be run from Banned. */
			if (sender.AccessLevel <= AccessLevel.Banned ||
				sender.AccessLevel < registration.MinimumAccessLevel)
			{
				OnCommandRefused?.Invoke(sender, cmd, registration.MinimumAccessLevel);
				return true;
			}

			registration.Func?.Invoke(sender, msg);
			return true;
		}

		/// <summary>
		/// Attempts to parse a chat command and get its details.
		/// If not found, defaults to the /say channel.
		/// </summary>
		/// <param name="cmd">Command string to parse.</param>
		/// <param name="commandDetails">Output details for the command.</param>
		/// <returns>True if the command was found or /say channel is available, otherwise false.</returns>
		public static bool TryParseChatCommand(string cmd, out ChatCommandDetails commandDetails)
		{
			// parse our command or send the message to our /say channel
			if (ChatHelper.CommandChannelMap.TryGetValue(cmd, out commandDetails) ||
				ChatHelper.ChatChannelCommands.TryGetValue(ChatChannel.Say, out commandDetails))
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Extracts the leading slash command from <paramref name="text"/>, removing it from the
		/// text and returning it <em>including</em> its leading slash.
		/// </summary>
		/// <remarks>
		/// The leading slash is part of the command, because it is part of every key the command
		/// is looked up under. <see cref="Commands"/> is registered with literals like
		/// <c>"/leaveinstance"</c> and <see cref="ChannelCommandMap"/> with <c>"/w"</c>,
		/// <c>"/guild"</c> and so on — these are the spellings players type and the spellings the
		/// registrations use.
		/// <para>
		/// This used to strip it, and the result matched nothing in either dictionary. The whole
		/// slash-command layer was therefore dead: no registered command ever ran, and every
		/// channel prefix fell through <see cref="TryParseChatCommand"/>'s <c>/say</c> fallback,
		/// so <c>/w hello</c> was said locally instead of going to world chat and
		/// <c>/leaveinstance</c> did nothing at all. Nothing surfaced it because both failures
		/// are silent by construction — the fallback is a legitimate branch, and a command with
		/// no arguments leaves empty text that the caller discards.
		/// </para>
		/// </remarks>
		/// <param name="text">Reference to the input text. The command is removed from it.</param>
		/// <returns>The command including its leading slash, or an empty string when there is none.</returns>
		public static string GetCommandAndTrim(ref string text)
		{
			if (string.IsNullOrEmpty(text) || !text.StartsWith("/"))
			{
				return "";
			}
			int firstSpace = text.IndexOf(' ');
			if (firstSpace < 0)
			{
				// Slash command with no arguments (e.g. "/leaveinstance").
				// Return the whole command and clear the remainder.
				string soloCmd = text;
				text = "";
				return soloCmd;
			}
			string cmd = text.Substring(0, firstSpace);
			text = text.Substring(firstSpace + 1).Trim();
			return cmd;
		}

		/// <summary>
		/// Attempts to get and remove the first single space-separated word from the rest of the text. If no targets are found it returns an empty string.
		/// </summary>
		/// <param name="text">Input text to parse.</param>
		/// <param name="trimmed">Output text with the first word removed.</param>
		/// <returns>First word if found, otherwise empty string.</returns>
		public static string GetWordAndTrimmed(string text, out string trimmed)
		{
			int firstSpace = text.IndexOf(' ');
			if (firstSpace < 0)
			{
				// no target?
				trimmed = text;
				return "";
			}
			string word = text.Substring(0, firstSpace);
			// firstSpace + 1 skips the space character; using firstSpace alone would
			// include the leading space in the result, masked only by the subsequent .Trim().
			trimmed = text.Substring(firstSpace + 1).Trim();
			return word;
		}

		/// <summary>
		/// Removes every Unity Rich Text formatting tag from a chat message.
		/// </summary>
		/// <remarks>
		/// Delegates to <see cref="ChatSanitizer.StripRichText"/>, which loops to a fixed point,
		/// matches case-insensitively and <em>fails closed</em>. The implementation this replaced did
		/// none of those three things and could be bypassed by any of them; see that method for the
		/// detail. Kept as a member of this class because a good deal of code already calls it.
		/// <para>
		/// This strips markup only. Untrusted text arriving at the network boundary should go through
		/// <see cref="SanitizeIncoming"/> instead, which also deals with line breaks, bidirectional
		/// overrides, in-band control codes and length.
		/// </para>
		/// </remarks>
		/// <param name="message">Input chat message.</param>
		/// <returns>Sanitized message with formatting removed, or empty if it could not be cleaned.</returns>
		public static string Sanitize(string message)
		{
			return ChatSanitizer.StripRichText(message);
		}

		/// <summary>
		/// Full cleaning pipeline for untrusted chat text: control characters, rich text,
		/// <c>FISHMMO_</c> control codes, then a hard length cap.
		/// </summary>
		/// <remarks>
		/// This is what the server runs on everything a client sends, and what the Discord bridge
		/// runs on everything Discord sends. See <see cref="ChatSanitizer.SanitizeIncoming"/> for
		/// why the order of the passes matters.
		/// </remarks>
		/// <param name="message">Untrusted text.</param>
		/// <param name="maxLength">Hard cap applied after cleaning; values below one disable it.</param>
		/// <returns>Clean single-line text, or empty if nothing survived.</returns>
		public static string SanitizeIncoming(string message, int maxLength)
		{
			return ChatSanitizer.SanitizeIncoming(message, maxLength);
		}
	}
}