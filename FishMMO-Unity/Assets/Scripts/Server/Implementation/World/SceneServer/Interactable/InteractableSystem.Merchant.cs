using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Merchant purchase handling: item purchases, ability learning, and ability event learning from merchant NPCs.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Handles a <see cref="MerchantPurchaseBroadcast"/> from a client. Validates the merchant interactable,
		/// checks sufficient currency, and processes the purchase (item, ability, or ability event) based on the tab type.
		/// </summary>
		private void OnServerMerchantPurchaseBroadcastReceived(NetworkConnection conn, MerchantPurchaseBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			// validate connection character
			if (conn.FirstObject == null)
			{
				return;
			}
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();

			if (character == null ||
				!character.TryGet(out IInventoryController inventoryController) ||
				!CharacterStateValidation.CanAct(character))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			try
			{

				// validate template exists
				MerchantTemplate merchantTemplate = MerchantTemplate.Get<MerchantTemplate>(msg.ID);
				if (merchantTemplate == null)
				{
					return;
				}

				// validate scene
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out _))
				{
					Log.Debug("InteractableSystem", "Missing Scene:" + character.SceneName);
					return;
				}

				// validate scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				// validate interactable
				IInteractable interactable = sceneObject.GameObject.GetComponent<IInteractable>();
				if (interactable == null ||
					!interactable.InRange(character.Transform))
				{
					return;
				}
				IMerchant merchant = interactable as IMerchant;
				if (merchant == null ||
					merchantTemplate.ID != merchant.Template.ID)
				{
					return;
				}

				switch (msg.Type)
				{
					case MerchantTabType.Item:
						if (merchantTemplate.Items == null ||
							msg.Index < 0 ||
							msg.Index >= merchantTemplate.Items.Count)
						{
							return;
						}

						BaseItemTemplate itemTemplate = merchantTemplate.Items[msg.Index];
						if (itemTemplate == null)
						{
							return;
						}

						// do we have enough currency to purchase this?
						if (currencyTemplate == null)
						{
							Log.Debug("InteractableSystem", "currencyTemplate is null.");
							return;
						}
						if (!character.TryGet(out ICharacterAttributeController attributeController) ||
							!attributeController.TryGetAttribute(currencyTemplate, out CharacterAttribute currency) ||
							currency.FinalValue < itemTemplate.Price ||
							itemTemplate.Price <= 0)
						{
							return;
						}

						Item newItem = new Item(itemTemplate, 1);

						if (SendNewItemBroadcast(conn, character, inventoryController, newItem))
						{
							currency.AddValue(-itemTemplate.Price);

							// Persist the currency deduction to the database.
							// Without this, server restart would restore the full currency
							// while the purchased item remains — an infinite money exploit.
							PersistMerchantAttributesAsync(character);
						}
						break;
					case MerchantTabType.Ability:
						if (merchantTemplate.Abilities != null &&
							msg.Index >= 0 &&
							msg.Index < merchantTemplate.Abilities.Count)
						{
							LearnAbilityTemplate(conn, character, merchantTemplate.Abilities[msg.Index]);
						}
						break;
					case MerchantTabType.AbilityEvent:
						if (merchantTemplate.AbilityEvents != null &&
							msg.Index >= 0 &&
							msg.Index < merchantTemplate.AbilityEvents.Count)
						{
							LearnAbilityEvent(conn, character, merchantTemplate.AbilityEvents[msg.Index]);
						}
						break;
					default: return;
				}

				// Increment achievement for any merchant interaction
				if (merchant != null &&
					merchant.AchievementTemplate != null &&
					character.TryGet(out IAchievementController achievementController))
				{
					achievementController.Increment(merchant.AchievementTemplate, 1);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Generic helper that validates a character can learn an ability or event, checks currency, persists
		/// the known ability asynchronously, applies the learned state, deducts currency, and notifies the client.
		/// </summary>
		/// <typeparam name="TTemplate">The ability or event template type.</typeparam>
		/// <typeparam name="TBroadcast">The broadcast type for notifying the client.</typeparam>
		/// <param name="conn">Client connection to notify.</param>
		/// <param name="character">Character purchasing the ability or event.</param>
		/// <param name="template">The template to learn.</param>
		/// <param name="knowsFunc">Function to check if the character already knows the template.</param>
		/// <param name="learnFunc">Function to apply the learned template to the ability controller.</param>
		/// <param name="idSelector">Function to extract the template ID.</param>
		/// <param name="priceSelector">Function to extract the template price.</param>
		/// <param name="broadcastFactory">Function to create the broadcast for the client.</param>
		private void LearnAbilityGeneric<TTemplate, TBroadcast>(
			NetworkConnection conn,
			IPlayerCharacter character,
			TTemplate template,
			Func<IAbilityController, int, bool> knowsFunc,
			Action<IAbilityController, List<TTemplate>> learnFunc,
			Func<TTemplate, int> idSelector,
			Func<TTemplate, int> priceSelector,
			Func<TTemplate, TBroadcast> broadcastFactory)
			where TTemplate : class
			where TBroadcast : struct, IBroadcast
		{
			if (template == null || character == null || !character.TryGet(out IAbilityController abilityController) || knowsFunc(abilityController, idSelector(template)))
			{
				return;
			}

			if (currencyTemplate == null)
			{
				Log.Debug("InteractableSystem", "currencyTemplate is null.");
				return;
			}
			if (!character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(currencyTemplate, out CharacterAttribute currency) ||
				currency.FinalValue < priceSelector(template))
			{
				Log.Debug("InteractableSystem", "Not enough currency!");
				return;
			}

			long charID = character.ID;
			int templateID = idSelector(template);
			if (!TryEnqueueAsyncWork(() => PersistKnownAbilityAsync(charID, templateID), charID))
			{
				Log.Warning("InteractableSystem", $"LearnAbilityGeneric: Async worker rejected known-ability persist for CharID={charID}, TemplateID={templateID}.");
				return;
			}

			// learn the ability or event
			learnFunc(abilityController, new List<TTemplate> { template });

			// remove the price from the characters currency
			currency.AddValue(-priceSelector(template));

			// tell the client about the new ability/event
			Server.NetworkWrapper.Broadcast(conn, broadcastFactory(template), true, Channel.Reliable);
		}

		/// <summary>
		/// Persists a known ability or event to the database asynchronously.
		/// </summary>
		private async Task PersistKnownAbilityAsync(long characterID, int templateID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterKnownAbilityService>(out var knownAbilityService))
				{
					return;
				}

				DatabaseResult result = await knownAbilityService.PersistAsync(characterID, templateID, 1);
				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"PersistKnownAbilityAsync DB error (CharID={characterID}, TemplateID={templateID}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error persisting known ability: {ex}");
			}
		}

		/// <summary>
		/// Learns a base ability template and synchronizes the result to the client.
		/// </summary>
		/// <typeparam name="T">Concrete base ability template type.</typeparam>
		/// <param name="conn">Client connection to notify.</param>
		/// <param name="character">Character learning the ability.</param>
		/// <param name="template">Ability template to learn.</param>
		public void LearnAbilityTemplate<T>(NetworkConnection conn, IPlayerCharacter character, T template) where T : BaseAbilityTemplate
		{
			LearnAbilityGeneric<BaseAbilityTemplate, KnownAbilityAddBroadcast>(
				conn,
				character,
				template,
				(abilityController, id) => abilityController.KnowsAbility(id),
				(abilityController, list) => abilityController.LearnBaseAbilities(list.Cast<BaseAbilityTemplate>().ToList()),
				t => t.ID,
				t => t.Price,
				t => new KnownAbilityAddBroadcast { TemplateID = t.ID }
			);
		}

		/// <summary>
		/// Learns an ability event template and synchronizes the result to the client.
		/// </summary>
		/// <typeparam name="T">Concrete ability event type.</typeparam>
		/// <param name="conn">Client connection to notify.</param>
		/// <param name="character">Character learning the ability event.</param>
		/// <param name="template">Ability event template to learn.</param>
		public void LearnAbilityEvent<T>(NetworkConnection conn, IPlayerCharacter character, T template) where T : AbilityEvent
		{
			LearnAbilityGeneric<AbilityEvent, KnownAbilityEventAddBroadcast>(
				conn,
				character,
				template,
				(abilityController, id) => abilityController.KnowsAbilityEvent(id),
				(abilityController, list) => abilityController.LearnAbilityEvents(list.Cast<AbilityEvent>().ToList()),
				t => t.ID,
				t => t.Price,
				t => new KnownAbilityEventAddBroadcast { TemplateID = t.ID }
			);
		}

		/// <summary>
		/// Persists character attribute data (including currency) to the database after a merchant purchase.
		/// Prevents currency rollback on server restart.
		/// </summary>
		private void PersistMerchantAttributesAsync(IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			long charID = character.ID;

			var dtos = new List<CharacterAttributeData>();
			foreach (var kvp in attributeController.Attributes)
			{
				kvp.Value.Version++;
				dtos.Add(new CharacterAttributeData(
					id: 0,
					version: kvp.Value.Version,
					characterID: charID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: 0.0f
				));
			}
			foreach (var kvp in attributeController.ResourceAttributes)
			{
				kvp.Value.Version++;
				dtos.Add(new CharacterAttributeData(
					id: 0,
					version: kvp.Value.Version,
					characterID: charID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: kvp.Value.CurrentValue
				));
			}

			if (dtos.Count > 0)
			{
				TryEnqueueAsyncWork(() => PersistMerchantAttributesToDbAsync(dtos, charID), charID);
			}
		}

		/// <summary>
		/// Asynchronously persists attribute changes from merchant purchases to the database.
		/// </summary>
		private async System.Threading.Tasks.Task PersistMerchantAttributesToDbAsync(List<CharacterAttributeData> dtos, long charID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterAttributeService>(out var service))
				{
					await Log.Error("InteractableSystem", "PersistMerchantAttributesToDbAsync: Failed to resolve ICharacterAttributeService");
					return;
				}

				DatabaseResult result = await service.PersistAsync(dtos);
				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"PersistMerchantAttributesToDbAsync DB error (CharID={charID}, {dtos.Count} attrs): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (System.Exception ex)
			{
				await Log.Error("InteractableSystem", $"PersistMerchantAttributesToDbAsync failed (CharID={charID}): {ex}");
			}
		}
	}
}