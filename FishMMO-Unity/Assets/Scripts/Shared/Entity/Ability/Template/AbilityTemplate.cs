using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability", menuName = "FishMMO/Character/Ability/Ability", order = 1)]
	public class AbilityTemplate : BaseAbilityTemplate, ITooltip
	{
		public GameObject AbilityObjectPrefab;
		public AbilitySpawnTarget AbilitySpawnTarget;
		public bool RequiresTarget;
		public byte AdditionalEventSlots;
		public int HitCount;
		public AbilityType Type;
		public List<AbilityEvent> Events;

		[Tooltip("Triggers to execute when the ability object 'ticks' (e.g., moves, applies continuous effects).")]
		public List<Trigger> OnTickTriggers = new List<Trigger>();

		[Tooltip("Triggers to execute when the ability object collides or hits a character.")]
		public List<Trigger> OnHitTriggers = new List<Trigger>();

		[Tooltip("Triggers to execute before the primary ability object is spawned.")]
		public List<Trigger> OnPreSpawnTriggers = new List<Trigger>();

		[Tooltip("Triggers to execute when the primary ability object is spawned.")]
		public List<Trigger> OnSpawnTriggers = new List<Trigger>();

		[Tooltip("Triggers to execute when the ability object is destroyed.")]
		public List<Trigger> OnDestroyTriggers = new List<Trigger>();

		public override string Tooltip()
		{
			string tooltip = base.Tooltip(new List<ITooltip>(Events));
			if (Type != AbilityType.None)
			{
				tooltip += RichText.Format($"\r\nType: {Type}", true, "f5ad6eFF", "120%");
			}
			return tooltip;
		}
	}
}