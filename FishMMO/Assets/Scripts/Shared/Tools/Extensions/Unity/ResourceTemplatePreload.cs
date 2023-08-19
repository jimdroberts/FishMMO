using UnityEngine;

/// <summary>
/// We pre-load all templates at runtime so there is no jitter.
/// These templates contain CONSTANT game data. DO NOT CHANGE.
/// </summary>
public class ResourceTemplatePreload : MonoBehaviour
{
	private void Awake()
	{
		var a = AbilityTemplate.Cache;
		var an = AbilityEvent.Cache;
		var ach = AchievementTemplate.Cache;
		var buff = BuffTemplate.Cache;
		var c = CharacterAttributeTemplate.Cache;
		var b = BaseItemTemplate.Cache;
		var q = QuestTemplate.Cache;
	}

	private void OnApplicationQuit()
	{
		AbilityTemplate.UnloadCache();
		AbilityEvent.UnloadCache();
		AchievementTemplate.UnloadCache();
		BuffTemplate.UnloadCache();
		CharacterAttributeTemplate.UnloadCache();
		BaseItemTemplate.UnloadCache();
		QuestTemplate.UnloadCache();
	}
}