using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class ChatService
	{
		/// <summary>
		/// Save a  chat message to the database.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, long characterID, long worldServerID, long sceneServerID, ChatChannel channel, string message)
		{
			dbContext.Chat.Add(new ChatEntity()
			{
				CharacterID = characterID,
				WorldServerID = worldServerID,
				SceneServerID = sceneServerID,
				TimeCreated = DateTime.UtcNow,
				Channel = (byte)channel,
				Message = message,
			});
		}

		/// <summary>
		/// KeepData is automatically true... This means we don't actually delete anything. Deleted is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, bool keepData = true)
		{
		}

		/// <summary>
		/// Load chat messages from the database.
		/// </summary>
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