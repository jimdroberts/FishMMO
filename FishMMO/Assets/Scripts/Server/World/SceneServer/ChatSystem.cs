using FishNet.Connection;
using FishNet.Transporting;
using FishNet.Managing.Scened;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Server.Services;
using FishMMO_DB.Entities;

namespace FishMMO.Server
{
	/// <summary>
	/// Server chat system.
	/// </summary>
	public class ChatSystem : ServerBehaviour, IChatHelper
	{
		public const int MAX_LENGTH = 128;

		public SceneManager SceneManager;

		public bool AllowRepeatMessages = false;
		[Tooltip("The server chat rate limit in milliseconds. This should be equal to the clients UIChat.messageRateLimit")]
		public float MessageRateLimit = 0.0f;
		[Tooltip("The server chat message pump rate limit in seconds.")]
		public float MessagePumpRate = 2.0f;
		public int MessageFetchCount = 20;

		private DateTime lastFetchTime = DateTime.UtcNow;
		private long lastFetchPosition = 0;
		private float nextPump = 0.0f;

		private LocalConnectionState serverState;

		public override void InitializeOnce()
		{
			if (ServerManager != null && ClientManager != null && SceneManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;

				ChatHelper.InitializeOnce(GetChannelCommand);
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
			if (serverState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<ChatBroadcast>(OnServerChatMessageReceived, true);
			}
			else if (serverState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<ChatBroadcast>(OnServerChatMessageReceived);
			}
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextPump < 0)
				{
					nextPump = MessagePumpRate;

					List<ChatEntity> messages = FetchChatMessages();
					if (messages != null)
					{
						ProcessChatMessages(messages);
					}

				}
				nextPump -= Time.deltaTime;
			}
		}

		private List<ChatEntity> FetchChatMessages()
		{
			using var dbContext = Server.DbContextFactory.CreateDbContext();

			// fetch chat messages from the database
			List<ChatEntity> messages = ChatService.Fetch(dbContext, lastFetchTime, lastFetchPosition, MessageFetchCount, Server.SceneServerSystem.ID);
			if (messages != null)
			{
				ChatEntity latest = messages.LastOrDefault();
				if (latest != null)
				{
					lastFetchPosition = latest.ID;
					lastFetchTime = latest.TimeCreated;
				}
			}
			return messages;
		}

