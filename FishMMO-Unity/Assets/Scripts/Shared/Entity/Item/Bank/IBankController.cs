namespace FishMMO.Shared
{
	public interface IBankController : ICharacterBehaviour, IItemContainer
	{
		long Currency { get; set; }
		bool CanSwapItemSlots(int from, int to, InventoryType fromInventory);
	}
}