using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterFactionService
	{
		/// <summary>
		/// Save a character Factions to the database.
		/// </summary>
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
		/// KeepData is automatically true... This means we don't actually delete anything. TODO Deleted is simply set to true just incase we need to reinstate a character..
		/// </summary>
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
		/// Load character Factions from the database.
		/// </summary>
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