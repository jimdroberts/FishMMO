using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing kick requests, including saving, deleting, and fetching kick request data from the database.
		/// </summary>
		public class KickRequestService
	{
		/// <summary>
		/// Saves a kick request for the specified account to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="accountName">The account name to kick.</param>
		public static void Save(NpgsqlDbContext dbContext, string accountName)
		{
			dbContext.KickRequests.Add(new KickRequestEntity()
			{
				AccountName = accountName,
				TimeCreated = DateTime.UtcNow,
			});
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes all kick requests for the specified account from the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="accountName">The account name whose kick requests will be deleted.</param>
		public static void Delete(NpgsqlDbContext dbContext, string accountName)
		{
			var kickRequest = dbContext.KickRequests.Where(a => a.AccountName == accountName);
			if (kickRequest != null)
			{
				dbContext.KickRequests.RemoveRange(kickRequest);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Fetches kick requests from the database based on the last fetch time, last position, and amount.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="lastFetch">The timestamp to compare requests against.</param>
		/// <param name="lastPosition">The last request ID fetched.</param>
		/// <param name="amount">The maximum number of requests to fetch.</param>
		/// <returns>A list of kick request entities matching the criteria.</returns>
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
