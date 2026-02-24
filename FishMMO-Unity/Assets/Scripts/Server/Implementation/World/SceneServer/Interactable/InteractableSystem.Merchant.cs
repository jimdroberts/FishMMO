using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
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
				!character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return;
			}


			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.MerchantPurchase, out long guardKey))
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
				Merchant merchant = interactable as Merchant;
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
							currency.FinalValue < itemTemplate.Price)
						{
							return;
						}

						Item newItem = new Item(itemTemplate, 1);

						if (SendNewItemBroadcast(conn, character, inventoryController, newItem))
						{
							currency.AddValue(-itemTemplate.Price);
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
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

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

				await knownAbilityService.PersistAsync(characterID, templateID, 1);
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
	}
}