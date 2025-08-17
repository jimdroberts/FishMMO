using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's guild membership, including saving, updating, deleting, and loading guild data from the database.
		/// </summary>
		public class CharacterGuildService
	{
		/// <summary>
		/// Checks if a guild exists and is not full.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="guildID">The guild ID.</param>
		/// <param name="max">The maximum allowed members in the guild.</param>
		/// <returns>True if the guild exists and is not full; otherwise, false.</returns>
		public static bool ExistsNotFull(NpgsqlDbContext dbContext, long guildID, int max)
		{
			if (guildID == 0)
			{
				return false;
			}
			var guildCharacters = dbContext.CharacterGuilds.Where(a => a.GuildID == guildID);
			if (guildCharacters != null && guildCharacters.Count() <= max)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Attempts to save the rank of a character in a guild.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="guildID">The guild ID.</param>
		/// <param name="rank">The rank to assign.</param>
		/// <returns>True if the rank was saved; otherwise, false.</returns>
		public static bool TrySaveRank(NpgsqlDbContext dbContext, long characterID, long guildID, GuildRank rank)
		{
			if (characterID == 0 ||
				guildID == 0)
			{
				return false;
			}
			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.CharacterID == characterID && a.GuildID == guildID);
			if (characterGuildEntity != null)
			{
				characterGuildEntity.Rank = (byte)rank;
				dbContext.SaveChanges();

				return true;
			}
			return false;
		}

		/// <summary>
		/// Saves a character's guild membership to the database, or updates it if it already exists.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="guildID">The guild ID.</param>
		/// <param name="rank">The guild rank.</param>
		/// <param name="location">The location of the character.</param>
		public static void Save(NpgsqlDbContext dbContext, long characterID, long guildID, GuildRank rank, string location)
		{
			if (characterID == 0 ||
				guildID == 0)
			{
				return;
			}
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
		/// Saves a character's guild membership to the database using the character and an optional location override.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The character whose guild membership will be saved.</param>
		/// <param name="locationOverride">An optional location override for the character.</param>
		public static void Save(NpgsqlDbContext dbContext, ICharacter character, string locationOverride = null)
		{
			if (character == null ||
				!character.TryGet(out IGuildController guildController))
			{
				return;
			}
			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.CharacterID == character.ID);
			if (characterGuildEntity == null)
			{
				characterGuildEntity = new CharacterGuildEntity()
				{
					CharacterID = character.ID,
					GuildID = guildController.ID,
					Rank = (byte)guildController.Rank,
					Location = !string.IsNullOrEmpty(locationOverride) ? locationOverride : character.GameObject.scene.name,
				};
				dbContext.CharacterGuilds.Add(characterGuildEntity);
			}
			else
			{
				characterGuildEntity.GuildID = guildController.ID;
				characterGuildEntity.Rank = (byte)guildController.Rank;
				characterGuildEntity.Location = !string.IsNullOrEmpty(locationOverride) ? locationOverride : character.GameObject.scene.name;
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Removes a specific character from their guild.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID to remove from the guild.</param>
		public static void Delete(NpgsqlDbContext dbContext, long characterID)
		{
			if (characterID == 0)
			{
				return;
			}
			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.CharacterID == characterID);
			if (characterGuildEntity != null)
			{
				dbContext.CharacterGuilds.Remove(characterGuildEntity);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Removes a character from their guild if the kicker has a higher rank and the guild ID matches the kicker's guild ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="kickerRank">The rank of the character performing the removal.</param>
		/// <param name="guildID">The guild ID.</param>
		/// <param name="memberID">The ID of the member to remove.</param>
		/// <returns>True if the member was removed; otherwise, false.</returns>
		public static bool Delete(NpgsqlDbContext dbContext, GuildRank kickerRank, long guildID, long memberID)
		{
			if (guildID == 0 || memberID == 0)
			{
				return false;
			}

			byte rank = (byte)kickerRank;

			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.GuildID == guildID && a.CharacterID == memberID && a.Rank < rank);
			if (characterGuildEntity != null)
			{
				dbContext.CharacterGuilds.Remove(characterGuildEntity);
				dbContext.SaveChanges();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Loads a character's guild membership from the database and assigns it to the character's guild controller.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load guild data for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IGuildController guildController))
			{
				return;
			}
			var characterGuildEntity = dbContext.CharacterGuilds.FirstOrDefault(a => a.CharacterID == character.ID);
			if (characterGuildEntity != null)
			{
				guildController.ID = characterGuildEntity.GuildID;
				guildController.Rank = (GuildRank)characterGuildEntity.Rank;
			}
		}

		/// <summary>
		/// Retrieves all members of a given guild.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="guildID">The guild ID.</param>
		/// <returns>A list of guild member entities, or null if the guild ID is invalid.</returns>
		public static List<CharacterGuildEntity> Members(NpgsqlDbContext dbContext, long guildID)
		{
			if (guildID == 0)
			{
				return null;
			}
			return dbContext.CharacterGuilds.Where(a => a.GuildID == guildID).ToList();
		}
	}
}