using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Container item operations: validates take-item requests and transfers items from container to player inventory.
	/// </summary>
	public partial class InteractableSystem
	{
		private void OnServerContainerTakeItemBroadcastReceived(NetworkConnection conn, ContainerTakeItemBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null ||
				!character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			try
			{
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				IInteractable interactable = sceneObject.GameObject.GetComponent<IInteractable>();
				if (interactable == null ||
					!interactable.InRange(character.Transform))
				{
					return;
				}

				IContainer container = interactable as IContainer;
				IItemContainer itemContainer = interactable as IItemContainer;
				if (container == null || itemContainer == null || container.Template == null)
				{
					return;
				}

				Item takenItem = itemContainer.RemoveItem(msg.Slot);
				if (takenItem == null)
				{
					return;
				}

				if (!SendNewItemBroadcast(conn, character, inventoryController, takenItem))
				{
					// Failed to add to inventory, put item back
					itemContainer.SetItemSlot(takenItem, msg.Slot);
					return;
				}

				// If container is empty and configured to despawn, despawn it
				if (container.Template.DespawnWhenEmpty && itemContainer.FilledSlots() <= 0)
				{
					container.Despawn();
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}
	}
}