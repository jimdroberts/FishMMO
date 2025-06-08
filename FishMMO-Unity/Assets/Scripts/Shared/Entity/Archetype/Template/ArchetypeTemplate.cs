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
		public AbilityResourceDictionary RequiredAttributes = new AbilityResourceDictionary();
		public ItemTemplateDatabase RequiredItems;

		public string Name { get { return this.name; } }

		public bool MeetsRequirements(IPlayerCharacter playerCharacter)
		{
			if (!playerCharacter.TryGet(out ICharacterAttributeController characterAttributeController))
			{
				return false;
			}

			// Check if we meet the attribute requirements to accept this Archetype. Use the base value for condition check instead of the final.
			foreach (var requiredAttribute in RequiredAttributes)
			{
				if (!characterAttributeController.TryGetAttribute(requiredAttribute.Key, out CharacterAttribute attribute) ||
					attribute.Value < requiredAttribute.Value)
				{
					return false;
				}
			}
			return true;
		}
	}
}