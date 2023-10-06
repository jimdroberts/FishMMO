using System.Collections.Generic;
using System.Linq;
using FishMMO_DB;
using FishMMO_DB.Entities;

namespace FishMMO.Server.Services
{
	public class CharacterPartyService
	{
		public static bool ExistsNotFull(ServerDbContext dbContext, long partyID, int max)
		{
			var partyCharacters = dbContext.CharacterParties.Where(a => a.PartyID == partyID);
			if (partyCharacters != null && partyCharacters.Count() < max)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Saves a CharacterPartyEntity to the database.
		/// </summary>
		public static void Save(ServerDbContext dbContext, long characterID, long partyID, PartyRank rank, string location)
		{
			var characterPartyEntity = dbContext.CharacterParties.FirstOrDefault(a => a.CharacterID == characterID);
			if (characterPartyEntity == null)
			{
				characterPartyEntity = new CharacterPartyEntity()
				{
					CharacterID = characterID,
					PartyID = partyID,
					Rank = (byte)rank,
					Location = location,
				};
				dbContext.CharacterParties.Add(characterPartyEntity);
			}
			else
			{
				characterPartyEntity.PartyID = partyID;
				characterPartyEntity.Rank = (byte)rank;
				characterPartyEntity.Location = location;
			}
		}

		/// <summary>
		/// Saves a CharacterPartyEntity to the database.
		/// </summary>
		public static void Save(ServerDbContext dbContext, Character character)
		{
			var characterPartyEntity = dbContext.CharacterParties.FirstOrDefault(a => a.CharacterID == character.ID);
			if (characterPartyEntity == null)
			{
				characterPartyEntity = new CharacterPartyEntity()
				{
					CharacterID = character.ID,
					PartyID = character.PartyController.ID,
					Rank = (byte)character.PartyController.Rank,
					Location = character.gameObject.scene.name,
				};
				dbContext.CharacterParties.Add(characterPartyEntity);
			}
			else
			{
				characterPartyEntity.PartyID = character.PartyController.ID;
				characterPartyEntity.Rank = (byte)character.PartyController.Rank;
				characterPartyEntity.Location = character.gameObject.scene.name;
			}
		}

		/// <summary>
		/// Removes a character from their party.
		/// </summary>
		public static bool Delete(ServerDbContext dbContext, PartyRank kickerRank, long partyID, long memberID)
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
		public static bool Delete(ServerDbContext dbContext, long partyID, long memberID)
		{
			var characterPartyEntity = dbContext.CharacterParties.FirstOrDefault(a => a.PartyID == partyID && a.CharacterID == memberID);
			if (characterPartyEntity != null)
			{
				dbContext.CharacterParties.Remove(characterPartyEntity);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Load a CharacterPartyEntity from the database.
		/// </summary>
		public static CharacterPartyEntity Load(ServerDbContext dbContext, Character character)
		{
			return dbContext.CharacterParties.FirstOrDefault(a => a.CharacterID == character.ID);
		}

		public static List<CharacterPartyEntity> Members(ServerDbContext dbContext, long partyID)
		{
			return dbContext.CharacterParties.Where(a => a.PartyID == partyID).ToList();
		}
	}
}