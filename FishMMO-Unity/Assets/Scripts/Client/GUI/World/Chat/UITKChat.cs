using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the in-world chat window. Mirrors the behaviour of the
	/// legacy UGUI <c>UIChat</c> control: channel-coloured messages, tab-based channel filtering,
	/// rate limiting, sanitisation and the full set of per-channel message handlers.
	/// </summary>
	public class UITKChat : UITKCharacterControl, IChatHelper
	{
		/// <summary>
		/// The maximum allowed length for chat messages.
		/// </summary>
		public const int MAX_LENGTH = 128;

		/// <summary>
		/// The maximum number of chat messages retained before the oldest are discarded.
		/// </summary>
		private const int MAX_MESSAGES = 128;

		/// <summary>
		/// The maximum number of chat tabs that may be created.
		/// </summary>
		private const int MAX_TABS = 12;

		private const string CHAT_ROOT_NAME = "chat-root";
		private const string CHAT_TABS_NAME = "chat-tabs";
		private const string CHAT_ADD_TAB_NAME = "chat-add-tab";
		private const string CHAT_MESSAGES_NAME = "chat-messages";
		private const string CHAT_INPUT_NAME = "chat-input";

		private const string TAB_CLASS = "chat-tab";
		private const string TAB_ACTIVE_CLASS = "chat-tab--active";
		private const string MESSAGE_ROW_CLASS = "chat-message";
		private const string MESSAGE_NAME_CLASS = "chat-message__name";
		private const string MESSAGE_TEXT_CLASS = "chat-message__text";

		/// <summary>
		/// The welcome message displayed when the chat is initialised.
		/// </summary>
		public string WelcomeMessage = "Welcome to " + Constants.Configuration.ProjectName + "!\r\nChat channels are available.";

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
		/// Colour mapping for each chat channel.
		/// </summary>
		public Dictionary<ChatChannel, Color> ChannelColors = new Dictionary<ChatChannel, Color>()
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
		/// Whether repeated messages are allowed.
		/// </summary>
		public bool AllowRepeatMessages = false;

		/// <summary>
		/// The rate at which messages can be sent, in milliseconds.
		/// </summary>
		[Tooltip("The rate at which messages can be sent in milliseconds.")]
		public float MessageRateLimit = 0.0f;

		/// <summary>
		/// Lightweight view model for a single rendered chat message.
		/// </summary>
		private class ChatMessageView
		{
			public VisualElement Root;
			public Label NameLabel;
			public Label TextLabel;
			public ChatChannel Channel;
		}

		/// <summary>
		/// Lightweight view model for a single chat tab and its active channel filter.
		/// </summary>
		private class ChatTabView
		{
			public Button Button;
			public string Label;
			public HashSet<ChatChannel> ActiveChannels = new HashSet<ChatChannel>()
			{
				ChatChannel.Say,
				ChatChannel.World,
				ChatChannel.Region,
				ChatChannel.Party,
				ChatChannel.Guild,
				ChatChannel.Tell,
				ChatChannel.Trade,
				ChatChannel.System,
			};
		}

		private VisualElement tabsContainer;
		private VisualElement messagesContainer;
		private TextField inputField;

		private readonly Dictionary<string, ChatTabView> tabs = new Dictionary<string, ChatTabView>();
		private readonly List<ChatMessageView> messages = new List<ChatMessageView>();

		/// <summary>
		/// The name of the currently active chat tab.
		/// </summary>
		public string CurrentTab = "";

		/// <summary>
		/// Stores the previous chat channel for message grouping logic.
		/// </summary>
		private ChatChannel previousChannel = ChatChannel.Command;

		/// <summary>
		/// Stores the previous sender name for message grouping logic.
		/// </summary>
		private string previousName = "";

		/// <summary>
		/// Resolves and caches visual elements, builds the default tab and seeds the welcome content.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			tabsContainer = Root.Q<VisualElement>(CHAT_TABS_NAME);
			messagesContainer = Root.Q<VisualElement>(CHAT_MESSAGES_NAME);
			inputField = Root.Q<TextField>(CHAT_INPUT_NAME);

			Button addTabButton = Root.Q<Button>(CHAT_ADD_TAB_NAME);
			if (addTabButton != null)
			{
				addTabButton.clicked += AddTab;
			}

			if (inputField != null)
			{
				inputField.maxLength = MAX_LENGTH;
				inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown);
			}

			AddTab();
			if (tabs.Count > 0)
			{
				foreach (ChatTabView tab in new List<ChatTabView>(tabs.Values))
				{
					if (tab != null)
					{
						CurrentTab = tab.Label;
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
		/// Registers the chat broadcast handler when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ChatBroadcast>(OnClientChatBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the chat broadcast handler when the client is cleared.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ChatBroadcast>(OnClientChatBroadcastReceived);
		}

		void Update()
		{
			EnableChatInput();
		}

		/// <summary>
		/// Focuses the chat input field when the chat key is pressed and no field currently has focus.
		/// </summary>
		public void EnableChatInput()
		{
			if (Character == null ||
				inputField == null)
			{
				return;
			}

			// If the input already has focus, skip to avoid interfering with typing.
			if (inputField.panel != null &&
				inputField.panel.focusController != null &&
				inputField.panel.focusController.focusedElement == inputField)
			{
				return;
			}

			if (PlayerInputHandler.Controls == null ||
				!PlayerInputHandler.Controls.Player.Chat.triggered)
			{
				return;
			}

			inputField.Focus();

			// Enable mouse mode so the cursor is available for typing.
			PlayerInputHandler.MouseMode = true;
		}

		/// <summary>
		/// Handles Enter key presses inside the input field to submit the current message.
		/// </summary>
		/// <param name="evt">The key down event.</param>
		private void OnInputKeyDown(KeyDownEvent evt)
		{
			if (evt.keyCode == KeyCode.Return ||
				evt.keyCode == KeyCode.KeypadEnter)
			{
				OnSubmit(inputField.value);
				evt.StopPropagation();
			}
		}

		/// <summary>
		/// Updates which messages are visible based on the active tab and its channels.
		/// </summary>
		public void ValidateMessages()
		{
			if (!tabs.TryGetValue(CurrentTab, out ChatTabView tab))
			{
				return;
			}

			for (int i = 0; i < messages.Count; ++i)
			{
				ChatMessageView message = messages[i];
				if (message == null || message.Channel == ChatChannel.Discord)
				{
					continue;
				}

				bool visible = tab.ActiveChannels.Contains(message.Channel);
				message.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Sets the chat input field text.
		/// </summary>
		/// <param name="input">Text to set in the input field.</param>
		public void SetInputText(string input)
		{
			if (inputField != null)
			{
				inputField.value = input;
			}
		}

		/// <summary>
		/// Handles chat message submission, including sanitisation, rate limiting and broadcasting.
		/// </summary>
		/// <param name="input">The submitted chat text.</param>
		public void OnSubmit(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				return;
			}

			// Remove Rich Text Tags if any exist.
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
						long nowTicks = DateTime.UtcNow.Ticks;
						if (Character.NextChatMessageTicks > nowTicks)
						{
							return;
						}
						Character.NextChatMessageTicks = nowTicks + (long)(MessageRateLimit * TimeSpan.TicksPerMillisecond);
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
				// Send the message to the server.
				Client.Broadcast(message, Channel.Reliable);
			}

			if (inputField != null)
			{
				inputField.value = "";
			}
		}

		/// <summary>
		/// Adds a new chat tab to the UI, ensuring unique tab names.
		/// </summary>
		public void AddTab()
		{
			if (tabs.Count > MAX_TABS || tabsContainer == null)
			{
				return;
			}

			string newTabName = "New Tab";
			string finalName = newTabName;
			for (int i = 0; tabs.ContainsKey(finalName); ++i)
			{
				finalName = newTabName + " " + i;
			}

			ChatTabView tab = new ChatTabView()
			{
				Label = finalName,
			};
			Button button = new Button(() => ActivateTab(tab))
			{
				text = finalName,
			};
			button.AddToClassList(TAB_CLASS);
			button.RegisterCallback<PointerDownEvent>((evt) => OnTabPointerDown(evt, tab));
			tab.Button = button;

			tabsContainer.Add(button);
			tabs.Add(finalName, tab);

			if (string.IsNullOrEmpty(CurrentTab))
			{
				ActivateTab(tab);
			}
		}

		/// <summary>
		/// Opens the channel picker overlay when a tab is right-clicked.
		/// </summary>
		/// <param name="evt">The pointer down event.</param>
		/// <param name="tab">The tab that was clicked.</param>
		private void OnTabPointerDown(PointerDownEvent evt, ChatTabView tab)
		{
			if (evt.button != 1)
			{
				return;
			}

			ActivateTab(tab);
			ToggleUIChatChannelPicker(tab);
		}

		/// <summary>
		/// Toggles the shared channel picker overlay for the supplied tab.
		/// </summary>
		/// <param name="tab">The tab whose channels are being edited.</param>
		private void ToggleUIChatChannelPicker(ChatTabView tab)
		{
			if (UIManager.TryGetTK("UIChatChannelPicker", out UITKChatChannelPicker channelPicker))
			{
				channelPicker.ToggleVisibility();
				if (channelPicker.Visible)
				{
					Vector3 position = tab.Button != null ? (Vector3)tab.Button.worldBound.center : Vector3.zero;
					channelPicker.Activate(tab.ActiveChannels, tab.Label, position);
				}
			}
		}

		/// <summary>
		/// Toggles the active state of a chat channel in the current tab.
		/// </summary>
		/// <param name="channel">The chat channel to toggle.</param>
		/// <param name="value">Whether the channel should be active.</param>
		public void ToggleChannel(ChatChannel channel, bool value)
		{
			if (tabs.TryGetValue(CurrentTab, out ChatTabView tab))
			{
				if (tab.ActiveChannels.Contains(channel))
				{
					if (!value)
					{
						tab.ActiveChannels.Remove(channel);
					}
				}
				else if (value)
				{
					tab.ActiveChannels.Add(channel);
				}
				ValidateMessages();
			}
		}

		/// <summary>
		/// Renames the current chat tab if the new name is not already taken.
		/// </summary>
		/// <param name="newName">The new name for the tab.</param>
		/// <returns>True if renamed successfully, false otherwise.</returns>
		public bool RenameCurrentTab(string newName)
		{
			if (tabs.ContainsKey(newName))
			{
				return false;
			}
			else if (tabs.TryGetValue(CurrentTab, out ChatTabView tab))
			{
				tabs.Remove(CurrentTab);
				tab.Label = newName;
				if (tab.Button != null)
				{
					tab.Button.text = newName;
				}
				tabs.Add(tab.Label, tab);
				ActivateTab(tab);
				return true;
			}
			return false; // something went wrong
		}

		/// <summary>
		/// Removes a chat tab and updates the current tab accordingly.
		/// </summary>
		/// <param name="tab">The tab to remove.</param>
		private void RemoveTab(ChatTabView tab)
		{
			if (tab == null)
			{
				return;
			}

			tabs.Remove(tab.Label);
			if (tab.Button != null)
			{
				tab.Button.RemoveFromHierarchy();
			}

			if (CurrentTab.Equals(tab.Label))
			{
				CurrentTab = "";
				foreach (ChatTabView chatTab in tabs.Values)
				{
					ActivateTab(chatTab);
				}
			}
		}

		/// <summary>
		/// Activates the specified chat tab and refreshes message visibility.
		/// </summary>
		/// <param name="tab">The tab to activate.</param>
		private void ActivateTab(ChatTabView tab)
		{
			if (tab == null)
			{
				return;
			}

			CurrentTab = tab.Label;
			foreach (ChatTabView other in tabs.Values)
			{
				if (other.Button != null)
				{
					other.Button.EnableInClassList(TAB_ACTIVE_CLASS, other == tab);
				}
			}
			ValidateMessages();
		}

		/// <summary>
		/// Adds a message view to the list and removes the oldest when the limit is exceeded.
		/// </summary>
		/// <param name="message">The message to add.</param>
		private void AddMessage(ChatMessageView message)
		{
			messages.Add(message);

			// Messages are FIFO: remove the first message when the limit is reached.
			if (messages.Count > MAX_MESSAGES)
			{
				ChatMessageView oldMessage = messages[0];
				messages.RemoveAt(0);
				oldMessage.Root.RemoveFromHierarchy();
			}
		}

		/// <summary>
		/// Instantiates a new chat message element and adds it to the chat view.
		/// </summary>
		/// <param name="channel">The chat channel.</param>
		/// <param name="name">The sender's name.</param>
		/// <param name="message">The message text.</param>
		/// <param name="color">Optional colour override.</param>
		public void InstantiateChatMessage(ChatChannel channel, string name, string message, Color? color = null)
		{
			if (messagesContainer == null)
			{
				return;
			}

			Color resolved = color ?? ChannelColors[channel];

			VisualElement row = new VisualElement();
			row.AddToClassList(MESSAGE_ROW_CLASS);

			Label nameLabel = new Label("[" + channel.ToString() + "] ");
			nameLabel.AddToClassList(MESSAGE_NAME_CLASS);
			nameLabel.style.color = resolved;

			bool showName = true;
			// Hide the character name if the previous message was from the same sender and channel, or if it's a system message.
			if (!string.IsNullOrWhiteSpace(name))
			{
				if (previousName.Equals(name) && previousChannel == channel)
				{
					showName = false;
				}
				else
				{
					nameLabel.text += name;
					previousName = name;
				}
			}
			else if (previousChannel == channel || channel == ChatChannel.System)
			{
				showName = false;
			}

			if (!showName)
			{
				nameLabel.style.display = DisplayStyle.None;
			}

			Label textLabel = new Label(message);
			textLabel.AddToClassList(MESSAGE_TEXT_CLASS);
			textLabel.style.color = resolved;

			row.Add(nameLabel);
			row.Add(textLabel);
			messagesContainer.Add(row);

			ChatMessageView view = new ChatMessageView()
			{
				Root = row,
				NameLabel = nameLabel,
				TextLabel = textLabel,
				Channel = channel,
			};
			AddMessage(view);

			// Apply current tab filter to the new message.
			if (channel != ChatChannel.Discord &&
				tabs.TryGetValue(CurrentTab, out ChatTabView tab) &&
				!tab.ActiveChannels.Contains(channel))
			{
				row.style.display = DisplayStyle.None;
			}

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
			if (!string.IsNullOrWhiteSpace(CurrentTab) && tabs.TryGetValue(CurrentTab, out ChatTabView tab))
			{
				if (msg.Channel == ChatChannel.Discord || tab.ActiveChannels.Contains(msg.Channel))
				{
					// Parse the local message.
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
			// Validate message length.
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
			string characterName = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed).TrimEnd(':');
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

			// Check if we have any special messages.
			if (!string.IsNullOrWhiteSpace(cmd))
			{
				// Returned message.
				if (cmd.Equals(ChatHelper.TELL_RELAYED))
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.SenderID, (s) =>
					{
						InstantiateChatMessage(msg.Channel, "[To: " + s + "]", trimmed);
					});
					return true;
				}
				// Target offline.
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
				// Messaging ourself??
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
			// We received a tell from someone else.
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
