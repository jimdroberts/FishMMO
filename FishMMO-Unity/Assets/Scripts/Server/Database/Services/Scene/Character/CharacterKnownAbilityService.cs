using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterKnownAbilityService
	{
		/// <summary>
		/// Adds a known ability for a character to the database using the Ability Template ID.
		/// </summary>
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
		/// Save a characters known abilities to the database.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, Character character)
		{
			if (character == null)
			{
				return;
			}

			var dbKnownAbilities = dbContext.CharacterKnownAbilities.Where(c => c.CharacterID == character.ID.Value)
																	.ToDictionary(k => k.TemplateID);

			// save base abilities
			foreach (int abilityTemplate in character.AbilityController.KnownBaseAbilities)
			{
				if (!dbKnownAbilities.ContainsKey(abilityTemplate))
				{
					dbContext.CharacterKnownAbilities.Add(new CharacterKnownAbilityEntity()
					{
						CharacterID = character.ID.Value,
						TemplateID = abilityTemplate,
					});
				}
			}

			// save event types
			foreach (int abilityTemplate in character.AbilityController.KnownEvents)
			{
				if (!dbKnownAbilities.ContainsKey(abilityTemplate))
				{
					dbContext.CharacterKnownAbilities.Add(new CharacterKnownAbilityEntity()
					{
						CharacterID = character.ID.Value,
						TemplateID = abilityTemplate,
					});
				}
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// KeepData is automatically false... This means we delete the ability entry. TODO Deleted field is simply set to true just incase we need to reinstate a character..
		/// </summary>
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
		/// KeepData is automatically false... This means we delete the ability entry. TODO Deleted field is simply set to true just incase we need to reinstate a character..
		/// </summary>
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
		/// Load a characters known abilities from the database.
		/// </summary>
		public static void Load(NpgsqlDbContext dbContext, Character character)
		{
			var dbKnownAbilities = dbContext.CharacterKnownAbilities.Where(c => c.CharacterID == character.ID.Value);

			List<BaseAbilityTemplate> templates = new List<BaseAbilityTemplate>();

			foreach (CharacterKnownAbilityEntity dbKnownAbility in dbKnownAbilities)
			{
				BaseAbilityTemplate template = BaseAbilityTemplate.Get<BaseAbilityTemplate>(dbKnownAbility.TemplateID);
				if (template != null)
				{
					templates.Add(template);
				}
			};

			character.AbilityController.LearnBaseAbilities(templates);
		}
	}
}