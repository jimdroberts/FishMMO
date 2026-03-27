using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template defining a quest, its requirements, objectives, and reward structure.
	/// Does not contain lifecycle logic; that lives in <see cref="QuestController"/>.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest", order = 1)]
	public class QuestTemplate : CachedScriptableObject<QuestTemplate>, ICachedObject
	{
		/// <summary>
		/// Description of the quest narrative.
		/// </summary>
		[TextArea(2, 6)]
		public string Description;

		/// <summary>
		/// Time allowed to complete the quest in seconds. Zero means unlimited.
		/// </summary>
		public uint TimeToCompleteInSeconds;

		/// <summary>
		/// Addressable reference to the icon texture for this quest.
		/// </summary>
		public AssetReferenceTexture2D IconReference;

		/// <summary>
		/// The loaded icon texture. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Texture2D loadedIcon;

		/// <summary>
		/// The icon for this quest (loaded at runtime on client).
		/// </summary>
		public Texture2D Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// Character attribute requirements needed to accept the quest.
		/// </summary>
		public List<QuestAttributeRequirement> CharacterAttributeRequirements;

		/// <summary>
		/// Quests that must have been turned in before this quest can be accepted.
		/// </summary>
		public List<QuestTemplate> CompletedQuestRequirements;

		/// <summary>
		/// Quests that are automatically offered after this quest is turned in.
		/// </summary>
		public List<QuestTemplate> AutoProgression;

		/// <summary>
		/// Objectives that must be completed for this quest.
		/// </summary>
		public List<QuestObjective> Objectives;

		/// <summary>
		/// Item rewards granted upon turn-in.
		/// </summary>
		public List<BaseItemTemplate> Rewards;

		/// <summary>
		/// The name of the quest derived from the asset name.
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Called when the quest template is loaded into cache. Loads the icon on the client.
		/// </summary>
		/// <param name="typeName">The type name of the resource.</param>
		/// <param name="resourceName">The resource name.</param>
		/// <param name="resourceID">The resource ID.</param>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(QuestTemplate))
				return;

#if !UNITY_SERVER
			if (IconReference != null && IconReference.RuntimeKeyIsValid())
			{
				IconReference.LoadAssetAsync<Texture2D>().Completed += (handle) =>
				{
					if (handle.Status == AsyncOperationStatus.Succeeded)
						loadedIcon = handle.Result;
				};
			}
#endif
		}

		/// <summary>
		/// Called when the quest template is unloaded from cache. Releases the icon on the client.
		/// </summary>
		/// <param name="typeName">The type name of the resource.</param>
		/// <param name="resourceName">The resource name.</param>
		/// <param name="resourceID">The resource ID.</param>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(QuestTemplate))
			{
#if !UNITY_SERVER
				if (IconReference != null && IconReference.IsValid())
				{
					IconReference.ReleaseAsset();
				}
				loadedIcon = null;
#endif
			}

			base.OnUnload(typeName, resourceName, resourceID);
		}

		/// <summary>
		/// Evaluates whether a player character meets all acceptance requirements.
		/// </summary>
		/// <param name="character">The player character to evaluate.</param>
		/// <returns>True if the character may accept this quest.</returns>
		public bool CanAcceptQuest(IPlayerCharacter character)
		{
			if (CharacterAttributeRequirements != null && CharacterAttributeRequirements.Count > 0)
			{
				if (!character.TryGet(out ICharacterAttributeController characterAttributeController))
				{
					return false;
				}
				for (int i = 0; i < CharacterAttributeRequirements.Count; i++)
				{
					if (!CharacterAttributeRequirements[i].MeetsRequirements(characterAttributeController))
					{
						return false;
					}
				}
			}

			if (CompletedQuestRequirements != null && CompletedQuestRequirements.Count > 0)
			{
				if (!character.TryGet(out IQuestController questController))
				{
					return false;
				}
				for (int i = 0; i < CompletedQuestRequirements.Count; i++)
				{
					QuestTemplate requirement = CompletedQuestRequirements[i];
					if (!questController.TryGetQuest(requirement.Name, out QuestInstance quest) ||
						quest.Status != QuestStatus.TurnedIn)
					{
						return false;
					}
				}
			}
			return true;
		}
	}

	/// <summary>
	/// Objective for killing a specific type of NPC identified by name.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Kill Objective", order = 1)]
	public class QuestKillObjective : QuestObjective
	{
		/// <summary>
		/// The NPC name to match when tracking kills.
		/// </summary>
		public string TargetNPCName;
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
	/// Objective for enchanting.
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
	/// Objective for reaching a character attribute value.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/CharacterAttribute Objective", order = 1)]
	public class QuestCharacterAttributeObjective : QuestObjective
	{
		/// <summary>
		/// The attribute template to check.
		/// </summary>
		public CharacterAttributeTemplate AttributeTemplate;
	}

	/// <summary>
	/// Objective for interacting with something.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Interact Objective", order = 1)]
	public class QuestInteractObjective : QuestObjective
	{
		/// <summary>
		/// The interactable scene object name to interact with.
		/// </summary>
		public string InteractableName;
	}

	/// <summary>
	/// Objective for gathering a specific item from gathering nodes.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Gather Objective", order = 1)]
	public class QuestGatherObjective : QuestObjective
	{
		/// <summary>
		/// The item to gather for this objective.
		/// </summary>
		public BaseItemTemplate ItemToGather;
	}

	/// <summary>
	/// Objective for socializing.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Socialize Objective", order = 1)]
	public class QuestSocializeObjective : QuestObjective
	{
	}

	/// <summary>
	/// Objective for exploring a location.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest", menuName = "FishMMO/Character/Quest/Quest Objective/Explore Objective", order = 1)]
	public class QuestExploreObjective : QuestObjective
	{
		/// <summary>
		/// The scene name to visit for exploration.
		/// </summary>
		public string SceneName;
	}
}