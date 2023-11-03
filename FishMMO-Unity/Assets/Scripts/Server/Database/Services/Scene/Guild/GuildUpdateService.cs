using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.DatabaseServices
{
	public class GuildUpdateService
	{
		public static void Save(NpgsqlDbContext dbContext, long guildID)
		{
			dbContext.GuildUpdates.Add(new GuildUpdateEntity()
			{
				GuildID = guildID,
				TimeCreated = DateTime.UtcNow,
			});
		}

		public static void Delete(NpgsqlDbContext dbContext, long guildID)
		{
			var guildEntity = dbContext.GuildUpdates.Where(a => a.GuildID == guildID);
			if (guildEntity != null)
			{
				dbContext.GuildUpdates.RemoveRange(guildEntity);
			}
		}

		public static List<GuildUpdateEntity> Fetch(NpgsqlDbContext dbContext, DateTime lastFetch, long lastPosition, int amount)
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