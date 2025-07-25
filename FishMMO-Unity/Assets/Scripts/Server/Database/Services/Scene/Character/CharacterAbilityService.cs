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
		public static void UpdateOrAdd(NpgsqlDbContext dbContext, long characterID, Ability ability, float cooldown = 0.0f)
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
				dbAbility.AbilityTriggers.Clear();
				dbAbility.AbilityTriggers = ability.AbilityTriggers.Keys.ToList();
				dbAbility.Cooldown = cooldown;
				dbContext.SaveChanges();
			}
			else
			{
				dbAbility = new CharacterAbilityEntity()
				{
					CharacterID = characterID,
					TemplateID = ability.Template.ID,
					AbilityTriggers = ability.AbilityTriggers.Keys.ToList(),
					Cooldown = cooldown,
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

			character.TryGet(out ICooldownController cooldownController);

			var dbAbilities = dbContext.CharacterAbilities.Where(c => c.CharacterID == character.ID)
																	.ToDictionary(k => k.ID);

			foreach (var pair in abilityController.KnownAbilities)
			{
				if (pair.Key < 0)
				{
					continue;
				}

				// Determine cooldown value
				cooldownController.TryGetCooldown(pair.Key, out float cooldown);

				// Either update an existing ability or add a new one
				if (dbAbilities.TryGetValue(pair.Key, out CharacterAbilityEntity ability))
				{
					ability.CharacterID = character.ID;
					ability.TemplateID = pair.Value.Template.ID;
					ability.AbilityTriggers.Clear();
					ability.AbilityTriggers = pair.Value.AbilityTriggers.Keys.ToList();
					ability.Cooldown = cooldown;
				}
				else
				{
					dbContext.CharacterAbilities.Add(new CharacterAbilityEntity()
					{
						CharacterID = character.ID,
						TemplateID = pair.Value.Template.ID,
						AbilityTriggers = pair.Value.AbilityTriggers.Keys.ToList(),
						Cooldown = cooldown,
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
					abilityController.LearnAbility(new Ability(dbAbility.ID, template, dbAbility.AbilityTriggers), dbAbility.Cooldown);
				}
			};
		}
	}
}