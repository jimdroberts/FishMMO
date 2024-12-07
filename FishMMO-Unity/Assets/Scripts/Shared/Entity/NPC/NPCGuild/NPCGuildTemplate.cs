using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class NPCGuildTemplate : CachedScriptableObject<NPCGuildTemplate>, ICachedObject
	{
		public Sprite Icon;
		public string Description;
		public List<ArcheTypeTemplate> ArcheTypes = new List<ArcheTypeTemplate>();
		public AbilityResourceDictionary Requirements = new AbilityResourceDictionary();

		public string Name { get { return this.name; } }
	}
}