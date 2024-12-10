using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Archetype", menuName = "Character/Archetype/Archetype", order = 1)]
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

		public string Name { get { return this.name; } }
	}
}