using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Armor", menuName = "FishMMO/Item/Armor", order = 0)]
	public class ArmorTemplate : EquippableItemTemplate
	{
		public ItemAttributeTemplate ArmorBonus;
	}
}