using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject for overriding the ability type in an event.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability Type Override Event", menuName = "FishMMO/Character/Ability/Override Event/Ability Type Override", order = 1)]
	public sealed class AbilityTypeOverrideEventType : BaseAbilityTemplate
	{
		/// <summary>
		/// The ability type to override with this event.
		/// </summary>
		public AbilityType OverrideAbilityType;
	}
}