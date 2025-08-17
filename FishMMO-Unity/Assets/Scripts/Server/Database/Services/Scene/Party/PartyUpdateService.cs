using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing party update timestamps, including saving, deleting, and fetching update records from the database.
		/// </summary>
		public class PartyUpdateService
	{
		/// <summary>
		/// Saves or updates the last update timestamp for a party. If the record does not exist, it is created.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="partyID">The party ID to update.</param>
		public static void Save(NpgsqlDbContext dbContext, long partyID)
		{
			if (partyID == 0)
			{
				return;
			}

			var currentTime = DateTime.UtcNow;

			// Start a transaction to ensure that the entire operation is atomic.
			using (var transaction = dbContext.Database.BeginTransaction())
			{
				try
				{
					// Query for the existing guild update entry.
					var existingUpdate = dbContext.PartyUpdates
						.FirstOrDefault(a => a.PartyID == partyID);

					if (existingUpdate != null)
					{
						// If the existing time is less than the current time, update it
						if (existingUpdate.LastUpdate < currentTime)
						{
							existingUpdate.LastUpdate = currentTime;
							dbContext.SaveChanges();
						}
					}
					else
					{
						// Row does not exist, insert a new entry
						dbContext.PartyUpdates.Add(new PartyUpdateEntity
						{
							PartyID = partyID,
							LastUpdate = currentTime
						});
						dbContext.SaveChanges();
					}

					// Commit the transaction
					transaction.Commit();
				}
				catch (Exception)
				{
					// If any error occurs, rollback the transaction
					transaction.Rollback();
					throw;
				}
			}
		}

		/// <summary>
		/// Deletes all update records for a specific party from the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="partyID">The party ID whose update records will be deleted.</param>
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

		/// <summary>
		/// Fetches all party update records for the specified party IDs that have been updated since the given timestamp.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="partyIDs">A list of party IDs to fetch updates for.</param>
		/// <param name="lastFetch">The timestamp to compare updates against.</param>
		/// <returns>A list of party update entities updated since the last fetch.</returns>
		public static List<PartyUpdateEntity> Fetch(NpgsqlDbContext dbContext, List<long> partyIDs, DateTime lastFetch)
		{
			if (partyIDs == null || partyIDs.Count < 1)
			{
				return new List<PartyUpdateEntity>();  // Return an empty list instead of null
			}

			// Format the lastFetch time as "yyyy-MM-dd HH:mm:ss.fffffff"
			//string formattedLastFetch = lastFetch.ToString("yyyy-MM-dd HH:mm:ss.fffffff");

			// Log the guild IDs being used in the query
			//Log.Debug($"Fetching updates for Party: {string.Join(", ", partyIDs)} at {formattedLastFetch}");

			var result = dbContext.PartyUpdates
				.Where(b => b.LastUpdate >= lastFetch && partyIDs.Contains(b.PartyID)) // Use IN under the hood
				.ToList();

			// Log the number of updates found
			//Log.Debug($"Found {result.Count} updates for the given party.");

			return result;
		}
	}
}