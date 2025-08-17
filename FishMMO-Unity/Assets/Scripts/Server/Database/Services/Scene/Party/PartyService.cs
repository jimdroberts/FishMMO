using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing parties, including creation, deletion, and retrieval of party data from the database.
		/// </summary>
		public class PartyService
	{
		/// <summary>
		/// Checks if a party with the specified ID exists in the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="partyID">The party ID.</param>
		/// <returns>True if the party exists; otherwise, false.</returns>
		public static bool Exists(NpgsqlDbContext dbContext, long partyID)
		{
			if (partyID == 0)
			{
				return false;
			}
			return dbContext.Parties.FirstOrDefault(a => a.ID == partyID) != null;
		}

		/// <summary>
		/// Saves a new party to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="party">The created party entity.</param>
		/// <returns>True if the party was created; otherwise, false.</returns>
		public static bool TryCreate(NpgsqlDbContext dbContext, out PartyEntity party)
		{
			party = new PartyEntity()
			{
				Characters = new List<CharacterPartyEntity>(),
			};
			dbContext.Parties.Add(party);
			dbContext.SaveChanges();
			return true;
		}

		/// <summary>
		/// Deletes a party from the database by its ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="partyID">The party ID to delete.</param>
		public static void Delete(NpgsqlDbContext dbContext, long partyID)
		{
			if (partyID == 0)
			{
				return;
			}
			var partyEntity = dbContext.Parties.FirstOrDefault(a => a.ID == partyID);
			if (partyEntity != null)
			{
				dbContext.Parties.Remove(partyEntity);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Loads a party from the database by its ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="partyID">The party ID to load.</param>
		/// <returns>The loaded party entity if found; otherwise, null.</returns>
		public static PartyEntity Load(NpgsqlDbContext dbContext, long partyID)
		{
			if (partyID == 0)
			{
				return null;
			}
			return dbContext.Parties.FirstOrDefault(a => a.ID == partyID);
		}
	}
}