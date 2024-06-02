using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterAbilityService
	{
		public static int GetCount(NpgsqlDbContext dbContext, long characterID)
		{
			if (characterID == 0)
			{
				return 0;
			}
			return dbContext.CharacterAbilities.Where((c) => c.CharacterID == characterID).Count();
		}

		/// <summary>
		/// Adds a known ability for a character to the database using the Ability Template ID.
		/// </summary>
		public static void UpdateOrAdd(NpgsqlDbContext dbContext, long characterID, Ability ability)
		{
			if (characterID == 0)
			{
				return;
			}

			if (ability == null)
			{
				return;
			}

			var dbAbility = dbContext.CharacterAbilities.FirstOrDefault(c => c.CharacterID == characterID && c.ID == ability.ID);
			// update or add to known abilities
			if (dbAbility != null)
			{
				dbAbility.CharacterID = characterID;
				dbAbility.TemplateID = ability.Template.ID;
				dbAbility.AbilityEvents.Clear();
				dbAbility.AbilityEvents = ability.AbilityEvents.Keys.ToList();
				dbContext.SaveChanges();
			}
			else
			{
				dbAbility = new CharacterAbilityEntity()
				{
					CharacterID = characterID,
					TemplateID = ability.Template.ID,
					AbilityEvents = ability.AbilityEvents.Keys.ToList(),
				};
				dbContext.CharacterAbilities.Add(dbAbility);
				dbContext.SaveChanges();
				ability.ID = dbAbility.ID;
			}
		}

		/// <summary>
		/// Save a characters abilities to the database.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IAbilityController abilityController))
			{
				return;
			}

			var dbAbilities = dbContext.CharacterAbilities.Where(c => c.CharacterID == character.ID)
																	.ToDictionary(k => k.ID);

			foreach (KeyValuePair<long, Ability> pair in abilityController.KnownAbilities)
			{
				if (pair.Key < 0)
				{
					continue;
				}
				if (dbAbilities.TryGetValue(pair.Key, out CharacterAbilityEntity ability))
				{
					ability.CharacterID = character.ID;
					ability.TemplateID = pair.Value.Template.ID;
					ability.AbilityEvents.Clear();
					ability.AbilityEvents = pair.Value.AbilityEvents.Keys.ToList();
				}
				else
				{
					dbContext.CharacterAbilities.Add(new CharacterAbilityEntity()
					{
						CharacterID = character.ID,
						TemplateID = pair.Value.Template.ID,
						AbilityEvents = pair.Value.AbilityEvents.Keys.ToList(),
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
				var dbAbilities = dbContext.CharacterAbilities.Where(c => c.CharacterID == characterID);
				if (dbAbilities != null)
				{
					dbContext.CharacterAbilities.RemoveRange(dbAbilities);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// KeepData is automatically false... This means we delete the ability entry. TODO Deleted field is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, long id, bool keepData = false)
		{
			if (characterID == 0 ||
				id == 0)
			{
				return;
			}

			if (!keepData)
			{
				var dbAbility = dbContext.CharacterAbilities.FirstOrDefault(c => c.CharacterID == characterID && c.ID == id);
				if (dbAbility != null)
				{
					dbContext.CharacterAbilities.Remove(dbAbility);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Load a characters known abilities from the database.
		/// </summary>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IAbilityController abilityController))
			{
				return;
			}

			var dbAbilities = dbContext.CharacterAbilities.Where(c => c.CharacterID == character.ID);

			foreach (CharacterAbilityEntity dbAbility in dbAbilities)
			{
				AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(dbAbility.TemplateID);
				if (template != null)
				{
					abilityController.LearnAbility(new Ability(dbAbility.ID, template, dbAbility.AbilityEvents));
				}
			};
		}
	}
}