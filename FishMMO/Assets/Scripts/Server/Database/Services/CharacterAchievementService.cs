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
		public static void SaveCharacterAchievements(ServerDbContext dbContext, Character existingCharacter)
		{
			if (existingCharacter == null)
			{
				return;
			}

			var achievements = dbContext.Achievements
				.Where(c => c.CharacterId == existingCharacter.ID)
				.ToDictionary(k => k.TemplateID);

			foreach (Achievement achievement in existingCharacter.AchievementController.Achievements.Values)
			{
				if (achievements.TryGetValue(achievement.Template.ID, out CharacterAchievementEntity dbAchievement))
				{
					dbAchievement.CharacterId = existingCharacter.ID;
					dbAchievement.TemplateID = achievement.Template.ID;
					dbAchievement.Tier = achievement.CurrentTier;
					dbAchievement.Value = achievement.CurrentValue;
				}
				else
				{
					dbContext.Achievements.Add(new CharacterAchievementEntity()
					{
						CharacterId = existingCharacter.ID,
						TemplateID = achievement.Template.ID,
						Tier = achievement.CurrentTier,
						Value = achievement.CurrentValue,
					});
				}
			}
		}

		/// <summary>
		/// Load character Achievements from the database.
		/// </summary>
		public static void LoadCharacterAchievements(ServerDbContext dbContext, Character existingCharacter)
		{
			var achievements = dbContext.Achievements
				.Where(c => c.CharacterId == existingCharacter.ID)
				.ToList();

			if (achievements != null)
			{
				foreach (var achievement in achievements)
				{
					AchievementTemplate template = AchievementTemplate.Get<AchievementTemplate>(achievement.TemplateID);
					if (template != null)
					{
						existingCharacter.AchievementController.SetAchievement(template.ID, achievement.Tier, achievement.Value);
					}
				}
			}
		}
	}
}