		// process chat messages from the database
		private void ProcessChatMessages(List<ChatEntity> messages)
		{
			foreach (ChatEntity message in messages)
			{
				ChatChannel channel = (ChatChannel)message.Channel;
				if (ChatHelper.ChannelCommands.TryGetValue(channel, out ChatCommandDetails sayCommand))
				{
					sayCommand.Func?.Invoke(null, new ChatBroadcast()
					{
						channel = channel,
						text = message.Message,
					});
				}
			}
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
				default: return OnSayChat;
			}
		}

		/// <summary>
		/// Chat message received from a character.
		/// </summary>
		private void OnServerChatMessageReceived(NetworkConnection conn, ChatBroadcast msg)
		{
			if (conn.FirstObject != null)
			{
				Character sender = conn.FirstObject.GetComponent<Character>();
				if (sender != null)
				{
					ProcessNewChatMessage(conn, sender, msg);
				}
			}
			else
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem);
			}
		}

		// parse a message received from a connection
		private void ProcessNewChatMessage(NetworkConnection conn, Character sender, ChatBroadcast msg)
		{
			// validate message length
			if (string.IsNullOrWhiteSpace(msg.text) || msg.text.Length > MAX_LENGTH)
			{
				conn.Kick(FishNet.Managing.Server.KickReason.ExploitExcessiveData);
				return;
			}

			// we rate limit client chat, the message is ignored
			if (MessageRateLimit > 0)
			{
				if (sender.NextChatMessageTime > DateTime.UtcNow)
				{
					return;
				}
				sender.NextChatMessageTime = DateTime.UtcNow.AddMilliseconds(MessageRateLimit);
			}
			// we spam limit client chat, the message is ignored
			if (!AllowRepeatMessages)
			{
				if (sender.LastChatMessage.Equals(msg.text))
				{
					return;
				}
				sender.LastChatMessage = msg.text;
			}

			string cmd = ChatHelper.GetCommandAndTrim(ref msg.text);

			// the text is empty
			if (msg.text.Length < 1)
			{
				return;
			}

			ChatCommand command = ChatHelper.ParseChatCommand(cmd, ref msg.channel);
			if (command != null)
			{
				// add the guild id for parsing
				if (msg.channel == ChatChannel.Guild)
				{
					if (sender.GuildController == null)
					{
						return;
					}

					// add the senders name and guild ID
					msg.text = sender.CharacterName + ": " + sender.GuildController.ID + " " + msg.text;
				}
				// add the party id for parsing
				else if (msg.channel == ChatChannel.Party)
				{
					if (sender.PartyController == null)
					{
						return;
					}

					// add the senders name and party ID
					msg.text = sender.CharacterName + ": " + sender.PartyController.Current.ID + " " + msg.text;
				}
				else
				{
					// add the senders name
					msg.text = sender.CharacterName + ": " + msg.text;
				}

				command?.Invoke(sender, msg);

				// write the parsed message to the database
				using var dbContext = Server.DbContextFactory.CreateDbContext();
				ChatService.Save(dbContext, sender.ID, sender.WorldServerID, Server.SceneServerSystem.ID, (byte)msg.channel, msg.text);
				dbContext.SaveChanges();
			}
		}

		public void OnWorldChat(Character sender, ChatBroadcast msg)
		{
			// send to all connection characters
			foreach (NetworkConnection activeConnection in new List<NetworkConnection>(Server.CharacterSystem.ConnectionCharacters.Keys))
			{
				activeConnection.Broadcast(msg);
			}
		}

		public void OnRegionChat(Character sender, ChatBroadcast msg)
		{
			if (sender == null)
			{
				return;
			}
			// get the senders observed scene
			UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sender.SceneName);
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

		public void OnPartyChat(Character sender, ChatBroadcast msg)
		{
			if (sender == null ||
				sender.PartyController.Current == null)
			{
				return;
			}
			foreach (PartyController member in sender.PartyController.Current.Members.Values)
			{
				if (member.OwnerId == sender.OwnerId)
					continue;

				// broadcast to party member... includes sender
				member.Owner.Broadcast(msg);
			}
		}

		public void OnGuildChat(Character sender, ChatBroadcast msg)
		{
			if (sender == null ||
				sender.GuildController.ID > 0)
			{
				return;
			}
			foreach (long member in sender.GuildController.Members)
			{
				if (member == sender.OwnerId)
					continue;

				if (Server.CharacterSystem.CharactersByID.TryGetValue(member, out Character character))
				{
					// broadcast to guild member...
					character.Owner.Broadcast(msg);
				}
			}
		}

		public void OnTellChat(Character sender, ChatBroadcast msg)
		{
			// get the sender
			string senderName = ChatHelper.GetWordAndTrimmed(msg.text, out string trimmed);
			if (string.IsNullOrWhiteSpace(senderName))
			{
				// no sender in the tell message
				return;
			}

			// get the target
			string targetName = ChatHelper.GetWordAndTrimmed(trimmed, out trimmed);
			if (string.IsNullOrWhiteSpace(targetName))
			{
				// no target in the tell message
				return;
			}
 
			// if the target character is on this server we send them the message
			if (Server.CharacterSystem != null &&
				Server.CharacterSystem.CharactersByName.TryGetValue(targetName, out Character targetCharacter))
			{
				targetCharacter.Owner.Broadcast(new ChatBroadcast()
				{
					channel = msg.channel,
					text = "[From:" + senderName + "]: " + trimmed,
				});
			}
		}

		public void OnTradeChat(Character sender, ChatBroadcast msg)
		{
			// send to all connection characters
			foreach (NetworkConnection activeConnection in new List<NetworkConnection>(Server.CharacterSystem.ConnectionCharacters.Keys))
			{
				activeConnection.Broadcast(msg);
			}
		}

		public void OnSayChat(Character sender, ChatBroadcast msg)
		{
			if (sender != null && sender.Observers != null)
			{
				// get the senders observed characters and send them the chat message
				foreach (NetworkConnection obsConnection in sender.Observers)
				{
					obsConnection.Broadcast(msg);
				}
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