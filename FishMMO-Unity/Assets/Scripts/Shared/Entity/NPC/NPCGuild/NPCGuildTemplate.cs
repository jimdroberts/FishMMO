using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New NPC Guild", menuName = "FishMMO/Character/NPC/NPC Guild", order = 1)]
	public class NPCGuildTemplate : CachedScriptableObject<NPCGuildTemplate>, ICachedObject
	{
		public Sprite Icon;
		public string Description;
		public List<ArchetypeTemplate> Archetypes = new List<ArchetypeTemplate>();
		public NPCGuildJoinCondition NPCGuildJoinCondition;

		public string Name { get { return this.name; } }
	}
}