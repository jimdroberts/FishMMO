using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Broadcast;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages server-side achievement tracking, updates, and completion rewards for player characters.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database persistence is fire-and-forget async to avoid blocking the main thread.
	/// </summary>
	[CreateAssetMenu(fileName = "AchievementSystem", menuName = "FishMMO/Server/SceneServer/Achievement System", order = 1)]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class AchievementSystem : ServerBehaviour, IAchievementSystem
	{
		/// <summary>
		/// Initializes the achievement system, subscribing to achievement update and completion events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("AchievementSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Achievement events
			IAchievementController.OnUpdateAchievement += IAchievementController_OnUpdateAchievement;
			IAchievementController.OnCompleteAchievement += IAchievementController_HandleAchievementRewards;

			Log.Debug("AchievementSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the achievement system, unsubscribing from achievement update and completion events.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("AchievementSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Achievement events
			IAchievementController.OnUpdateAchievement -= IAchievementController_OnUpdateAchievement;
			IAchievementController.OnCompleteAchievement -= IAchievementController_HandleAchievementRewards;
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

			playerCharacter.Owner.Broadcast(new AchievementUpdateBroadcast()
			{
				TemplateID = achievement.Template.ID,
				Value = achievement.CurrentValue,
				Tier = achievement.CurrentTier,
			});
		}

		/// <summary>
		/// Handles achievement completion events, validates input, and processes achievement rewards for the player character.
		/// Game logic and Broadcasts are synchronous on the main thread. DB writes are fire-and-forget async.
		/// </summary>
		/// <param name="character">The character who completed the achievement.</param>
		/// <param name="template">The achievement template.</param>
		/// <param name="tier">The achievement tier completed.</param>
		private void IAchievementController_HandleAchievementRewards(ICharacter character, AchievementTemplate template, AchievementTier tier)
		{
			if (character == null || template == null || tier == null)
			{
				return;
			}

			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter == null)
			{
				return;
			}

			// Resolve services once for all reward handlers
			if (Server.Database?.ServiceRegistry == null)
			{
				return;
			}

			Server.Database.ServiceRegistry.TryGet<ICharacterKnownAbilityService>(out var knownAbilityService);
			Server.Database.ServiceRegistry.TryGet<ICharacterInventoryService>(out var inventoryService);
			Server.Database.ServiceRegistry.TryGet<ICharacterBankService>(out var bankService);

			HandleAbilityRewards(knownAbilityService, playerCharacter, tier);
			HandleAbilityEventRewards(knownAbilityService, playerCharacter, tier);
			HandleItemRewards(inventoryService, bankService, playerCharacter, tier);
		}

		/// <summary>
		/// Generic handler for ability rewards, processes learning and broadcasting new abilities to the player character.
		/// Game logic and Broadcasts are synchronous. DB persistence is fire-and-forget async.
		/// </summary>
		/// <typeparam name="TTemplate">Type of ability template.</typeparam>
		/// <typeparam name="TBroadcast">Type of single ability broadcast.</typeparam>
		/// <typeparam name="TMultiBroadcast">Type of multiple ability broadcast.</typeparam>
		/// <param name="knownAbilityService">Async service for persisting known abilities, or null if unavailable.</param>
		/// <param name="character">Player character receiving rewards.</param>
		/// <param name="rewards">List of ability rewards.</param>
		/// <param name="knowsFunc">Function to check if ability is already known.</param>
		/// <param name="learnFunc">Function to learn new abilities.</param>
		/// <param name="idSelector">Function to select ability ID.</param>
		/// <param name="singleBroadcastFactory">Factory to create single ability broadcast.</param>
		/// <param name="multiBroadcastFactory">Factory to create multiple ability broadcast.</param>
		private void HandleAbilityGenericRewards<TTemplate, TBroadcast, TMultiBroadcast>(
			ICharacterKnownAbilityService knownAbilityService,
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

				// Fire-and-forget async DB persist — game state already updated in-memory
				if (knownAbilityService != null)
				{
					long characterId = character.ID;
					EnqueuePersistence(() => PersistKnownAbilityAsync(knownAbilityService, characterId, id));
				}

				broadcasts.Add(singleBroadcastFactory(reward));
			}

			if (broadcasts.Count > 0)
			{
				character.Owner.Broadcast(multiBroadcastFactory(broadcasts), true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Asynchronously persists a known ability to the database.
		/// </summary>
		/// <param name="service">The known ability service.</param>
		/// <param name="characterId">The character ID.</param>
		/// <param name="templateId">The ability template ID.</param>
		private async Task PersistKnownAbilityAsync(ICharacterKnownAbilityService service, long characterId, int templateId)
		{
			try
			{
				DatabaseResult result = await service.PersistAsync(characterId, templateId, 1);
				if (!result.IsSuccess)
				{
					await Log.Warning("AchievementSystem", $"PersistKnownAbilityAsync DB error (CharID={characterId}, TemplateID={templateId}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("AchievementSystem", $"Error persisting known ability (CharID={characterId}, TemplateID={templateId}): {ex}");
			}
		}

		/// <summary>
		/// Handles ability rewards for achievement tiers, processes learning and broadcasting new base abilities.
		/// </summary>
		/// <param name="knownAbilityService">Async service for persisting known abilities, or null if unavailable.</param>
		/// <param name="character">Player character receiving rewards.</param>
		/// <param name="tier">Achievement tier containing ability rewards.</param>
		private void HandleAbilityRewards(ICharacterKnownAbilityService knownAbilityService, IPlayerCharacter character, AchievementTier tier)
		{
			HandleAbilityGenericRewards<BaseAbilityTemplate, KnownAbilityAddBroadcast, KnownAbilityAddMultipleBroadcast>(
				knownAbilityService,
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
		/// <param name="knownAbilityService">Async service for persisting known abilities, or null if unavailable.</param>
		/// <param name="character">Player character receiving rewards.</param>
		/// <param name="tier">Achievement tier containing ability event rewards.</param>
		private void HandleAbilityEventRewards(ICharacterKnownAbilityService knownAbilityService, IPlayerCharacter character, AchievementTier tier)
		{
			HandleAbilityGenericRewards<AbilityEvent, KnownAbilityEventAddBroadcast, KnownAbilityEventAddMultipleBroadcast>(
				knownAbilityService,
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
		/// Game logic and Broadcasts are synchronous. DB persistence is fire-and-forget async.
		/// </summary>
		/// <param name="inventoryService">Async service for persisting inventory items, or null if unavailable.</param>
		/// <param name="bankService">Async service for persisting bank items, or null if unavailable.</param>
		/// <param name="character">Player character receiving rewards.</param>
		/// <param name="tier">Achievement tier containing item rewards.</param>
		private void HandleItemRewards(ICharacterInventoryService inventoryService, ICharacterBankService bankService, IPlayerCharacter character, AchievementTier tier)
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

							// Fire-and-forget async DB persist — build DTO on main thread for thread safety
							if (inventoryService != null)
							{
								item.Version++;
								var dto = new CharacterInventoryData(
									id: item.ID,
									version: item.Version,
									characterID: character.ID,
									templateID: item.Template.ID,
									slot: item.Slot,
									seed: item.IsGenerated ? item.Generator.Seed : 0,
									amount: item.IsStackable ? item.Stackable.Amount : 0
								);
								EnqueuePersistence(() => PersistInventorySlotAsync(inventoryService, dto));
							}

							modifiedItemBroadcasts.Add(new InventorySetItemBroadcast()
							{
								InstanceID = item.ID,
								TemplateID = item.Template.ID,
								Slot = item.Slot,
								Seed = item.IsGenerated ? item.Generator.Seed : 0,
								StackSize = item.IsStackable ? item.Stackable.Amount : 0,
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

					if (bankController.TryAddItem(newItem, out List<Item> modifiedItems))
					{
						foreach (Item item in modifiedItems)
						{
							if (item == null)
							{
								continue;
							}

							// Fire-and-forget async DB persist — build DTO on main thread for thread safety
							if (bankService != null)
							{
								item.Version++;
								var dto = new CharacterBankData(
									id: item.ID,
									version: item.Version,
									characterID: character.ID,
									templateID: item.Template.ID,
									slot: item.Slot,
									seed: item.IsGenerated ? item.Generator.Seed : 0,
									amount: item.IsStackable ? item.Stackable.Amount : 0
								);
								EnqueuePersistence(() => PersistBankSlotAsync(bankService, dto));
							}

							modifiedItemBroadcasts.Add(new BankSetItemBroadcast()
							{
								InstanceID = item.ID,
								TemplateID = item.Template.ID,
								Slot = item.Slot,
								Seed = item.IsGenerated ? item.Generator.Seed : 0,
								StackSize = item.IsStackable ? item.Stackable.Amount : 0,
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
			else
			{
				// Both inventory and bank are full — notify the player so items are not silently lost.
				Log.Warning("AchievementSystem", $"Achievement item rewards dropped for CharID={character.ID}: both inventory and bank are full ({itemRewards.Count} items lost).");
				character.Owner.Broadcast(new ChatBroadcast()
				{
					Channel = ChatChannel.System,
					Text = "Your inventory and bank are full. Achievement item rewards could not be delivered.",
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Asynchronously persists an inventory item slot to the database.
		/// </summary>
		/// <param name="service">The inventory service.</param>
		/// <param name="dto">Pre-built DTO captured on the main thread.</param>
		private async Task PersistInventorySlotAsync(ICharacterInventoryService service, CharacterInventoryData dto)
		{
			try
			{
				DatabaseResult<long> result = await service.PersistAsync(dto);
				if (!result.IsSuccess)
				{
					await Log.Warning("AchievementSystem", $"PersistInventorySlotAsync DB error (CharID={dto.CharacterID}, Slot={dto.Slot}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("AchievementSystem", $"Error persisting inventory slot (CharID={dto.CharacterID}, Slot={dto.Slot}): {ex}");
			}
		}

		/// <summary>
		/// Asynchronously persists a bank item slot to the database.
		/// </summary>
		/// <param name="service">The bank service.</param>
		/// <param name="dto">Pre-built DTO captured on the main thread.</param>
		private async Task PersistBankSlotAsync(ICharacterBankService service, CharacterBankData dto)
		{
			try
			{
				DatabaseResult<long> result = await service.PersistAsync(dto);
				if (!result.IsSuccess)
				{
					await Log.Warning("AchievementSystem", $"PersistBankSlotAsync DB error (CharID={dto.CharacterID}, Slot={dto.Slot}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("AchievementSystem", $"Error persisting bank slot (CharID={dto.CharacterID}, Slot={dto.Slot}): {ex}");
			}
		}
	}
}