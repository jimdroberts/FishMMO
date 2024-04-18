namespace FishMMO.Shared
{
	public interface IBankController : ICharacterBehaviour, IItemContainer
	{
		int LastInteractableID { get; set; }
		long Currency { get; set; }
		bool CanSwapItemSlots(int from, int to, InventoryType fromInventory);
	}
}