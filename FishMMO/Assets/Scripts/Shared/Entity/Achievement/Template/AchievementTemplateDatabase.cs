using System;
using UnityEngine;

[CreateAssetMenu(fileName = "New Achievement Database", menuName = "Character/Achievement/Database", order = 0)]
public class AchievementTemplateDatabase : ScriptableObject
{
	[Serializable]
	public class AchievementDictionary : SerializableDictionary<string, AchievementTemplate> { }

	[SerializeField]
	private AchievementDictionary achievements = new AchievementDictionary();
	public AchievementDictionary Achievements { get { return this.achievements; } }

	public AchievementTemplate GetAchievement(string name)
	{
		AchievementTemplate achievement;
		this.achievements.TryGetValue(name, out achievement);
		return achievement;
	}
}