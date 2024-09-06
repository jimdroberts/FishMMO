using FishNet.Transporting;
using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishNet.Component.Prediction;

namespace FishMMO.Client
{
	public class UIChat : UICharacterControl, IChatHelper
	{
		public const int MAX_LENGTH = 128;

		public string WelcomeMessage = "Welcome to " + Constants.Configuration.ProjectName + "!\r\nChat channels are available.";
		public Transform ChatViewParent;
		public UIChatMessage ChatMessagePrefab;
		public Transform ChatTabViewParent;
		public ChatTab ChatTabPrefab;

		public Dictionary<string, string> ErrorCodes = new Dictionary<string, string>()
		{
			{ ChatHelper.GUILD_ERROR_TARGET_IN_GUILD, " is already in a guild." },
			{ ChatHelper.PARTY_ERROR_TARGET_IN_PARTY, " is already in a party." },
			{ ChatHelper.TARGET_OFFLINE, " is not online." },
			{ ChatHelper.TELL_ERROR_MESSAGE_SELF, "... Are you messaging yourself again?" },
		};

		public UIChatChannelColorDictionary ChannelColors = new UIChatChannelColorDictionary()
		{
			{ ChatChannel.Say,		Color.white },
			{ ChatChannel.World,	Color.cyan },
			{ ChatChannel.Region,	Color.blue },
			{ ChatChannel.Party,	Color.red },
			{ ChatChannel.Guild,	Color.green},
			{ ChatChannel.Tell,		Color.magenta },
			{ ChatChannel.Trade,	Color.black },
			{ ChatChannel.System,	Color.yellow },
		};

		public Dictionary<string, ChatTab> Tabs = new Dictionary<string, ChatTab>();
		public string CurrentTab = "";

		public delegate void ChatMessageChange(UIChatMessage message);
		public event ChatMessageChange OnMessageAdded;
		public event ChatMessageChange OnMessageRemoved;
		public List<UIChatMessage> Messages = new List<UIChatMessage>();
		public bool AllowRepeatMessages = false;
		[Tooltip("The rate at which messages can be sent in milliseconds.")]
		public float MessageRateLimit = 0.0f;

		private ChatChannel previousChannel = ChatChannel.Command;
		private string previousName = "";

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

			foreach (KeyValuePair<ChatChannel, List<string>> pair in ChatHelper.ChannelCommandMap)
			{
				string newLine = pair.Key.ToString() + ": ";
				foreach (string command in pair.Value)
				{
					newLine += command + ", ";
				}
				InstantiateChatMessage(ChatChannel.System, "", newLine, ChannelColors[pair.Key]);
			}

			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
		}

