using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that sends a <see cref="ContainerOpenBroadcast"/> to the player containing
	/// the container's current item contents. Requires the interactable to implement both
	/// <see cref="IContainer"/> and <see cref="IItemContainer"/>.
	/// Server-only.
	/// </summary>
	[Serializable]
	public class SendContainerOpenBroadcastAction : BaseAction
	{
		/// <summary>
		/// Builds the container slot data list and broadcasts it to the player.
		/// </summary>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (initiator is not IPlayerCharacter player) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			IContainer container = data.Interactable as IContainer;
			if (container?.Template == null) return;

			IItemContainer itemContainer = data.Interactable as IItemContainer;
			if (itemContainer == null) return;

			List<ContainerSlotData> slotData = new List<ContainerSlotData>(itemContainer.Items.Count);
			for (int i = 0; i < itemContainer.Items.Count; i++)
			{
				Item item = itemContainer.Items[i];
				if (item != null)
				{
					slotData.Add(new ContainerSlotData()
					{
						Slot = i,
						TemplateID = item.Template.ID,
						Amount = item.IsStackable ? item.Stackable.Amount : 1,
					});
				}
			}

			initiator.NetworkObject.Broadcast(new ContainerOpenBroadcast()
			{
				InteractableID = data.Interactable.ID,
				TemplateID = container.Template.ID,
				Items = slotData.ToArray(),
			});

			if (container.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(container.AchievementTemplate, 1);
			}
		}
	}
}