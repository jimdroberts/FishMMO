using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Weapon", menuName = "Item/Weapon", order = 0)]
	public class WeaponTemplate : EquippableItemTemplate
	{
		public ItemAttributeTemplate AttackPower;
		public ItemAttributeTemplate AttackSpeed;
	}
}