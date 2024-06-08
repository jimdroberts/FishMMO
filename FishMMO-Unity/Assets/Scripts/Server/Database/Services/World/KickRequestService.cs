using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.DatabaseServices
{
	public class KickRequestService
	{
		public static void Save(NpgsqlDbContext dbContext, string accountName)
		{
			dbContext.KickRequests.Add(new KickRequestEntity()
			{
				AccountName = accountName,
				TimeCreated = DateTime.UtcNow,
			});
			dbContext.SaveChanges();
		}

		public static void Delete(NpgsqlDbContext dbContext, string accountName)
		{
			var kickRequest = dbContext.KickRequests.Where(a => a.AccountName == accountName);
			if (kickRequest != null)
			{
				dbContext.KickRequests.RemoveRange(kickRequest);
				dbContext.SaveChanges();
			}
		}

		public static List<KickRequestEntity> Fetch(NpgsqlDbContext dbContext, DateTime lastFetch, long lastPosition, int amount)
		{
			var nextPage = dbContext.KickRequests
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
