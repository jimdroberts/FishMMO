using System.Collections.Generic;
using System.Linq;
using FishMMO.Database;
using FishMMO.Database.Entities;

namespace FishMMO.Server.Services
{
	public class PartyService
	{
		public static bool Exists(ServerDbContext dbContext, long partyID)
		{
			return dbContext.Parties.FirstOrDefault(a => a.ID == partyID) != null;
		}

		/// <summary>
		/// Saves a new Party to the database.
		/// </summary>
		public static bool TryCreate(ServerDbContext dbContext, out PartyEntity party)
		{
			party = new PartyEntity()
			{
				Characters = new List<CharacterPartyEntity>(),
			};
			dbContext.Parties.Add(party);
			dbContext.SaveChanges();
			return true;
		}

		public static void Delete(ServerDbContext dbContext, long partyID)
		{
			var partyEntity = dbContext.Parties.FirstOrDefault(a => a.ID == partyID);
			if (partyEntity != null)
			{
				dbContext.Parties.Remove(partyEntity);
			}
		}

		/// <summary>
		/// Load a Party from the database.
		/// </summary>
		public static PartyEntity Load(ServerDbContext dbContext, long partyID)
		{
			return dbContext.Parties.FirstOrDefault(a => a.ID == partyID);
		}
	}
}