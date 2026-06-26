using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action for gathering node interactions. Broadcasts the gathering progress bar
	/// to the client, rolls the drop table, invokes <see cref="PlayerInteractionEventData.OnGrantItem"/>
	/// for DB persistence, manages node state, and increments the achievement counter.
	/// Server-only.
	/// </summary>
	[Serializable]
	public class GatheringNodeAction : BaseAction
	{
		/// <summary>
		/// Executes the gathering node interaction: broadcasts the progress bar, rolls the drop table,
		/// grants the item to the player, and manages node state.
		/// Server-only.
		/// </summary>
		/// <param name="initiator">The character performing the gathering.</param>
		/// <param name="eventData">The event data containing the interaction context.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			IGatheringNode node = data.Interactable as IGatheringNode;
			if (node == null || node.Template == null || node.RemainingUses <= 0) return;

			GatheringNodeTemplate template = node.Template;

			// Notify the client about the gathering attempt (shows progress bar)
			initiator.NetworkObject.Broadcast(new GatheringNodeBroadcast()
			{
				InteractableID = data.Interactable.ID,
				TemplateID = template.ID,
				GatherTimeSeconds = template.GatherTimeSeconds,
			});

			GatheringDrop drop = RollDropTable(template);
			if (drop?.Item == null) return;

			if (!initiator.TryGet(out IInventoryController inventoryController)) return;

			int amount = drop.MinAmount;
			if (drop.MaxAmount > drop.MinAmount)
			{
				amount = DeterministicRNG.Shared.Range(drop.MinAmount, drop.MaxAmount + 1);
			}

			Item newItem = new Item(drop.Item, (uint)amount);
			data.OnGrantItem?.Invoke(initiator, inventoryController, newItem);

			if (node.AchievementTemplate != null &&
				initiator.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(node.AchievementTemplate, 1);
			}

			node.RemainingUses--;
			if (node.RemainingUses <= 0)
			{
				node.Despawn();
			}
#endif
		}

		/// <summary>
		/// Performs a weighted random selection over the gathering node's drop table.
		/// Returns <c>null</c> if the table is empty or all weights are zero.
		/// </summary>
		private static GatheringDrop RollDropTable(GatheringNodeTemplate template)
		{
			if (template.Drops == null || template.Drops.Count == 0) return null;

			float totalWeight = 0f;
			for (int i = 0; i < template.Drops.Count; i++)
			{
				if (template.Drops[i] != null)
					totalWeight += template.Drops[i].Weight;
			}
			if (totalWeight <= 0f) return null;

			float roll = DeterministicRNG.Shared.Range(0f, totalWeight);
			float cumulative = 0f;
			for (int i = 0; i < template.Drops.Count; i++)
			{
				GatheringDrop drop = template.Drops[i];
				if (drop == null) continue;
				cumulative += drop.Weight;
				if (roll <= cumulative) return drop;
			}

			// Fallback: return last valid drop
			return template.Drops[template.Drops.Count - 1];
		}
	}
}