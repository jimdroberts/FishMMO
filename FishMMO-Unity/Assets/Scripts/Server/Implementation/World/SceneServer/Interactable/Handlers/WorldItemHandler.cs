using FishMMO.Shared;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// Handles interactions with world item objects, allowing players to pick up items from the world.
	/// </summary>
	   public class WorldItemHandler : IInteractableHandler
	   {
		   private readonly Server server;

		   public WorldItemHandler(Server server)
		   {
			   this.server = server;
		   }

		   /// <summary>
		   /// Handles the interaction between a player character and a world item.
		   /// Validates the item, attempts to add it to the player's inventory, and despawns the item if picked up.
		   /// </summary>
		   /// <param name="interactable">The interactable object (should be a WorldItem).</param>
		   /// <param name="character">The player character interacting with the item.</param>
		   /// <param name="sceneObject">The scene object associated with the interaction.</param>
		   /// <param name="serverInstance">The server instance managing interactables.</param>
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
				   //Log.Debug($"WorldItem Amount {worldItem.Amount}");
				   using var dbContext = serverInstance.Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
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
						   //Log.Debug($"WorldItem Remaining {newItem.Stackable.Amount}");
						   worldItem.Amount = newItem.Stackable.Amount;
					   }
					   else
					   {
						   //Log.Debug($"WorldItem Despawn");
						   worldItem.Despawn();
					   }
				   }
			   }
		   }
	   }
}