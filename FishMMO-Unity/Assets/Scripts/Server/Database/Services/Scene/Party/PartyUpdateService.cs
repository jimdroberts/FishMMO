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

		public static List<PartyUpdateEntity> Fetch(NpgsqlDbContext dbContext, List<long> partyIDs, DateTime lastFetch)
		{
			if (partyIDs == null || partyIDs.Count < 1)
			{
				return new List<PartyUpdateEntity>();  // Return an empty list instead of null
			}

			// Format the lastFetch time as "yyyy-MM-dd HH:mm:ss.fffffff"
			//string formattedLastFetch = lastFetch.ToString("yyyy-MM-dd HH:mm:ss.fffffff");

			// Log the guild IDs being used in the query
			//Debug.Log($"Fetching updates for Party: {string.Join(", ", partyIDs)} at {formattedLastFetch}");

			var result = dbContext.PartyUpdates
				.Where(b => b.LastUpdate >= lastFetch && partyIDs.Contains(b.PartyID)) // Use IN under the hood
				.ToList();

			// Log the number of updates found
			//Debug.Log($"Found {result.Count} updates for the given party.");

			return result;
		}
	}
}