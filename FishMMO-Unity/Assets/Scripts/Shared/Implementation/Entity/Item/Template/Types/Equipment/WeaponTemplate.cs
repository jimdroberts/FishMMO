using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for weapon items, defining attack power and attack speed attributes.
	/// Inherits from EquippableItemTemplate for equipment logic.
	/// Weapons are not skinned — they attach as MeshRenderer children of the specified bone transform.
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

		/// <summary>
		/// The skeleton bone name to parent the weapon mesh under (e.g., "RightHand", "LeftHand").
		/// The weapon GameObject becomes a child of this bone and follows its animations.
		/// </summary>
		[Tooltip("Skeleton bone name to attach the weapon to (e.g., 'RightHand', 'LeftHand').")]
		public string AttachBoneName = "RightHand";
	}
}