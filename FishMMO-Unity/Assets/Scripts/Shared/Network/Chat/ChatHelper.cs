using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Delegate for chat commands. Returns true if the chat message should be written to the database.
	/// </summary>
	public delegate bool ChatCommand(IPlayerCharacter character, ChatBroadcast msg);

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
		/// <summary>Error code for when the target is already in a guild.</summary>
		public const string GUILD_ERROR_TARGET_IN_GUILD = "$&(|)";
		/// <summary>Error code for when the target is already in a party.</summary>
		public const string PARTY_ERROR_TARGET_IN_PARTY = "$*(|)";
		/// <summary>Code for relayed tell messages.</summary>
		public const string TELL_RELAYED = "$(|)";
		/// <summary>Error code for sending a tell message to oneself.</summary>
		public const string TELL_ERROR_MESSAGE_SELF = "$(<)";
		/// <summary>Error code for when the target is offline.</summary>
		public const string TARGET_OFFLINE = "$(_)";

		#region Regex
		// Regex patterns for Unity Rich Text tags. Used to sanitize chat messages by removing formatting.
		private const string AlignPattern = @"<align=[^>]+?>|<\/align>";
		private const string AllCapsPattern = @"<allcaps>|<\/allcaps>";
		private const string AlphaPattern = @"<alpha=[^>]+?>|<\/alpha>";
		private const string BoldPattern = @"<b>|<\/b>";
		private const string BrPattern = @"<br>|<\/br>";
		private const string ColorPattern = @"<color=[^>]+?>|<\/color>";
		private const string CspacePattern = @"<cspace=[^>]+?>|<\/cspace>";
		private const string FontPattern = @"<font=[^>]+?>|<\/font>";
		private const string FontWeightPattern = @"<font-weight=[^>]+?>|<\/font-weight>";
		private const string GradientPattern = @"<gradient=[^>]+?>|<\/gradient>";
		private const string ItalicPattern = @"<i>|<\/i>";
		private const string IndentPattern = @"<indent=[^>]+?>|<\/indent>";
		private const string LineHeightPattern = @"<line-height=[^>]+?>|<\/line-height>";
		private const string LineIndentPattern = @"<line-indent=[^>]+?>|<\/line-indent>";
		private const string LinkPattern = @"<link=[^>]+?>|<\/link>";
		private const string LowercasePattern = @"<lowercase>|<\/lowercase>";
		private const string MarginPattern = @"<margin=[^>]+?>|<\/margin>";
		private const string MarkPattern = @"<mark=[^>]+?>|<\/mark>";
		private const string MspacePattern = @"<mspace=[^>]+?>|<\/mspace>";
		private const string NobrPattern = @"<nobr>|<\/nobr>";
		private const string NoparsePattern = @"<noparse>|<\/noparse>";
		private const string PagePattern = @"<page=[^>]+?>|<\/page>";
		private const string PosPattern = @"<pos=[^>]+?>|<\/pos>";
		private const string RotatePattern = @"<rotate=[^>]+?>|<\/rotate>";
		private const string SPattern = @"<s>|<\/s>";
		private const string SizePattern = @"<size=[^>]+?>|<\/size>";
		private const string SmallcapsPattern = @"<smallcaps>|<\/smallcaps>";
		private const string SpacePattern = @"<space=[^>]+?>|<\/space>";
		private const string SpritePattern = @"<sprite=[^>]+?\/>";
		private const string StrikethroughPattern = @"<strikethrough>|<\/strikethrough>";
		private const string StylePattern = @"<style=[^>]+?>|<\/style>";
		private const string SubPattern = @"<sub>|<\/sub>";
		private const string SupPattern = @"<sup>|<\/sup>";
		private const string UPattern = @"<u>|<\/u>";
		private const string UppercasePattern = @"<uppercase>|<\/uppercase>";
		private const string VoffsetPattern = @"<voffset=[^>]+?>|<\/voffset>";
		private const string WidthPattern = @"<width=[^>]+?>|<\/width>";

		/// <summary>
		/// Combined regex pattern for all supported Unity Rich Text tags.
		/// Used to sanitize chat messages by removing formatting tags.
		/// </summary>
		private static readonly string CombinedRTTPattern = $"{AlignPattern}|{AllCapsPattern}|{AlphaPattern}|{BoldPattern}|{BrPattern}|{ColorPattern}|{CspacePattern}|{FontPattern}|{FontWeightPattern}|{GradientPattern}|{ItalicPattern}|{IndentPattern}|{LineHeightPattern}|{LineIndentPattern}|{LinkPattern}|{LowercasePattern}|{MarginPattern}|{MarkPattern}|{MspacePattern}|{NobrPattern}|{NoparsePattern}|{PagePattern}|{PosPattern}|{RotatePattern}|{SPattern}|{SizePattern}|{SmallcapsPattern}|{SpacePattern}|{SpritePattern}|{StrikethroughPattern}|{StylePattern}|{SubPattern}|{SupPattern}|{UPattern}|{UppercasePattern}|{VoffsetPattern}|{WidthPattern}";
		#endregion

		private static bool initialized = false;

		/// <summary>
		/// Dictionary mapping command strings to their corresponding ChatCommand delegates.
		/// </summary>
		public static Dictionary<string, ChatCommand> Commands { get; private set; }

		/// <summary>
		/// Dictionary mapping chat channels to their command details.
		/// </summary>
		public static Dictionary<ChatChannel, ChatCommandDetails> ChatChannelCommands { get; private set; }

		/// <summary>
		/// Dictionary mapping command strings to chat command details.
		/// </summary>
		public static Dictionary<string, ChatCommandDetails> CommandChannelMap = new Dictionary<string, ChatCommandDetails>();

		/// <summary>
		/// Dictionary mapping chat channels to their supported command strings.
		/// </summary>
		public static Dictionary<ChatChannel, List<string>> ChannelCommandMap = new Dictionary<ChatChannel, List<string>>()
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
			Commands = new Dictionary<string, ChatCommand>();
			ChatChannelCommands = new Dictionary<ChatChannel, ChatCommandDetails>();
		}

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
		/// Adds new chat commands to the Commands dictionary.
		/// </summary>
		/// <param name="commands">Dictionary of command strings and their delegates.</param>
		public static void AddCommands(Dictionary<string, ChatCommand> commands)
		{
			if (commands == null)
				return;

			foreach (KeyValuePair<string, ChatCommand> pair in commands)
			{
				Log.Debug("ChatHelper", $"Added Command[" + pair.Key + "]");
				Commands[pair.Key] = pair.Value;
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
				CommandChannelMap.Add(command, details);
			}
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
		/// Attempts to parse and execute a chat command by its string.
		/// </summary>
		/// <param name="cmd">Command string to parse.</param>
		/// <param name="sender">Sender character.</param>
		/// <param name="msg">Chat message broadcast.</param>
		/// <returns>True if the command was found and executed, otherwise false.</returns>
		public static bool TryParseCommand(string cmd, IPlayerCharacter sender, ChatBroadcast msg)
		{
			// try to find the command
			if (ChatHelper.Commands.TryGetValue(cmd, out ChatCommand command))
			{
				command?.Invoke(sender, msg);
				return true;
			}
			return false;
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
		/// Attempts to get the command from the text. If no commands are found it returns an empty string.
		/// Modifies the input text to remove the command.
		/// </summary>
		/// <param name="text">Reference to the input text.</param>
		/// <returns>Command string if found, otherwise empty string.</returns>
		public static string GetCommandAndTrim(ref string text)
		{
			if (!text.StartsWith("/"))
			{
				return "";
			}
			int firstSpace = text.IndexOf(' ');
			if (firstSpace < 0)
			{
				return "";
			}
			string cmd = text.Substring(0, firstSpace);
			text = text.Substring(firstSpace, text.Length - firstSpace).Trim();
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
			trimmed = text.Substring(firstSpace, text.Length - firstSpace).Trim();
			return word;
		}

		/// <summary>
		/// Attempts to sanitize a chat message by removing any Unity Rich Text formatting tags.
		/// </summary>
		/// <param name="message">Input chat message.</param>
		/// <returns>Sanitized message with formatting removed.</returns>
		public static string Sanitize(string message)
		{
			return Regex.Replace(message, CombinedRTTPattern, "");
		}
	}
}