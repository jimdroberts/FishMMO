using System.Collections.Concurrent;
using FishMMO.Shared;
using FishMMO.Server.Core;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles interactions with world item objects, allowing players to pick up items from the world.
	/// </summary>
	[HandlesInteractable(typeof(WorldItem))]
	public class WorldItemHandler : IInteractableHandler
	{
		/// <summary>
		/// Tracks world item instance IDs currently being processed to prevent
		/// concurrent pickup of the same item (S3: item duplication exploit prevention).
		/// </summary>
		private static readonly ConcurrentDictionary<int, byte> processingItems = new ConcurrentDictionary<int, byte>();

		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public WorldItemHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
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

			// Per-object concurrency guard — prevent two players from picking up
			// the same world item simultaneously (item duplication exploit).
			int objectId = worldItem.GetInstanceID();
			if (!processingItems.TryAdd(objectId, 0))
			{
				return; // Another interaction is already processing this item
			}

			try
			{
				if (worldItem.Amount < 1)
				{
					worldItem.Despawn();
				}
				else if (character.TryGet(out IInventoryController inventoryController))
				{
					//Log.Debug($"WorldItem Amount {worldItem.Amount}");
					Item newItem = new Item(worldItem.Template, worldItem.Amount);
					if (newItem == null)
					{
						return;
					}

					if (serverInstance.SendNewItemBroadcast(character.Owner, character, inventoryController, newItem))
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
			finally
			{
				processingItems.TryRemove(objectId, out _);
			}
		}
	}
}