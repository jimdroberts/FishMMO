using FishNet.Transporting;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIChat : UIControl, IChatHelper
	{
		public const int MAX_LENGTH = 128;

		public Transform chatViewParent;
		public UIChatMessage chatMessagePrefab;
		public Transform chatTabViewParent;
		public ChatTab chatTabPrefab;

		public Dictionary<string, string> ErrorCodes = new Dictionary<string, string>()
		{
			{ ChatHelper.ERROR_TARGET_OFFLINE, " is not online." },
			{ ChatHelper.ERROR_MESSAGE_SELF, "... Are you messaging yourself again?" },
		};

		public Dictionary<ChatChannel, Color> ChannelColors = new Dictionary<ChatChannel, Color>()
		{
			{ ChatChannel.Say, Color.white },
			{ ChatChannel.World, Color.red },
			{ ChatChannel.Region, Color.yellow },
			{ ChatChannel.Party, Color.green },
			{ ChatChannel.Guild, Color.cyan },
			{ ChatChannel.Tell, Color.magenta },
			{ ChatChannel.Trade, Color.blue },
			{ ChatChannel.System, Color.black },
		};

		public List<ChatTab> initialTabs = new List<ChatTab>();
		public Dictionary<string, ChatTab> tabs = new Dictionary<string, ChatTab>();
		public string currentTab = "";

		public delegate void ChatMessageChange(UIChatMessage message);
		public event ChatMessageChange OnMessageAdded;
		public event ChatMessageChange OnMessageRemoved;
		public List<UIChatMessage> messages = new List<UIChatMessage>();
		public bool AllowRepeatMessages = false;
		[Tooltip("The rate at which messages can be sent in milliseconds.")]
		public float MessageRateLimit = 0.0f;

		public override void OnStarting()
		{
			ChatHelper.InitializeOnce(GetChannelCommand);

			if (initialTabs != null && initialTabs.Count > 0)
			{
				// activate the first tab
				ActivateTab(initialTabs[0]);
				// add all the tabs to our list and add our events
				foreach (ChatTab tab in initialTabs)
				{
					if (!tabs.ContainsKey(tab.name))
					{
						tabs.Add(tab.name, tab);
					}
				}
			}

			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
		}

		public override void OnDestroying()
		{
			if (initialTabs != null && initialTabs.Count > 0)
			{
				foreach (ChatTab tab in initialTabs)
				{
					if (!tabs.ContainsKey(tab.name))
					{
						tabs.Remove(tab.name);
					}
				}
			}

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
			ValidateMessages();
		}

		public void ValidateMessages()
		{
			if (tabs.TryGetValue(currentTab, out ChatTab tab))
			{
				foreach (UIChatMessage message in messages)
				{
					if (message == null) continue;

					if (tab.activeChannels.Contains(message.Channel))
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
			if (string.IsNullOrWhiteSpace(input))
			{
				return;
			}
			if (Client.NetworkManager.IsClient)
			{
				if (input.Length > MAX_LENGTH)
				{
					input = input.Substring(0, MAX_LENGTH);
				}
				Character character = Character.localCharacter;
				if (character != null)
				{
					if (MessageRateLimit > 0)
					{
						if (character.NextChatMessageTime > DateTime.UtcNow)
						{
							return;
						}
						character.NextChatMessageTime = DateTime.UtcNow.AddMilliseconds(MessageRateLimit);
					}
					if (!AllowRepeatMessages)
					{
						if (character.LastChatMessage.Equals(input))
						{
							return;
						}
						character.LastChatMessage = input;
					}
				}
				ChatBroadcast message = new ChatBroadcast() { text = input };
				// send the message to the server
				Client.NetworkManager.ClientManager.Broadcast(message);
			}
			InputField.text = "";
		}

		public void AddTab()
		{
			const int MAX_TABS = 12;

			if (tabs.Count > MAX_TABS) return;

			ChatTab newTab = Instantiate(chatTabPrefab, chatTabViewParent);
			string newTabName = "New Tab";
			newTab.label.text = newTabName;
			for (int i = 0; tabs.ContainsKey(newTab.label.text); ++i)
			{
				newTab.label.text = newTabName + " " + i;
			}
			newTab.name = newTab.label.text;
			tabs.Add(newTab.label.text, newTab);
		}

		public void ToggleChannel(ChatChannel channel, bool value)
		{
			if (tabs.TryGetValue(currentTab, out ChatTab tab))
			{
				tab.ToggleActiveChannel(channel, value);
			}
		}

		public bool RenameCurrentTab(string newName)
		{
			if (tabs.ContainsKey(newName))
			{
				return false;
			}
			else if (tabs.TryGetValue(currentTab, out ChatTab tab))
			{
				tabs.Remove(currentTab);
				tab.name = newName;
				tab.label.text = newName;
				tabs.Add(tab.name, tab);
				ActivateTab(tab);
				return true;
			}
			return false; // something went wrong
		}

		public void RemoveTab(ChatTab tab)
		{

		}

		public void ActivateTab(ChatTab tab)
		{
			currentTab = tab.name;
		}

		private void AddMessage(UIChatMessage message)
		{
			const int MAX_MESSAGES = 128;

			messages.Add(message);
			OnMessageAdded?.Invoke(message);

			if (messages.Count > MAX_MESSAGES)
			{
				// messages are FIFO.. remove the first message when we hit our limit.
				UIChatMessage oldMessage = messages[0];
				messages.RemoveAt(0);
				//OnWriteMessageToDisk?.Invoke(oldMessage); can we add logging to disc later?
				OnMessageRemoved?.Invoke(oldMessage);
				Destroy(oldMessage.gameObject);
			}
		}

		private void InstantiateChatMessage(ChatChannel channel, string name, string message)
		{
			UIChatMessage newMessage = Instantiate(chatMessagePrefab, chatViewParent);
			newMessage.Channel = channel;
			newMessage.CharacterName.color = ChannelColors[channel];
			newMessage.CharacterName.text = "[" + channel + "] " + name;
			newMessage.Text.color = ChannelColors[channel];
			newMessage.Text.text = message;
			AddMessage(newMessage);
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

		private void OnClientChatBroadcastReceived(ChatBroadcast msg)
		{
			if (!string.IsNullOrWhiteSpace(currentTab) && tabs.TryGetValue(currentTab, out ChatTab tab))
			{
				if (tab.activeChannels.Contains(msg.channel))
				{
					// parse the local message
					ParseLocalMessage(Character.localCharacter, msg);
				}
			}
		}

		private void ParseLocalMessage(Character localCharacter, ChatBroadcast msg)
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

		public bool OnWorldChat(Character localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			
			return true;
		}

		public bool OnRegionChat(Character localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			return true;
		}

		public bool OnPartyChat(Character localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			return true;
		}

		public bool OnGuildChat(Character localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			return true;
		}

		public bool OnTellChat(Character localCharacter, ChatBroadcast msg)
		{
			string cmd = ChatHelper.GetWordAndTrimmed(msg.text, out string trimmed);

			// check if we have any special messages
			if (!string.IsNullOrWhiteSpace(cmd))
			{
				// returned message
				if (cmd.Equals(ChatHelper.RELAYED))
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
					{
						InstantiateChatMessage(msg.channel, "[To: " + s + "]", msg.text);
					});
					return true;
				}
				// target offline
				else if (cmd.Equals(ChatHelper.ERROR_TARGET_OFFLINE) &&
						 ErrorCodes.TryGetValue(ChatHelper.ERROR_TARGET_OFFLINE, out string offlineMsg))
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
				else if (cmd.Equals(ChatHelper.ERROR_MESSAGE_SELF) &&
						 ErrorCodes.TryGetValue(ChatHelper.ERROR_MESSAGE_SELF, out string errorMsg))
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
					{
						InstantiateChatMessage(msg.channel, s, errorMsg);
					});
					return true;
				}
			}
			// we received a tell from someone else
			if (msg.senderID == Character.localCharacter.ID)
			{
				ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
				{
					InstantiateChatMessage(msg.channel, "[From: " + s + "]", msg.text);
				});
			}
			return true;
		}

		public bool OnTradeChat(Character localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			return true;
		}

		public bool OnSayChat(Character localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			return true;
		}

		public bool OnSystemChat(Character localCharacter, ChatBroadcast msg)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.senderID, (s) =>
			{
				InstantiateChatMessage(msg.channel, s, msg.text);
			});
			return true;
		}
	}
}