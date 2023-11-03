using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterPartyService
	{
		public static bool ExistsNotFull(NpgsqlDbContext dbContext, long partyID, int max)
		{
			var partyCharacters = dbContext.CharacterParties.Where(a => a.PartyID == partyID);
			if (partyCharacters != null && partyCharacters.Count() <= max)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Saves a CharacterPartyEntity to the database.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, long characterID, long partyID, PartyRank rank, float healthPCT)
		{
			var characterPartyEntity = dbContext.CharacterParties.FirstOrDefault(a => a.CharacterID == characterID);
			if (characterPartyEntity == null)
			{
				characterPartyEntity = new CharacterPartyEntity()
				{
					CharacterID = characterID,
					PartyID = partyID,
					Rank = (byte)rank,
					HealthPCT = healthPCT,
				};
				dbContext.CharacterParties.Add(characterPartyEntity);
			}
			else
			{
				characterPartyEntity.PartyID = partyID;
				characterPartyEntity.Rank = (byte)rank;
				characterPartyEntity.HealthPCT = healthPCT;
			}
		}

		/// <summary>
		/// Removes a character from their party.
		/// </summary>
		public static bool Delete(NpgsqlDbContext dbContext, PartyRank kickerRank, long partyID, long memberID)
		{
			var characterPartyEntity = dbContext.CharacterParties.FirstOrDefault(a => a.PartyID == partyID && a.CharacterID == memberID && a.Rank < (byte)kickerRank);
			if (characterPartyEntity != null)
			{
				dbContext.CharacterParties.Remove(characterPartyEntity);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Removes a character from their party.
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long memberID)
		{
			var characterPartyEntity = dbContext.CharacterParties.FirstOrDefault(a => a.CharacterID == memberID);
			if (characterPartyEntity != null)
			{
				dbContext.CharacterParties.Remove(characterPartyEntity);
			}
		}

		/// <summary>
		/// Load a CharacterPartyEntity from the database.
		/// </summary>
		public static void Load(NpgsqlDbContext dbContext, Character character)
		{
			if (character.PartyController != null)
			{
				var characterPartyEntity = dbContext.CharacterParties.FirstOrDefault(a => a.CharacterID == character.ID);
				if (characterPartyEntity != null)
				{
					character.PartyController.ID = characterPartyEntity.PartyID;
					character.PartyController.Rank = (PartyRank)characterPartyEntity.Rank;
				}
			}
		}

		public static List<CharacterPartyEntity> Members(NpgsqlDbContext dbContext, long partyID)
		{
			return dbContext.CharacterParties.Where(a => a.PartyID == partyID).ToList();
		}
	}
}