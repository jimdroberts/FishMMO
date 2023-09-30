using System.Collections.Generic;
using System.Linq;
using FishMMO_DB;
using FishMMO_DB.Entities;

namespace FishMMO.Server.Services
{
	public class CharacterGuildService
	{
		public static bool ExistsNotFull(ServerDbContext dbContext, long guildID, int max)
		{
			var guildCharacters = dbContext.CharacterGuilds.Where(a => a.GuildID == guildID);
			if (guildCharacters != null && guildCharacters.Count() < max)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Saves a CharacterGuildEntity to the database.
		/// </summary>
		public static bool Save(ServerDbContext dbContext, long characterID, long guildID, GuildRank rank)
		{
			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.CharacterID == characterID);
			if (characterGuildEntity == null)
			{
				characterGuildEntity = new CharacterGuildEntity()
				{
					CharacterID = characterID,
					GuildID = guildID,
					Rank = (byte)rank,
				};
				dbContext.CharacterGuilds.Add(characterGuildEntity);
				return true;
			}
			else
			{
				characterGuildEntity.CharacterID = characterID;
				characterGuildEntity.GuildID = guildID;
				characterGuildEntity.Rank = (byte)rank;
			}
			return false;
		}

		/// <summary>
		/// Saves a CharacterGuildEntity to the database.
		/// </summary>
		public static bool Save(ServerDbContext dbContext, Character character)
		{
			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.CharacterID == character.ID);
			if (characterGuildEntity == null)
			{
				characterGuildEntity = new CharacterGuildEntity()
				{
					CharacterID = character.ID,
					GuildID = character.GuildController.ID,
					Rank = (byte)character.GuildController.Rank,
				};
				dbContext.CharacterGuilds.Add(characterGuildEntity);
				return true;
			}
			else
			{
				characterGuildEntity.CharacterID = character.ID;
				characterGuildEntity.GuildID = character.GuildController.ID;
				characterGuildEntity.Rank = (byte)character.GuildController.Rank;
			}
			return false;
		}

		/// <summary>
		/// Removes a character from their guild.
		/// </summary>
		public static bool Delete(ServerDbContext dbContext, GuildRank kickerRank, long guildID, long memberID, bool keepData = true)
		{
			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.GuildID == guildID && a.CharacterID == memberID && a.Rank < (byte)kickerRank);
			if (characterGuildEntity != null)
			{
				if (!keepData)
				{
					dbContext.CharacterGuilds.Remove(characterGuildEntity);
				}
				else
				{
					characterGuildEntity.GuildID = 0;
					characterGuildEntity.Rank = (byte)GuildRank.None;
				}
				return true;
			}
			return false;
		}

		/// <summary>
		/// Load a CharacterGuildEntity from the database.
		/// </summary>
		public static CharacterGuildEntity Load(ServerDbContext dbContext, Character character)
		{
			return dbContext.CharacterGuilds.FirstOrDefault(a => a.CharacterID == character.ID);
		}

		public static List<CharacterGuildEntity> Members(ServerDbContext dbContext, long guildID)
		{
			return dbContext.CharacterGuilds.Where(a => a.GuildID == guildID).ToList();
		}
	}
}