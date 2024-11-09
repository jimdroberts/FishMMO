using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Server.DatabaseServices;
using FishMMO.Database.Npgsql;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Server
{
	// Character Inventory Manager handles the players inventory
	public class CharacterInventorySystem : ServerBehaviour
	{
		public override void InitializeOnce()
		{
			if (Server != null)
			{
				Server.RegisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived, true);
				Server.RegisterBroadcast<InventorySwapItemSlotsBroadcast>(OnServerInventorySwapItemSlotsBroadcastReceived, true);

				Server.RegisterBroadcast<EquipmentEquipItemBroadcast>(OnServerEquipmentEquipItemBroadcastReceived, true);
				Server.RegisterBroadcast<EquipmentUnequipItemBroadcast>(OnServerEquipmentUnequipItemBroadcastReceived, true);

				Server.RegisterBroadcast<BankRemoveItemBroadcast>(OnServerBankRemoveItemBroadcastReceived, true);
				Server.RegisterBroadcast<BankSwapItemSlotsBroadcast>(OnServerBankSwapItemSlotsBroadcastReceived, true);
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (Server != null)
			{
				Server.UnregisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived);
				Server.UnregisterBroadcast<InventorySwapItemSlotsBroadcast>(OnServerInventorySwapItemSlotsBroadcastReceived);

				Server.UnregisterBroadcast<EquipmentEquipItemBroadcast>(OnServerEquipmentEquipItemBroadcastReceived);
				Server.UnregisterBroadcast<EquipmentUnequipItemBroadcast>(OnServerEquipmentUnequipItemBroadcastReceived);

				Server.UnregisterBroadcast<BankRemoveItemBroadcast>(OnServerBankRemoveItemBroadcastReceived);
				Server.UnregisterBroadcast<BankSwapItemSlotsBroadcast>(OnServerBankSwapItemSlotsBroadcastReceived);
			}
		}

		public bool SwapContainerItems(NpgsqlDbContext dbContext,
									   long characterID,
									   IItemContainer container,
									   int fromIndex, int toIndex,
									   Action<NpgsqlDbContext, long, Item> onDatabaseUpdateSlot)
		{
			if (container != null &&
				container.SwapItemSlots(fromIndex, toIndex, out Item fromItem, out Item toItem))
			{
				// we can update the item slots in the database easily
				onDatabaseUpdateSlot?.Invoke(dbContext, characterID, fromItem);
				onDatabaseUpdateSlot?.Invoke(dbContext, characterID, toItem);
				return true;
			}
			return false;
		}

		public bool SwapContainerItems(NpgsqlDbContext dbContext,
									   long characterID,
									   IItemContainer from, IItemContainer to,
									   int fromIndex, int toIndex,
									   Action<NpgsqlDbContext, long, Item> onDatabaseSetOldSlot = null,
									   Action<NpgsqlDbContext, long, long> onDatabaseDeleteOldSlot = null,
									   Action<NpgsqlDbContext, long, Item> onDatabaseSetNewSlot = null)
		{
			// same container... do the quick swap
			if (from == to)
			{
				return SwapContainerItems(dbContext, characterID, from, fromIndex, toIndex, onDatabaseSetOldSlot);
			}
			if (from != null &&
				to != null &&
				from.TryGetItem(fromIndex, out Item fromItem))
			{
				// check if we need to swap items
				if (to.TryGetItem(toIndex, out Item toItem))
				{
					// put the target item in the old container
					from.SetItemSlot(toItem, fromIndex);
					onDatabaseSetOldSlot?.Invoke(dbContext, characterID, toItem);
				}
				// the slot we want to move the item to is empty
				else
				{
					// remove the item from the old container
					from.SetItemSlot(null, fromIndex);
					onDatabaseDeleteOldSlot?.Invoke(dbContext, characterID, fromItem.Slot);
				}
				// put the item in the new container
				to.SetItemSlot(fromItem, toIndex);
				onDatabaseSetNewSlot?.Invoke(dbContext, characterID, fromItem);
				return true;
			}
			return false;
		}

		private void OnServerInventoryRemoveItemBroadcastReceived(NetworkConnection conn, InventoryRemoveItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character != null &&
				!character.IsTeleporting &&
				character.TryGet(out IInventoryController inventoryController))
			{
				Item item = inventoryController.RemoveItem(msg.Slot);

				// remove the item from the database
				CharacterInventoryService.Delete(dbContext, character.ID, msg.Slot);

				Server.Broadcast(conn, msg, true, Channel.Reliable);
			}
		}

		private void OnServerInventorySwapItemSlotsBroadcastReceived(NetworkConnection conn, InventorySwapItemSlotsBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			using var dbTransaction = dbContext.Database.BeginTransaction();
			if (dbTransaction == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null ||
				character.IsTeleporting ||
				!character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			switch (msg.FromInventory)
			{
				case InventoryType.Inventory:
					// swap the items in the inventory
					if (msg.To != msg.From &&
						SwapContainerItems(dbContext, character.ID, inventoryController, msg.From, msg.To, (db, id, i) =>
					{
						CharacterInventoryService.Update(db, id, i);
					}))
					{
						dbTransaction.Commit();

						// tell the client we succeeded
						Server.Broadcast(conn, msg, true, Channel.Reliable);
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

						if (SwapContainerItems(dbContext, character.ID, bankController, inventoryController, msg.From, msg.To,
							(db, id, a) =>
							{
								CharacterBankService.SetSlot(db, id, a);
							},
							(db, id, s) =>
							{
								CharacterBankService.Delete(db, id, s);
							},
							(db, id, b) =>
							{
								CharacterInventoryService.SetSlot(db, id, b);
							}))
						{
							dbTransaction.Commit();

							// tell the client
							Server.Broadcast(conn, msg, true, Channel.Reliable);
						}
					}
					break;
				default: break;
			}
		}

		private void OnServerEquipmentEquipItemBroadcastReceived(NetworkConnection conn, EquipmentEquipItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			using var dbTransaction = dbContext.Database.BeginTransaction();
			if (dbTransaction == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null ||
				character.IsTeleporting ||
				!character.TryGet(out IEquipmentController equipmentController))
			{
				return;
			}

			switch (msg.FromInventory)
			{
				case InventoryType.Inventory:
					if (character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem(msg.InventoryIndex, out Item inventoryItem))
					{
						if (!equipmentController.Equip(inventoryItem, msg.InventoryIndex, inventoryController, (ItemSlot)msg.Slot))
						{
							return;
						}

						// did we replace an already equipped item?
						if (inventoryController.TryGetItem(msg.InventoryIndex, out Item prevItem))
						{
							CharacterInventoryService.SetSlot(dbContext, character.ID, prevItem);
						}
						// remove the inventory item from the database
						else
						{
							CharacterInventoryService.Delete(dbContext, character.ID, msg.InventoryIndex);
						}

						// set the equipment slot in the database
						CharacterEquipmentService.SetSlot(dbContext, character.ID, inventoryItem);
						CharacterAttributeService.Save(dbContext, character);

						dbTransaction.Commit();

						Server.Broadcast(conn, msg, true, Channel.Reliable);
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

						if (bankController.TryGetItem(msg.InventoryIndex, out Item bankItem))
						{
							if (!equipmentController.Equip(bankItem, msg.InventoryIndex, bankController, (ItemSlot)msg.Slot))
							{
								return;
							}

							// did we replace an already equipped item?
							if (bankController.TryGetItem(msg.InventoryIndex, out Item prevItem))
							{
								CharacterBankService.SetSlot(dbContext, character.ID, prevItem);
							}
							// remove the inventory item from the database
							else
							{
								CharacterBankService.Delete(dbContext, character.ID, msg.InventoryIndex);
							}

							// set the equipment slot in the database
							CharacterEquipmentService.SetSlot(dbContext, character.ID, bankItem);
							CharacterAttributeService.Save(dbContext, character);

							dbTransaction.Commit();

							Server.Broadcast(conn, msg, true, Channel.Reliable);
						}
					}
					break;
				default: return;
			}
		}

		private void OnServerEquipmentUnequipItemBroadcastReceived(NetworkConnection conn, EquipmentUnequipItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			using var dbTransaction = dbContext.Database.BeginTransaction();
			if (dbTransaction == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null ||
				character.IsTeleporting ||
				!character.TryGet(out IEquipmentController equipmentController))
			{
				return;
			}

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

						// update all of the modified slots
						foreach (Item item in modifiedItems)
						{
							// just in case..
							if (item == null)
							{
								continue;
							}

							// update or add the item to the database and initialize
							CharacterInventoryService.SetSlot(dbContext, character.ID, item);
						}

						// delete the item from the equipment table
						CharacterEquipmentService.Delete(dbContext, character.ID, oldSlot);
						CharacterAttributeService.Save(dbContext, character);

						dbTransaction.Commit();

						Server.Broadcast(conn, msg, true, Channel.Reliable);
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

							// update all of the modified slots
							foreach (Item item in modifiedItems)
							{
								// just in case..
								if (item == null)
								{
									continue;
								}

								// update or add the item to the database and initialize
								CharacterBankService.SetSlot(dbContext, character.ID, item);
							}

							// delete the item from the equipment table
							CharacterEquipmentService.Delete(dbContext, character.ID, oldSlot);
							CharacterAttributeService.Save(dbContext, character);

							dbTransaction.Commit();

							Server.Broadcast(conn, msg, true, Channel.Reliable);
						}
					}
					break;
				default: return;
			}
		}

		private void OnServerBankRemoveItemBroadcastReceived(NetworkConnection conn, BankRemoveItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character != null &&
				!character.IsTeleporting &&
				character.TryGet(out IBankController bankController))
			{
				Item item = bankController.RemoveItem(msg.Slot);

				// remove the item from the database
				CharacterBankService.Delete(dbContext, character.ID, item.Slot);

				Server.Broadcast(conn, msg, true, Channel.Reliable);
			}
		}

		private void OnServerBankSwapItemSlotsBroadcastReceived(NetworkConnection conn, BankSwapItemSlotsBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}
			using var dbTransaction = dbContext.Database.BeginTransaction();
			if (dbTransaction == null)
			{
				return;
			}

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

			switch (msg.FromInventory)
			{
				case InventoryType.Inventory:
					if (character.TryGet(out IInventoryController inventoryController) &&
						SwapContainerItems(dbContext, character.ID, inventoryController, bankController, msg.From, msg.To,
					(db, id, a) =>
					{
						CharacterInventoryService.SetSlot(db, id, a);
					},
					(db, id, s) =>
					{
						CharacterInventoryService.Delete(db, id, s);
					},
					(db, id, b) =>
					{
						CharacterBankService.SetSlot(db, id, b);
					}))
					{
						dbTransaction.Commit();

						// tell the client
						Server.Broadcast(conn, msg, true, Channel.Reliable);
					}
					break;
				case InventoryType.Equipment:
					break;
				case InventoryType.Bank:
					// swap the items in the bank
					if (msg.To != msg.From &&
						SwapContainerItems(dbContext, character.ID, bankController, msg.From, msg.To, (db, id, i) =>
					{
						CharacterBankService.Update(db, id, i);
					}))
					{
						dbTransaction.Commit();

						// tell the client we succeeded
						Server.Broadcast(conn, msg, true, Channel.Reliable);
					}
					break;
				default: break;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool ValidateBankerSceneObject(long sceneObjectID, IPlayerCharacter character)
		{
			if (!SceneObject.Objects.TryGetValue(sceneObjectID, out ISceneObject sceneObject))
			{
				if (sceneObject == null)
				{
					Debug.Log("Missing SceneObject");
				}
				else
				{
					Debug.Log("Missing ID:" + sceneObjectID);
				}
				return false;
			}
			if (sceneObject.GameObject.scene.handle != character.GameObject.scene.handle)
			{
				Debug.Log("Object scene mismatch.");
				return false;
			}
			IInteractable interactable = sceneObject.GameObject.GetComponent<IInteractable>();
			if (interactable == null ||
				!interactable.InRange(character.Transform))
			{
				Debug.Log($"{character.CharacterName} is not in range of {sceneObject.GameObject.name}!");
				return false;
			}
			Banker banker = interactable as Banker;
			if (banker == null)
			{
				Debug.Log($"{sceneObject.GameObject.name} is not a banker!");
				return false;
			}
			return true;
		}
	}
}