using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Server.DatabaseServices;
using FishMMO.Database.Npgsql;

namespace FishMMO.Server
{
	public class AchievementSystem : ServerBehaviour
	{
		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				IAchievementController.OnUpdateAchievement += IAchievementController_OnUpdateAchievement;
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
				IAchievementController.OnUpdateAchievement -= IAchievementController_OnUpdateAchievement;
				IAchievementController.OnCompleteAchievement -= IAchievementController_HandleAchievementRewards;
			}
		}

		private void IAchievementController_OnUpdateAchievement(ICharacter character, Achievement achievement)
		{
			if (character == null || achievement == null)
			{
				return;
			}

			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			playerCharacter.Owner.Broadcast(new AchievementUpdateBroadcast()
			{
				TemplateID = achievement.Template.ID,
				Value = achievement.CurrentValue,
				Tier = achievement.CurrentTier,
			});
		}

		private void IAchievementController_HandleAchievementRewards(ICharacter character, AchievementTemplate template, AchievementTier tier)
		{
			if (character == null || tier == null)
			{
				return;
			}

			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			HandleAbilityRewards(dbContext, playerCharacter, tier);
			HandleItemRewards(dbContext, playerCharacter, tier);
		}

		public void HandleAbilityRewards(NpgsqlDbContext dbContext, IPlayerCharacter character, AchievementTier tier)
		{
			List<BaseAbilityTemplate> abilityRewards = tier.AbilityRewards;
			if (abilityRewards == null ||
				abilityRewards.Count < 1)
			{
				return;
			}

			if (!character.TryGet(out IAbilityController abilityController))
			{
				return;
			}

			List<KnownAbilityAddBroadcast> modifiedAbilityBroadcasts = new List<KnownAbilityAddBroadcast>();

			for (int i = 0; i < abilityRewards.Count; ++i)
			{
				BaseAbilityTemplate abilityReward = abilityRewards[i];
				if (abilityController.KnowsAbility(abilityReward.ID))
				{
					continue;
				}

				// learn the ability
				abilityController.LearnBaseAbilities(new List<BaseAbilityTemplate> { abilityReward });

				// add the known ability to the database
				CharacterKnownAbilityService.Add(dbContext, character.ID, abilityReward.ID);

				modifiedAbilityBroadcasts.Add(new KnownAbilityAddBroadcast()
				{
					TemplateID = abilityReward.ID,
				});
			}

			if (modifiedAbilityBroadcasts.Count > 0)
			{
				// tell the client about the new ability event
				character.Owner.Broadcast(new KnownAbilityAddMultipleBroadcast()
				{
					Abilities = modifiedAbilityBroadcasts,
				}, true, Channel.Reliable);
			}
		}

		private void HandleItemRewards(NpgsqlDbContext dbContext, IPlayerCharacter character, AchievementTier tier)
		{
			List<BaseItemTemplate> itemRewards = tier.ItemRewards;
			if (itemRewards == null ||
				itemRewards.Count < 1)
			{
				return;
			}
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
					character.Owner.Broadcast(new InventorySetMultipleItemsBroadcast()
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
					character.Owner.Broadcast(new BankSetMultipleItemsBroadcast()
					{
						Items = modifiedItemBroadcasts,
					}, true, Channel.Reliable);
				}
			}
		}
	}
}
