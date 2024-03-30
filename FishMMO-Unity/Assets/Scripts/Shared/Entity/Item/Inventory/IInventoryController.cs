namespace FishMMO.Shared
{
	public interface IInventoryController : ICharacterBehaviour, IItemContainer
	{
		long Currency { get; set; }
		void Activate(int index);
		bool CanSwapItemSlots(int from, int to, InventoryType fromInventory);
	}
}