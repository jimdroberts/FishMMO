using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing guilds, including creation, deletion, and retrieval of guild data from the database.
		/// </summary>
		public class GuildService
	{
		/// <summary>
		/// Checks if a guild with the specified name exists in the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="name">The name of the guild.</param>
		/// <returns>True if the guild exists; otherwise, false.</returns>
		public static bool Exists(NpgsqlDbContext dbContext, string name)
		{
			return dbContext.Guilds.FirstOrDefault(a => a.Name.ToUpper() == name.ToUpper()) != null;
		}

		/// <summary>
		/// Gets the name of a guild by its ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="guildID">The guild ID.</param>
		/// <returns>The name of the guild if found; otherwise, an empty string.</returns>
		public static string GetNameByID(NpgsqlDbContext dbContext, long guildID)
		{
			if (guildID == 0)
			{
				return "";
			}
			var guild = dbContext.Guilds.FirstOrDefault(a => a.ID == guildID);
			if (guild == null)
			{
				return "";
			}
			return guild.Name;
		}

		/// <summary>
		/// Saves a guild to the database if it doesn't exist.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="name">The name of the guild to create.</param>
		/// <param name="guild">The created or found guild entity.</param>
		/// <returns>True if the guild was created; otherwise, false.</returns>
		public static bool TryCreate(NpgsqlDbContext dbContext, string name, out GuildEntity guild)
		{
			guild = dbContext.Guilds.FirstOrDefault(a => a.Name.ToUpper() == name.ToUpper());
			if (guild == null)
			{
				guild = new GuildEntity()
				{
					Name = name,
					Notice = "",
					Characters = new List<CharacterGuildEntity>(),
				};
				dbContext.Guilds.Add(guild);
				dbContext.SaveChanges();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Deletes a guild from the database by its ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="guildID">The guild ID to delete.</param>
		public static void Delete(NpgsqlDbContext dbContext, long guildID)
		{
			if (guildID == 0)
			{
				return;
			}
			var guildEntity = dbContext.Guilds.FirstOrDefault(a => a.ID == guildID);
			if (guildEntity != null)
			{
				dbContext.Guilds.Remove(guildEntity);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Loads a guild from the database by its name.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="name">The name of the guild to load.</param>
		/// <returns>The loaded guild entity if found; otherwise, null.</returns>
		public static GuildEntity Load(NpgsqlDbContext dbContext, string name)
		{
			return dbContext.Guilds.FirstOrDefault(a => a.Name.ToUpper() == name.ToUpper());
		}

		/// <summary>
		/// Loads a guild from the database by its ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="id">The ID of the guild to load.</param>
		/// <returns>The loaded guild entity if found; otherwise, null.</returns>
		public static GuildEntity Load(NpgsqlDbContext dbContext, long id)
		{
			if (id == 0)
			{
				return null;
			}
			return dbContext.Guilds.FirstOrDefault(a => a.ID == id);
		}
	}
}