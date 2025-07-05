using FishMMO.Shared;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Server
{
	[CreateAssetMenu(fileName = "New Pickup WorldItem Action", menuName = "FishMMO/Actions/WorldItem/Pickup", order = 0)]
	public class PickupWorldItemAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!eventData.TryGet(out InteractableEventData interactableEventData))
			{
				Log.Error("PickupWorldItemAction", "Missing InteractableEventData.");
				return;
			}
			if (!interactableEventData.TryGet(out InventoryControllerEventData inventoryControllerEventData))
			{
				Log.Error("PickupWorldItemAction", "Missing InventoryControllerEventData.");
				return;
			}

			WorldItem worldItem = interactableEventData.Interactable as WorldItem;
			IInventoryController inventoryController = inventoryControllerEventData.InventoryController;
			IPlayerCharacter character = initiator as IPlayerCharacter;
			InteractableSystem serverInstance = interactableEventData.ServerInstance; // Get server instance directly

			if (worldItem == null || worldItem.Template == null || worldItem.Amount < 1 || inventoryController == null || character == null || serverInstance == null)
			{
				Log.Warning("PickupWorldItemAction", "Invalid data for item pickup.");
				return;
			}

			using var dbContext = serverInstance.Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				Log.Error("PickupWorldItemAction", "Could not create DbContext.");
				return;
			}

			Item newItem = new Item(worldItem.Template, worldItem.Amount);
			if (newItem == null)
			{
				Log.Error("PickupWorldItemAction", "Failed to create new item.");
				return;
			}

			if (serverInstance.SendNewItemBroadcast(dbContext, character.Owner, character, inventoryController, newItem))
			{
				if (newItem.IsStackable && newItem.Stackable.Amount > 0)
				{
					worldItem.Amount = newItem.Stackable.Amount;
					Log.Debug("PickupWorldItemAction", $"WorldItem partially picked up. Remaining: {worldItem.Amount}");
				}
				else
				{
					worldItem.Despawn();
					Log.Debug("PickupWorldItemAction", "WorldItem fully picked up and despawned.");
				}
			}
			else
			{
				Log.Warning("PickupWorldItemAction", "Failed to send new item broadcast or add item to inventory.");
			}
		}
	}
}