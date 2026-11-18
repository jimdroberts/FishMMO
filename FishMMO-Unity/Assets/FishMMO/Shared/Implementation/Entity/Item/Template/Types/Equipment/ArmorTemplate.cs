using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for armor items, defining armor bonus attributes.
	/// Inherits from EquippableItemTemplate for equipment logic.
	/// </summary>
	[CreateAssetMenu(fileName = "New Armor", menuName = "FishMMO/Item/Armor", order = 0)]
	public class ArmorTemplate : EquippableItemTemplate
	{
		/// <summary>
		/// The attribute template representing the armor bonus provided by this item.
		/// </summary>
		public ItemAttributeTemplate ArmorBonus;

		/// <summary>Describes the armour, and the protection it can roll.</summary>
		/// <param name="content">The content being assembled.</param>
		public override void BuildTooltip(TooltipContent content, bool describingInstance)
		{
			base.BuildTooltip(content, describingInstance);
			AddAttributeRange(content, "Armor", ArmorBonus, TooltipPriority.Stats, describingInstance);
		}
	}
}