using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class CharacterDeathController : NetworkBehaviour, IKillable
{
	public Character character;

	public List<AchievementTemplate> OnKilledAchievements;
	public List<AchievementTemplate> OnKillAchievements;

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (character == null || !base.IsOwner)
		{
			enabled = false;
			return;
		}
	}

	public void Kill(Character killer)
	{
		//UILabel3D.Create("DEAD!", 32, Color.red, true, transform);

		//SELF
		/*EntitySpawnTracker spawnTracker = character.GetComponent<EntitySpawnTracker>();
		if (spawnTracker != null)
		{
			spawnTracker.OnKilled();
		}
		if (character.BuffController != null)
		{
			character.BuffController.RemoveAll();
		}
		if (character.QuestController != null)
		{
			//QuestController.OnKill(Entity);
		}
		if (character.AchievementController != null && OnKilledAchievements != null && OnKilledAchievements.Count > 0)
		{
			foreach (AchievementTemplate achievement in OnKilledAchievements)
			{
				achievement.OnGainValue.Invoke(character, killer, 1);
			}
		}

		//KILLER
		CharacterAttributeController killerAttributes = killer.GetComponent<CharacterAttributeController>();
		if (killerAttributes != null)
		{
			//handle kill rewards?
			CharacterAttribute experienceAttribute;
			if (killerAttributes.TryGetAttribute("Experience", out experienceAttribute))
			{

			}
		}
		QuestController killersQuests = killer.GetComponent<QuestController>();
		if (killersQuests != null)
		{
			//killersQuests.OnKill(Entity);
		}
		if (OnKillAchievements != null && OnKillAchievements.Count > 0)
		{
			AchievementController achievements = killer.GetComponent<AchievementController>();
			if (achievements != null)
			{
				foreach (AchievementTemplate achievement in OnKillAchievements)
				{
					achievement.OnGainValue.Invoke(killer, character, 1);
				}
			}
		}

		Destroy(this.gameObject);
		this.gameObject.SetActive(false);*/
	}
}