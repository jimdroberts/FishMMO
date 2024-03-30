using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface IEquipmentController : ICharacterBehaviour, IItemContainer
	{
		void Activate(int index);
		bool Equip(Item item, int inventoryIndex, IItemContainer container, ItemSlot toSlot);
		bool Unequip(IItemContainer container, byte slot, out List<Item> modifiedItems);
	}
}