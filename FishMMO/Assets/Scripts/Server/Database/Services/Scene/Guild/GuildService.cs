using System.Collections.Generic;
using System.Linq;
using FishMMO_DB;
using FishMMO_DB.Entities;

namespace FishMMO.Server.Services
{
	public class GuildService
	{
		public static bool Exists(ServerDbContext dbContext, string name)
		{
			return dbContext.Guilds.FirstOrDefault(a => a.Name.ToUpper() == name.ToUpper()) != null;
		}

		/// <summary>
		/// Saves a Guild to the database if it doesn't exist.
		/// </summary>
		public static bool TryCreate(ServerDbContext dbContext, string name, out GuildEntity guild)
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
		/// KeepData is automatically true... This means we don't actually delete anything. Deleted is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(ServerDbContext dbContext, long characterID, bool keepData = true)
		{
			/*if (!keepData)
			{
				var achievements = dbContext.CharacterAchievements.Where(c => c.CharacterID == characterID);
				dbContext.CharacterAchievements.RemoveRange(achievements);
			}*/
		}

		/// <summary>
		/// Load a Guild from the database.
		/// </summary>
		public static GuildEntity Load(ServerDbContext dbContext, string name)
		{
			return dbContext.Guilds.FirstOrDefault(a => a.Name.ToUpper() == name.ToUpper());
		}

		/// <summary>
		/// Load a Guild from the database.
		/// </summary>
		public static GuildEntity Load(ServerDbContext dbContext, long id)
		{
			return dbContext.Guilds.FirstOrDefault(a => a.ID == id);
		}
	}
}