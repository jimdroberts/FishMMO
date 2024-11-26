using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Server.DatabaseServices;

namespace FishMMO.Server
{
	public class AchievementSystem : ServerBehaviour
	{
		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				IAchievementController.OnCompleteAchievement += IAchievementController_HandleAchievementRewards;
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
				IAchievementController.OnCompleteAchievement -= IAchievementController_HandleAchievementRewards;
			}
		}

		private void IAchievementController_HandleAchievementRewards(ICharacter character, AchievementTemplate template, AchievementTier tier)
		{
			if (character == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			List<BaseItemTemplate> itemRewards = tier.ItemRewards;
			if (itemRewards != null &&
				itemRewards.Count > 0)
			{
				if (character.TryGet(out IInventoryController inventoryController) &&
					inventoryController.FreeSlots() >= itemRewards.Count)
				{
					List<InventorySetItemBroadcast> modifiedItemBroadcasts = new List<InventorySetItemBroadcast>();

					for (int i = 0; i < itemRewards.Count; ++i)
					{
						Item newItem = new Item(itemRewards[i], 1);

						if (inventoryController.TryAddItem(newItem, out List<Item> modifiedItems))
						{
							foreach (Item item in modifiedItems)
							{
								if (item == null)
								{
									continue;
								}
								
								CharacterInventoryService.SetSlot(dbContext, character.ID, item);

								modifiedItemBroadcasts.Add(new InventorySetItemBroadcast()
								{
									InstanceID = newItem.ID,
									TemplateID = newItem.Template.ID,
									Slot = newItem.Slot,
									Seed = newItem.IsGenerated ? newItem.Generator.Seed : 0,
									StackSize = newItem.IsStackable ? newItem.Stackable.Amount : 0,
								});
							}
						}
					}
					if (modifiedItemBroadcasts.Count > 0)
					{
						IPlayerCharacter playerCharacter = character as IPlayerCharacter;
						playerCharacter.Owner.Broadcast(new InventorySetMultipleItemsBroadcast()
						{
							Items = modifiedItemBroadcasts,
						}, true, Channel.Reliable);
					}
				}
				else if (character.TryGet(out IBankController bankController) &&
						 bankController.FreeSlots() >= itemRewards.Count)
				{
					List<BankSetItemBroadcast> modifiedItemBroadcasts = new List<BankSetItemBroadcast>();

					for (int i = 0; i < itemRewards.Count; ++i)
					{
						Item newItem = new Item(itemRewards[i], 1);

						if (inventoryController.TryAddItem(newItem, out List<Item> modifiedItems))
						{
							foreach (Item item in modifiedItems)
							{
								if (item == null)
								{
									continue;
								}

								CharacterInventoryService.SetSlot(dbContext, character.ID, item);

								modifiedItemBroadcasts.Add(new BankSetItemBroadcast()
								{
									InstanceID = newItem.ID,
									TemplateID = newItem.Template.ID,
									Slot = newItem.Slot,
									Seed = newItem.IsGenerated ? newItem.Generator.Seed : 0,
									StackSize = newItem.IsStackable ? newItem.Stackable.Amount : 0,
								});
							}
						}
					}
					if (modifiedItemBroadcasts.Count > 0)
					{
						IPlayerCharacter playerCharacter = character as IPlayerCharacter;
						playerCharacter.Owner.Broadcast(new BankSetMultipleItemsBroadcast()
						{
							Items = modifiedItemBroadcasts,
						}, true, Channel.Reliable);
					}
				}
			}
		}
	}
}
