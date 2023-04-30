using FishNet.Object;
using System.Collections.Generic;

public class AchievementController : NetworkBehaviour
{
	public AchievementTemplateDatabase AchievementDatabase;

	private Dictionary<string, Achievement> achievements = new Dictionary<string, Achievement>();

	private void Awake()
	{
		if (AchievementDatabase != null)
		{
			foreach (AchievementTemplate achievement in AchievementDatabase.Achievements.Values)
			{
				AddAchievement(new Achievement(achievement.ID, achievement.InitialValue));
			}
		}
	}

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (!base.IsOwner)
		{
			enabled = false;
			return;
		}

		ClientManager.RegisterBroadcast<AchievementUpdateBroadcast>(OnClientAchievementUpdateBroadcastReceived);
		ClientManager.RegisterBroadcast<AchievementUpdateMultipleBroadcast>(OnClientAchievementUpdateMultipleBroadcastReceived);
	}

	public override void OnStopClient()
	{
		base.OnStopClient();

		if (base.IsOwner)
		{
			ClientManager.UnregisterBroadcast<AchievementUpdateBroadcast>(OnClientAchievementUpdateBroadcastReceived);
			ClientManager.UnregisterBroadcast<AchievementUpdateMultipleBroadcast>(OnClientAchievementUpdateMultipleBroadcastReceived);
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

	/// <summary>
	/// Server sent an achievement update broadcast.
	/// </summary>
	private void OnClientAchievementUpdateBroadcastReceived(AchievementUpdateBroadcast msg)
	{
		if (AchievementTemplate.Cache.TryGetValue(msg.templateID, out AchievementTemplate template) &&
			achievements.TryGetValue(template.Name, out Achievement achievement))
		{
			achievement.currentValue = msg.newValue;
		}
	}

	/// <summary>
	/// Server sent a multiple achievement update broadcasts.
	/// </summary>
	private void OnClientAchievementUpdateMultipleBroadcastReceived(AchievementUpdateMultipleBroadcast msg)
	{
		foreach (AchievementUpdateBroadcast subMsg in msg.achievements)
		{
			if (AchievementTemplate.Cache.TryGetValue(subMsg.templateID, out AchievementTemplate template) &&
				achievements.TryGetValue(template.Name, out Achievement achievement))
			{
				achievement.currentValue = subMsg.newValue;
			}
		}
	}
}