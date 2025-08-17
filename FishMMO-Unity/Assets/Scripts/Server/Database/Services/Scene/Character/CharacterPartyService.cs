using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing character party data, including creation, updates, deletion, and retrieval of party members.
		/// </summary>
		public class CharacterPartyService
	{
		/// <summary>
		/// Checks if a party exists and is not full.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="partyID">The party ID.</param>
		/// <param name="max">The maximum allowed members in the party.</param>
		/// <returns>True if the party exists and is not full; otherwise, false.</returns>
		public static bool ExistsNotFull(NpgsqlDbContext dbContext, long partyID, int max)
		{
			if (partyID == 0)
			{
				return false;
			}
			var partyCharacters = dbContext.CharacterParties.Where(a => a.PartyID == partyID);
			if (partyCharacters != null && partyCharacters.Count() <= max)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Attempts to save the rank of a character in a party.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="partyID">The party ID.</param>
		/// <param name="rank">The rank to assign.</param>
		/// <returns>True if the rank was saved; otherwise, false.</returns>
		public static bool TrySaveRank(NpgsqlDbContext dbContext, long characterID, long partyID, PartyRank rank)
		{
			if (partyID == 0)
			{
				return false;
			}
			var characterPartyEntity = dbContext.CharacterParties.FirstOrDefault(a => a.CharacterID == characterID && a.PartyID == partyID);
			if (characterPartyEntity != null)
			{
				characterPartyEntity.Rank = (byte)rank;
				dbContext.SaveChanges();

				return true;
			}
			return false;
		}

		/// <summary>
		/// Saves a character's party entity to the database, or updates it if it already exists.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="partyID">The party ID.</param>
		/// <param name="rank">The party rank.</param>
		/// <param name="healthPCT">The health percentage of the character.</param>
		public static void Save(NpgsqlDbContext dbContext, long characterID, long partyID, PartyRank rank, float healthPCT)
		{
			if (partyID == 0)
			{
				return;
			}
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
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Removes a character from their party if the kicker has a higher rank.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="kickerRank">The rank of the character performing the removal.</param>
		/// <param name="partyID">The party ID.</param>
		/// <param name="memberID">The ID of the member to remove.</param>
		/// <returns>True if the member was removed; otherwise, false.</returns>
		public static bool Delete(NpgsqlDbContext dbContext, PartyRank kickerRank, long partyID, long memberID)
		{
			if (partyID == 0)
			{
				return false;
			}
			
			byte rank = (byte)kickerRank;

			var characterPartyEntity = dbContext.CharacterParties.FirstOrDefault(a => a.PartyID == partyID && a.CharacterID == memberID && a.Rank < rank);
			if (characterPartyEntity != null)
			{
				dbContext.CharacterParties.Remove(characterPartyEntity);
				dbContext.SaveChanges();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Removes a character from their party by member ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="memberID">The ID of the member to remove.</param>
		public static void Delete(NpgsqlDbContext dbContext, long memberID)
		{
			if (memberID == 0)
			{
				return;
			}
			var characterPartyEntity = dbContext.CharacterParties.FirstOrDefault(a => a.CharacterID == memberID);
			if (characterPartyEntity != null)
			{
				dbContext.CharacterParties.Remove(characterPartyEntity);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Loads a character's party data from the database and assigns it to the character's party controller.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load party data for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IPartyController partyController))
			{
				return;
			}
			var characterPartyEntity = dbContext.CharacterParties.FirstOrDefault(a => a.CharacterID == character.ID);
			if (characterPartyEntity != null)
			{
				partyController.ID = characterPartyEntity.PartyID;
				partyController.Rank = (PartyRank)characterPartyEntity.Rank;
			}
		}

		/// <summary>
		/// Retrieves all members of a given party.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="partyID">The party ID.</param>
		/// <returns>A list of party member entities, or null if the party ID is invalid.</returns>
		public static List<CharacterPartyEntity> Members(NpgsqlDbContext dbContext, long partyID)
		{
			if (partyID == 0)
			{
				return null;
			}
			return dbContext.CharacterParties.Where(a => a.PartyID == partyID).ToList();
		}
	}
}