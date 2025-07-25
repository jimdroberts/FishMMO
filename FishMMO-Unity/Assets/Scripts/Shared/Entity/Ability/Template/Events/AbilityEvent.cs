using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class AbilityEvent : Trigger, ITooltip
	{
		[SerializeField]
		private Sprite icon;
		public float ActivationTime;
		public float LifeTime;
		public float Speed;
		public float Cooldown;
		public int Price;
		public AbilityResourceDictionary Resources = new AbilityResourceDictionary();
		public AbilityResourceDictionary RequiredAttributes = new AbilityResourceDictionary();
		public FactionTemplate RequiredFaction;
		public ArchetypeTemplate RequiredArchetype;

		public string Name { get { return this.name; } }
		public Sprite Icon { get { return this.icon; } }

		public string GetFormattedDescription()
		{
			return "Ability Event: " + Name;
		}

		public string Tooltip()
		{
			return GetFormattedDescription();
		}

		public string Tooltip(List<ITooltip> combineList)
		{
			return GetFormattedDescription();
		}
	}
}