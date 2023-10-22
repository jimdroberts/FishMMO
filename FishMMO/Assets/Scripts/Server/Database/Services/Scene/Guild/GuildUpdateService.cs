using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO_DB;
using FishMMO_DB.Entities;

namespace FishMMO.Server.Services
{
	public class GuildUpdateService
	{
		public static void Save(ServerDbContext dbContext, long guildID)
		{
			dbContext.GuildUpdates.Add(new GuildUpdateEntity()
			{
				GuildID = guildID,
				TimeCreated = DateTime.UtcNow,
			});
		}

		public static void Delete(ServerDbContext dbContext, long guildID)
		{
			var guildEntity = dbContext.GuildUpdates.Where(a => a.GuildID == guildID);
			if (guildEntity != null)
			{
				dbContext.GuildUpdates.RemoveRange(guildEntity);
			}
		}

		public static List<GuildUpdateEntity> Fetch(ServerDbContext dbContext, DateTime lastFetch, long lastPosition, int amount)
		{
			var nextPage = dbContext.GuildUpdates
				.OrderBy(b => b.TimeCreated)
				.ThenBy(b => b.ID)
				.Where(b => b.TimeCreated >= lastFetch &&
							b.ID > lastPosition)
				.Take(amount)
				.ToList();
			return nextPage;
		}
	}
}