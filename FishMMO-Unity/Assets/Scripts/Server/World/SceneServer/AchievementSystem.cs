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
		/// <summary>
		/// Initializes the achievement system, subscribing to achievement update and completion events.
		/// </summary>
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

		/// <summary>
		/// Cleans up the achievement system, unsubscribing from achievement update and completion events.
		/// </summary>
		public override void Destroying()
		{
			if (ServerManager != null)
			{
				IAchievementController.OnUpdateAchievement -= IAchievementController_OnUpdateAchievement;
				IAchievementController.OnCompleteAchievement -= IAchievementController_HandleAchievementRewards;
			}
		}

		/// <summary>
		/// Handles achievement update events for characters, validates input, and broadcasts achievement changes to the player client.
		/// </summary>
		/// <param name="character">The character whose achievement was updated.</param>
		/// <param name="achievement">The updated achievement data.</param>
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

		/// <summary>
		/// Handles achievement completion events, validates input, and processes achievement rewards for the player character.
		/// </summary>
		/// <param name="character">The character who completed the achievement.</param>
		/// <param name="template">The achievement template.</param>
		/// <param name="tier">The achievement tier completed.</param>
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

		/// <summary>
		/// Generic handler for ability rewards, processes learning and broadcasting new abilities to the player character.
		/// </summary>
		/// <typeparam name="TTemplate">Type of ability template.</typeparam>
		/// <typeparam name="TBroadcast">Type of single ability broadcast.</typeparam>
		/// <typeparam name="TMultiBroadcast">Type of multiple ability broadcast.</typeparam>
		/// <param name="dbContext">Database context for updates.</param>
		/// <param name="character">Player character receiving rewards.</param>
		/// <param name="rewards">List of ability rewards.</param>
		/// <param name="knowsFunc">Function to check if ability is already known.</param>
		/// <param name="learnFunc">Function to learn new abilities.</param>
		/// <param name="idSelector">Function to select ability ID.</param>
		/// <param name="singleBroadcastFactory">Factory to create single ability broadcast.</param>
		/// <param name="multiBroadcastFactory">Factory to create multiple ability broadcast.</param>
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

		/// <summary>
		/// Handles ability rewards for achievement tiers, processes learning and broadcasting new base abilities.
		/// </summary>
		/// <param name="dbContext">Database context for updates.</param>
		/// <param name="character">Player character receiving rewards.</param>
		/// <param name="tier">Achievement tier containing ability rewards.</param>
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

		/// <summary>
		/// Handles ability event rewards for achievement tiers, processes learning and broadcasting new ability events.
		/// </summary>
		/// <param name="dbContext">Database context for updates.</param>
		/// <param name="character">Player character receiving rewards.</param>
		/// <param name="tier">Achievement tier containing ability event rewards.</param>
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

		/// <summary>
		/// Handles item rewards for achievement tiers, adds items to inventory or bank and broadcasts updates to the client.
		/// </summary>
		/// <param name="dbContext">Database context for updates.</param>
		/// <param name="character">Player character receiving rewards.</param>
		/// <param name="tier">Achievement tier containing item rewards.</param>
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