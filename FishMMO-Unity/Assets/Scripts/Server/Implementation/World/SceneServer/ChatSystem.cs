using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// Server chat system.
	/// </summary>
	public class ChatSystem : ServerBehaviour, IChatHelper
	{
		/// <summary>
		/// Maximum allowed chat message length.
		/// </summary>
		public const int MAX_LENGTH = 128;

		/// <summary>
		/// If true, allows repeat messages from clients without spam filtering.
		/// </summary>
		public bool AllowRepeatMessages = false;
		/// <summary>
		/// The server chat rate limit in milliseconds. Should match the client's UIChat.messageRateLimit.
		/// </summary>
		[Tooltip("The server chat rate limit in milliseconds. This should be equal to the clients UIChat.messageRateLimit")]
		public float MessageRateLimit = 0.0f;
		/// <summary>
		/// The server chat message pump rate limit in seconds.
		/// </summary>
		[Tooltip("The server chat message pump rate limit in seconds.")]
		public float MessagePumpRate = 2.0f;
		/// <summary>
		/// Number of chat messages to fetch per database poll.
		/// </summary>
		public int MessageFetchCount = 20;

		/// <summary>
		/// Timestamp of the last successful fetch from the database.
		/// </summary>
		private DateTime lastFetchTime = DateTime.UtcNow;
		/// <summary>
		/// Position of the last fetched chat message in the database.
		/// </summary>
		private long lastFetchPosition = 0;
		/// <summary>
		/// Time remaining until the next database poll for chat messages.
		/// </summary>
		private float nextPump = 0.0f;

		/// <summary>
		/// Current connection state of the server.
		/// </summary>
		private LocalConnectionState serverState;

		/// <summary>
		/// Initializes the chat system, registering broadcast handlers and chat helper commands.
		/// </summary>
		public override void InitializeOnce()
		{
			if (ServerManager != null &&
				Server != null)
			{
				ChatHelper.InitializeOnce(GetChannelCommand);
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
				Server.NetworkWrapper.RegisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived, true);
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Cleans up the chat system, unregistering broadcast handlers and chat helper commands.
		/// </summary>
		public override void Destroying()
		{
			if (ServerManager != null &&
				Server != null)
			{
				ServerManager.OnServerConnectionState -= ServerManager_OnServerConnectionState;
				Server.NetworkWrapper.UnregisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles changes in the server's connection state.
		/// </summary>
		/// <param name="args">Arguments containing the new connection state.</param>
		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

		/// <summary>
		/// Unity LateUpdate callback. Polls the database for chat messages at the specified rate and processes them.
		/// </summary>
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

		/// <summary>
		/// Fetches new chat messages from the database since the last fetch.
		/// </summary>
		/// <returns>List of new chat message entities.</returns>
		private List<ChatEntity> FetchChatMessages()
		{
			if (!ServerBehaviourRegistry.TryGet(out SceneServerSystem sceneServerSystem))
			{
				return null;
			}
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

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

		/// <summary>
		/// Processes a list of chat messages, broadcasting them to appropriate channels and clients.
		/// </summary>
		/// <param name="messages">List of chat message entities to process.</param>
		private void ProcessChatMessages(List<ChatEntity> messages)
		{
			if (messages == null || messages.Count < 1)
			{
				return;
			}
			foreach (ChatEntity message in messages)
			{
				ChatChannel channel = (ChatChannel)message.Channel;
				if (channel == ChatChannel.Discord)
				{
					OnSendDiscordMessage(message.WorldServerID, message.SceneServerID, message.Message);
				}
				else if (ChatHelper.ChatChannelCommands.TryGetValue(channel, out ChatCommandDetails sayCommand))
				{
					sayCommand.Func?.Invoke(null, new ChatBroadcast()
					{
						Channel = channel,
						Text = message.Message,
					});
				}
			}
		}

		/// <summary>
		/// Gets the chat command handler for a specific chat channel.
		/// </summary>
		/// <param name="channel">Chat channel to get the command for.</param>
		/// <returns>Chat command delegate for the channel.</returns>
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
		/// Handles incoming chat broadcast from a character, validates and processes the message.
		/// </summary>
		/// <param name="conn">Network connection of the sender.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
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

		/// <summary>
		/// Parses and processes a new chat message received from a connection, including validation, rate limiting, spam filtering, and command handling.
		/// </summary>
		/// <param name="conn">Network connection of the sender.</param>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
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
					ServerBehaviourRegistry.TryGet(out SceneServerSystem sceneServerSystem))
				{
					// write the parsed message to the database
					using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
					ChatService.Save(dbContext, sender.ID, sender.WorldServerID, sceneServerSystem.ID, msg.Channel, msg.Text);
				}
			}
		}

		/// <summary>
		/// Handles world chat messages, broadcasting to all characters in the specified world.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>True if message was broadcast, false otherwise.</returns>
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

			if (ServerBehaviourRegistry.TryGet(out CharacterSystem characterSystem) &&
				characterSystem.CharactersByWorld.TryGetValue(worldID, out Dictionary<long, IPlayerCharacter> characters))
			{
				// send to all world characters
				foreach (IPlayerCharacter character in new List<IPlayerCharacter>(characters.Values))
				{
					Server.NetworkWrapper.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
				}
			}
			return true;
		}

		/// <summary>
		/// Handles region chat messages, broadcasting to all connections in the sender's scene.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>False to prevent message from being written to the database.</returns>
		public bool OnRegionChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender == null)
			{
				return false;
			}
			// get the senders observed scene
			UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sender.SceneName);
			if (scene != null &&
				Server.NetworkWrapper.NetworkManager != null &&
				Server.NetworkWrapper.NetworkManager.SceneManager != null)
			{
				if (Server.NetworkWrapper.NetworkManager.SceneManager.SceneConnections.TryGetValue(scene, out HashSet<NetworkConnection> connections))
				{
					foreach (NetworkConnection connection in connections)
					{
						Server.NetworkWrapper.Broadcast(connection, msg, true, Channel.Reliable);
					}
				}
			}
			return false; // we return false here so the message is not written to the database
		}

		/// <summary>
		/// Handles party chat messages, broadcasting to all party members.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>True if message was broadcast, false otherwise.</returns>
		public bool OnPartyChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
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

			if (ServerBehaviourRegistry.TryGet(out CharacterSystem characterSystem))
			{
				// get all the member data so we can broadcast
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
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
						Server.NetworkWrapper.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
					}
				}
				return true;
			}
			return false;
		}

		/// <summary>
		/// Handles guild chat messages, broadcasting to all guild members.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>True if message was broadcast, false otherwise.</returns>
		public bool OnGuildChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
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

			if (ServerBehaviourRegistry.TryGet(out CharacterSystem characterSystem))
			{
				// get all the member data so we can broadcast
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
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
						Server.NetworkWrapper.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
					}
				}
				return true;
			}
			return false;
		}

		/// <summary>
		/// Handles tell (private) chat messages, broadcasting to the target character if online.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>True if message was broadcast, false otherwise.</returns>
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
			if (Server.CoreServer.NpgsqlDbContextFactory != null)
			{
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
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
					Server.NetworkWrapper.Broadcast(sender.Owner, new ChatBroadcast()
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
					Server.NetworkWrapper.Broadcast(sender.Owner, new ChatBroadcast()
					{
						Channel = msg.Channel,
						SenderID = msg.SenderID,
						Text = ChatHelper.TARGET_OFFLINE + " " + targetName,
					}, true, Channel.Reliable);
					return false;
				}
				else if (targetID > 0)
				{
					Server.NetworkWrapper.Broadcast(sender.Owner, new ChatBroadcast()
					{
						Channel = msg.Channel,
						SenderID = targetID,
						Text = ChatHelper.TELL_RELAYED + " " + trimmed,
					}, true, Channel.Reliable);
				}
			}

			if (ServerBehaviourRegistry.TryGet(out CharacterSystem characterSystem))
			{
				// if the target character is on this server we send them the message
				if (characterSystem != null &&
					characterSystem.CharactersByID.TryGetValue(targetID, out IPlayerCharacter targetCharacter))
				{
					Server.NetworkWrapper.Broadcast(targetCharacter.Owner, new ChatBroadcast()
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

		/// <summary>
		/// Handles trade chat messages, broadcasting to all characters in the specified world.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>True if message was broadcast, false otherwise.</returns>
		public bool OnTradeChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			// get the world ID
			string wid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(wid) || !long.TryParse(wid, out long worldID))
			{
				// no worldID in the message
				return false;
			}

			if (ServerBehaviourRegistry.TryGet(out CharacterSystem characterSystem))
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
						Server.NetworkWrapper.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
					}
				}
				return true;
			}
			return false;
		}

		/// <summary>
		/// Handles say (local) chat messages, broadcasting to all observers of the sender.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>False to prevent message from being written to the database.</returns>
		public bool OnSayChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender != null && sender.Observers != null)
			{
				// get the senders observed characters and send them the chat message
				foreach (NetworkConnection obsConnection in sender.Observers)
				{
					Server.NetworkWrapper.Broadcast(obsConnection, msg, true, Channel.Reliable);
				}
			}
			return false; // we return false here so the message is not written to the database
		}

		/// <summary>
		/// Allows the server to send system messages to the connection.
		/// </summary>
		/// <param name="conn">Network connection to send the system message to.</param>
		/// <param name="message">System message text.</param>
		public void OnSendSystemMessage(NetworkConnection conn, string message)
		{
			if (conn == null)
				return;

			ChatBroadcast msg = new ChatBroadcast()
			{
				Channel = ChatChannel.System,
				Text = message,
			};

			Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
		}

		/// <summary>
		/// Allows the server to send Discord messages to a specific world server.
		/// </summary>
		/// <param name="worldServerID">World server ID to send the message to.</param>
		/// <param name="sceneServerID">Scene server ID (for context).</param>
		/// <param name="message">Discord message text.</param>
		public void OnSendDiscordMessage(long worldServerID, long sceneServerID, string message)
		{
			ChatBroadcast newMsg = new ChatBroadcast()
			{
				Channel = ChatChannel.Discord,
				SenderID = 0,
				Text = message,
			};

			if (ServerBehaviourRegistry.TryGet(out CharacterSystem characterSystem) &&
				characterSystem.CharactersByWorld.TryGetValue(worldServerID, out Dictionary<long, IPlayerCharacter> characters))
			{
				// send to all world characters
				foreach (IPlayerCharacter character in new List<IPlayerCharacter>(characters.Values))
				{
					Server.NetworkWrapper.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
				}
			}
		}
	}
}