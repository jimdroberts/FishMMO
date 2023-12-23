using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
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
						Item newItem = new Item(1, itemTemplate, 1);
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
								modifiedItemBroadcasts.Add(new InventorySetItemBroadcast()
								{
									instanceID = item.InstanceID,
									templateID = item.Template.ID,
									slot = item.Slot,
									stackSize = item.IsStackable ? item.Stackable.Amount : 0,
								});
							}
						}

						// save modified inventory slots to the database immediately

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

					}
					break;
				case MerchantTabType.AbilityEvent:
					if (merchantTemplate.AbilityEvents != null &&
						merchantTemplate.AbilityEvents.Count >= msg.Index)
					{

					}
					break;
				default: return;
			}
		}
	}
}