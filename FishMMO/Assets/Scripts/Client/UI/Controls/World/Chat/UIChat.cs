using UnityEngine;
using System;
using System.Collections.Generic;
using FishNet.Transporting;
using FishNet.Managing;

namespace FishMMO.Client
{
	public class UIChat : UIControl
	{
		public const int MAX_LENGTH = 128;

		public Transform chatViewParent;
		public ClientChatMessage chatMessagePrefab;
		public Transform chatTabViewParent;
		public ChatTab chatTabPrefab;

		public Dictionary<ChatChannel, Color> channelColors = new Dictionary<ChatChannel, Color>()
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

		public delegate void ChatMessageChange(ClientChatMessage message);
		public event ChatMessageChange OnMessageAdded;
		public event ChatMessageChange OnMessageRemoved;
		public List<ClientChatMessage> messages = new List<ClientChatMessage>();
		public bool allowRepeatMessages = false;
		[Tooltip("The rate at which messages can be sent in milliseconds.")]
		public float messageRateLimit = 0.0f;

		public delegate void ChatCommand(Character sender, ChatBroadcast msg);
		public Dictionary<string, ChatCommand> commandEvents = new Dictionary<string, ChatCommand>();

		private NetworkManager networkManager;

		public override void OnStarting()
		{
			networkManager = FindObjectOfType<NetworkManager>();
			if (networkManager == null)
			{
				Debug.LogError("UIChat: NetworkManager not found, UI will not function.");
				return;
			}

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

			if (networkManager.ClientManager != null)
			{
				networkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			}
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

			if (networkManager.ClientManager != null)
			{
				networkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			}
		}

		public void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				networkManager.ClientManager.RegisterBroadcast<ChatBroadcast>(OnClientChatMessageReceived);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				networkManager.ClientManager.UnregisterBroadcast<ChatBroadcast>(OnClientChatMessageReceived);
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
				foreach (ClientChatMessage message in messages)
				{
					if (message == null) continue;

					if (tab.activeChannels.Contains(message.channel))
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
			if (networkManager.IsClient)
			{
				if (input.Length > MAX_LENGTH)
				{
					input = input.Substring(0, MAX_LENGTH);
				}
				Character character = Character.localCharacter;
				if (character != null)
				{
					if (messageRateLimit > 0)
					{
						if (character.nextChatMessageTime > DateTime.UtcNow)
						{
							return;
						}
						character.nextChatMessageTime = DateTime.UtcNow.AddMilliseconds(messageRateLimit);
					}
					if (!allowRepeatMessages)
					{
						if (character.lastChatMessage.Equals(input))
						{
							return;
						}
						character.lastChatMessage = input;
					}
				}
				networkManager.ClientManager.Broadcast(new ChatBroadcast() { text = input });
			}
			inputField.text = "";
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

		private void AddMessage(ClientChatMessage message)
		{
			const int MAX_MESSAGES = 128;

			messages.Add(message);
			OnMessageAdded?.Invoke(message);

			if (messages.Count > MAX_MESSAGES)
			{
				// messages are FIFO.. remove the first message when we hit our limit.
				ClientChatMessage oldMessage = messages[0];
				messages.RemoveAt(0);
				//OnWriteMessageToDisk?.Invoke(oldMessage); can we add logging to disc later?
				OnMessageRemoved?.Invoke(oldMessage);
				Destroy(oldMessage.gameObject);
			}
		}

		private void OnClientChatMessageReceived(ChatBroadcast msg)
		{
			if (!string.IsNullOrWhiteSpace(currentTab) && tabs.TryGetValue(currentTab, out ChatTab tab))
			{
				if (tab.activeChannels.Contains(msg.channel))
				{
					ClientChatMessage newMessage = Instantiate(chatMessagePrefab, chatViewParent);
					newMessage.channel = msg.channel;
					newMessage.text.color = channelColors[msg.channel];
					newMessage.text.text = "[" + msg.channel + "] " + msg.text;
					AddMessage(newMessage);
				}
			}
		}
	}
}