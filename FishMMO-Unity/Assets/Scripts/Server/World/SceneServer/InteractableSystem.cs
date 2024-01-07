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
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived);
				ServerManager.UnregisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived);
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
			if (!SceneObjectUID.IDs.TryGetValue(msg.InteractableID, out SceneObjectUID sceneObject))
			{
				if (sceneObject == null)
				{
					UnityEngine.Debug.Log("Missing SceneObject");
				}
				else
				{
					UnityEngine.Debug.Log("Missing ID:" + msg.InteractableID);
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
			MerchantTemplate merchantTemplate = MerchantTemplate.Get<MerchantTemplate>(msg.ID);
			if (merchantTemplate == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			switch (msg.Type)
			{
				case MerchantTabType.Item:
					BaseItemTemplate itemTemplate = merchantTemplate.Items[msg.Index];
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
						merchantTemplate.Items.Count >= msg.Index)
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
						merchantTemplate.Abilities.Count >= msg.Index)
					{
						LearnAbilityTemplate(dbContext, conn, character, merchantTemplate.Abilities[msg.Index]);
					}
					break;
				case MerchantTabType.AbilityEvent:
					if (merchantTemplate.AbilityEvents != null &&
						merchantTemplate.AbilityEvents.Count >= msg.Index)
					{
						LearnAbilityTemplate(dbContext, conn, character, merchantTemplate.AbilityEvents[msg.Index]);
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
	}
}