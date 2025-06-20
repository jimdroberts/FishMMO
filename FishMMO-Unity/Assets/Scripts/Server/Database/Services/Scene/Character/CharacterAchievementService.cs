﻿using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterAchievementService
	{
		/// <summary>
		/// Save a character Achievements to the database.
		/// </summary>
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
				var achievements = dbContext.CharacterAchievements.Where(c => c.CharacterID == characterID);
				if (achievements != null)
				{
					dbContext.CharacterAchievements.RemoveRange(achievements);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Load character Achievements from the database.
		/// </summary>
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