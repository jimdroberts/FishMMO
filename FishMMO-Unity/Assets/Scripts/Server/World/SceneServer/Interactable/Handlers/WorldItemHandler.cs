using FishMMO.Shared;

namespace FishMMO.Server
{
	public class WorldItemHandler : IInteractableHandler
	{
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance)
		{
			WorldItem worldItem = interactable as WorldItem;
			if (worldItem == null || worldItem.Template == null)
			{
				return;
			}
			if (worldItem.Amount < 1)
			{
				worldItem.Despawn();
			}
			else if (character.TryGet(out IInventoryController inventoryController))
			{
				//Debug.Log($"WorldItem Amount {worldItem.Amount}");
				using var dbContext = serverInstance.Server.NpgsqlDbContextFactory.CreateDbContext();
				if (dbContext == null)
				{
					return;
				}

				Item newItem = new Item(worldItem.Template, worldItem.Amount);
				if (newItem == null)
				{
					return;
				}

				if (serverInstance.SendNewItemBroadcast(dbContext, character.Owner, character, inventoryController, newItem))
				{
					if (newItem.IsStackable &&
						newItem.Stackable.Amount > 1)
					{
						//Debug.Log($"WorldItem Remaining {newItem.Stackable.Amount}");
						worldItem.Amount = newItem.Stackable.Amount;
					}
					else
					{
						//Debug.Log($"WorldItem Despawn");
						worldItem.Despawn();
					}
				}
			}
		}
	}
}