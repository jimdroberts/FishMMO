using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's achievements, including saving, deleting, and loading achievement data from the database.
		/// </summary>
		public class CharacterAchievementService
	{
		/// <summary>
		/// Saves a character's achievements to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character whose achievements will be saved.</param>
		public static void Save(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IAchievementController achievementController))
			{
				return;
			}

			var achievements = dbContext.CharacterAchievements.Where(c => c.CharacterID == character.ID)
															  .ToDictionary(k => k.TemplateID);

			foreach (Achievement achievement in achievementController.Achievements.Values)
			{
				if (achievements.TryGetValue(achievement.Template.ID, out CharacterAchievementEntity dbAchievement))
				{
					dbAchievement.CharacterID = character.ID;
					dbAchievement.TemplateID = achievement.Template.ID;
					dbAchievement.Tier = achievement.CurrentTier;
					dbAchievement.Value = achievement.CurrentValue;
				}
				else
				{
					dbContext.CharacterAchievements.Add(new CharacterAchievementEntity()
					{
						CharacterID = character.ID,
						TemplateID = achievement.Template.ID,
						Tier = achievement.CurrentTier,
						Value = achievement.CurrentValue,
					});
				}
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes all achievements for a character from the database. If keepData is false, the entries are removed.
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
				var achievements = dbContext.CharacterAchievements.Where(c => c.CharacterID == characterID);
				if (achievements != null)
				{
					dbContext.CharacterAchievements.RemoveRange(achievements);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Loads a character's achievements from the database and assigns them to the character's achievement controller.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load achievements for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out IAchievementController achievementController))
			{
				return;
			}
			var achievements = dbContext.CharacterAchievements.Where(c => c.CharacterID == character.ID);
			foreach (CharacterAchievementEntity achievement in  achievements)
			{
				achievementController.SetAchievement(achievement.TemplateID, achievement.Tier, achievement.Value, true);
			};
		}
	}
}