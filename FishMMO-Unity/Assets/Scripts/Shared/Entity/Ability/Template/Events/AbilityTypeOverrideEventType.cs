using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability Type Override Event", menuName = "FishMMO/Character/Ability/Override Event/Ability Type Override", order = 1)]
	public sealed class AbilityTypeOverrideEventType : AbilityEvent
	{
		public AbilityType OverrideAbilityType;
	}
}