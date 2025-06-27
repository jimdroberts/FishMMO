using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using UnityEngine;

namespace FishMMO.Server.DatabaseServices
{
	public class GuildUpdateService
	{
		public static void Save(NpgsqlDbContext dbContext, long guildID)
		{
			if (guildID == 0)
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
					var existingUpdate = dbContext.GuildUpdates
						.FirstOrDefault(a => a.GuildID == guildID);

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
						dbContext.GuildUpdates.Add(new GuildUpdateEntity
						{
							GuildID = guildID,
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

		public static void Delete(NpgsqlDbContext dbContext, long guildID)
		{
			var guildEntity = dbContext.GuildUpdates.Where(a => a.GuildID == guildID);
			if (guildEntity != null)
			{
				dbContext.GuildUpdates.RemoveRange(guildEntity);
				dbContext.SaveChanges();
			}
		}

		public static List<GuildUpdateEntity> Fetch(NpgsqlDbContext dbContext, List<long> guildIDs, DateTime lastFetch)
		{
			if (guildIDs == null || guildIDs.Count < 1)
			{
				return new List<GuildUpdateEntity>();  // Return an empty list instead of null
			}

			// Format the lastFetch time as "yyyy-MM-dd HH:mm:ss.fffffff"
			//string formattedLastFetch = lastFetch.ToString("yyyy-MM-dd HH:mm:ss.fffffff");

			// Log the guild IDs being used in the query
			//Log.Debug($"Fetching updates for guilds: {string.Join(", ", guildIDs)} at {formattedLastFetch}");

			var result = dbContext.GuildUpdates
				.Where(b => b.LastUpdate >= lastFetch && guildIDs.Contains(b.GuildID)) // Use IN under the hood
				.ToList();

			// Log the number of updates found
			//Log.Debug($"Found {result.Count} updates for the given guilds.");

			return result;
		}
	}
}