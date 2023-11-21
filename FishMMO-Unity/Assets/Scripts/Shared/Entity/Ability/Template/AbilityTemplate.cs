using System.Text;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class AbilityTemplate : BaseAbilityTemplate, ITooltip
	{
		public GameObject FXPrefab;
		public AbilitySpawnTarget AbilitySpawnTarget;
		public bool RequiresTarget;
		public byte EventSlots;
		public int HitCount;
		public CharacterAttributeTemplate ActivationSpeedReductionAttribute;
		public CharacterAttributeTemplate CooldownReductionAttribute;

		public override string Tooltip()
		{
			string tooltip = base.Tooltip();
			StringBuilder sb = new StringBuilder(tooltip);
			if (HitCount > 0)
			{
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Hit Count: ");
				sb.Append(HitCount);
				sb.Append("</color>");
			}
			return sb.ToString();
		}
	}
}