using FishNet.Connection;
using FishNet.Transporting;
using FishNet.Managing.Scened;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;

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
				ServerManager.RegisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived, true);
			}
			else if (serverState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived);
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
					ProcessChatMessages(messages);

				}
				nextPump -= Time.deltaTime;
			}
		}

		private List<ChatEntity> FetchChatMessages()
		{
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

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
			if (messages == null || messages.Count < 1)
			{
				return;
			}
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
				// ChatChannel.System is Server->Client only. We never parse system messages locally.
				default: return OnSayChat;
			}
		}

		/// <summary>
		/// Chat message received from a character.
		/// </summary>
		private void OnServerChatBroadcastReceived(NetworkConnection conn, ChatBroadcast msg, Channel channel)
		{
			if (conn.FirstObject != null)
			{
				Character sender = conn.FirstObject.GetComponent<Character>();
				ProcessNewChatMessage(conn, sender, msg);
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
			if (sender == null || string.IsNullOrWhiteSpace(msg.text) || msg.text.Length > MAX_LENGTH)
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

			// remove Rich Text Tags if any exist
			msg.text = ChatHelper.Sanitize(msg.text);

			string cmd = ChatHelper.GetCommandAndTrim(ref msg.text);

			// direct commands are handled differently
			if (ChatHelper.TryParseDirectCommand(cmd, sender, msg))
			{
				return;
			}

			// the text is empty
			if (msg.text.Length < 1)
			{
				return;
			}

			ChatCommand command = ChatHelper.ParseChatCommand(cmd, ref msg.channel);
			if (command != null)
			{
				msg.senderID = sender.ID.Value;

				switch (msg.channel)
				{
					case ChatChannel.Guild:
						if (sender.GuildController == null || sender.GuildController.ID.Value < 1)
						{
							return;
						}

						// add the senders guild ID
						msg.text = sender.GuildController.ID.Value + " " + msg.text;
						break;
					case ChatChannel.Party:
						if (sender.PartyController == null || sender.PartyController.ID < 1)
						{
							return;
						}

						// add the senders party ID
						msg.text = sender.PartyController.ID + " " + msg.text;
						break;
					case ChatChannel.Trade:
					case ChatChannel.World:
						// add the senders world id
						msg.text = sender.WorldServerID + " " + msg.text;
						break;
					default:
						break;
				}

				if (command.Invoke(sender, msg))
				{
					// write the parsed message to the database
					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
					ChatService.Save(dbContext, sender.ID.Value, sender.WorldServerID, Server.SceneServerSystem.ID, msg.channel, msg.text);
				}
			}
		}

		public bool OnWorldChat(Character sender, ChatBroadcast msg)
		{
			// get the world ID
			string wid = ChatHelper.GetWordAndTrimmed(msg.text, out string trimmed);
			if (string.IsNullOrWhiteSpace(wid) || !long.TryParse(wid, out long worldID))
			{
				// no worldID in the message
				return false;
			}

			ChatBroadcast newMsg = new ChatBroadcast()
			{
				channel = msg.channel,
				senderID = msg.senderID,
				text = trimmed,
			};
			if (Server.CharacterSystem != null &&
				Server.CharacterSystem.CharactersByWorld.TryGetValue(worldID, out Dictionary<long, Character> characters))
			{
				// send to all world characters
				foreach (Character character in new List<Character>(characters.Values))
				{
					character.Owner.Broadcast(newMsg, true, Channel.Reliable);
				}
			}
			return true;
		}

		public bool OnRegionChat(Character sender, ChatBroadcast msg)
		{
			if (sender == null)
			{
				return false;
			}
			// get the senders observed scene
			UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sender.SceneName.Value);
			if (scene != null)
			{
				if (SceneManager.SceneConnections.TryGetValue(scene, out HashSet<NetworkConnection> connections))
				{
					foreach (NetworkConnection connection in connections)
					{
						connection.Broadcast(msg, true, Channel.Reliable);
					}
				}
			}
			return false; // we return false here so the message is not written to the database
		}

		public bool OnPartyChat(Character sender, ChatBroadcast msg)
		{
			if (Server.NpgsqlDbContextFactory == null)
			{
				return false;
			}

			// get the party ID
			string gid = ChatHelper.GetWordAndTrimmed(msg.text, out string trimmed);
			if (string.IsNullOrWhiteSpace(gid) || !long.TryParse(gid, out long partyID))
			{
				// no partyID in the message
				return false;
			}

			// get all the member data so we can broadcast
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			List<CharacterPartyEntity> dbMembers = CharacterPartyService.Members(dbContext, partyID);

			ChatBroadcast newMsg = new ChatBroadcast()
			{
				channel = msg.channel,
				senderID = msg.senderID,
				text = trimmed,
			};
			foreach (CharacterPartyEntity member in dbMembers)
			{
				if (Server.CharacterSystem.CharactersByID.TryGetValue(member.CharacterID, out Character character))
				{
					// broadcast to party member...
					character.Owner.Broadcast(newMsg, true, Channel.Reliable);
				}
			}
			return true;
		}

		public bool OnGuildChat(Character sender, ChatBroadcast msg)
		{
			if (Server.NpgsqlDbContextFactory == null)
			{
				return false;
			}

			// get the guild ID
			string gid = ChatHelper.GetWordAndTrimmed(msg.text, out string trimmed);
			if (string.IsNullOrWhiteSpace(gid) || !long.TryParse(gid, out long guildID))
			{
				// no guildID in the message
				return false;
			}

			// get all the member data so we can broadcast
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			List<CharacterGuildEntity> dbMembers = CharacterGuildService.Members(dbContext, guildID);

			ChatBroadcast newMsg = new ChatBroadcast()
			{
				channel = msg.channel,
				senderID = msg.senderID,
				text = trimmed,
			};
			foreach (CharacterGuildEntity member in dbMembers)
			{
				if (Server.CharacterSystem.CharactersByID.TryGetValue(member.CharacterID, out Character character))
				{
					// broadcast to guild member...
					character.Owner.Broadcast(newMsg, true, Channel.Reliable);
				}
			}
			return true;
		}

		public bool OnTellChat(Character sender, ChatBroadcast msg)
		{
			// get the target
			string targetName = ChatHelper.GetWordAndTrimmed(msg.text, out string trimmed);
			if (string.IsNullOrWhiteSpace(targetName))
			{
				// no target in the tell message
				return false;
			}

			long targetID = 0;
			bool online = false;
			if (Server.NpgsqlDbContextFactory != null)
			{
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				targetID = CharacterService.GetIdByName(dbContext, targetName);
				online = CharacterService.ExistsAndOnline(dbContext, targetID);
			}

			// did we find the ID?
			if (targetID < 1)
			{
				return false;
			}

			// if the sender exists then we can send a return message if the target character is valid
			if (sender != null)
			{
				// are we messaging ourself?
				if (msg.senderID == targetID)
				{
					sender.Owner.Broadcast(new ChatBroadcast()
					{
						channel = msg.channel,
						senderID = msg.senderID,
						text = ChatHelper.ERROR_MESSAGE_SELF + " ",
					}, true, Channel.Reliable);
					return false;
				}
				else if (!online)
				{
					// if the target character is not online
					sender.Owner.Broadcast(new ChatBroadcast()
					{
						channel = msg.channel,
						senderID = msg.senderID,
						text = ChatHelper.ERROR_TARGET_OFFLINE + " " + targetName,
					}, true, Channel.Reliable);
					return false;
				}
				else if (targetID > 0)
				{
					sender.Owner.Broadcast(new ChatBroadcast()
					{
						channel = msg.channel,
						senderID = targetID,
						text = ChatHelper.RELAYED + " " + trimmed,
					}, true, Channel.Reliable);
				}
			}
 
			// if the target character is on this server we send them the message
			if (Server.CharacterSystem != null &&
				Server.CharacterSystem.CharactersByID.TryGetValue(targetID, out Character targetCharacter))
			{
				targetCharacter.Owner.Broadcast(new ChatBroadcast()
				{
					channel = msg.channel,
					senderID = msg.senderID,
					text = trimmed,
				}, true, Channel.Reliable);
			}
			return true;
		}

		public bool OnTradeChat(Character sender, ChatBroadcast msg)
		{
			// get the world ID
			string wid = ChatHelper.GetWordAndTrimmed(msg.text, out string trimmed);
			if (string.IsNullOrWhiteSpace(wid) || !long.TryParse(wid, out long worldID))
			{
				// no worldID in the message
				return false;
			}

			ChatBroadcast newMsg = new ChatBroadcast()
			{
				channel = msg.channel,
				senderID = msg.senderID,
				text = trimmed,
			};
			if (Server.CharacterSystem != null &&
				Server.CharacterSystem.CharactersByWorld.TryGetValue(worldID, out Dictionary<long, Character> characters))
			{
				// send to all world characters
				foreach (Character character in new List<Character>(characters.Values))
				{
					character.Owner.Broadcast(newMsg, true, Channel.Reliable);
				}
			}
			return true;
		}

		public bool OnSayChat(Character sender, ChatBroadcast msg)
		{
			if (sender != null && sender.Observers != null)
			{
				// get the senders observed characters and send them the chat message
				foreach (NetworkConnection obsConnection in sender.Observers)
				{
					obsConnection.Broadcast(msg, true, Channel.Reliable);
				}
			}
			return false; // we return false here so the message is not written to the database
		}

		/// <summary>
		/// Allows the server to send system messages to the connection
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

			conn.Broadcast(msg, true, Channel.Reliable);
		}
	}
}