﻿using FishNet.Connection;
using FishNet.Transporting;
using FishNet.Managing.Scened;
using UnityEngine;
using System.Collections.Generic;
using System;

namespace Server
{
	/// <summary>
	/// Server chat system.
	/// </summary>
	public class ChatSystem : ServerBehaviour
	{
		public const int MAX_LENGTH = 128;

		public SceneManager SceneManager;
		public CharacterSystem CharacterSystem;

		public bool allowRepeatMessages = false;
		[Tooltip("The server chat rate limit in milliseconds. This should be equal to the clients UIChat.messageRateLimit")]
		public float messageRateLimit = 0.0f;

		public delegate void ChatCommand(NetworkConnection conn, Character character, ChatBroadcast msg);
		public Dictionary<string, ChatCommand> commandEvents = new Dictionary<string, ChatCommand>();

		public override void InitializeOnce()
		{
			if (ServerManager != null && ClientManager != null && SceneManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				// server handles command parsing so add default commands here
				AddCommandEvent("/w", OnWorldChat);
				AddCommandEvent("/world", OnWorldChat);

				AddCommandEvent("/r", OnRegionChat);
				AddCommandEvent("/region", OnRegionChat);

				AddCommandEvent("/p", OnPartyChat);
				AddCommandEvent("/party", OnPartyChat);

				AddCommandEvent("/g", OnGuildChat);
				AddCommandEvent("/guild", OnGuildChat);

				AddCommandEvent("/tell", OnTellChat);

				AddCommandEvent("/t", OnTradeChat);
				AddCommandEvent("/trade", OnTradeChat);

				AddCommandEvent("/s", OnSayChat);
				AddCommandEvent("/say", OnSayChat);

				ServerManager.RegisterBroadcast<ChatBroadcast>(OnServerChatMessageReceived, true);
					
				ClientManager.RegisterBroadcast<WorldChatBroadcast>(OnServerWorldChatBroadcastReceived);
				ClientManager.RegisterBroadcast<WorldChatTellBroadcast>(OnServerWorldChatTellBroadcastReceived);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				commandEvents.Clear();

				ServerManager.UnregisterBroadcast<ChatBroadcast>(OnServerChatMessageReceived);
					
				ClientManager.UnregisterBroadcast<WorldChatBroadcast>(OnServerWorldChatBroadcastReceived);
				ClientManager.UnregisterBroadcast<WorldChatTellBroadcast>(OnServerWorldChatTellBroadcastReceived);
			}
		}

		public void AddCommandEvent(string command, ChatCommand commandFunction)
		{
			if (!commandEvents.ContainsKey(command))
			{
				Debug.Log("[" + DateTime.UtcNow + "] ChatSystem: Added command[" + command + "]");

				commandEvents.Add(command, commandFunction);
			}
		}	

