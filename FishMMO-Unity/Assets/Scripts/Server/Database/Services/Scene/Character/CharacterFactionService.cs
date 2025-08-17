using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's factions, including saving, deleting, and loading faction data from the database.
		/// </summary>
		public class CharacterFactionService
	{
		/// <summary>
		/// Saves a character's factions to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character whose factions will be saved.</param>
		public static void Save(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IFactionController factionController))
			{
				return;
			}

			var factions = dbContext.CharacterFactions.Where(c => c.CharacterID == character.ID)
															  .ToDictionary(k => k.TemplateID);

			foreach (Faction faction in factionController.Factions.Values)
			{
				if (factions.TryGetValue(faction.Template.ID, out CharacterFactionEntity dbFaction))
				{
					dbFaction.CharacterID = character.ID;
					dbFaction.TemplateID = faction.Template.ID;
					dbFaction.Value = faction.Value;
				}
				else
				{
					dbContext.CharacterFactions.Add(new CharacterFactionEntity()
					{
						CharacterID = character.ID,
						TemplateID = faction.Template.ID,
						Value = faction.Value,
					});
				}
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes all factions for a character from the database. If keepData is false, the entries are removed.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="keepData">Whether to keep the data (currently not implemented).</param>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, bool keepData = true)
		{
			if (characterID == 0)
			{
				return;
			}

			if (!keepData)
			{
				var factions = dbContext.CharacterFactions.Where(c => c.CharacterID == characterID);
				if (factions != null)
				{
					dbContext.CharacterFactions.RemoveRange(factions);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Loads a character's factions from the database and assigns them to the character's faction controller.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load factions for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IFactionController factionController))
			{
				return;
			}
			var factions = dbContext.CharacterFactions.Where(c => c.CharacterID == character.ID);
			foreach (CharacterFactionEntity faction in factions)
			{
				factionController.SetFaction(faction.TemplateID, faction.Value, true);
			};
		}
	}
}