		public override void OnDestroying()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
		}

		public void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				Client.NetworkManager.ClientManager.RegisterBroadcast<ChatBroadcast>(OnClientChatBroadcastReceived);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<ChatBroadcast>(OnClientChatBroadcastReceived);
			}
		}

		void Update()
		{
			EnableChatInput();
			ValidateMessages();
		}

		public void EnableChatInput()
		{
			// if an input has focus already we should skip input otherwise things will happen while we are typing!
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

		public void ValidateMessages()
		{
			if (Tabs.TryGetValue(CurrentTab, out ChatTab tab))
			{
				foreach (UIChatMessage message in Messages)
				{
					if (message == null) continue;

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
				ChatBroadcast message = new ChatBroadcast() { text = input };
				// send the message to the server
				Client.Broadcast(message, Channel.Reliable);
			}
			InputField.text = "";
		}

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

		public void ToggleChannel(ChatChannel channel, bool value)
		{
			if (Tabs.TryGetValue(CurrentTab, out ChatTab tab))
			{
				tab.ToggleActiveChannel(channel, value);
			}
		}

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

		public void ActivateTab(ChatTab tab)
		{
			CurrentTab = tab.Label.text;
		}

		private void AddMessage(UIChatMessage message)
		{
			const int MAX_MESSAGES = 128;

			Messages.Add(message);
			OnMessageAdded?.Invoke(message);

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

		public void InstantiateChatMessage(ChatChannel channel, string name, string message, Color? color = null)
		{
			UIChatMessage newMessage = Instantiate(ChatMessagePrefab, ChatViewParent);
			newMessage.Channel = channel;
			newMessage.CharacterName.color = color ?? ChannelColors[channel];
			newMessage.CharacterName.text = "[" + channel.ToString() + "] ";
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
			else if (previousChannel == channel && channel == ChatChannel.System)
			{
				newMessage.CharacterName.enabled = false;
			}
			newMessage.Text.color = color ?? ChannelColors[channel];
			newMessage.Text.text = message;
			newMessage.gameObject.SetActive(true);
			AddMessage(newMessage);
			
			previousChannel = channel;
		}

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

		private void OnClientChatBroadcastReceived(ChatBroadcast msg, Channel channel)
		{
			if (!string.IsNullOrWhiteSpace(CurrentTab) && Tabs.TryGetValue(CurrentTab, out ChatTab tab))
			{
				if (tab.ActiveChannels.Contains(msg.channel))
				{
					// parse the local message
					ParseLocalMessage(Character, msg);
				}
			}
		}

		private void ParseLocalMessage(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			// validate message length
			if (string.IsNullOrWhiteSpace(msg.text) || msg.text.Length > MAX_LENGTH)
			{
				return;
			}

			ChatCommand command = ChatHelper.ParseChatChannel(msg.channel);
			if (command != null)
			{
				command?.Invoke(localCharacter, msg);
			}
		}

		public bool OnWorldChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			
			return true;
		}

		public bool OnRegionChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			return true;
		}

		public bool OnPartyChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			string cmd = ChatHelper.GetWordAndTrimmed(msg.text, out string trimmed);
			if (!string.IsNullOrWhiteSpace(cmd))
			{
				if (cmd.Equals(ChatHelper.PARTY_ERROR_TARGET_IN_PARTY) &&
					ErrorCodes.TryGetValue(ChatHelper.PARTY_ERROR_TARGET_IN_PARTY, out string targetErrorMsg))
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
					{
						InstantiateChatMessage(msg.channel, s, targetErrorMsg);
					});
				}
			}
			else
			{
				ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
				{
					InstantiateChatMessage(msg.channel, s, msg.text);
				});
			}
			return true;
		}

		public bool OnGuildChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			string cmd = ChatHelper.GetWordAndTrimmed(msg.text, out string trimmed);
			if (!string.IsNullOrWhiteSpace(cmd))
			{
				if (cmd.Equals(ChatHelper.GUILD_ERROR_TARGET_IN_GUILD) &&
					ErrorCodes.TryGetValue(ChatHelper.GUILD_ERROR_TARGET_IN_GUILD, out string targetErrorMsg))
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
					{
						InstantiateChatMessage(msg.channel, s, targetErrorMsg);
					});
				}
			}
			else
			{
				ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
				{
					InstantiateChatMessage(msg.channel, s, msg.text);
				});
			}
			return true;
		}

		public bool OnTellChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			string cmd = ChatHelper.GetWordAndTrimmed(msg.text, out string trimmed);

			// check if we have any special messages
			if (!string.IsNullOrWhiteSpace(cmd))
			{
				// returned message
				if (cmd.Equals(ChatHelper.TELL_RELAYED))
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
					{
						InstantiateChatMessage(msg.channel, "[To: " + s + "]", trimmed);
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
						ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
						{
							InstantiateChatMessage(msg.channel, s, targetName + offlineMsg);
						});
						return true;
					}
				}
				// messaging ourself??
				else if (cmd.Equals(ChatHelper.TELL_ERROR_MESSAGE_SELF) &&
						 ErrorCodes.TryGetValue(ChatHelper.TELL_ERROR_MESSAGE_SELF, out string errorMsg))
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
					{
						InstantiateChatMessage(msg.channel, s, errorMsg);
					});
					return true;
				}
			}
			// we received a tell from someone else
			if (localCharacter == null || msg.senderID != localCharacter.ID)
			{
				ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
				{
					InstantiateChatMessage(msg.channel, "[From: " + s + "]", msg.text);
				});
			}
			return true;
		}

		public bool OnTradeChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			return true;
		}

		public bool OnSayChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			return true;
		}

		public bool OnSystemChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			return true;
		}
	}
}