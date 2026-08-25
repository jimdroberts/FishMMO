using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Container item operations: validates take-item requests and transfers items from container to player inventory.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Handles a <see cref="ContainerTakeItemBroadcast"/> request from a client.
		/// Validates the container interactable, removes the item from the container slot,
		/// and transfers it into the player's inventory. Despawns the container if it is
		/// configured to despawn when empty.
		/// </summary>
		private void OnServerContainerTakeItemBroadcastReceived(NetworkConnection conn, ContainerTakeItemBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			
			if (character == null ||
				!character.TryGet(out IInventoryController inventoryController) ||
				!CharacterStateValidation.CanAct(character))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				SendContainerResult(conn, msg.InteractableID, msg.Slot, false, ContainerFailureReason.ServerError);
				return;
			}

			bool succeeded = false;
			ContainerFailureReason reason = ContainerFailureReason.ServerError;
			IContainer takenFrom = null;

			try
			{
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				// Resolved through the shared rule and gated on CanInteract rather than InRange —
				// see the note on the merchant purchase path for why the raw GetComponent pair was
				// both order-dependent and missing the corpse gate.
				IInteractable interactable = InteractableResolver.Resolve(sceneObject);
				IContainer container = interactable as IContainer;
				IItemContainer itemContainer = interactable as IItemContainer;
				if (container == null ||
					itemContainer == null ||
					container.Template == null ||
					!interactable.CanInteract(character))
				{
					reason = ContainerFailureReason.NoContainer;
					return;
				}

				takenFrom = container;

				if (!itemContainer.IsValidSlot(msg.Slot))
				{
					reason = ContainerFailureReason.AlreadyTaken;
					return;
				}

				Item takenItem = itemContainer.RemoveItem(msg.Slot);
				if (takenItem == null)
				{
					reason = ContainerFailureReason.AlreadyTaken;
					return;
				}

				if (!SendNewItemBroadcast(conn, character, inventoryController, takenItem))
				{
					// Failed to add to inventory, put item back
					itemContainer.SetItemSlot(takenItem, msg.Slot);
					reason = ContainerFailureReason.InventoryFull;
					return;
				}

				succeeded = true;
				reason = ContainerFailureReason.None;

				// If container is empty and configured to despawn, despawn it
				if (container.Template.DespawnWhenEmpty && itemContainer.FilledSlots() <= 0)
				{
					container.Despawn();
					// Despawned out from under the player: the window has to close, and a refresh
					// naming a scene object that no longer resolves would be worse than none.
					takenFrom = null;
				}
			}
			finally
			{
				EndIngressGuard(guardKey);

				SendContainerResult(conn, msg.InteractableID, msg.Slot, succeeded, reason);

				/* Re-send the whole contents after a take, rather than trusting the client to
				 * remove one row. Containers are not private — two players can be looking at the
				 * same chest — so what the taker sees has to come from the server's copy. */
				if (succeeded && takenFrom != null)
				{
					SendContainerContents(conn, takenFrom);
				}
			}
		}

		/// <summary>
		/// Sends the reply that releases the client's pending lock on a container slot.
		/// </summary>
		private void SendContainerResult(NetworkConnection conn, long interactableID, int slot, bool success, ContainerFailureReason reason)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new ContainerTakeResultBroadcast()
			{
				InteractableID = interactableID,
				Slot = slot,
				Success = success,
				Reason = reason,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Sends one connection a container's current contents.
		/// </summary>
		/// <remarks>
		/// The same message the container's ECA open action sends, reused as the refresh — so the
		/// client has exactly one code path for "here is what is in the box" and cannot end up
		/// with an open window whose contents came from two different shapes of update.
		/// </remarks>
		private void SendContainerContents(NetworkConnection conn, IContainer container)
		{
			if (conn == null || !conn.IsActive || container?.Template == null)
			{
				return;
			}

			if (container is not IItemContainer itemContainer)
			{
				return;
			}

			List<ContainerSlotData> slots = new List<ContainerSlotData>(itemContainer.Items.Count);
			for (int i = 0; i < itemContainer.Items.Count; ++i)
			{
				Item item = itemContainer.Items[i];
				if (item == null || item.Template == null)
				{
					continue;
				}
				slots.Add(new ContainerSlotData()
				{
					Slot = i,
					TemplateID = item.Template.ID,
					Amount = item.IsStackable ? item.Stackable.Amount : 1,
				});
			}

			Server.NetworkWrapper.Broadcast(conn, new ContainerOpenBroadcast()
			{
				InteractableID = container.ID,
				TemplateID = container.Template.ID,
				Items = slots.ToArray(),
			}, true, Channel.Reliable);
		}
	}
}