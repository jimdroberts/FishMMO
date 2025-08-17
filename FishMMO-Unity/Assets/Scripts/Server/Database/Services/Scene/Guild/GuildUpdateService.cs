using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using UnityEngine;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing guild update timestamps, including saving, deleting, and fetching update records from the database.
		/// </summary>
		public class GuildUpdateService
	{
		/// <summary>
		/// Saves or updates the last update timestamp for a guild. If the record does not exist, it is created.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="guildID">The guild ID to update.</param>
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

		/// <summary>
		/// Deletes all update records for a specific guild from the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="guildID">The guild ID whose update records will be deleted.</param>
		public static void Delete(NpgsqlDbContext dbContext, long guildID)
		{
			var guildEntity = dbContext.GuildUpdates.Where(a => a.GuildID == guildID);
			if (guildEntity != null)
			{
				dbContext.GuildUpdates.RemoveRange(guildEntity);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Fetches all guild update records for the specified guild IDs that have been updated since the given timestamp.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="guildIDs">A list of guild IDs to fetch updates for.</param>
		/// <param name="lastFetch">The timestamp to compare updates against.</param>
		/// <returns>A list of guild update entities updated since the last fetch.</returns>
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