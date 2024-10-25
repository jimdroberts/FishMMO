using FishNet.Connection;
using FishNet.Transporting;
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
			if (ServerManager != null &&
				Server != null)
			{
				ChatHelper.InitializeOnce(GetChannelCommand);
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
				Server.RegisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived, true);
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (ServerManager != null &&
				Server != null)
			{
				ServerManager.OnServerConnectionState -= ServerManager_OnServerConnectionState;
				Server.UnregisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived);
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
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
			if (!ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				return null;
			}
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			// fetch chat messages from the database
			List<ChatEntity> messages = ChatService.Fetch(dbContext, lastFetchTime, lastFetchPosition, MessageFetchCount, sceneServerSystem.ID);
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
				if (ChatHelper.ChatChannelCommands.TryGetValue(channel, out ChatCommandDetails sayCommand))
				{
					sayCommand.Func?.Invoke(null, new ChatBroadcast()
					{
						Channel = channel,
						Text = message.Message,
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
				IPlayerCharacter sender = conn.FirstObject.GetComponent<IPlayerCharacter>();
				ProcessNewChatMessage(conn, sender, msg);
			}
			else
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem);
			}
		}

		// parse a message received from a connection
		private void ProcessNewChatMessage(NetworkConnection conn, IPlayerCharacter sender, ChatBroadcast msg)
		{
			// validate message length
			if (sender == null ||
				string.IsNullOrWhiteSpace(msg.Text) ||
				msg.Text.Length > MAX_LENGTH)
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
				if (!string.IsNullOrWhiteSpace(sender.LastChatMessage) &&
					sender.LastChatMessage.Equals(msg.Text))
				{
					return;
				}
				sender.LastChatMessage = msg.Text;
			}

			// remove Rich Text Tags if any exist
			msg.Text = ChatHelper.Sanitize(msg.Text);

			string cmd = ChatHelper.GetCommandAndTrim(ref msg.Text);

			// commands are handled differently from chat commands
			if (ChatHelper.TryParseCommand(cmd, sender, msg))
			{
				return;
			}

			// the text is empty
			if (msg.Text.Length < 1)
			{
				return;
			}

			if (ChatHelper.TryParseChatCommand(cmd, out ChatCommandDetails commandDetails))
			{
				msg.SenderID = sender.ID;
				msg.Channel = commandDetails.Channel;

				switch (msg.Channel)
				{
					case ChatChannel.Guild:
						if (!sender.TryGet(out IGuildController guildController) ||
							guildController.ID < 1)
						{
							return;
						}

						// add the senders guild ID
						msg.Text = guildController.ID + " " + msg.Text;
						break;
					case ChatChannel.Party:
						if (!sender.TryGet(out IPartyController partyController) ||
							partyController.ID < 1)
						{
							return;
						}

						// add the senders party ID
						msg.Text = partyController.ID + " " + msg.Text;
						break;
					case ChatChannel.Trade:
					case ChatChannel.World:
						// add the senders world id
						msg.Text = sender.WorldServerID + " " + msg.Text;
						break;
					default:
						break;
				}

				if (commandDetails.Func.Invoke(sender, msg) &&
					ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
				{
					// write the parsed message to the database
					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
					ChatService.Save(dbContext, sender.ID, sender.WorldServerID, sceneServerSystem.ID, msg.Channel, msg.Text);
				}
			}
		}

		public bool OnWorldChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			// get the world ID
			string wid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(wid) || !long.TryParse(wid, out long worldID))
			{
				// no worldID in the message
				return false;
			}

			ChatBroadcast newMsg = new ChatBroadcast()
			{
				Channel = msg.Channel,
				SenderID = msg.SenderID,
				Text = trimmed,
			};

			if (ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
				characterSystem.CharactersByWorld.TryGetValue(worldID, out Dictionary<long, IPlayerCharacter> characters))
			{
				// send to all world characters
				foreach (IPlayerCharacter character in new List<IPlayerCharacter>(characters.Values))
				{
					Server.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
				}
			}
			return true;
		}

		public bool OnRegionChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender == null)
			{
				return false;
			}
			// get the senders observed scene
			UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sender.SceneName);
			if (scene != null &&
				Server.NetworkManager != null &&
				Server.NetworkManager.SceneManager != null)
			{
				if (Server.NetworkManager.SceneManager.SceneConnections.TryGetValue(scene, out HashSet<NetworkConnection> connections))
				{
					foreach (NetworkConnection connection in connections)
					{
						Server.Broadcast(connection, msg, true, Channel.Reliable);
					}
				}
			}
			return false; // we return false here so the message is not written to the database
		}

		public bool OnPartyChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (Server.NpgsqlDbContextFactory == null)
			{
				return false;
			}

			// get the party ID
			string gid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(gid) || !long.TryParse(gid, out long partyID))
			{
				// no partyID in the message
				return false;
			}

			if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
			{
				// get all the member data so we can broadcast
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				List<CharacterPartyEntity> dbMembers = CharacterPartyService.Members(dbContext, partyID);

				ChatBroadcast newMsg = new ChatBroadcast()
				{
					Channel = msg.Channel,
					SenderID = msg.SenderID,
					Text = trimmed,
				};

				foreach (CharacterPartyEntity member in dbMembers)
				{
					if (characterSystem.CharactersByID.TryGetValue(member.CharacterID, out IPlayerCharacter character))
					{
						// broadcast to party member...
						Server.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
					}
				}
				return true;
			}
			return false;
		}

		public bool OnGuildChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (Server.NpgsqlDbContextFactory == null)
			{
				return false;
			}

			// get the guild ID
			string gid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(gid) || !long.TryParse(gid, out long guildID))
			{
				// no guildID in the message
				return false;
			}

			if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
			{
				// get all the member data so we can broadcast
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				List<CharacterGuildEntity> dbMembers = CharacterGuildService.Members(dbContext, guildID);

				ChatBroadcast newMsg = new ChatBroadcast()
				{
					Channel = msg.Channel,
					SenderID = msg.SenderID,
					Text = trimmed,
				};
				foreach (CharacterGuildEntity member in dbMembers)
				{
					if (characterSystem.CharactersByID.TryGetValue(member.CharacterID, out IPlayerCharacter character))
					{
						// broadcast to guild member...
						Server.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
					}
				}
				return true;
			}
			return false;
		}

		public bool OnTellChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			// get the target
			string targetName = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
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
				if (msg.SenderID == targetID)
				{
					Server.Broadcast(sender.Owner, new ChatBroadcast()
					{
						Channel = msg.Channel,
						SenderID = msg.SenderID,
						Text = ChatHelper.TELL_ERROR_MESSAGE_SELF + " ",
					}, true, Channel.Reliable);
					return false;
				}
				else if (!online)
				{
					// if the target character is not online
					Server.Broadcast(sender.Owner, new ChatBroadcast()
					{
						Channel = msg.Channel,
						SenderID = msg.SenderID,
						Text = ChatHelper.TARGET_OFFLINE + " " + targetName,
					}, true, Channel.Reliable);
					return false;
				}
				else if (targetID > 0)
				{
					Server.Broadcast(sender.Owner, new ChatBroadcast()
					{
						Channel = msg.Channel,
						SenderID = targetID,
						Text = ChatHelper.TELL_RELAYED + " " + trimmed,
					}, true, Channel.Reliable);
				}
			}

			if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
			{
				// if the target character is on this server we send them the message
				if (characterSystem != null &&
					characterSystem.CharactersByID.TryGetValue(targetID, out IPlayerCharacter targetCharacter))
				{
					Server.Broadcast(targetCharacter.Owner, new ChatBroadcast()
					{
						Channel = msg.Channel,
						SenderID = msg.SenderID,
						Text = trimmed,
					}, true, Channel.Reliable);
				}
				return true;
			}
			return false;
		}

		public bool OnTradeChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			// get the world ID
			string wid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(wid) || !long.TryParse(wid, out long worldID))
			{
				// no worldID in the message
				return false;
			}

			if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
			{
				ChatBroadcast newMsg = new ChatBroadcast()
				{
					Channel = msg.Channel,
					SenderID = msg.SenderID,
					Text = trimmed,
				};
				if (characterSystem != null &&
					characterSystem.CharactersByWorld.TryGetValue(worldID, out Dictionary<long, IPlayerCharacter> characters))
				{
					// send to all world characters
					foreach (IPlayerCharacter character in new List<IPlayerCharacter>(characters.Values))
					{
						Server.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
					}
				}
				return true;
			}
			return false;
		}

		public bool OnSayChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender != null && sender.Observers != null)
			{
				// get the senders observed characters and send them the chat message
				foreach (NetworkConnection obsConnection in sender.Observers)
				{
					Server.Broadcast(obsConnection, msg, true, Channel.Reliable);
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
				Channel = ChatChannel.System,
				Text = message,
			};

			Server.Broadcast(conn, msg, true, Channel.Reliable);
		}
	}
}