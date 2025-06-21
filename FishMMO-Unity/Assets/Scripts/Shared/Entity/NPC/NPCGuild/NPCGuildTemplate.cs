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
		public BaseCondition GuildRequirements;

		public string Name { get { return this.name; } }

		public bool MeetsRequirements(IPlayerCharacter playerCharacter)
		{
			if (GuildRequirements == null)
			{
				//Log.Warning($"NPCGuildTemplate: No Guild Requirements assigned for {this.name}. Assuming requirements are met.");
				return true;
			}
			return GuildRequirements.Evaluate(playerCharacter);
		}
	}
}