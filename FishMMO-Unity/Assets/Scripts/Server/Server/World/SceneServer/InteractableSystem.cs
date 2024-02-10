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
		public int MaxAbilityCount = 25;

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
		private void OnServerInteractableBroadcastReceived(NetworkConnection conn, InteractableBroadcast msg, Channel channel)
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
			if (!WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName.Value, out WorldSceneDetails details))
			{
				UnityEngine.Debug.Log("Missing Scene:" + character.SceneName.Value);
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
			Character character = conn.FirstObject.GetComponent<Character>();
			if (character == null ||
				!character.TryGet(out InventoryController inventoryController))
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
					if (inventoryController.Currency < itemTemplate.Price)
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
						if (inventoryController.TryAddItem(newItem, out List<Item> modifiedItems) &&
							modifiedItems != null &&
							modifiedItems.Count > 0)
						{
							// remove the price from the characters currency
							inventoryController.Currency -= itemTemplate.Price;

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
							Server.Broadcast(conn, new InventorySetMultipleItemsBroadcast()
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
				!character.TryGet(out AbilityController abilityController) ||
				abilityController.KnowsAbility(template.ID) ||
				!character.TryGet(out InventoryController inventoryController) ||
				inventoryController.Currency < template.Price)
			{
				return;
			}

			// learn the ability
			abilityController.LearnBaseAbilities(new List<BaseAbilityTemplate> { template });

			// remove the price from the characters currency
			inventoryController.Currency -= template.Price;

			// add the known ability to the database
			CharacterKnownAbilityService.Add(dbContext, character.ID, template.ID);

			// tell the client about the new ability event
			Server.Broadcast(conn, new KnownAbilityAddBroadcast()
			{
				templateID = template.ID,
			}, true, Channel.Reliable);
		}

		public void OnServerAbilityCraftBroadcastReceived(NetworkConnection conn, AbilityCraftBroadcast msg, Channel channel)
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
			if (character == null ||
				!character.TryGet(out AbilityController abilityController))
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
			if (!abilityController.KnowsAbility(mainAbility.ID))
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
					if (!abilityController.KnowsAbility(eventTemplate.ID))
					{
						return;
					}
					price += eventTemplate.Price;
				}
			}

			if (character.Currency.Value < price)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			if (CharacterAbilityService.GetCount(dbContext, character.ID) >= MaxAbilityCount)
			{
				// too many abilities! tell the player to forget a few of them first...
				return;
			}

			Ability newAbility = new Ability(mainAbility, msg.events);
			if (newAbility == null)
			{
				return;
			}

			CharacterAbilityService.UpdateOrAdd(dbContext, character.ID, newAbility);

			abilityController.LearnAbility(newAbility);

			character.Currency.Value -= price;

			AbilityAddBroadcast abilityAddBroadcast = new AbilityAddBroadcast()
			{
				id = newAbility.ID,
				templateID = newAbility.Template.ID,
				events = msg.events,
			};

			Server.Broadcast(conn, abilityAddBroadcast, true, Channel.Reliable);
		}
	}
}