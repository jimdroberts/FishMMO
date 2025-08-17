using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's abilities, including updating, saving, deleting, and loading ability data from the database.
		/// </summary>
		public class CharacterAbilityService
	{
		/// <summary>
		/// Gets the number of abilities for a given character.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <returns>The count of abilities.</returns>
		public static int GetCount(NpgsqlDbContext dbContext, long characterID)
		{
			if (characterID == 0)
			{
				return 0;
			}
			return dbContext.CharacterAbilities.Where((c) => c.CharacterID == characterID).Count();
		}

		/// <summary>
		/// Updates or adds an ability for a character in the database using the ability template ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="ability">The ability to update or add.</param>
		/// <param name="cooldown">The cooldown value for the ability.</param>
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
				dbAbility.AbilityEvents.Clear();
				dbAbility.AbilityEvents = ability.AbilityEvents.Keys.ToList();
				dbAbility.Cooldown = cooldown;
				dbContext.SaveChanges();
			}
			else
			{
				dbAbility = new CharacterAbilityEntity()
				{
					CharacterID = characterID,
					TemplateID = ability.Template.ID,
					AbilityEvents = ability.AbilityEvents.Keys.ToList(),
					Cooldown = cooldown,
				};
				dbContext.CharacterAbilities.Add(dbAbility);
				dbContext.SaveChanges();
				ability.ID = dbAbility.ID;
			}
		}

		/// <summary>
		/// Saves a character's abilities to the database.
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
					ability.AbilityEvents.Clear();
					ability.AbilityEvents = pair.Value.AbilityEvents.Keys.ToList();
					ability.Cooldown = cooldown;
				}
				else
				{
					dbContext.CharacterAbilities.Add(new CharacterAbilityEntity()
					{
						CharacterID = character.ID,
						TemplateID = pair.Value.Template.ID,
						AbilityEvents = pair.Value.AbilityEvents.Keys.ToList(),
						Cooldown = cooldown,
					});
				}
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes all abilities for a character from the database. If keepData is false, the entries are removed.
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
				var dbAbilities = dbContext.CharacterAbilities.Where(c => c.CharacterID == characterID);
				if (dbAbilities != null)
				{
					dbContext.CharacterAbilities.RemoveRange(dbAbilities);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Deletes a specific ability for a character from the database. If keepData is false, the entry is removed.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="id">The ability ID to delete.</param>
		/// <param name="keepData">Whether to keep the data (currently not implemented).</param>
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

			var dbAbilities = dbContext.CharacterAbilities.Where(c => c.CharacterID == character.ID);

			foreach (CharacterAbilityEntity dbAbility in dbAbilities)
			{
				AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(dbAbility.TemplateID);
				if (template != null)
				{
					abilityController.LearnAbility(new Ability(dbAbility.ID, template, dbAbility.AbilityEvents), dbAbility.Cooldown);
				}
			}
			;
		}
	}
}