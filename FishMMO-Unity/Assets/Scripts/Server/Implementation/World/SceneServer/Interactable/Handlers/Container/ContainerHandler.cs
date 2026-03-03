using FishMMO.Shared;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishNet.Transporting;
using FishNet.Connection;
using System.Collections.Generic;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles container interactions. Sends a <see cref="ContainerOpenBroadcast"/> to the client
	/// with the container's current item contents so the player can view and take items.
	/// </summary>
	[HandlesInteractable(typeof(Container))]
	public class ContainerHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public ContainerHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
		{
			IContainer container = interactable as IContainer;
			if (container == null || container.Template == null)
			{
				return;
			}

			IItemContainer itemContainer = interactable as IItemContainer;
			if (itemContainer == null)
			{
				return;
			}

			// Build item slot data for the broadcast
			List<ContainerSlotData> slotData = new List<ContainerSlotData>();
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

			server.NetworkWrapper.Broadcast(character.Owner, new ContainerOpenBroadcast()
			{
				InteractableID = sceneObject.ID,
				TemplateID = container.Template.ID,
				Items = slotData,
			}, true, Channel.Reliable);

			// Increment achievement
			if (container.AchievementTemplate != null &&
				character.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(container.AchievementTemplate, 1);
			}
		}
	}
}