		/// <summary>
		/// Chat message received from a character.
		/// </summary>
		private void OnServerChatMessageReceived(NetworkConnection conn, ChatBroadcast msg)
		{
			if (conn.FirstObject != null)
			{
				Character character = conn.FirstObject.GetComponent<Character>();
				if (character != null)
				{
					if (!ValidateMessage(msg))
					{
						conn.Kick(FishNet.Managing.Server.KickReason.ExploitExcessiveData);
						return;
					}

					ParseMessage(conn, character, msg);
				}
			}
			else
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem);
			}
		}

		/// <summary>
		/// Chat message should be parsed already when the World server replies to us with a chat message.
		/// </summary>
		private void OnServerWorldChatBroadcastReceived(WorldChatBroadcast msg)
		{
			switch (msg.chatMsg.channel)
			{
				case ChatChannel.World:
				case ChatChannel.Trade:
					ServerManager.Broadcast(msg.chatMsg); // broadcast to all connected clients
					break;
				default: // do nothing
					break;
			}
		}

		/// <summary>
		/// Chat message should be parsed already when the World server replies to us with a chat message.
		/// </summary>
		private void OnServerWorldChatTellBroadcastReceived(WorldChatTellBroadcast msg)
		{
			Server.CharacterSystem.SendBroadcastToCharacter(msg.targetId, msg);
		}

		private bool ValidateMessage(ChatBroadcast msg)
		{
			if (!string.IsNullOrWhiteSpace(msg.text) &&
				msg.text.Length <= MAX_LENGTH)
			{
				return true;
			}
			return false;
		}

		private void ParseMessage(NetworkConnection conn, Character character, ChatBroadcast msg)
		{
			// do things here
			string cmd = GetAndRemoveCommand(ref msg.text);

			//Debug.Log("[" + DateTime.UtcNow + "] ChatSystem: " + character.characterName +
			//		  " last chat message[" + character.lastChatMessage + "] " +
			//		  " current chat message[" + msg.text + "]");

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
				if (character.lastChatMessage.Equals(msg.text))
				{
					return;
				}
				character.lastChatMessage = msg.text;
			}

			// parse our command or send the message to our /say channel
			if (commandEvents.TryGetValue(cmd, out ChatCommand command))
			{
				//Debug.Log("[" + DateTime.UtcNow + "] ChatSystem: Invoking command[" + cmd + "]");
				command?.Invoke(conn, character, msg);
			}
			else
			{
				//Debug.Log("[" + DateTime.UtcNow + "] ChatSystem: Default command[Say]");
				OnSayChat(conn, character, msg); // default to say chat?
			}
		}

		/// <summary>
		/// Attempts to get the command from the text. If no commands are found it returns an empty string.
		/// </summary>
		private string GetAndRemoveCommand(ref string text)
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
		/// Attempts to get the Tell target from the text. If no targets are found it returns an empty string.
		/// </summary>
		private string GetTellTargetAndTrim(ref string text)
		{
			int firstSpace = text.IndexOf(' ');
			if (firstSpace < 0)
			{
				return "";
			}
			string targetName = text.Substring(0, firstSpace);

			text = text.Substring(firstSpace, text.Length - firstSpace).Trim();
			return targetName;
		}

		private void OnWorldChat(NetworkConnection conn, Character sender, ChatBroadcast msg)
		{
			if (msg.text.Length < 1)
			{
				return;
			}
			msg.channel = ChatChannel.World;
			msg.text = sender.characterName + ": " + msg.text;

			// World chat is broadcast to all scene servers... forward to the World Server
			ClientManager.Broadcast(new WorldChatBroadcast() { chatMsg = msg, });
		}

		private void OnRegionChat(NetworkConnection conn, Character sender, ChatBroadcast msg)
		{
			if (msg.text.Length < 1)
			{
				return;
			}
			msg.channel = ChatChannel.Region;
			msg.text = sender.characterName + ": " + msg.text;

			// get the senders observed scene
			UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sender.sceneName);
			if (scene != null)
			{
				if (SceneManager.SceneConnections.TryGetValue(scene, out HashSet<NetworkConnection> connections))
				{
					foreach (NetworkConnection connection in connections)
					{
						connection.Broadcast(msg);
					}
				}
			}
		}

		private void OnPartyChat(NetworkConnection conn, Character sender, ChatBroadcast msg)
		{
			if (msg.text.Length < 1)
			{
				return;
			}

			if (sender.PartyController.current != null)
			{
				msg.channel = ChatChannel.Party;
				msg.text = sender.characterName + ": " + msg.text;

				foreach (PartyController member in sender.PartyController.current.members)
				{
					// broadcast to party member... includes sender
					member.Owner.Broadcast(msg);
				}
			}
		}

		private void OnGuildChat(NetworkConnection conn, Character sender, ChatBroadcast msg)
		{
			if (msg.text.Length < 1)
			{
				return;
			}

			if (sender.GuildController.current != null)
			{
				msg.channel = ChatChannel.Guild;
				msg.text = sender.characterName + ": " + msg.text;

				foreach (GuildController member in sender.GuildController.current.members)
				{
					// broadcast to guild member... includes sender
					member.Owner.Broadcast(msg);
				}
			}
		}

		private void OnTellChat(NetworkConnection conn, Character sender, ChatBroadcast msg)
		{
			if (msg.text.Length < 1)
			{
				return;
			}

			string targetName = GetTellTargetAndTrim(ref msg.text);

			if (string.IsNullOrWhiteSpace(targetName))
			{
				return;
			}

			if (CharacterSystem == null ||
				!CharacterSystem.charactersByName.TryGetValue(targetName, out Character targetCharacter))
			{
				return;
			}

			// cache the original message
			string text = msg.text;

			msg.channel = ChatChannel.Tell;
			msg.text = "[To:" + targetName + "]: " + text;

			// send the message back to the client after we format it
			conn.Broadcast(msg);

			// format the message to send to the target player
			msg.text = "[From:" + sender.characterName + "]: " + text;

			// attempt to broadcast the message to the target
			if (!Server.CharacterSystem.SendBroadcastToCharacter(targetCharacter.id, msg))
			{
				// attempt to find the target on the world server
				ServerManager.Broadcast(new WorldChatTellBroadcast()
				{
					targetId = targetCharacter.id,
					chatMsg = msg,
				});
			}
		}

		private void OnTradeChat(NetworkConnection conn, Character sender, ChatBroadcast msg)
		{
			if (msg.text.Length < 1)
			{
				return;
			}
			msg.channel = ChatChannel.Trade;
			msg.text = sender.characterName + ": " + msg.text;

			// trade chat is broadcast to all scene servers... forward to the World Server
			// **TODO** Optimize chat channels. Make server aware of which channels we have enabled.
			ClientManager.Broadcast(new WorldChatBroadcast() { chatMsg = msg, });
		}

		private void OnSayChat(NetworkConnection conn, Character sender, ChatBroadcast msg)
		{
			if (msg.text.Length < 1 || sender.Observers == null)
			{
				return;
			}

			msg.channel = ChatChannel.Say;
			msg.text = sender.characterName + ": " + msg.text;

			// get the senders observed characters and send them the chat message
			foreach (NetworkConnection obsConnection in sender.Observers)
			{
				obsConnection.Broadcast(msg);
			}
		}

		/// <summary>
		/// Allows the server to send system messages to the client
		/// </summary>
		public void OnSendSystemMessage(NetworkConnection conn, string message)
		{
			if (conn == null)
				return;

			ChatBroadcast msg = new ChatBroadcast()
			{
				channel = ChatChannel.System,
				text = message,
			};

			conn.Broadcast(msg);
		}
	}
}