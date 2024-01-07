using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Server.DatabaseServices;
using FishMMO.Database.Npgsql;
using System.Collections.Generic;

namespace FishMMO.Server
{
	/// <summary>
	/// This service helps the server validate clients interacting with Interactable objects in scenes.
	/// </summary>
	public class InteractableSystem : ServerBehaviour
	{
		public WorldSceneDetailsCache WorldSceneDetailsCache;

		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived, true);
				ServerManager.RegisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived, true);
				ServerManager.RegisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived, true);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived);
				ServerManager.UnregisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived);
				ServerManager.UnregisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived);
			}
		}

		/// <summary>
		/// Interactable broadcast received from a character.
		/// </summary>
		private void OnServerInteractableBroadcastReceived(NetworkConnection conn, InteractableBroadcast msg)
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
			Character character = conn.FirstObject.GetComponent<Character>();
			if (character == null)
			{
				return;
			}

			// valid scene object
			if (!WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out WorldSceneDetails details))
			{
				UnityEngine.Debug.Log("Missing Scene:" + character.SceneName);
				return;
			}
			if (!SceneObjectUID.IDs.TryGetValue(msg.interactableID, out SceneObjectUID sceneObject))
			{
				if (sceneObject == null)
				{
					UnityEngine.Debug.Log("Missing SceneObject");
				}
				else
				{
					UnityEngine.Debug.Log("Missing ID:" + msg.interactableID);
				}
				return;
			}

			IInteractable interactable = sceneObject.GetComponent<IInteractable>();
			interactable?.OnInteract(character);
		}

		private void OnServerMerchantPurchaseBroadcastReceived(NetworkConnection conn, MerchantPurchaseBroadcast msg)
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
			Character character = conn.FirstObject.GetComponent<Character>();
			if (character == null &&
				character.InventoryController != null)
			{
				return;
			}

			// validate request
			MerchantTemplate merchantTemplate = MerchantTemplate.Get<MerchantTemplate>(msg.id);
			if (merchantTemplate == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			switch (msg.type)
			{
				case MerchantTabType.Item:
					BaseItemTemplate itemTemplate = merchantTemplate.Items[msg.index];
					if (itemTemplate == null)
					{
						return;
					}

					// do we have enough currency to purchase this?
					if (character.InventoryController.Currency < itemTemplate.Price)
					{
						return;
					}

					if (merchantTemplate.Items != null &&
						merchantTemplate.Items.Count >= msg.index)
					{
						Item newItem = new Item(itemTemplate, 1);
						if (newItem == null)
						{
							return;
						}
						
						List<InventorySetItemBroadcast> modifiedItemBroadcasts = new List<InventorySetItemBroadcast>();

						// see if we have successfully added the item
						if (character.InventoryController.TryAddItem(newItem, out List<Item> modifiedItems) &&
							modifiedItems != null &&
							modifiedItems.Count > 0)
						{
							// remove the price from the characters currency
							character.InventoryController.Currency -= itemTemplate.Price;

							// add slot update requests to our message
							foreach (Item item in modifiedItems)
							{
								// just in case..
								if (item == null)
								{
									continue;
								}

								// update or add the item to the database and initialize
								CharacterInventoryService.SetSlot(dbContext, character.ID, item);

								// create the new item broadcast
								modifiedItemBroadcasts.Add(new InventorySetItemBroadcast()
								{
									instanceID = item.ID,
									templateID = item.Template.ID,
									slot = item.Slot,
									seed = item.IsGenerated ? item.Generator.Seed : 0,
									stackSize = item.IsStackable ? item.Stackable.Amount : 0,
								});
							}
						}

						// tell the client they have new items
						if (modifiedItemBroadcasts.Count > 0)
						{
							conn.Broadcast(new InventorySetMultipleItemsBroadcast()
							{
								items = modifiedItemBroadcasts,
							}, true, Channel.Reliable);
						}
					}
					break;
				case MerchantTabType.Ability:
					if (merchantTemplate.Abilities != null &&
						merchantTemplate.Abilities.Count >= msg.index)
					{
						LearnAbilityTemplate(dbContext, conn, character, merchantTemplate.Abilities[msg.index]);
					}
					break;
				case MerchantTabType.AbilityEvent:
					if (merchantTemplate.AbilityEvents != null &&
						merchantTemplate.AbilityEvents.Count >= msg.index)
					{
						LearnAbilityTemplate(dbContext, conn, character, merchantTemplate.AbilityEvents[msg.index]);
					}
					break;
				default: return;
			}
		}

		public void LearnAbilityTemplate<T>(NpgsqlDbContext dbContext, NetworkConnection conn, Character character, T template) where T : BaseAbilityTemplate
		{
			// do we already know this ability?
			if (template == null ||
				character == null ||
				character.AbilityController == null ||
				character.AbilityController.KnowsAbility(template.ID) ||
				character.InventoryController.Currency < template.Price)
			{
				return;
			}

			// learn the ability
			character.AbilityController.LearnBaseAbilities(new List<BaseAbilityTemplate> { template });

			// remove the price from the characters currency
			character.InventoryController.Currency -= template.Price;

			// add the known ability to the database
			CharacterKnownAbilityService.Add(dbContext, character.ID, template.ID);

			// tell the client about the new ability event
			conn.Broadcast(new KnownAbilityAddBroadcast()
			{
				templateID = template.ID,
			}, true, Channel.Reliable);
		}

		public void OnServerAbilityCraftBroadcastReceived(NetworkConnection conn, AbilityCraftBroadcast msg)
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
			Character character = conn.FirstObject.GetComponent<Character>();
			if (character == null &&
				character.AbilityController != null)
			{
				return;
			}

			// validate main ability exists
			AbilityTemplate mainAbility = AbilityTemplate.Get<AbilityTemplate>(msg.templateID);
			if (mainAbility == null)
			{
				return;
			}

			// validate that the character knows the main ability
			if (!character.AbilityController.KnowsAbility(mainAbility.ID))
			{
				return;
			}

			long price = mainAbility.Price;

			// validate eventIds if there are any...
			if (msg.events != null)
			{
				for (int i = 0; i < msg.events.Count; ++i)
				{
					AbilityEvent eventTemplate = AbilityEvent.Get<AbilityEvent>(msg.events[i]);
					if (eventTemplate == null)
					{
						// couldn't validate this event...
						return;
					}
					// validate that the character knows the ability event
					if (!character.AbilityController.KnowsAbility(eventTemplate.ID))
					{
						return;
					}
					price += eventTemplate.Price;
				}
			}

			if (character.Currency < price)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			Ability newAbility = new Ability(mainAbility, msg.events);
			if (newAbility == null)
			{
				return;
			}

			CharacterAbilityService.UpdateOrAdd(dbContext, character.ID, newAbility);

			character.AbilityController.LearnAbility(newAbility);

			character.Currency -= price;

			AbilityAddBroadcast abilityAddBroadcast = new AbilityAddBroadcast()
			{
				id = newAbility.ID,
				templateID = newAbility.Template.ID,
				events = msg.events,
			};

			conn.Broadcast(abilityAddBroadcast, true, Channel.Reliable);
		}
	}
}