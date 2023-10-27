using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Armor", menuName = "Item/Armor", order = 0)]
	public class ArmorTemplate : EquippableItemTemplate
	{
		public ItemAttributeTemplate ArmorBonus;
	}
}