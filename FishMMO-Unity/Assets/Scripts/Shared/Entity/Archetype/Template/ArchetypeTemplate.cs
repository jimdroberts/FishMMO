using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Archetype", menuName = "FishMMO/Character/Archetype/Archetype", order = 1)]
	public class ArchetypeTemplate : CachedScriptableObject<ArchetypeTemplate>, ICachedObject
	{
		public NPCGuildTemplate NPCGuild;
		public Sprite Icon;
		public string Description;
		public List<CharacterAttributeTemplate> AttributeRewards;
		public List<BaseAbilityTemplate> AbilityRewards;
		public List<BaseItemTemplate> ItemRewards;
		public List<BaseBuffTemplate> BuffRewards;
		public List<string> TitleRewards;
		public BaseCondition ArchetypeRequirements;

		public string Name { get { return this.name; } }

		public bool MeetsRequirements(IPlayerCharacter playerCharacter)
		{
			if (ArchetypeRequirements == null)
			{
				//Debug.LogWarning($"ArchetypeTemplate: No Archetype Requirements assigned for {this.name}. Assuming requirements are met.");
				return true;
			}
			return ArchetypeRequirements.Evaluate(playerCharacter);
		}
	}
}