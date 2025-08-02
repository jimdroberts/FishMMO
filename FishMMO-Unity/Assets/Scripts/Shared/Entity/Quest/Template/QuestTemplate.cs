using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for defining quests, their requirements, objectives, and progression logic.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest", order = 1)]
	public class QuestTemplate : CachedScriptableObject<QuestTemplate>, ICachedObject
	{
		/// <summary>
		/// Description of the quest and its narrative.
		/// </summary>
		public string Description;

		/// <summary>
		/// Time allowed to complete the quest, in seconds.
		/// </summary>
		public uint TimeToCompleteInSeconds;

		/// <summary>
		/// Icon representing the quest in UI.
		/// </summary>
		public Texture2D Icon;

		/// <summary>
		/// List of character attribute requirements needed to accept the quest.
		/// </summary>
		public List<QuestAttributeRequirement> CharacterAttributeRequirements;

		/// <summary>
		/// List of quests that must be completed before this quest can be accepted.
		/// </summary>
		public List<QuestTemplate> CompletedQuestRequirements;

		/// <summary>
		/// List of quests that are automatically progressed after this quest is completed.
		/// </summary>
		public List<QuestTemplate> AutoProgression;

		/// <summary>
		/// List of objectives that must be completed for this quest.
		/// </summary>
		public List<QuestObjective> Objectives;

		/// <summary>
		/// The name of the quest, derived from the asset name.
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Checks if the given player character can accept this quest, based on attribute and completed quest requirements.
		/// </summary>
		/// <param name="character">The player character to evaluate.</param>
		/// <returns>True if requirements are met, false otherwise.</returns>
		public bool CanAcceptQuest(IPlayerCharacter character)
		{
			// Check attribute requirements
			if (CharacterAttributeRequirements != null && CharacterAttributeRequirements.Count > 0)
			{
				if (!character.TryGet(out ICharacterAttributeController characterAttributeController))
				{
					return false;
				}
				foreach (QuestAttributeRequirement attributeRequirement in CharacterAttributeRequirements)
				{
					if (!attributeRequirement.MeetsRequirements(characterAttributeController))
					{
						return false;
					}
				}
			}
			// Check completed quest requirements
			if (CompletedQuestRequirements != null && CompletedQuestRequirements.Count > 0)
			{
				if (!character.TryGet(out IQuestController questController))
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

		/// <summary>
		/// Accepts the quest for the given player character, if not already acquired.
		/// </summary>
		/// <param name="character">The player character accepting the quest.</param>
		public void AcceptQuest(IPlayerCharacter character)
		{
			if (!character.TryGet(out IQuestController questController))
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

		/// <summary>
		/// Attempts to complete the quest for the given quest instance. (Implementation needed)
		/// </summary>
		/// <param name="questInstance">The quest instance to complete.</param>
		public void TryCompleteQuest(QuestInstance questInstance)
		{
			// Implementation for quest completion should be added here.
		}
	}

	/// <summary>
	/// Objective for harvesting a specific item.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Harvest Objective", order = 1)]
	public class QuestHarvestObjective : QuestObjective
	{
		/// <summary>
		/// The item to harvest for this objective.
		/// </summary>
		public BaseItemTemplate ItemToHarvest;
	}

	/// <summary>
	/// Objective for crafting a specific item.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Craft Objective", order = 1)]
	public class QuestCraftObjective : QuestObjective
	{
		/// <summary>
		/// The item to craft for this objective.
		/// </summary>
		public BaseItemTemplate ItemToCraft;
	}

	/// <summary>
	/// Objective for enchanting (details to be implemented).
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Enchant Objective", order = 1)]
	public class QuestEnchantObjective : QuestObjective
	{
	}

	/// <summary>
	/// Objective for purchasing a specific item.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Purchase Objective", order = 1)]
	public class QuestPurchaseObjective : QuestObjective
	{
		/// <summary>
		/// The item to purchase for this objective.
		/// </summary>
		public BaseItemTemplate ItemToPurchase;
	}

	/// <summary>
	/// Objective for reaching a character attribute value (details to be implemented).
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/CharacterAttribute Objective", order = 1)]
	public class QuestCharacterAttributeObjective : QuestObjective
	{
	}

	/// <summary>
	/// Objective for interacting with something (details to be implemented).
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Interact Objective", order = 1)]
	public class QuestInteractObjective : QuestObjective
	{
	}

	/// <summary>
	/// Objective for socializing (details to be implemented).
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Socialize Objective", order = 1)]
	public class QuestSocializeObjective : QuestObjective
	{
	}

	/// <summary>
	/// Objective for exploring a location (details to be implemented).
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Explore Objective", order = 1)]
	public class QuestExploreObjective : QuestObjective
	{
		//public BaseWorldScene SceneToExplore;
	}
}