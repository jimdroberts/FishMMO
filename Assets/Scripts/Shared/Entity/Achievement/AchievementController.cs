using FishNet.Object;
using System.Collections.Generic;

public class AchievementController : NetworkBehaviour
{
	public AchievementTemplateDatabase AchievementDatabase;

	private Dictionary<string, Achievement> achievements = new Dictionary<string, Achievement>();

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (!base.IsOwner)
		{
			enabled = false;
			return;
		}

		if (AchievementDatabase != null)
		{
			foreach (AchievementTemplate achievement in AchievementDatabase.Achievements.Values)
			{
				AddAchievement(new Achievement(achievement.ID, achievement.InitialValue));
			}
		}
	}

	public List<Achievement> GetAchievements()
	{
		return new List<Achievement>(achievements.Values);
	}

	public bool TryGetAchievement(string name, out Achievement achievement)
	{
		return achievements.TryGetValue(name, out achievement);
	}

	private void AddAchievement(Achievement instance)
	{
		if (!achievements.ContainsKey(instance.Template.Name))
		{
			achievements.Add(instance.Template.Name, instance);
		}
	}
}