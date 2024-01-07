using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterGuildService
	{
		public static bool ExistsNotFull(NpgsqlDbContext dbContext, long guildID, int max)
		{
			var guildCharacters = dbContext.CharacterGuilds.Where(a => a.GuildID == guildID);
			if (guildCharacters != null && guildCharacters.Count() <= max)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Saves a CharacterGuildEntity to the database.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, long characterID, long guildID, GuildRank rank, string location)
		{
			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.CharacterID == characterID);
			if (characterGuildEntity == null)
			{
				characterGuildEntity = new CharacterGuildEntity()
				{
					CharacterID = characterID,
					GuildID = guildID,
					Rank = (byte)rank,
					Location = location,
				};
				dbContext.CharacterGuilds.Add(characterGuildEntity);
			}
			else
			{
				characterGuildEntity.GuildID = guildID;
				characterGuildEntity.Rank = (byte)rank;
				characterGuildEntity.Location = location;
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Saves a CharacterGuildEntity to the database.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, Character character)
		{
			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.CharacterID == character.ID);
			if (characterGuildEntity == null)
			{
				characterGuildEntity = new CharacterGuildEntity()
				{
					CharacterID = character.ID,
					GuildID = character.GuildController.ID,
					Rank = (byte)character.GuildController.Rank,
					Location = character.gameObject.scene.name,
				};
				dbContext.CharacterGuilds.Add(characterGuildEntity);
			}
			else
			{
				characterGuildEntity.GuildID = character.GuildController.ID;
				characterGuildEntity.Rank = (byte)character.GuildController.Rank;
				characterGuildEntity.Location = character.gameObject.scene.name;
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Removes a specific character from their guild.
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long characterID)
		{
			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.CharacterID == characterID);
			if (characterGuildEntity != null)
			{
				dbContext.CharacterGuilds.Remove(characterGuildEntity);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Removes a character from their guild if they have a higher rank and the guild id matches the kickers guild id.
		/// </summary>
		public static bool Delete(NpgsqlDbContext dbContext, GuildRank kickerRank, long guildID, long memberID)
		{
			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.GuildID == guildID && a.CharacterID == memberID && a.Rank < (byte)kickerRank);
			if (characterGuildEntity != null)
			{
				dbContext.CharacterGuilds.Remove(characterGuildEntity);
				dbContext.SaveChanges();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Load a CharacterGuildEntity from the database.
		/// </summary>
		public static void Load(NpgsqlDbContext dbContext, Character character)
		{
			if (character.GuildController != null)
			{
				var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.CharacterID == character.ID);
				if (characterGuildEntity != null)
				{
					character.GuildController.ID = characterGuildEntity.GuildID;
					character.GuildController.Rank = (GuildRank)characterGuildEntity.Rank;
				}
			}
		}

		public static List<CharacterGuildEntity> Members(NpgsqlDbContext dbContext, long guildID)
		{
			return dbContext.CharacterGuilds.Where(a => a.GuildID == guildID).ToList();
		}
	}
}