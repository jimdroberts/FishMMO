using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Quest", menuName = "Character/Quest/Quest", order = 1)]
public class QuestTemplate : CachedScriptableObject<QuestTemplate>
{
	public string Description;
	public uint TimeToCompleteInSeconds;
	public Texture2D Icon;
	public List<QuestAttributeRequirement> CharacterAttributeRequirements;
	public List<QuestTemplate> CompletedQuestRequirements;
	public List<QuestTemplate> AutoProgression;
	public List<QuestObjective> Objectives;

	public string Name { get { return this.name; } }

	public bool CanAcceptQuest(Character character)
	{
		if (CharacterAttributeRequirements != null && CharacterAttributeRequirements.Count > 0)
		{
			CharacterAttributeController characterAttributes = character.GetComponent<CharacterAttributeController>();
			if (characterAttributes == null)
			{
				return false;
			}
			foreach (QuestAttributeRequirement attributeRequirement in CharacterAttributeRequirements)
			{
				if (!attributeRequirement.MeetsRequirements(characterAttributes))
				{
					return false;
				}
			}
		}
		if (CompletedQuestRequirements != null && CompletedQuestRequirements.Count > 0)
		{
			QuestController questController = character.GetComponent<QuestController>();
			if (questController == null)
			{
				return false;
			}
			foreach (QuestTemplate questRequirement in CompletedQuestRequirements)
			{
				QuestInstance quest;
				if (!questController.TryGetQuest(questRequirement.Name, out quest) || quest.Status != QuestStatus.Completed)
				{
					return false;
				}
			}
		}
		return true;
	}

	public void AcceptQuest(Character character)
	{
		QuestController questController = character.GetComponent<QuestController>();
		if (questController == null)
		{
			return;
		}

		QuestInstance quest;
		if (questController.TryGetQuest(this.Name, out quest))
		{
			return;
		}

		questController.Acquire(this);
	}

	public void TryCompleteQuest(QuestInstance questInstance)
	{

	}
}

/*[CreateAssetMenu(fileName = "New Quest", menuName = "Character/Quest/Quest Objective/Kill Creature Objective", order = 1)]
public class QuestKillCreatureObjective : QuestObjective
{
	public Creature CreatureToKill;
}*/

[CreateAssetMenu(fileName = "New Quest", menuName = "Character/Quest/Quest Objective/Harvest Objective", order = 1)]
public class QuestHarvestObjective : QuestObjective
{
	public BaseItemTemplate ItemToHarvest;
}

[CreateAssetMenu(fileName = "New Quest", menuName = "Character/Quest/Quest Objective/Craft Objective", order = 1)]
public class QuestCraftObjective : QuestObjective
{
	public BaseItemTemplate ItemToCraft;
}

[CreateAssetMenu(fileName = "New Quest", menuName = "Character/Quest/Quest Objective/Enchant Objective", order = 1)]
public class QuestEnchantObjective : QuestObjective
{
}

[CreateAssetMenu(fileName = "New Quest", menuName = "Character/Quest/Quest Objective/Purchase Objective", order = 1)]
public class QuestPurchaseObjective : QuestObjective
{
	public BaseItemTemplate ItemToPurchase;
}

[CreateAssetMenu(fileName = "New Quest", menuName = "Character/Quest/Quest Objective/CharacterAttribute Objective", order = 1)]
public class QuestCharacterAttributeObjective : QuestObjective
{
}

[CreateAssetMenu(fileName = "New Quest", menuName = "Character/Quest/Quest Objective/Interact Objective", order = 1)]
public class QuestInteractObjective : QuestObjective
{
}

[CreateAssetMenu(fileName = "New Quest", menuName = "Character/Quest/Quest Objective/Socialize Objective", order = 1)]
public class QuestSocializeObjective : QuestObjective
{
}

[CreateAssetMenu(fileName = "New Quest", menuName = "Character/Quest/Quest Objective/Explore Objective", order = 1)]
public class QuestExploreObjective : QuestObjective
{
	//public BaseWorldScene SceneToExplore;
}