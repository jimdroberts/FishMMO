using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing chat messages, including saving, deleting, and fetching chat data from the database.
		/// </summary>
		public class ChatService
	{
		/// <summary>
		/// Saves a chat message to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID sending the message.</param>
		/// <param name="worldServerID">The world server ID.</param>
		/// <param name="sceneServerID">The scene server ID.</param>
		/// <param name="channel">The chat channel.</param>
		/// <param name="message">The chat message content.</param>
		public static void Save(NpgsqlDbContext dbContext, long characterID, long worldServerID, long sceneServerID, ChatChannel channel, string message)
		{
			if (worldServerID == 0 ||
				sceneServerID == 0)
			{
				return;
			}
			dbContext.Chat.Add(new ChatEntity()
			{
				CharacterID = characterID,
				WorldServerID = worldServerID,
				SceneServerID = sceneServerID,
				TimeCreated = DateTime.UtcNow,
				Channel = (byte)channel,
				Message = message,
			});
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes all chat messages for a character from the database. If keepData is false, the entries are removed. (Currently not implemented)
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="keepData">Whether to keep the data (currently not implemented).</param>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, bool keepData = true)
		{
			if (characterID == 0)
			{
				return;
			}
		}

		/// <summary>
		/// Loads chat messages from the database based on the last fetch time, last position, amount, and scene server ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="lastFetch">The timestamp to compare messages against.</param>
		/// <param name="lastPosition">The last message ID fetched.</param>
		/// <param name="amount">The maximum number of messages to fetch.</param>
		/// <param name="sceneServerID">The scene server ID to filter messages.</param>
		/// <returns>A list of chat entities matching the criteria.</returns>
		public static List<ChatEntity> Fetch(NpgsqlDbContext dbContext, DateTime lastFetch, long lastPosition, int amount, long sceneServerID)
		{
			var nextPage = dbContext.Chat
				.OrderBy(b => b.TimeCreated)
				.ThenBy(b => b.ID)
				.Where(b => b.TimeCreated >= lastFetch &&
							b.ID > lastPosition &&
							// we don't process local messages
							!((b.Channel == (byte)ChatChannel.Tell ||
							   b.Channel == (byte)ChatChannel.Guild ||
							   b.Channel == (byte)ChatChannel.Party ||
							   b.Channel == (byte)ChatChannel.World ||
							   b.Channel == (byte)ChatChannel.Trade) && b.SceneServerID == sceneServerID)
							)
				.Take(amount)
				.ToList();
			return nextPage;
		}
	}
}