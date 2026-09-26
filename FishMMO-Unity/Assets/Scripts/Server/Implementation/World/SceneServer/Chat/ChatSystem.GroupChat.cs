using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	// Group channel handlers: Party and Guild.
	public partial class ChatSystem
	{
		/* Local recipients come from this server's own trackers, on the main thread.
		 *
		 * Party and guild lines used to read the group's whole roster from the database on every
		 * line — for a live line on the sender's server, and again for the pumped copy on EVERY
		 * other scene server — only to test each member against CharactersByID and keep the ones
		 * that were here. With twenty scene servers, three guild lines a second cost sixty roster
		 * reads a second, almost all on servers hosting none of the guild (hot-path audit M17).
		 *
		 * The party and guild systems already track which members of each group are on this
		 * server (PartyCharacterTracker, GuildCharacterTracker), because their own pumps need
		 * exactly that. A group nobody here belongs to is now an empty lookup, and the pumped copy
		 * of its line does not even reach this server: the pump only asks for groups tracked
		 * here. Each candidate is still checked against the character's own controller, the one
		 * authority on which group a character is in right now, so a tracker a pump behind a
		 * leave cannot deliver a line to somebody who has left. */

		/// <summary>
		/// Handles party chat: persists a live line for the other scene servers and delivers it to
		/// this server's members of the party. Returns false so the caller does not persist it again.
		/// </summary>
		/// <param name="sender">Player character sending the message, or null for a pumped line.</param>
		/// <param name="msg">Chat broadcast message; its text starts with the party ID.</param>
		/// <returns>False — persistence is handled here.</returns>
		public bool OnPartyChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			// get the party ID
			string gid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(gid) || !long.TryParse(gid, out long partyID))
			{
				// no partyID in the message
				return false;
			}

			/* Persisted whatever the local delivery finds: the row is what carries the line to the
			 * party's members on every other scene server, and it is the audit record that it was
			 * said. Only live player messages: pumped ones are already persisted. */
			if (sender != null)
			{
				EnqueuePersist(sender.ID, sender.CharacterName, sender.Account, sender.WorldServerID, msg.Channel, partyID + " " + trimmed, msg.ReceivedUtcTicks);
			}

			SendToLocalGroupMembers(partyID, isGuild: false, new ChatBroadcast()
			{
				Channel = msg.Channel,
				SenderID = msg.SenderID,
				Text = trimmed,
			});
			return false;
		}

		/// <summary>
		/// Handles guild chat: persists a live line for the other scene servers and delivers it to
		/// this server's members of the guild. Returns false so the caller does not persist it again.
		/// </summary>
		/// <param name="sender">Player character sending the message, or null for a pumped line.</param>
		/// <param name="msg">Chat broadcast message; its text starts with the guild ID.</param>
		/// <returns>False — persistence is handled here.</returns>
		public bool OnGuildChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			// get the guild ID
			string gid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(gid) || !long.TryParse(gid, out long guildID))
			{
				// no guildID in the message
				return false;
			}

			// Persisted whatever the local delivery finds; see OnPartyChat.
			if (sender != null)
			{
				EnqueuePersist(sender.ID, sender.CharacterName, sender.Account, sender.WorldServerID, msg.Channel, guildID + " " + trimmed, msg.ReceivedUtcTicks);
			}

			SendToLocalGroupMembers(guildID, isGuild: true, new ChatBroadcast()
			{
				Channel = msg.Channel,
				SenderID = msg.SenderID,
				Text = trimmed,
			});
			return false;
		}

		/// <summary>
		/// Sends a line to every member of a party or guild who is on this scene server, in one
		/// multicast. Main thread only.
		/// </summary>
		/// <param name="groupID">The party or guild ID.</param>
		/// <param name="isGuild">True for a guild, false for a party.</param>
		/// <param name="relay">The line as members receive it.</param>
		private void SendToLocalGroupMembers(long groupID, bool isGuild, ChatBroadcast relay)
		{
			if (groupID < 1 ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) ||
				!Server.DataContainerRegistry.TryGet<IChatSystemRuntimeData>(out var chatData) ||
				chatData.ConnectionBroadcastSet == null)
			{
				return;
			}

			HashSet<long> memberIDs = null;
			if (isGuild)
			{
				if (Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var guildData))
				{
					guildData.GuildCharacterTracker.TryGetValue(groupID, out memberIDs);
				}
			}
			else if (Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var partyData))
			{
				partyData.PartyCharacterTracker.TryGetValue(groupID, out memberIDs);
			}

			if (memberIDs == null || memberIDs.Count < 1)
			{
				return;
			}

			HashSet<NetworkConnection> recipients = chatData.ConnectionBroadcastSet;
			recipients.Clear();
			try
			{
				foreach (long memberID in memberIDs)
				{
					if (!mappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter character) ||
						!IsInGroup(character, groupID, isGuild))
					{
						continue;
					}
					AddRecipient(recipients, character);
				}

				if (recipients.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(recipients, relay, true, Channel.Reliable);
				}
			}
			finally
			{
				recipients.Clear();
			}
		}

		/// <summary>
		/// Whether a character's own controller says it is in the group right now.
		/// </summary>
		private static bool IsInGroup(IPlayerCharacter character, long groupID, bool isGuild)
		{
			if (character == null)
			{
				return false;
			}
			if (isGuild)
			{
				return character.TryGet(out IGuildController guildController) && guildController.ID == groupID;
			}
			return character.TryGet(out IPartyController partyController) && partyController.ID == groupID;
		}
	}
}
