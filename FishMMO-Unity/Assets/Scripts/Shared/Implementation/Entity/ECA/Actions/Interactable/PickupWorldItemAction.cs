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

					if (newItem.IsStackable && newItem.Stackable.Amount > 1)
						worldItem.Amount = newItem.Stackable.Amount;
					else
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