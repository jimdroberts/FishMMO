using System;
using System.Collections.Concurrent;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that handles picking up a world item. Validates the item, applies a
	/// per-object concurrency guard to prevent duplicate pickups, creates the item,
	/// invokes <see cref="PlayerInteractionEventData.OnGrantItem"/> to grant it to the
	/// player's inventory and persist it to the database, and manages world-item state.
	/// Server-only.
	/// </summary>
	[Serializable]
	public class PickupWorldItemAction : BaseAction
	{
		/// <summary>
		/// Tracks world-item scene object IDs currently being processed to prevent
		/// concurrent pickup of the same item (item-duplication exploit prevention).
		/// </summary>
		private static readonly ConcurrentDictionary<long, byte> processingItems = new ConcurrentDictionary<long, byte>();

		/// <summary>
		/// Handles picking up a world item: validates the item, applies a concurrency guard to prevent
		/// duplicate pickups, grants the item to the player's inventory, and manages world-item state.
		/// Server-only.
		/// </summary>
		/// <param name="initiator">The character picking up the world item.</param>
		/// <param name="eventData">The event data containing the interaction context.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			IWorldItem worldItem = data.Interactable as IWorldItem;
			if (worldItem?.Template == null) return;

			long objectId = data.Interactable.ID;
			if (!processingItems.TryAdd(objectId, 0)) return; // Another request is already processing this item

			try
			{
				if (worldItem.Amount < 1)
				{
					worldItem.Despawn();
					return;
				}

				if (!initiator.TryGet(out IInventoryController inventoryController)) return;

				Item newItem = new Item(worldItem.Template, worldItem.Amount);
				if (newItem == null) return;

				if (data.OnGrantItem != null && data.OnGrantItem(initiator, inventoryController, newItem))
				{
					if (worldItem.AchievementTemplateID != 0 &&
						initiator.TryGet(out IAchievementController achievementController))
					{
						achievementController.Increment(worldItem.AchievementTemplateID, 1);
					}

					/* A successful grant always takes the WHOLE pile, so the pickup always
					 * despawns.
					 *
					 * The previous code tried to leave a remainder behind:
					 *
					 *     if (newItem.IsStackable && newItem.Stackable.Amount > 1)
					 *         worldItem.Amount = newItem.Stackable.Amount;
					 *     else
					 *         worldItem.Despawn();
					 *
					 * — but there is no such thing as a remainder here. ItemContainer.TryAddItem
					 * opens with CanAddItem, which refuses unless the entire stack fits, so it is
					 * all-or-nothing; and SendNewItemBroadcast only reports success when TryAddItem
					 * succeeded. What newItem.Stackable.Amount actually holds afterwards depends on
					 * HOW the stack was placed: zero when it was merged into stacks the player
					 * already had, and the full original amount when it went into an empty slot,
					 * because a placed item keeps its own count.
					 *
					 * The empty-slot case is the common one, and it took the branch that did not
					 * despawn — so picking up a stack of two or more into a free slot granted the
					 * items AND left the pile lying there at its original size, ready to be picked
					 * up again. Unbounded item duplication on the most ordinary loot action in the
					 * game, on any stackable drop of size two or more.
					 *
					 * The amount is zeroed before the despawn so that even a stale scene object ID
					 * replayed against this instance before the pool reuses it finds nothing to
					 * take. */
					worldItem.Amount = 0;
					worldItem.Despawn();
				}
			}
			finally
			{
				processingItems.TryRemove(objectId, out _);
			}
		}
	}
}