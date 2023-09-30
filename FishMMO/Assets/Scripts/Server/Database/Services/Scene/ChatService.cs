using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO_DB;
using FishMMO_DB.Entities;

namespace FishMMO.Server.Services
{
	public class ChatService
	{
		/// <summary>
		/// Save a character Achievements to the database.
		/// </summary>
		public static void Save(ServerDbContext dbContext, long characterID, long worldServerID, long sceneServerID, byte channel, string message)
		{
			dbContext.Chat.Add(new ChatEntity()
			{
				CharacterID = characterID,
				WorldServerID = worldServerID,
				SceneServerID = sceneServerID,
				TimeCreated = DateTime.UtcNow,
				Channel = channel,
				Message = message,
			});
		}

		/// <summary>
		/// KeepData is automatically true... This means we don't actually delete anything. Deleted is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(ServerDbContext dbContext, long characterID, bool keepData = true)
		{
		}

		/// <summary>
		/// Load character chat messages from the database.
		/// </summary>
		public static List<ChatEntity> Fetch(ServerDbContext dbContext, DateTime lastFetch, long lastPosition, int amount, /*long worldServerID,*/ long sceneServerID)
		{
			var nextPage = dbContext.Chat
				.OrderBy(b => b.TimeCreated)
				.ThenBy(b => b.ID)
				.Where(b => b.TimeCreated >= lastFetch &&
							b.ID > lastPosition &&
							// we don't process local channels, say and region are ignored here
							b.Channel != (byte)ChatChannel.Say &&
							b.Channel != (byte)ChatChannel.Region &&
							// we don't process local tell, guild, and party messages
							!((b.Channel == (byte)ChatChannel.Tell || b.Channel == (byte)ChatChannel.Guild || b.Channel == (byte)ChatChannel.Party) && b.SceneServerID == sceneServerID)
							// we don't process other worlds global chat
							//!((b.Channel == (byte)ChatChannel.World || b.Channel == (byte)ChatChannel.Trade) && b.WorldServerID != worldServerID) &&
							)
				.Take(amount)
				.ToList();
			return nextPage;
		}
	}
}