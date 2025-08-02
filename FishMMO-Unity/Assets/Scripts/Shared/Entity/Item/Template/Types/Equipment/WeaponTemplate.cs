using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for weapon items, defining attack power and attack speed attributes.
	/// Inherits from EquippableItemTemplate for equipment logic.
	/// </summary>
	[CreateAssetMenu(fileName = "New Weapon", menuName = "FishMMO/Item/Weapon", order = 0)]
	public class WeaponTemplate : EquippableItemTemplate
	{
		/// <summary>
		/// The attribute template representing the attack power provided by this weapon.
		/// </summary>
		public ItemAttributeTemplate AttackPower;

		/// <summary>
		/// The attribute template representing the attack speed provided by this weapon.
		/// </summary>
		public ItemAttributeTemplate AttackSpeed;
	}
}