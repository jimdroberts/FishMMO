using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.DatabaseServices
{
	public class PartyUpdateService
	{
		public static void Save(NpgsqlDbContext dbContext, long partyID)
		{
			if (partyID == 0)
			{
				return;
			}
			dbContext.PartyUpdates.Add(new PartyUpdateEntity()
			{
				PartyID = partyID,
				TimeCreated = DateTime.UtcNow,
			});
			dbContext.SaveChanges();
		}

		public static void Delete(NpgsqlDbContext dbContext, long partyID)
		{
			if (partyID == 0)
			{
				return;
			}
			var partyEntity = dbContext.PartyUpdates.Where(a => a.PartyID == partyID);
			if (partyEntity != null)
			{
				dbContext.PartyUpdates.RemoveRange(partyEntity);
				dbContext.SaveChanges();
			}
		}

		public static List<PartyUpdateEntity> Fetch(NpgsqlDbContext dbContext, DateTime lastFetch, long lastPosition, int amount)
		{
			var nextPage = dbContext.PartyUpdates
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