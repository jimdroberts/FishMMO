using System.Linq;
using FishMMO_DB;
using FishMMO_DB.Entities;

namespace FishMMO.Server.Services
{
	public class CharacterAchievementService
	{
		/// <summary>
		/// Save a character Achievements to the database.
		/// </summary>
		public static void Save(ServerDbContext dbContext, Character character)
		{
			if (character == null)
			{
				return;
			}

			var achievements = dbContext.CharacterAchievements.Where(c => c.CharacterId == character.ID)
															  .ToDictionary(k => k.TemplateID);

			foreach (Achievement achievement in character.AchievementController.Achievements.Values)
			{
				if (achievements.TryGetValue(achievement.Template.ID, out CharacterAchievementEntity dbAchievement))
				{
					dbAchievement.CharacterId = character.ID;
					dbAchievement.TemplateID = achievement.Template.ID;
					dbAchievement.Tier = achievement.CurrentTier;
					dbAchievement.Value = achievement.CurrentValue;
				}
				else
				{
					dbContext.CharacterAchievements.Add(new CharacterAchievementEntity()
					{
						CharacterId = character.ID,
						TemplateID = achievement.Template.ID,
						Tier = achievement.CurrentTier,
						Value = achievement.CurrentValue,
					});
				}
			}
		}

		/// <summary>
		/// KeepData is automatically true... This means we don't actually delete anything. Deleted is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(ServerDbContext dbContext, long characterID, bool keepData = true)
		{
			if (!keepData)
			{
				var achievements = dbContext.CharacterAchievements.Where(c => c.CharacterId == characterID);
				dbContext.CharacterAchievements.RemoveRange(achievements);
			}
		}

		/// <summary>
		/// Load character Achievements from the database.
		/// </summary>
		public static void Load(ServerDbContext dbContext, Character character)
		{
			dbContext.CharacterAchievements
			.Where(c => c.CharacterId == character.ID)
			.ToList()
			.ForEach(achievement =>
			{
				character.AchievementController.SetAchievement(achievement.TemplateID, achievement.Tier, achievement.Value);
			});
		}
	}
}