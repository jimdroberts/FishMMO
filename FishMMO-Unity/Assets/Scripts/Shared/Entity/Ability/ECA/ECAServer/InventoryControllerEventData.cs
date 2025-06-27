using FishMMO.Shared;

namespace FishMMO.Server
{
	public class InventoryControllerEventData : EventData
	{
		public IInventoryController InventoryController { get; }

		public InventoryControllerEventData(IInventoryController inventoryController) : base(null)
		{
			InventoryController = inventoryController;
		}
	}
}