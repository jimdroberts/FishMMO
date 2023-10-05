using UnityEngine;
using System;
using System.Collections.Generic;

public delegate void ChatCommand(Character character, ChatBroadcast msg);
public struct ChatCommandDetails
{
	public ChatChannel Channel;
	public ChatCommand Func;
}

public static class ChatHelper
{
	public const string ERROR_TARGET_OFFLINE = "$_";
	public const string ERROR_MESSAGE_SELF = "$<";

	private static bool initialized = false;

	public static Dictionary<string, ChatCommandDetails> Commands { get; private set; }
	public static Dictionary<ChatChannel, ChatCommandDetails> ChannelCommands { get; private set; }

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

	public static void InitializeOnce(Func<ChatChannel, ChatCommand> onGetChannelCommand)
	{
		if (initialized) return;
		initialized = true;

		Commands = new Dictionary<string, ChatCommandDetails>();
		ChannelCommands = new Dictionary<ChatChannel, ChatCommandDetails>();

		foreach (KeyValuePair<ChatChannel, List<string>> pair in ChatHelper.ChannelCommandMap)
		{
			AddChatCommandDetails(pair.Value, new ChatCommandDetails()
			{
				Channel = pair.Key,
				Func = onGetChannelCommand?.Invoke(pair.Key),
			});
		}
	}

	internal static void AddChatCommandDetails(List<string> commands, ChatCommandDetails details)
	{
		foreach (string command in commands)
		{
			if (!Commands.ContainsKey(command))
			{
				Debug.Log("ChatSystem: Added Command[" + command + "]");

				Commands.Add(command, details);
			}
			if (!ChannelCommands.ContainsKey(details.Channel))
			{
				ChannelCommands.Add(details.Channel, details);
			}
		}
	}

	public static ChatCommand ParseChatChannel(ChatChannel channel)
	{
		ChatCommand command = null;
		if (ChatHelper.ChannelCommands.TryGetValue(channel, out ChatCommandDetails sayCommand))
		{
			command = sayCommand.Func;
		}
		return command;
	}

	public static ChatCommand ParseChatCommand(string cmd, ref ChatChannel channel)
	{
		ChatCommand command = null;
		// parse our command or send the message to our /say channel
		if (ChatHelper.Commands.TryGetValue(cmd, out ChatCommandDetails commandDetails))
		{
			channel = commandDetails.Channel;
			command = commandDetails.Func;
		}
		// default is say chat, if we have no command the text goes to say
		else if (ChatHelper.ChannelCommands.TryGetValue(ChatChannel.Say, out ChatCommandDetails sayCommand))
		{
			channel = sayCommand.Channel;
			command = sayCommand.Func;
		}
		return command;
	}

	/// <summary>
	/// Attempts to get the command from the text. If no commands are found it returns an empty string.
	/// </summary>
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
	/// Attempts to get and remove the first single space separated word from the rest of the text. If no targets are found it returns an empty string.
	/// </summary>
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
}