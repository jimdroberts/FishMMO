using UnityEngine;
using System;
using System.Collections.Generic;

public delegate void ChatCommand(Character character, ChatBroadcast msg);
public struct CommandDetails
{
	public ChatChannel Channel;
	public ChatCommand Func;
}

public static class ChatHelper
{
	private static bool initialized = false;

	public static Dictionary<string, CommandDetails> Commands { get; private set; }
	public static Dictionary<ChatChannel, CommandDetails> ChannelCommands { get; private set; }

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

		Commands = new Dictionary<string, CommandDetails>();
		ChannelCommands = new Dictionary<ChatChannel, CommandDetails>();

		foreach (KeyValuePair<ChatChannel, List<string>> pair in ChatHelper.ChannelCommandMap)
		{
			AddCommandDetails(pair.Value, new CommandDetails()
			{
				Channel = pair.Key,
				Func = onGetChannelCommand?.Invoke(pair.Key),
			});
		}
	}

	internal static void AddCommandDetails(List<string> commands, CommandDetails details)
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

	public static ChatCommand ParseChatCommand(string cmd, ref ChatChannel channel)
	{
		ChatCommand command = null;
		// parse our command or send the message to our /say channel
		if (ChatHelper.Commands.TryGetValue(cmd, out CommandDetails commandDetails))
		{
			channel = commandDetails.Channel;
			command = commandDetails.Func;
		}
		// default is say chat, if we have no command the text goes to say
		else if (ChatHelper.ChannelCommands.TryGetValue(ChatChannel.Say, out CommandDetails sayCommand))
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
		string targetName = text.Substring(0, firstSpace);
		trimmed = text.Substring(firstSpace, text.Length - firstSpace).Trim();
		return targetName;
	}
}