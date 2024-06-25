using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Server
{
	public class AchievementSystem : ServerBehaviour
	{
		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				IAchievementController.OnCompleteAchievement += HandleAchievementRewards;
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (ServerManager != null)
			{
				IAchievementController.OnCompleteAchievement -= HandleAchievementRewards;
			}
		}

		private void HandleAchievementRewards(ICharacter character, AchievementTemplate template, AchievementTier tier)
		{
			if (character != null &&
				character.TryGet(out InventoryController inventoryController))
			{
				BaseItemTemplate[] itemRewards = tier.ItemRewards;
				if (itemRewards != null && itemRewards.Length > 0 && inventoryController.FreeSlots() >= itemRewards.Length)
				{
					List<InventorySetItemBroadcast> modifiedItemBroadcasts = new List<InventorySetItemBroadcast>();

					for (int i = 0; i < itemRewards.Length; ++i)
					{
						Item newItem = new Item(123, 0, itemRewards[i].ID, 1);

						if (inventoryController.TryAddItem(newItem, out List<Item> modifiedItems))
						{
							foreach (Item item in modifiedItems)
							{
								modifiedItemBroadcasts.Add(new InventorySetItemBroadcast()
								{
									instanceID = newItem.ID,
									templateID = newItem.Template.ID,
									slot = newItem.Slot,
									seed = newItem.IsGenerated ? newItem.Generator.Seed : 0,
									stackSize = newItem.IsStackable ? newItem.Stackable.Amount : 0,
								});
							}
						}
					}
					if (modifiedItemBroadcasts.Count > 0)
					{
						IPlayerCharacter playerCharacter = character as IPlayerCharacter;
						playerCharacter.Owner.Broadcast(new InventorySetMultipleItemsBroadcast()
						{
							items = modifiedItemBroadcasts,
						}, true, Channel.Reliable);
					}
				}
			}
		}
	}
}
