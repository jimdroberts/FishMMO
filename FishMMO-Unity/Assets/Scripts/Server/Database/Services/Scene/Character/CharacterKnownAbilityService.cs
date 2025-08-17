using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's known abilities, including adding, saving, deleting, and loading abilities from the database.
		/// </summary>
		public class CharacterKnownAbilityService
	{
		/// <summary>
		/// Adds a known ability for a character to the database using the ability template ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="templateID">The ability template ID.</param>
		public static void Add(NpgsqlDbContext dbContext, long characterID, int templateID)
		{
			if (characterID == 0)
			{
				return;
			}
			var dbKnownAbility = dbContext.CharacterKnownAbilities.FirstOrDefault(c => c.CharacterID == characterID && c.TemplateID == templateID);
			// add to known abilities
			if (dbKnownAbility == null)
			{
				dbKnownAbility = new CharacterKnownAbilityEntity()
				{
					CharacterID = characterID,
					TemplateID = templateID,
				};
				dbContext.CharacterKnownAbilities.Add(dbKnownAbility);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Saves a character's known abilities to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character whose abilities will be saved.</param>
		public static void Save(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IAbilityController abilityController))
			{
				return;
			}

			var dbKnownAbilities = dbContext.CharacterKnownAbilities.Where(c => c.CharacterID == character.ID)
																	.ToDictionary(k => k.TemplateID);

			// save base abilities
			foreach (int abilityTemplate in abilityController.KnownBaseAbilities)
			{
				if (!dbKnownAbilities.ContainsKey(abilityTemplate))
				{
					dbContext.CharacterKnownAbilities.Add(new CharacterKnownAbilityEntity()
					{
						CharacterID = character.ID,
						TemplateID = abilityTemplate,
					});
				}
			}

			// save event types
			foreach (int abilityTemplate in abilityController.KnownAbilityEvents)
			{
				if (!dbKnownAbilities.ContainsKey(abilityTemplate))
				{
					dbContext.CharacterKnownAbilities.Add(new CharacterKnownAbilityEntity()
					{
						CharacterID = character.ID,
						TemplateID = abilityTemplate,
					});
				}
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes all known abilities for a character from the database. If keepData is false, the entries are removed.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="keepData">Whether to keep the data (currently not implemented).</param>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, bool keepData = false)
		{
			if (characterID == 0)
			{
				return;
			}
			if (!keepData)
			{
				var dbKnownAbilities = dbContext.CharacterKnownAbilities.Where(c => c.CharacterID == characterID);
				if (dbKnownAbilities != null)
				{
					dbContext.CharacterKnownAbilities.RemoveRange(dbKnownAbilities);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Deletes a specific known ability for a character from the database. If keepData is false, the entry is removed.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="templateID">The ability template ID.</param>
		/// <param name="keepData">Whether to keep the data (currently not implemented).</param>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, long templateID, bool keepData = false)
		{
			if (characterID == 0)
			{
				return;
			}
			if (!keepData)
			{
				var dbKnownAbility = dbContext.CharacterKnownAbilities.FirstOrDefault(c => c.CharacterID == characterID && c.TemplateID == templateID);
				if (dbKnownAbility != null)
				{
					dbContext.CharacterKnownAbilities.Remove(dbKnownAbility);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Loads a character's known abilities from the database and assigns them to the character's ability controller.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load abilities for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IAbilityController abilityController))
			{
				return;
			}

			var dbKnownAbilities = dbContext.CharacterKnownAbilities.Where(c => c.CharacterID == character.ID);

			List<BaseAbilityTemplate> templates = new List<BaseAbilityTemplate>();
			List<AbilityEvent> abilityEvents = new List<AbilityEvent>();

			foreach (CharacterKnownAbilityEntity dbKnownAbility in dbKnownAbilities)
			{
				BaseAbilityTemplate template = BaseAbilityTemplate.Get<BaseAbilityTemplate>(dbKnownAbility.TemplateID);
				if (template != null)
				{
					templates.Add(template);
				}
				else
				{
					AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(dbKnownAbility.TemplateID);
					if (abilityEvent != null)
					{
						abilityEvents.Add(abilityEvent);
					}
				}
			}

			abilityController.LearnBaseAbilities(templates);
			abilityController.LearnAbilityEvents(abilityEvents);
		}
	}
}