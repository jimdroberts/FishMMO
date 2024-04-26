namespace FishMMO.Shared
{
	public interface IInventoryController : ICharacterBehaviour, IItemContainer
	{
		void Activate(int index);
		bool CanSwapItemSlots(int from, int to, InventoryType fromInventory);
	}
}