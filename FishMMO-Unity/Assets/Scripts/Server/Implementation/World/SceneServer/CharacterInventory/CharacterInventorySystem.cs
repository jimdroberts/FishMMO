using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages player character inventory, equipment, and bank operations with validation and database persistence.
	/// </summary>
	[CreateAssetMenu(fileName = "CharacterInventorySystem", menuName = "FishMMO/Server/SceneServer/Character Inventory System", order = 1)]
	[RequiresDataContainer(typeof(CharacterInventorySystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class CharacterInventorySystem : ServerBehaviour, ICharacterInventorySystem
	{
		/// <summary>
		/// Debounce window in milliseconds applied to inventory ingress operations.
		/// </summary>
		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between identical inventory requests from the same connection")]
		[SerializeField] private int ingressDebounceMilliseconds = 60;

		/// <summary>
		/// Interval in seconds between bounded ingress-guard cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		/// <summary>
		/// Guard entry time-to-live in seconds.
		/// </summary>
		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		/// <summary>
		/// Maximum number of stale guard entries removed per sweep pass.
		/// </summary>
		[Tooltip("Maximum stale ingress guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		/// <summary>
		/// Maximum entries in the ingress tracker dictionaries before new requests are rejected.
		/// </summary>

		/// <summary>
		/// Global per-connection rate limit in milliseconds across all inventory operations.
		/// </summary>
		private const int GlobalPerConnectionRateMilliseconds = 15;

		/// <summary>
		/// Operation codes used by ingress guards.
		/// </summary>
		private enum IngressOperation : byte
		{
			InventoryRemove = 1,
			InventorySwap = 2,
			EquipmentEquip = 3,
			EquipmentUnequip = 4,
			BankRemove = 5,
			BankSwap = 6,
		}

		/// <summary>
		/// Initializes the character inventory system, registering broadcast handlers for inventory, equipment, and bank actions.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("CharacterInventorySystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.Database == null ||
				Server.Database.ServiceRegistry == null)
			{
				Log.Error("CharacterInventorySystem", "InitializeOnce: Database or ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			var registry = Server.Database.ServiceRegistry;
			if (!registry.TryGet<ICharacterInventoryService>(out _) ||
				!registry.TryGet<ICharacterBankService>(out _) ||
				!registry.TryGet<ICharacterEquipmentService>(out _) ||
				!registry.TryGet<ICharacterAttributeService>(out _))
			{
				Log.Error("CharacterInventorySystem", "InitializeOnce: One or more required database services could not be resolved");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterInventorySystemRuntimeData>(out var runtimeData))
			{
				Log.Error("CharacterInventorySystem", "InitializeOnce: ICharacterInventorySystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Inventory broadcasts
			Server.NetworkWrapper.RegisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<InventorySwapItemSlotsBroadcast>(OnServerInventorySwapItemSlotsBroadcastReceived, true);

			// Equipment broadcasts
			Server.NetworkWrapper.RegisterBroadcast<EquipmentEquipItemBroadcast>(OnServerEquipmentEquipItemBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<EquipmentUnequipItemBroadcast>(OnServerEquipmentUnequipItemBroadcastReceived, true);

			// Bank broadcasts
			Server.NetworkWrapper.RegisterBroadcast<BankRemoveItemBroadcast>(OnServerBankRemoveItemBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<BankSwapItemSlotsBroadcast>(OnServerBankSwapItemSlotsBroadcastReceived, true);

			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);

			Log.Debug("CharacterInventorySystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the character inventory system, unregistering broadcast handlers for inventory, equipment, and bank actions.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("CharacterInventorySystem", "OnDeinitialize: Server is null");
				return;
			}

			// Inventory broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<InventorySwapItemSlotsBroadcast>(OnServerInventorySwapItemSlotsBroadcastReceived);

			// Equipment broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<EquipmentEquipItemBroadcast>(OnServerEquipmentEquipItemBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<EquipmentUnequipItemBroadcast>(OnServerEquipmentUnequipItemBroadcastReceived);

			// Bank broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<BankRemoveItemBroadcast>(OnServerBankRemoveItemBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<BankSwapItemSlotsBroadcast>(OnServerBankSwapItemSlotsBroadcastReceived);

			if (Server.DataContainerRegistry.TryGet<ICharacterInventorySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}
		}

		/// <summary>
		/// Sweeps stale ingress guard entries.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterInventorySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
		}

		/// <summary>
		/// Attempts to acquire ingress debounce and in-flight guard for a connection operation.
		/// </summary>
		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<ICharacterInventorySystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, (byte)operation, ingressDebounceMilliseconds, out guardKey, GlobalPerConnectionRateMilliseconds);
		}

		/// <summary>
		/// Releases an ingress in-flight guard key.
		/// </summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterInventorySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Swaps two items within the same container and collects the affected items.
		/// </summary>
		/// <param name="container">Item container to swap items in.</param>
		/// <param name="fromIndex">Source slot index.</param>
		/// <param name="toIndex">Target slot index.</param>
		/// <param name="affectedItems">Out: list of items whose slots changed.</param>
		/// <returns>True if swap succeeded, false otherwise.</returns>
		public bool SwapContainerItems(IItemContainer container, int fromIndex, int toIndex, out List<Item> affectedItems)
		{
			affectedItems = null;
			if (container != null &&
				container.SwapItemSlots(fromIndex, toIndex, out Item fromItem, out Item toItem))
			{
				affectedItems = new List<Item>(2);
				if (fromItem != null) affectedItems.Add(fromItem);
				if (toItem != null) affectedItems.Add(toItem);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Swaps items between two containers and collects affected items and deleted slots.
		/// </summary>
		/// <param name="from">Source item container.</param>
		/// <param name="to">Target item container.</param>
		/// <param name="fromIndex">Source slot index.</param>
		/// <param name="toIndex">Target slot index.</param>
		/// <param name="affectedFromItems">Out: items placed into the source container.</param>
		/// <param name="deletedFromSlots">Out: slot indices vacated in the source container.</param>
		/// <param name="affectedToItems">Out: items placed into the destination container.</param>
		/// <returns>True if swap succeeded, false otherwise.</returns>
		public bool SwapContainerItems(IItemContainer from, IItemContainer to, int fromIndex, int toIndex,
			out List<Item> affectedFromItems, out List<long> deletedFromSlots, out List<Item> affectedToItems)
		{
			affectedFromItems = null;
			deletedFromSlots = null;
			affectedToItems = null;

			// same container... do the quick swap
			if (from == to)
			{
				if (SwapContainerItems(from, fromIndex, toIndex, out affectedFromItems))
				{
					return true;
				}
				return false;
			}
			if (from != null &&
				to != null &&
				from.TryGetItem(fromIndex, out Item fromItem))
			{
				affectedFromItems = new List<Item>(1);
				deletedFromSlots = new List<long>(1);
				affectedToItems = new List<Item>(1);

				// Capture original state for rollback in case of an unexpected exception.
				Item originalFromItem = fromItem;
				Item originalToItem = null;
				bool hadToItem = to.TryGetItem(toIndex, out Item toItem);

				try
				{
					// check if we need to swap items
					if (hadToItem)
					{
						originalToItem = toItem;
						// put the target item in the old container
						from.SetItemSlot(toItem, fromIndex);
						affectedFromItems.Add(toItem);
					}
					// the slot we want to move the item to is empty
					else
					{
						// remove the item from the old container
						from.SetItemSlot(null, fromIndex);
						deletedFromSlots.Add(fromItem.Slot);
					}
					// put the item in the new container
					to.SetItemSlot(fromItem, toIndex);
					affectedToItems.Add(fromItem);
				}
				catch (Exception ex)
				{
					// Rollback: restore both containers to their original state.
					from.SetItemSlot(originalFromItem, fromIndex);
					if (hadToItem)
					{
						to.SetItemSlot(originalToItem, toIndex);
					}
					affectedFromItems.Clear();
					deletedFromSlots.Clear();
					affectedToItems.Clear();
					Log.Error("CharacterInventorySystem", $"Cross-container swap failed, rolled back: {ex}");
					return false;
				}
				return true;
			}
			return false;
		}

		/// <summary>
		/// Handles broadcast to remove an item from the player's inventory, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">InventoryRemoveItemBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerInventoryRemoveItemBroadcastReceived(NetworkConnection conn, InventoryRemoveItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.InventoryRemove, out long guardKey))
			{
				return;
			}

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character != null &&
					!character.IsTeleporting &&
					character.TryGet(out IInventoryController inventoryController))
				{
					// Validate slot bounds before processing
					if (!inventoryController.IsValidSlot(msg.Slot))
					{
						return;
					}

					Item item = inventoryController.RemoveItem(msg.Slot);
					if (item == null)
					{
						return;
					}

					// fire-and-forget async delete
					long characterID = character.ID;
					int slot = msg.Slot;
					item.Version++;
					long version = item.Version;
					EnqueuePersistence(() => DeleteInventorySlotAsync(characterID, slot, version), characterID);

					Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles broadcast to swap item slots in the player's inventory or bank, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">InventorySwapItemSlotsBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerInventorySwapItemSlotsBroadcastReceived(NetworkConnection conn, InventorySwapItemSlotsBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.InventorySwap, out long guardKey))
			{
				return;
			}

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character == null ||
					character.IsTeleporting ||
					!character.TryGet(out IInventoryController inventoryController))
				{
					return;
				}

				long characterID = character.ID;

				switch (msg.FromInventory)
				{
					case InventoryType.Inventory:
						// Validate slot bounds before processing
						if (!inventoryController.IsValidSlot(msg.From) || !inventoryController.IsValidSlot(msg.To))
						{
							return;
						}
						// swap the items in the inventory
						if (msg.To != msg.From &&
							SwapContainerItems(inventoryController, msg.From, msg.To, out List<Item> invAffected))
						{
							// fire-and-forget async persist for each affected inventory item
							var invDtos = BuildInventoryItemDataList(characterID, invAffected);
							EnqueuePersistence(() => PersistInventoryItemsAsync(invDtos), characterID);

							// tell the client we succeeded
							Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
						}
						break;
					case InventoryType.Equipment:
						break;
					case InventoryType.Bank:
						{
							if (!character.TryGet(out IBankController bankController))
							{
								return;
							}

							// Validate banker scene object
							if (!ValidateBankerSceneObject(bankController.LastInteractableID, character))
							{
								return;
							}

							// Validate slot bounds before processing
							if (!bankController.IsValidSlot(msg.From) || !inventoryController.IsValidSlot(msg.To))
							{
								return;
							}

							if (SwapContainerItems(bankController, inventoryController, msg.From, msg.To,
								out List<Item> fromItems, out List<long> deletedSlots, out List<Item> toItems))
							{
								// Persist bank items that moved to the source (bank) container
								if (fromItems != null && fromItems.Count > 0)
								{
									var bankDtos = BuildBankItemDataList(characterID, fromItems);
									EnqueuePersistence(() => PersistBankItemsAsync(bankDtos), characterID);
								}
								// Delete vacated bank slots
								if (deletedSlots != null && deletedSlots.Count > 0)
								{
									foreach (long slot in deletedSlots)
									{
										// Deleted slots no longer have an item reference;
										// use long.MaxValue to ensure the delete succeeds.
										EnqueuePersistence(() => DeleteBankSlotAsync(characterID, (int)slot, long.MaxValue), characterID);
									}
								}
								// Persist inventory items that moved to the destination (inventory) container
								if (toItems != null && toItems.Count > 0)
								{
									var invDtos2 = BuildInventoryItemDataList(characterID, toItems);
									EnqueuePersistence(() => PersistInventoryItemsAsync(invDtos2), characterID);
								}

								// Tell the client
								Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
							}
						}
						break;
					default: break;
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles broadcast to equip an item from inventory or bank, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">EquipmentEquipItemBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerEquipmentEquipItemBroadcastReceived(NetworkConnection conn, EquipmentEquipItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.EquipmentEquip, out long guardKey))
			{
				return;
			}

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character == null ||
					character.IsTeleporting ||
					!character.TryGet(out IEquipmentController equipmentController))
				{
					return;
				}

				// Validate that the target equipment slot is a defined enum value.
				if (!Enum.IsDefined(typeof(ItemSlot), (byte)msg.Slot))
				{
					return;
				}

				long characterID = character.ID;

				switch (msg.FromInventory)
				{
					case InventoryType.Inventory:
						if (character.TryGet(out IInventoryController inventoryController) &&
							inventoryController.IsValidSlot(msg.InventoryIndex) &&
							inventoryController.TryGetItem(msg.InventoryIndex, out Item inventoryItem))
						{
							if (!equipmentController.Equip(inventoryItem, msg.InventoryIndex, inventoryController, (ItemSlot)msg.Slot))
							{
								return;
							}

							// did we replace an already equipped item?
							if (inventoryController.TryGetItem(msg.InventoryIndex, out Item prevItem))
							{
								var dto = BuildInventoryItemData(characterID, prevItem);
								EnqueuePersistence(() => PersistInventoryItemAsync(dto), characterID);
							}
							// remove the inventory item from the database
							else
							{
								// Item moved out of inventory — use long.MaxValue to ensure delete succeeds
								EnqueuePersistence(() => DeleteInventorySlotAsync(characterID, msg.InventoryIndex, long.MaxValue), characterID);
							}

							// set the equipment slot in the database
							var equipDto = BuildEquipmentItemData(characterID, inventoryItem);
							EnqueuePersistence(() => PersistEquipmentItemAsync(equipDto), characterID);

							// save attributes
							var attrDtos = BuildAttributeDataList(character);
							EnqueuePersistence(() => PersistAttributesAsync(attrDtos), characterID);

							Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
						}
						break;
					case InventoryType.Equipment:
						return;
					case InventoryType.Bank:
						{
							if (!character.TryGet(out IBankController bankController))
							{
								return;
							}

							// validate banker scene object
							if (!ValidateBankerSceneObject(bankController.LastInteractableID, character))
							{
								return;
							}

							// Validate slot bounds before processing
							if (!bankController.IsValidSlot(msg.InventoryIndex))
							{
								return;
							}

							if (bankController.TryGetItem(msg.InventoryIndex, out Item bankItem))
							{
								if (!equipmentController.Equip(bankItem, msg.InventoryIndex, bankController, (ItemSlot)msg.Slot))
								{
									return;
								}

								// did we replace an already equipped item?
								if (bankController.TryGetItem(msg.InventoryIndex, out Item prevItem))
								{
									var dto = BuildBankItemData(characterID, prevItem);
									EnqueuePersistence(() => PersistBankItemAsync(dto), characterID);
								}
								// remove the bank item from the database
								else
								{
									// Item moved out of bank — use long.MaxValue to ensure delete succeeds
									EnqueuePersistence(() => DeleteBankSlotAsync(characterID, msg.InventoryIndex, long.MaxValue), characterID);
								}

								// set the equipment slot in the database
								var equipDto = BuildEquipmentItemData(characterID, bankItem);
								EnqueuePersistence(() => PersistEquipmentItemAsync(equipDto), characterID);

								// save attributes
								var attrDtos = BuildAttributeDataList(character);
								EnqueuePersistence(() => PersistAttributesAsync(attrDtos), characterID);

								Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
							}
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

		/// <summary>
		/// Handles broadcast to unequip an item to inventory or bank, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">EquipmentUnequipItemBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerEquipmentUnequipItemBroadcastReceived(NetworkConnection conn, EquipmentUnequipItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.EquipmentUnequip, out long guardKey))
			{
				return;
			}

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character == null ||
					character.IsTeleporting ||
					!character.TryGet(out IEquipmentController equipmentController))
				{
					return;
				}

				// Validate that the equipment slot is a defined enum value.
				if (!Enum.IsDefined(typeof(ItemSlot), (byte)msg.Slot))
				{
					return;
				}

				long characterID = character.ID;

				switch (msg.ToInventory)
				{
					case InventoryType.Inventory:
						if (character.TryGet(out IInventoryController inventoryController) &&
							equipmentController.TryGetItem(msg.Slot, out Item toInventory))
						{
							// save the old slot index so we can delete the item
							int oldSlot = toInventory.Slot;

							// if we found the item we should unequip it
							if (!equipmentController.Unequip(inventoryController, msg.Slot, out List<Item> modifiedItems))
							{
								return;
							}

							// see if we have successfully added the item
							if (modifiedItems == null ||
								modifiedItems.Count < 1)
							{
								return;
							}

							// persist all modified inventory slots
							var invDtos = BuildInventoryItemDataList(characterID, modifiedItems);
							EnqueuePersistence(() => PersistInventoryItemsAsync(invDtos), characterID);

							// delete the item from the equipment table
							// Item moved out of equipment — use long.MaxValue to ensure delete succeeds
							EnqueuePersistence(() => DeleteEquipmentSlotAsync(characterID, oldSlot, long.MaxValue), characterID);

							// save attributes
							var attrDtos = BuildAttributeDataList(character);
							EnqueuePersistence(() => PersistAttributesAsync(attrDtos), characterID);

							Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
						}
						break;
					case InventoryType.Equipment:
						break;
					case InventoryType.Bank:
						{
							if (!character.TryGet(out IBankController bankController))
							{
								return;
							}

							// validate banker scene object
							if (!ValidateBankerSceneObject(bankController.LastInteractableID, character))
							{
								return;
							}

							if (equipmentController.TryGetItem(msg.Slot, out Item toBank))
							{
								int oldSlot = toBank.Slot;

								if (!equipmentController.Unequip(bankController, msg.Slot, out List<Item> modifiedItems))
								{
									return;
								}

								// see if we have successfully added the item
								if (modifiedItems == null ||
									modifiedItems.Count < 1)
								{
									return;
								}

								// persist all modified bank slots
								var bankDtos = BuildBankItemDataList(characterID, modifiedItems);
								EnqueuePersistence(() => PersistBankItemsAsync(bankDtos), characterID);

								// delete the item from the equipment table
								// Item moved out of equipment — use long.MaxValue to ensure delete succeeds
								EnqueuePersistence(() => DeleteEquipmentSlotAsync(characterID, oldSlot, long.MaxValue), characterID);

								// save attributes
								var attrDtos = BuildAttributeDataList(character);
								EnqueuePersistence(() => PersistAttributesAsync(attrDtos), characterID);

								Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
							}
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

		/// <summary>
		/// Handles broadcast to remove an item from the player's bank, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">BankRemoveItemBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerBankRemoveItemBroadcastReceived(NetworkConnection conn, BankRemoveItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.BankRemove, out long guardKey))
			{
				return;
			}

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character != null &&
					!character.IsTeleporting &&
					character.TryGet(out IBankController bankController))
				{
					// validate banker scene object before allowing bank removal
					if (!ValidateBankerSceneObject(bankController.LastInteractableID, character))
					{
						return;
					}

					// Validate slot bounds before processing
					if (!bankController.IsValidSlot(msg.Slot))
					{
						return;
					}

					Item item = bankController.RemoveItem(msg.Slot);
					if (item == null)
					{
						return;
					}

					// fire-and-forget async delete
					long characterID = character.ID;
					int slot = msg.Slot;
					item.Version++;
					long version = item.Version;
					EnqueuePersistence(() => DeleteBankSlotAsync(characterID, slot, version), characterID);

					Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles broadcast to swap item slots in the player's bank, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">BankSwapItemSlotsBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerBankSwapItemSlotsBroadcastReceived(NetworkConnection conn, BankSwapItemSlotsBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.BankSwap, out long guardKey))
			{
				return;
			}

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character == null ||
					character.IsTeleporting ||
					!character.TryGet(out IBankController bankController))
				{
					return;
				}

				// validate banker scene object
				if (!ValidateBankerSceneObject(bankController.LastInteractableID, character))
				{
					return;
				}

				long characterID = character.ID;

				switch (msg.FromInventory)
				{
					case InventoryType.Inventory:
						if (character.TryGet(out IInventoryController inventoryController) &&
							inventoryController.IsValidSlot(msg.From) &&
							bankController.IsValidSlot(msg.To) &&
							SwapContainerItems(inventoryController, bankController, msg.From, msg.To,
								out List<Item> fromItems, out List<long> deletedSlots, out List<Item> toItems))
						{
							// persist inventory items that went into source (inventory) container
							if (fromItems != null && fromItems.Count > 0)
							{
								var invDtos = BuildInventoryItemDataList(characterID, fromItems);
								EnqueuePersistence(() => PersistInventoryItemsAsync(invDtos), characterID);
							}
							// delete vacated inventory slots
							if (deletedSlots != null && deletedSlots.Count > 0)
							{
								foreach (long slot in deletedSlots)
								{
									// Deleted slots no longer have an item reference;
									// use long.MaxValue to ensure the delete succeeds.
									EnqueuePersistence(() => DeleteInventorySlotAsync(characterID, (int)slot, long.MaxValue), characterID);
								}
							}
							// persist bank items that went into destination (bank) container
							if (toItems != null && toItems.Count > 0)
							{
								var bankDtos = BuildBankItemDataList(characterID, toItems);
								EnqueuePersistence(() => PersistBankItemsAsync(bankDtos), characterID);
							}

							// tell the client
							Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
						}
						break;
					case InventoryType.Equipment:
						break;
					case InventoryType.Bank:
						// Validate slot bounds before processing
						if (!bankController.IsValidSlot(msg.From) || !bankController.IsValidSlot(msg.To))
						{
							return;
						}
						// swap the items in the bank
						if (msg.To != msg.From &&
							SwapContainerItems(bankController, msg.From, msg.To, out List<Item> bankAffected))
						{
							var bankDtos2 = BuildBankItemDataList(characterID, bankAffected);
							EnqueuePersistence(() => PersistBankItemsAsync(bankDtos2), characterID);

							// tell the client we succeeded
							Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
						}
						break;
					default: break;
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Validates that the banker scene object is present, in the correct scene, and the character is in range.
		/// </summary>
		/// <param name="sceneObjectID">ID of the scene object to validate.</param>
		/// <param name="character">Player character interacting with the banker.</param>
		/// <returns>True if validation succeeds, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool ValidateBankerSceneObject(long sceneObjectID, IPlayerCharacter character)
		{
			if (!SceneObject.Objects.TryGetValue(sceneObjectID, out ISceneObject sceneObject))
			{
				Log.Debug("CharacterInventorySystem", $"Missing SceneObject ID:{sceneObjectID}");
				return false;
			}
			if (sceneObject.GameObject.scene.handle != character.GameObject.scene.handle)
			{
				Log.Debug("CharacterInventorySystem", "Object scene mismatch.");
				return false;
			}
			IInteractable interactable = sceneObject.GameObject.GetComponent<IInteractable>();
			if (interactable == null ||
				!interactable.InRange(character.Transform))
			{
				Log.Debug("CharacterInventorySystem", $"{character.CharacterName} is not in range of {sceneObject.GameObject.name}!");
				return false;
			}
			Banker banker = interactable as Banker;
			if (banker == null)
			{
				Log.Debug("CharacterInventorySystem", $"{sceneObject.GameObject.name} is not a banker!");
				return false;
			}
			return true;
		}

		#region DTO Builders

		/// <summary>
		/// Builds a CharacterInventoryData DTO from a live Item instance.
		/// Increments item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="item">Runtime item instance to serialize.</param>
		/// <returns>Inventory DTO ready for persistence.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private CharacterInventoryData BuildInventoryItemData(long characterID, Item item)
		{
			item.Version++;
			return new CharacterInventoryData(
				id: item.ID,
				version: item.Version,
				characterID: characterID,
				templateID: item.Template.ID,
				slot: item.Slot,
				seed: item.IsGenerated ? item.Generator.Seed : 0,
				amount: item.IsStackable ? item.Stackable.Amount : 1
			);
		}

		/// <summary>
		/// Builds a list of CharacterInventoryData DTOs from live Item instances.
		/// Increments each item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="items">Runtime item instances to serialize.</param>
		/// <returns>List of inventory DTOs ready for persistence.</returns>
		private List<CharacterInventoryData> BuildInventoryItemDataList(long characterID, List<Item> items)
		{
			var dtos = new List<CharacterInventoryData>(items.Count);
			foreach (Item item in items)
			{
				if (item == null) continue;
				dtos.Add(BuildInventoryItemData(characterID, item));
			}
			return dtos;
		}

		/// <summary>
		/// Builds a CharacterBankData DTO from a live Item instance.
		/// Increments item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="item">Runtime item instance to serialize.</param>
		/// <returns>Bank DTO ready for persistence.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private CharacterBankData BuildBankItemData(long characterID, Item item)
		{
			item.Version++;
			return new CharacterBankData(
				id: item.ID,
				version: item.Version,
				characterID: characterID,
				templateID: item.Template.ID,
				slot: item.Slot,
				seed: item.IsGenerated ? item.Generator.Seed : 0,
				amount: item.IsStackable ? item.Stackable.Amount : 1
			);
		}

		/// <summary>
		/// Builds a list of CharacterBankData DTOs from live Item instances.
		/// Increments each item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="items">Runtime item instances to serialize.</param>
		/// <returns>List of bank DTOs ready for persistence.</returns>
		private List<CharacterBankData> BuildBankItemDataList(long characterID, List<Item> items)
		{
			var dtos = new List<CharacterBankData>(items.Count);
			foreach (Item item in items)
			{
				if (item == null) continue;
				dtos.Add(BuildBankItemData(characterID, item));
			}
			return dtos;
		}

		/// <summary>
		/// Builds a CharacterEquipmentData DTO from a live Item instance.
		/// Increments item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="item">Runtime item instance to serialize.</param>
		/// <returns>Equipment DTO ready for persistence.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private CharacterEquipmentData BuildEquipmentItemData(long characterID, Item item)
		{
			item.Version++;
			return new CharacterEquipmentData(
				id: item.ID,
				version: item.Version,
				characterID: characterID,
				templateID: item.Template.ID,
				slot: item.Slot,
				seed: item.IsGenerated ? item.Generator.Seed : 0,
				amount: item.IsStackable ? item.Stackable.Amount : 1
			);
		}

		/// <summary>
		/// Builds a list of CharacterAttributeData DTOs from the character's attribute controllers.
		/// Increments each attribute's Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="character">Character providing attribute controller data.</param>
		/// <returns>List of attribute DTOs ready for persistence.</returns>
		private List<CharacterAttributeData> BuildAttributeDataList(IPlayerCharacter character)
		{
			var dtos = new List<CharacterAttributeData>();
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return dtos;
			}

			foreach (var kvp in attributeController.Attributes)
			{
				kvp.Value.Version++;
				dtos.Add(new CharacterAttributeData(
					id: 0,
					version: kvp.Value.Version,
					characterID: character.ID,
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
					characterID: character.ID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: kvp.Value.CurrentValue
				));
			}
			return dtos;
		}

		#endregion

		#region Async Persistence

		/// <summary>
		/// Persists a single inventory item asynchronously.
		/// </summary>
		/// <param name="dto">Inventory DTO to persist.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistInventoryItemAsync(CharacterInventoryData dto)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterInventoryService>(out var service))
				{
					await Log.Error("CharacterInventorySystem", "PersistInventoryItemAsync: Failed to resolve ICharacterInventoryService");
					return;
				}
				DatabaseResult<long> result = await service.PersistAsync(dto);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterInventorySystem", $"PersistInventoryItemAsync DB error (CharID={dto.CharacterID}, Slot={dto.Slot}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterInventorySystem", $"PersistInventoryItemAsync failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Persists multiple inventory items asynchronously.
		/// </summary>
		/// <param name="dtos">Inventory DTO batch to persist.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistInventoryItemsAsync(List<CharacterInventoryData> dtos)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterInventoryService>(out var service))
				{
					await Log.Error("CharacterInventorySystem", "PersistInventoryItemsAsync: Failed to resolve ICharacterInventoryService");
					return;
				}
				DatabaseResult result = await service.PersistAsync(dtos);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterInventorySystem", $"PersistInventoryItemsAsync DB error ({dtos.Count} items): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterInventorySystem", $"PersistInventoryItemsAsync failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Deletes a single inventory slot asynchronously.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="slot">Inventory slot to delete.</param>
		/// <param name="version">Optimistic concurrency token.</param>
		/// <returns>Asynchronous delete task.</returns>
		private async Task DeleteInventorySlotAsync(long characterID, int slot, long version)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterInventoryService>(out var service))
				{
					await Log.Error("CharacterInventorySystem", "DeleteInventorySlotAsync: Failed to resolve ICharacterInventoryService");
					return;
				}
				DatabaseResult result = await service.DeleteAsync(characterID, slot, version);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterInventorySystem", $"DeleteInventorySlotAsync DB error (CharID={characterID}, Slot={slot}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterInventorySystem", $"DeleteInventorySlotAsync failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Persists a single bank item asynchronously.
		/// </summary>
		/// <param name="dto">Bank DTO to persist.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistBankItemAsync(CharacterBankData dto)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterBankService>(out var service))
				{
					await Log.Error("CharacterInventorySystem", "PersistBankItemAsync: Failed to resolve ICharacterBankService");
					return;
				}
				DatabaseResult<long> result = await service.PersistAsync(dto);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterInventorySystem", $"PersistBankItemAsync DB error (CharID={dto.CharacterID}, Slot={dto.Slot}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterInventorySystem", $"PersistBankItemAsync failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Persists multiple bank items asynchronously.
		/// </summary>
		/// <param name="dtos">Bank DTO batch to persist.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistBankItemsAsync(List<CharacterBankData> dtos)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterBankService>(out var service))
				{
					await Log.Error("CharacterInventorySystem", "PersistBankItemsAsync: Failed to resolve ICharacterBankService");
					return;
				}
				DatabaseResult result = await service.PersistAsync(dtos);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterInventorySystem", $"PersistBankItemsAsync DB error ({dtos.Count} items): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterInventorySystem", $"PersistBankItemsAsync failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Deletes a single bank slot asynchronously.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="slot">Bank slot to delete.</param>
		/// <param name="version">Optimistic concurrency token.</param>
		/// <returns>Asynchronous delete task.</returns>
		private async Task DeleteBankSlotAsync(long characterID, int slot, long version)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterBankService>(out var service))
				{
					await Log.Error("CharacterInventorySystem", "DeleteBankSlotAsync: Failed to resolve ICharacterBankService");
					return;
				}
				DatabaseResult result = await service.DeleteAsync(characterID, slot, version);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterInventorySystem", $"DeleteBankSlotAsync DB error (CharID={characterID}, Slot={slot}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterInventorySystem", $"DeleteBankSlotAsync failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Persists a single equipment item asynchronously.
		/// </summary>
		/// <param name="dto">Equipment DTO to persist.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistEquipmentItemAsync(CharacterEquipmentData dto)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterEquipmentService>(out var service))
				{
					await Log.Error("CharacterInventorySystem", "PersistEquipmentItemAsync: Failed to resolve ICharacterEquipmentService");
					return;
				}
				DatabaseResult<long> result = await service.PersistAsync(dto);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterInventorySystem", $"PersistEquipmentItemAsync DB error (CharID={dto.CharacterID}, Slot={dto.Slot}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterInventorySystem", $"PersistEquipmentItemAsync failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Deletes a single equipment slot asynchronously.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="slot">Equipment slot to delete.</param>
		/// <param name="version">Optimistic concurrency token.</param>
		/// <returns>Asynchronous delete task.</returns>
		private async Task DeleteEquipmentSlotAsync(long characterID, int slot, long version)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterEquipmentService>(out var service))
				{
					await Log.Error("CharacterInventorySystem", "DeleteEquipmentSlotAsync: Failed to resolve ICharacterEquipmentService");
					return;
				}
				DatabaseResult result = await service.DeleteAsync(characterID, slot, version);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterInventorySystem", $"DeleteEquipmentSlotAsync DB error (CharID={characterID}, Slot={slot}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterInventorySystem", $"DeleteEquipmentSlotAsync failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Persists character attributes asynchronously.
		/// </summary>
		/// <param name="dtos">Attribute DTO batch to persist.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistAttributesAsync(List<CharacterAttributeData> dtos)
		{
			try
			{
				if (dtos == null || dtos.Count < 1) return;

				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterAttributeService>(out var service))
				{
					await Log.Error("CharacterInventorySystem", "PersistAttributesAsync: Failed to resolve ICharacterAttributeService");
					return;
				}
				DatabaseResult result = await service.PersistAsync(dtos);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterInventorySystem", $"PersistAttributesAsync DB error ({dtos.Count} attributes): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterInventorySystem", $"PersistAttributesAsync failed: {ex.Message}");
			}
		}

		#endregion
	}
}