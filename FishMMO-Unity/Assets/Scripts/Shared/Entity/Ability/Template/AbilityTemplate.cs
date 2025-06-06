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
		public byte EventSlots;
		public int HitCount;
		public AbilityType Type;
		public List<AbilityEvent> Events;

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