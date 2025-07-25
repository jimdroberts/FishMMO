using FishNet.Transporting;
using System;
using System.Collections.Generic;
using FishNet.Broadcast;
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
			HandleAbilityEventRewards(dbContext, playerCharacter, tier);
			HandleItemRewards(dbContext, playerCharacter, tier);
		}

		private void HandleAbilityGenericRewards<TTemplate, TBroadcast, TMultiBroadcast>(
			NpgsqlDbContext dbContext,
			IPlayerCharacter character,
			List<TTemplate> rewards,
			Func<IAbilityController, int, bool> knowsFunc,
			Action<IAbilityController, List<TTemplate>> learnFunc,
			Func<TTemplate, int> idSelector,
			Func<TTemplate, TBroadcast> singleBroadcastFactory,
			Func<List<TBroadcast>, TMultiBroadcast> multiBroadcastFactory
		)
			where TTemplate : class
			where TBroadcast : struct, IBroadcast
			where TMultiBroadcast : struct, IBroadcast
		{
			if (rewards == null || rewards.Count < 1) return;
			if (!character.TryGet(out IAbilityController abilityController)) return;

			List<TBroadcast> broadcasts = new List<TBroadcast>();

			foreach (var reward in rewards)
			{
				int id = idSelector(reward);
				if (knowsFunc(abilityController, id)) continue;

				learnFunc(abilityController, new List<TTemplate> { reward });
				CharacterKnownAbilityService.Add(dbContext, character.ID, id);
				broadcasts.Add(singleBroadcastFactory(reward));
			}

			if (broadcasts.Count > 0)
			{
				character.Owner.Broadcast(multiBroadcastFactory(broadcasts), true, Channel.Reliable);
			}
		}

		public void HandleAbilityRewards(NpgsqlDbContext dbContext, IPlayerCharacter character, AchievementTier tier)
		{
			HandleAbilityGenericRewards<BaseAbilityTemplate, KnownAbilityAddBroadcast, KnownAbilityAddMultipleBroadcast>(
				dbContext,
				character,
				tier.AbilityRewards,
				(abilityController, id) => abilityController.KnowsAbility(id),
				(abilityController, list) => abilityController.LearnBaseAbilities(list),
				t => t.ID,
				t => new KnownAbilityAddBroadcast { TemplateID = t.ID },
				list => new KnownAbilityAddMultipleBroadcast { Abilities = list }
			);
		}

		private void HandleAbilityEventRewards(NpgsqlDbContext dbContext, IPlayerCharacter character, AchievementTier tier)
		{
			HandleAbilityGenericRewards<AbilityEvent, KnownAbilityEventAddBroadcast, KnownAbilityEventAddMultipleBroadcast>(
				dbContext,
				character,
				tier.AbilityEventRewards,
				(abilityController, id) => abilityController.KnowsAbilityEvent(id),
				(abilityController, list) => abilityController.LearnAbilityEvents(list),
				t => t.ID,
				t => new KnownAbilityEventAddBroadcast { TemplateID = t.ID },
				list => new KnownAbilityEventAddMultipleBroadcast { AbilityEvents = list }
			);
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