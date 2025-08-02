using FishNet.Transporting;
using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIChat : UICharacterControl, IChatHelper
	{
		/// <summary>
		/// The maximum allowed length for chat messages.
		/// </summary>
		public const int MAX_LENGTH = 128;

		/// <summary>
		/// The welcome message displayed when the chat is initialized.
		/// </summary>
		public string WelcomeMessage = "Welcome to " + Constants.Configuration.ProjectName + "!\r\nChat channels are available.";
		/// <summary>
		/// The parent transform for chat message views.
		/// </summary>
		public Transform ChatViewParent;
		/// <summary>
		/// The prefab used to instantiate chat messages.
		/// </summary>
		public UIChatMessage ChatMessagePrefab;
		/// <summary>
		/// The parent transform for chat tab views.
		/// </summary>
		public Transform ChatTabViewParent;
		/// <summary>
		/// The prefab used to instantiate chat tabs.
		/// </summary>
		public ChatTab ChatTabPrefab;

		/// <summary>
		/// Error code messages mapped to their respective error keys.
		/// </summary>
		public Dictionary<string, string> ErrorCodes = new Dictionary<string, string>()
		{
			{ ChatHelper.GUILD_ERROR_TARGET_IN_GUILD, " is already in a guild." },
			{ ChatHelper.PARTY_ERROR_TARGET_IN_PARTY, " is already in a party." },
			{ ChatHelper.TARGET_OFFLINE, " is not online." },
			{ ChatHelper.TELL_ERROR_MESSAGE_SELF, "... Are you messaging yourself again?" },
		};

		/// <summary>
		/// Color mapping for each chat channel.
		/// </summary>
		public UIChatChannelColorDictionary ChannelColors = new UIChatChannelColorDictionary()
		{
			{ ChatChannel.Say,      Color.white },
			{ ChatChannel.World,    Color.cyan },
			{ ChatChannel.Region,   Color.blue },
			{ ChatChannel.Party,    Color.red },
			{ ChatChannel.Guild,    Color.green},
			{ ChatChannel.Tell,     Color.magenta },
			{ ChatChannel.Trade,    Color.black },
			{ ChatChannel.System,   Color.yellow },
			{ ChatChannel.Discord,  TinyColor.turquoise.ToUnityColor() },
		};

		/// <summary>
		/// Dictionary of chat tabs by their names.
		/// </summary>
		public Dictionary<string, ChatTab> Tabs = new Dictionary<string, ChatTab>();
		/// <summary>
		/// The name of the currently active chat tab.
		/// </summary>
		public string CurrentTab = "";

		/// <summary>
		/// Delegate for chat message change events.
		/// </summary>
		public delegate void ChatMessageChange(UIChatMessage message);
		/// <summary>
		/// Event triggered when a chat message is added.
		/// </summary>
		public event ChatMessageChange OnMessageAdded;
		/// <summary>
		/// Event triggered when a chat message is removed.
		/// </summary>
		public event ChatMessageChange OnMessageRemoved;
		/// <summary>
		/// List of all chat messages currently displayed.
		/// </summary>
		public List<UIChatMessage> Messages = new List<UIChatMessage>();
		/// <summary>
		/// Whether repeated messages are allowed.
		/// </summary>
		public bool AllowRepeatMessages = false;
		/// <summary>
		/// The rate at which messages can be sent, in milliseconds.
		/// </summary>
		[Tooltip("The rate at which messages can be sent in milliseconds.")]
		public float MessageRateLimit = 0.0f;

		/// <summary>
		/// Stores the previous chat channel for message grouping logic.
		/// </summary>
		private ChatChannel previousChannel = ChatChannel.Command;
		/// <summary>
		/// Stores the previous sender name for message grouping logic.
		/// </summary>
		private string previousName = "";

		/// <summary>
		/// Called when the chat UI is starting. Initializes tabs, welcome message, and channel commands.
		/// </summary>
		public override void OnStarting()
		{
			AddTab();
			if (Tabs.Count > 0)
			{
				foreach (ChatTab tab in new List<ChatTab>(Tabs.Values))
				{
					if (tab != null)
					{
						CurrentTab = tab.Label.text;
						RenameCurrentTab("General");
					}
				}
			}

			ChatHelper.InitializeOnce(GetChannelCommand);

			InstantiateChatMessage(ChatChannel.System, "", WelcomeMessage);

			// Display available channel commands in the chat window.
			foreach (KeyValuePair<ChatChannel, List<string>> pair in ChatHelper.ChannelCommandMap)
			{
				string newLine = pair.Key.ToString() + ": ";
				foreach (string command in pair.Value)
				{
					newLine += command + ", ";
				}
				InstantiateChatMessage(ChatChannel.System, "", newLine, ChannelColors[pair.Key]);
			}
		}

		/// <summary>
		/// Called when the client is set. Registers chat broadcast event handler.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ChatBroadcast>(OnClientChatBroadcastReceived);
		}

		/// <summary>
		/// Called when the client is unset. Unregisters chat broadcast event handler.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ChatBroadcast>(OnClientChatBroadcastReceived);
		}

		/// <summary>
		/// Unity Update loop. Handles chat input and message validation.
		/// </summary>
		void Update()
		{
			EnableChatInput();
			ValidateMessages();
		}

		/// <summary>
		/// Enables chat input if no other input field has focus and chat keys are pressed.
		/// </summary>
		public void EnableChatInput()
		{
			// if an input has focus already we should skip input otherwise things will happen while we are typing!
			// If an input has focus already, skip input to avoid interfering with typing.
			if (Character == null ||
				UIManager.ControlHasFocus(this))
			{
				return;
			}

			if (InputManager.GetKeyDown("Chat") ||
				InputManager.GetKeyDown("Chat2"))
			{
				if (InputManager.MouseMode &&
					!InputField.isFocused)
				{
					InputField.OnSelect(new BaseEventData(EventSystem.current)
					{
						selectedObject = InputField.gameObject,
					});
				}
				else
				{
					InputField.OnSelect(new BaseEventData(EventSystem.current)
					{
						selectedObject = InputField.gameObject,
					});

					// enable mouse mode
					InputManager.MouseMode = true;
				}
			}
		}

		/// <summary>
		/// Validates which messages should be visible based on the active tab and its channels.
		/// </summary>
		public void ValidateMessages()
		{
			if (Tabs.TryGetValue(CurrentTab, out ChatTab tab))
			{
				foreach (UIChatMessage message in Messages)
				{
					if (message == null || message.Channel == ChatChannel.Discord) continue;

					if (tab.ActiveChannels.Contains(message.Channel))
					{
						message.gameObject.SetActive(true);
					}
					else
					{
						message.gameObject.SetActive(false);
					}
				}
			}
		}

		/// <summary>
		/// Sets the chat input field text.
		/// </summary>
		/// <param name="input">Text to set in the input field.</param>
		public void SetInputText(string input)
		{
			InputField.text = input;
		}

		/// <summary>
		/// Handles chat message submission, including sanitization, rate limiting, and broadcasting.
		/// </summary>
		/// <param name="input">The submitted chat text.</param>
		public void OnSubmit(string input)
		{
			if (!InputManager.GetKeyDown("Chat") &&
				!InputManager.GetKeyDown("Chat2"))
			{
				return;
			}

			if (string.IsNullOrWhiteSpace(input))
			{
				return;
			}

			// remove Rich Text Tags if any exist
			// Remove Rich Text Tags if any exist
			input = ChatHelper.Sanitize(input);

			if (Client.NetworkManager.IsClientStarted)
			{
				if (input.Length > MAX_LENGTH)
				{
					input = input.Substring(0, MAX_LENGTH);
				}
				if (Character != null)
				{
					if (MessageRateLimit > 0)
					{
						if (Character.NextChatMessageTime > DateTime.UtcNow)
						{
							return;
						}
						Character.NextChatMessageTime = DateTime.UtcNow.AddMilliseconds(MessageRateLimit);
					}
					if (!AllowRepeatMessages)
					{
						if (!string.IsNullOrWhiteSpace(Character.LastChatMessage) &&
							Character.LastChatMessage.Equals(input))
						{
							return;
						}
						Character.LastChatMessage = input;
					}
				}
				ChatBroadcast message = new ChatBroadcast() { Text = input };
				// send the message to the server
				Client.Broadcast(message, Channel.Reliable);
			}
			InputField.text = "";
		}

		/// <summary>
		/// Adds a new chat tab to the UI, ensuring unique tab names.
		/// </summary>
		public void AddTab()
		{
			const int MAX_TABS = 12;

			if (Tabs.Count > MAX_TABS) return;

			ChatTab newTab = Instantiate(ChatTabPrefab, ChatTabViewParent);
			string newTabName = "New Tab";
			newTab.Label.text = newTabName;
			for (int i = 0; Tabs.ContainsKey(newTab.Label.text); ++i)
			{
				newTab.Label.text = newTabName + " " + i;
			}
			newTab.name = newTab.Label.text;
			newTab.OnRemoveTab += Tab_OnRemoveTab;
			newTab.gameObject.SetActive(true);
			Tabs.Add(newTab.Label.text, newTab);
		}

		/// <summary>
		/// Toggles the active state of a chat channel in the current tab.
		/// </summary>
		/// <param name="channel">The chat channel to toggle.</param>
		/// <param name="value">Whether the channel should be active.</param>
		public void ToggleChannel(ChatChannel channel, bool value)
		{
			if (Tabs.TryGetValue(CurrentTab, out ChatTab tab))
			{
				tab.ToggleActiveChannel(channel, value);
			}
		}

		/// <summary>
		/// Renames the current chat tab if the new name is not already taken.
		/// </summary>
		/// <param name="newName">The new name for the tab.</param>
		/// <returns>True if renamed successfully, false otherwise.</returns>
		public bool RenameCurrentTab(string newName)
		{
			if (Tabs.ContainsKey(newName))
			{
				return false;
			}
			else if (Tabs.TryGetValue(CurrentTab, out ChatTab tab))
			{
				Tabs.Remove(CurrentTab);
				tab.Label.text = newName;
				tab.name = tab.Label.text;
				Tabs.Add(tab.Label.text, tab);
				ActivateTab(tab);
				return true;
			}
			return false; // something went wrong
		}

		/// <summary>
		/// Handles removal of a chat tab and updates the current tab accordingly.
		/// </summary>
		/// <param name="tab">The tab to remove.</param>
		public void Tab_OnRemoveTab(ChatTab tab)
		{
			Tabs.Remove(tab.Label.text);
			if (CurrentTab.Equals(tab.Label.text))
			{
				if (Tabs.Count > 0)
				{
					foreach (ChatTab chatTab in Tabs.Values)
					{
						CurrentTab = chatTab.Label.text;
					}
				}
				else
				{
					CurrentTab = "";
				}
			}
		}

		/// <summary>
		/// Activates the specified chat tab.
		/// </summary>
		/// <param name="tab">The tab to activate.</param>
		public void ActivateTab(ChatTab tab)
		{
			CurrentTab = tab.Label.text;
		}

		/// <summary>
		/// Adds a chat message to the list and handles FIFO removal if the limit is reached.
		/// </summary>
		/// <param name="message">The message to add.</param>
		private void AddMessage(UIChatMessage message)
		{
			const int MAX_MESSAGES = 128;

			Messages.Add(message);
			OnMessageAdded?.Invoke(message);

			// Messages are FIFO: remove the first message when the limit is reached.
			if (Messages.Count > MAX_MESSAGES)
			{
				// messages are FIFO.. remove the first message when we hit our limit.
				UIChatMessage oldMessage = Messages[0];
				Messages.RemoveAt(0);
				//OnWriteMessageToDisk?.Invoke(oldMessage); can we add logging to disc later?
				OnMessageRemoved?.Invoke(oldMessage);
				Destroy(oldMessage.gameObject);
			}
		}

		/// <summary>
		/// Instantiates a new chat message UI element and adds it to the chat view.
		/// </summary>
		/// <param name="channel">The chat channel.</param>
		/// <param name="name">The sender's name.</param>
		/// <param name="message">The message text.</param>
		/// <param name="color">Optional color override.</param>
		public void InstantiateChatMessage(ChatChannel channel, string name, string message, Color? color = null)
		{
			UIChatMessage newMessage = Instantiate(ChatMessagePrefab, ChatViewParent);
			newMessage.Channel = channel;
			newMessage.CharacterName.color = color ?? ChannelColors[channel];
			newMessage.CharacterName.text = "[" + channel.ToString() + "] ";
			// Hide the character name if the previous message was from the same sender and channel, or if it's a system message.
			if (!string.IsNullOrWhiteSpace(name))
			{
				if (previousName.Equals(name) && previousChannel == channel)
				{
					newMessage.CharacterName.enabled = false;
				}
				else
				{
					newMessage.CharacterName.text += name;
					previousName = name;
				}
			}
			else if (previousChannel == channel || channel == ChatChannel.System)
			{
				newMessage.CharacterName.enabled = false;
			}
			newMessage.Text.color = color ?? ChannelColors[channel];
			newMessage.Text.text = message;
			newMessage.gameObject.SetActive(true);
			AddMessage(newMessage);

			previousChannel = channel;
		}

		/// <summary>
		/// Gets the chat command delegate for the specified channel.
		/// </summary>
		/// <param name="channel">The chat channel.</param>
		/// <returns>The chat command delegate.</returns>
		public ChatCommand GetChannelCommand(ChatChannel channel)
		{
			switch (channel)
			{
				case ChatChannel.World: return OnWorldChat;
				case ChatChannel.Region: return OnRegionChat;
				case ChatChannel.Party: return OnPartyChat;
				case ChatChannel.Guild: return OnGuildChat;
				case ChatChannel.Tell: return OnTellChat;
				case ChatChannel.Trade: return OnTradeChat;
				case ChatChannel.Say: return OnSayChat;
				case ChatChannel.System: return OnSystemChat;
				default: return OnSayChat;
			}
		}

		/// <summary>
		/// Handles incoming chat broadcasts from the server.
		/// </summary>
		/// <param name="msg">The chat broadcast message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientChatBroadcastReceived(ChatBroadcast msg, Channel channel)
		{
			if (!string.IsNullOrWhiteSpace(CurrentTab) && Tabs.TryGetValue(CurrentTab, out ChatTab tab))
			{
				if (msg.Channel == ChatChannel.Discord || tab.ActiveChannels.Contains(msg.Channel))
				{
					// parse the local message
					ParseLocalMessage(Character, msg);
				}
			}
		}

		/// <summary>
		/// Parses and processes a local chat message, including Discord and other channels.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		private void ParseLocalMessage(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			// validate message length
			if (string.IsNullOrWhiteSpace(msg.Text) || msg.Text.Length > MAX_LENGTH)
			{
				return;
			}

			if (msg.Channel == ChatChannel.Discord)
			{
				OnDiscordChat(msg);
			}
			else
			{
				ChatCommand command = ChatHelper.ParseChatChannel(msg.Channel);
				if (command != null)
				{
					command?.Invoke(localCharacter, msg);
				}
			}
		}

		/// <summary>
		/// Handles Discord chat messages and displays them in the chat view.
		/// </summary>
		/// <param name="msg">The chat broadcast message.</param>
		public void OnDiscordChat(ChatBroadcast msg)
		{
			string characterName = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed).TrimEnd(':'); ;
			InstantiateChatMessage(ChatChannel.Discord, characterName, trimmed);
		}

		/// <summary>
		/// Handles World chat messages and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnWorldChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
			{
				InstantiateChatMessage(msg.Channel, s, msg.Text);
			});

			return true;
		}

		/// <summary>
		/// Handles Region chat messages and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnRegionChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
			{
				InstantiateChatMessage(msg.Channel, s, msg.Text);
			});
			return true;
		}

		/// <summary>
		/// Handles Party chat messages, including error messages, and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnPartyChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			string cmd = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (!string.IsNullOrWhiteSpace(cmd) &&
				 cmd.Equals(ChatHelper.PARTY_ERROR_TARGET_IN_PARTY) &&
				 ErrorCodes.TryGetValue(ChatHelper.PARTY_ERROR_TARGET_IN_PARTY, out string targetErrorMsg))
			{
				ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
				{
					InstantiateChatMessage(msg.Channel, s, targetErrorMsg);
				});
			}
			else
			{
				ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
				{
					InstantiateChatMessage(msg.Channel, s, msg.Text);
				});
			}
			return true;
		}

		/// <summary>
		/// Handles Guild chat messages, including error messages, and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnGuildChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			string cmd = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (!string.IsNullOrWhiteSpace(cmd) &&
				 cmd.Equals(ChatHelper.GUILD_ERROR_TARGET_IN_GUILD) &&
				 ErrorCodes.TryGetValue(ChatHelper.GUILD_ERROR_TARGET_IN_GUILD, out string targetErrorMsg))
			{
				ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
				{
					InstantiateChatMessage(msg.Channel, s, targetErrorMsg);
				});
			}
			else
			{
				ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
				{
					InstantiateChatMessage(msg.Channel, s, msg.Text);
				});
			}
			return true;
		}

		/// <summary>
		/// Handles Tell (private) chat messages, including error and relay messages, and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnTellChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			string cmd = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);

			// check if we have any special messages
			if (!string.IsNullOrWhiteSpace(cmd))
			{
				// returned message
				if (cmd.Equals(ChatHelper.TELL_RELAYED))
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
					{
						InstantiateChatMessage(msg.Channel, "[To: " + s + "]", trimmed);
					});
					return true;
				}
				// target offline
				else if (cmd.Equals(ChatHelper.TARGET_OFFLINE) &&
						 ErrorCodes.TryGetValue(ChatHelper.TARGET_OFFLINE, out string offlineMsg))
				{
					ChatHelper.GetWordAndTrimmed(trimmed, out string targetName);
					if (!string.IsNullOrWhiteSpace(targetName))
					{
						ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
						{
							InstantiateChatMessage(msg.Channel, s, targetName + offlineMsg);
						});
						return true;
					}
				}
				// messaging ourself??
				else if (cmd.Equals(ChatHelper.TELL_ERROR_MESSAGE_SELF) &&
						 ErrorCodes.TryGetValue(ChatHelper.TELL_ERROR_MESSAGE_SELF, out string errorMsg))
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
					{
						InstantiateChatMessage(msg.Channel, s, errorMsg);
					});
					return true;
				}
			}
			// we received a tell from someone else
			if (localCharacter == null || msg.SenderID != localCharacter.ID)
			{
				ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
				{
					InstantiateChatMessage(msg.Channel, "[From: " + s + "]", msg.Text);
				});
			}
			return true;
		}

		/// <summary>
		/// Handles Trade chat messages and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnTradeChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
			{
				InstantiateChatMessage(msg.Channel, s, msg.Text);
			});
			return true;
		}

		/// <summary>
		/// Handles Say chat messages and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnSayChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
			{
				InstantiateChatMessage(msg.Channel, s, msg.Text);
			});
			return true;
		}

		/// <summary>
		/// Handles System chat messages and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnSystemChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
			{
				InstantiateChatMessage(msg.Channel, s, msg.Text);
			});
			return true;
		}
	}
}