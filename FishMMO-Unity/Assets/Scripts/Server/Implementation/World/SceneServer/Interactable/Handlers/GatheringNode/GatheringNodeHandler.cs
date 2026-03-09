using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Core;
using FishMMO.Shared.Core;
using FishNet.Transporting;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles gathering node interactions. Rolls the drop table, grants items to the player,
	/// decrements remaining uses, and despawns the node when depleted.
	/// Sends a <see cref="GatheringNodeBroadcast"/> to the client for progress bar display.
	/// </summary>
	[HandlesInteractable(typeof(GatheringNode))]
	public class GatheringNodeHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public GatheringNodeHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
		{
			IGatheringNode node = interactable as IGatheringNode;
			if (node == null || node.Template == null || node.RemainingUses <= 0)
			{
				return;
			}

			GatheringNodeTemplate template = node.Template;

			// Notify the client about the gathering attempt
			server.NetworkWrapper.Broadcast(character.Owner, new GatheringNodeBroadcast()
			{
				InteractableID = sceneObject.ID,
				TemplateID = template.ID,
				GatherTimeSeconds = template.GatherTimeSeconds,
			}, true, Channel.Reliable);

			// Roll the drop table using weighted random selection
			GatheringDrop drop = RollDropTable(template);
			if (drop == null || drop.Item == null)
			{
				return;
			}

			if (!character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			// Determine drop amount
			int amount = drop.MinAmount;
			if (drop.MaxAmount > drop.MinAmount)
			{
				amount = DeterministicRNG.Shared.Range(drop.MinAmount, drop.MaxAmount + 1);
			}

			Item newItem = new Item(drop.Item, (uint)amount);
			interactableSystem.SendNewItemBroadcast(character.Owner, character, inventoryController, newItem);

			// Increment achievement
			if (node.AchievementTemplate != null &&
				character.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(node.AchievementTemplate, 1);
			}

			// Decrement uses and despawn if depleted
			node.RemainingUses--;
			if (node.RemainingUses <= 0)
			{
				node.Despawn();
			}
		}

		/// <summary>
		/// Performs a weighted random selection on the gathering node's drop table.
		/// </summary>
		/// <param name="template">The gathering node template containing the drop table.</param>
		/// <returns>The selected drop entry, or null if the table is empty.</returns>
		private GatheringDrop RollDropTable(GatheringNodeTemplate template)
		{
			if (template.Drops == null || template.Drops.Count == 0)
			{
				return null;
			}

			float totalWeight = 0f;
			for (int i = 0; i < template.Drops.Count; i++)
			{
				if (template.Drops[i] != null)
				{
					totalWeight += template.Drops[i].Weight;
				}
			}

			if (totalWeight <= 0f)
			{
				return null;
			}

			float roll = DeterministicRNG.Shared.Range(0f, totalWeight);
			float cumulative = 0f;

			for (int i = 0; i < template.Drops.Count; i++)
			{
				GatheringDrop drop = template.Drops[i];
				if (drop == null)
				{
					continue;
				}

				cumulative += drop.Weight;
				if (roll <= cumulative)
				{
					return drop;
				}
			}

			// Fallback: return last valid drop
			return template.Drops[template.Drops.Count - 1];
		}
	}
}