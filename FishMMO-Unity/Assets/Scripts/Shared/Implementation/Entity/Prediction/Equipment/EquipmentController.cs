using FishNet.Connection;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls the character's equipment slots, handling equip/unequip logic and network synchronization.
	/// Manages client-server broadcasts for equipment changes and slot management.
	///
	/// Implements <see cref="IPredictableController"/> at Order 93 so equipment state participates
	/// in the prediction pipeline. Equipment-driven attribute changes are reconciled alongside
	/// other predicted state, eliminating the broadcast/reconcile race on <c>ExternalModifier</c>.
	/// </summary>
	public class EquipmentController : ItemContainer, IEquipmentController, IPredictableController
	{
		/// <inheritdoc />
		public event Action<Item, ItemSlot> OnItemEquipped;
		/// <inheritdoc />
		public event Action<Item, ItemSlot> OnItemUnequipped;
		[Header("ECA - Equipment")]
		[Tooltip("Triggers invoked when this character equips an item.")]
		[SerializeField]
		private List<Trigger> onEquipTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when this character unequips an item.")]
		[SerializeField]
		private List<Trigger> onUnequipTriggers = new List<Trigger>();

		/// <inheritdoc />
		public List<Trigger> OnEquipTriggers => onEquipTriggers;
		/// <inheritdoc />
		public List<Trigger> OnUnequipTriggers => onUnequipTriggers;

		// ── IPredictableController ──────────────────────────────────────

		/// <summary>
		/// Execution order in the unified prediction pipeline.
		/// Runs after CooldownController (90) and before CharacterAttributeController (95)
		/// so equipment attribute modifiers are settled before the attribute reconcile snapshot.
		/// </summary>
		public int Order => 93;

		/// <summary>
		/// Cached equipment reconcile snapshot, reused across ticks when equipment hasn't changed.
		/// </summary>
		private EquipmentReconcileEntry[] cachedEquipmentSnapshot;
		private bool equipmentSnapshotDirty = true;

		/// <inheritdoc />
		public void PopulateInput(ref CharacterReplicateData input)
		{
			// Equipment changes are event-driven (broadcast), not per-tick input.
		}

		/// <inheritdoc />
		public void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel)
		{
			// Equipment state is not driven by tick simulation.
			// OnReconcile handles state restoration.
		}

		/// <inheritdoc />
		public void OnCreateReconcile(ref CharacterReconcileData data)
		{
			if (equipmentSnapshotDirty || cachedEquipmentSnapshot == null)
			{
				BuildEquipmentSnapshot();
			}
			data.Equipment = cachedEquipmentSnapshot;
		}

		/// <inheritdoc />
		public void OnReconcile(CharacterReconcileData data, Channel channel)
		{
			RestoreFromReconcile(data.Equipment);
		}

		/// <summary>
		/// Builds an <see cref="EquipmentReconcileEntry"/> snapshot from the current filled slots.
		/// </summary>
		private void BuildEquipmentSnapshot()
		{
			int slotCount = Items != null ? Items.Count : 0;
			if (slotCount == 0)
			{
				cachedEquipmentSnapshot = null;
				equipmentSnapshotDirty = false;
				return;
			}

			// Count filled slots
			int filledCount = 0;
			for (int i = 0; i < slotCount; i++)
			{
				if (Items[i] != null)
					filledCount++;
			}

			if (filledCount == 0)
			{
				cachedEquipmentSnapshot = null;
				equipmentSnapshotDirty = false;
				return;
			}

			EquipmentReconcileEntry[] snapshot = new EquipmentReconcileEntry[filledCount];
			int writeIndex = 0;
			for (int i = 0; i < slotCount; i++)
			{
				Item item = Items[i];
				if (item != null)
				{
					snapshot[writeIndex++] = new EquipmentReconcileEntry
					{
						TemplateID = item.Template != null ? item.Template.ID : 0,
						Slot = (byte)i,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						InstanceID = item.ID,
					};
				}
			}

			cachedEquipmentSnapshot = snapshot;
			equipmentSnapshotDirty = false;
		}

		/// <summary>
		/// Restores equipment state from a reconcile snapshot.
		/// Only applies attribute changes for items that differ from current state.
		/// </summary>
		private void RestoreFromReconcile(EquipmentReconcileEntry[] entries)
		{
			int slotCount = Items != null ? Items.Count : 0;
			if (slotCount == 0) return;

			// Build current state map: slot → item (null if empty)
			Item[] currentItems = new Item[slotCount];
			for (int i = 0; i < slotCount; i++)
			{
				currentItems[i] = Items[i];
			}

			// Build reconcile state set for fast lookup
			HashSet<int> reconcileSlots = new HashSet<int>();
			if (entries != null)
			{
				for (int i = 0; i < entries.Length; i++)
				{
					reconcileSlots.Add(entries[i].Slot);
				}
			}

			bool changed = false;

			// 1. Unequip items in slots that server says are empty
			for (int i = 0; i < slotCount; i++)
			{
				if (currentItems[i] != null && !reconcileSlots.Contains(i))
				{
					if (currentItems[i].IsEquippable)
					{
						currentItems[i].Equippable.Unequip();
					}
					OnItemUnequipped?.Invoke(currentItems[i], (ItemSlot)i);
					SetItemSlot(null, i);
					changed = true;
				}
			}

			// 2. Equip items from reconcile that differ from current state
			if (entries != null)
			{
				for (int i = 0; i < entries.Length; i++)
				{
					EquipmentReconcileEntry entry = entries[i];
					int slot = entry.Slot;
					if (slot < 0 || slot >= slotCount) continue;
					Item currentItem = Items[slot];

					// Check if the slot already has the correct item
					if (currentItem != null &&
						currentItem.Template != null &&
						currentItem.Template.ID == entry.TemplateID &&
						currentItem.ID == entry.InstanceID)
					{
						continue; // Already correct
					}

					// Unequip old item if present
					if (currentItem != null && currentItem.IsEquippable)
					{
						currentItem.Equippable.Unequip();
					}
					if (currentItem != null)
					{
						OnItemUnequipped?.Invoke(currentItem, (ItemSlot)slot);
					}

					// Create and equip new item
					Item newItem = new Item(entry.InstanceID, entry.Seed, entry.TemplateID, 1); // Equipment items are never stackable
					SetItemSlot(newItem, slot);
					if (newItem.IsEquippable)
					{
						newItem.Equippable.Equip(Character);
					}

					OnItemEquipped?.Invoke(newItem, (ItemSlot)slot);
					changed = true;
				}
			}

			if (changed)
			{
				equipmentSnapshotDirty = true;
			}
		}

		/// <inheritdoc />
		public override void OnAwake()
		{
			AddSlots(null, System.Enum.GetNames(typeof(ItemSlot)).Length);
		}

		/// <inheritdoc />
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			Clear();
			equipmentSnapshotDirty = true;
			cachedEquipmentSnapshot = null;
		}

		/// <inheritdoc />
		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			int itemCount = reader.ReadInt32();
			for (int i = 0; i < itemCount; ++i)
			{
				long id = reader.ReadInt64();
				int templateID = reader.ReadInt32();
				int slot = reader.ReadInt32();
				int seed = reader.ReadInt32();
				uint stackSize = reader.ReadUInt32();

				Item item = new Item(id, seed, templateID, stackSize);

				SetItemSlot(item, slot);
				if (item.IsEquippable)
				{
					item.Equippable.Equip(Character);
				}
			}
			equipmentSnapshotDirty = true;
		}

		/// <inheritdoc />
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			if (Items == null ||
				Items.Count < 1)
			{
				writer.WriteInt32(0);
				return;
			}

			writer.WriteInt32(FilledSlots());
			foreach (Item item in Items)
			{
				if (item == null)
				{
					continue;
				}
				writer.WriteInt64(item.ID);
				writer.WriteInt32(item.Template.ID);
				writer.WriteInt32(item.Slot);
				writer.WriteInt32(item.IsGenerated ? item.Generator.Seed : 0);
				writer.WriteUInt32(item.IsStackable ? item.Stackable.Amount : 0);
			}
		}

#if !UNITY_SERVER
		/// <inheritdoc />
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<EquipmentEquipItemBroadcast>(OnClientEquipmentEquipItemBroadcastReceived);
			ClientManager.RegisterBroadcast<EquipmentUnequipItemBroadcast>(OnClientEquipmentUnequipItemBroadcastReceived);
		}

		/// <inheritdoc />
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<EquipmentEquipItemBroadcast>(OnClientEquipmentEquipItemBroadcastReceived);
				ClientManager.UnregisterBroadcast<EquipmentUnequipItemBroadcast>(OnClientEquipmentUnequipItemBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles an equip broadcast from the server, equipping the item from the specified inventory.
		/// Called on the client when the server authorizes an equip operation.
		/// </summary>
		/// <param name="msg">The equip broadcast message.</param>
		/// <param name="channel">Channel the broadcast was received on.</param>
		private void OnClientEquipmentEquipItemBroadcastReceived(EquipmentEquipItemBroadcast msg, Channel channel)
		{
			switch (msg.FromInventory)
			{
				case InventoryType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem(msg.InventoryIndex, out Item inventoryItem))
					{
						Equip(inventoryItem, msg.InventoryIndex, inventoryController, (ItemSlot)msg.Slot, applyAttributes: false);
					}
					break;
				case InventoryType.Equipment:
					// Equipment swaps are not handled here.
					break;
				case InventoryType.Bank:
					if (Character.TryGet(out IBankController bankController) &&
						bankController.TryGetItem(msg.InventoryIndex, out Item bankItem))
					{
						Equip(bankItem, msg.InventoryIndex, bankController, (ItemSlot)msg.Slot, applyAttributes: false);
					}
					break;
				default: return;
			}
		}

		/// <summary>
		/// Handles an unequip broadcast from the server, moving the item to the specified inventory.
		/// Called on the client when the server authorizes an unequip operation.
		/// </summary>
		/// <param name="msg">The unequip broadcast message.</param>
		/// <param name="channel">Channel the broadcast was received on.</param>
		private void OnClientEquipmentUnequipItemBroadcastReceived(EquipmentUnequipItemBroadcast msg, Channel channel)
		{
			switch (msg.ToInventory)
			{
				case InventoryType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController))
					{
						Unequip(inventoryController, msg.Slot, out List<Item> modifiedItems, applyAttributes: false);
					}
					break;
				case InventoryType.Equipment:
					// Equipment swaps are not handled here.
					break;
				case InventoryType.Bank:
					if (Character.TryGet(out IBankController bankController))
					{
						Unequip(bankController, msg.Slot, out List<Item> modifiedItems, applyAttributes: false);
					}
					break;
				default: return;
			}
		}
#endif

		/// <inheritdoc />
		public void Activate(int index)
		{
			if (!Character.TryGet(out ICharacterDamageController damageController) ||
				!damageController.IsAlive)
			{
				return;
			}
			if (TryGetItem(index, out Item item))
			{
				Log.Debug("EquipmentController", $"Using item in slot[{index}]");
			}
		}

		/// <inheritdoc />
		public bool Equip(Item item, int inventoryIndex, IItemContainer container, ItemSlot toSlot)
		{
			return Equip(item, inventoryIndex, container, toSlot, applyAttributes: true);
		}

		/// <summary>
		/// Equips an item with control over whether attribute modifiers are applied.
		/// When <paramref name="applyAttributes"/> is false, only slot state and visual events
		/// are updated — the prediction pipeline's reconcile handles attribute changes.
		/// </summary>
		private bool Equip(Item item, int inventoryIndex, IItemContainer container, ItemSlot toSlot, bool applyAttributes)
		{
			if (item == null ||
				!item.IsEquippable ||
				!CanManipulate())
			{
				return false;
			}

			EquippableItemTemplate equippable = item.Template as EquippableItemTemplate;
			if (equippable == null || toSlot != equippable.Slot)
			{
				return false;
			}

			byte slotIndex = (byte)toSlot;

			if (container != null)
			{
				if (TryGetItem(slotIndex, out Item previousItem) &&
					previousItem.IsEquippable)
				{
					if (applyAttributes)
					{
						previousItem.Equippable.Unequip();
					}

					if (!container.SetItemSlot(previousItem, inventoryIndex))
					{
						if (applyAttributes)
						{
							previousItem.Equippable.Equip(Character);
						}
						return false;
					}
				}
				else
				{
					container.RemoveItem(inventoryIndex);
				}
			}

			if (!SetItemSlot(item, slotIndex))
			{
				return false;
			}

			if (item.IsEquippable && applyAttributes)
			{
				item.Equippable.Equip(Character);
			}

			equipmentSnapshotDirty = true;
			Character.Invoke(onEquipTriggers, new EquipItemEventData(Character, item, toSlot));
			OnItemEquipped?.Invoke(item, toSlot);
			return true;
		}

		/// <inheritdoc />
		public bool Unequip(IItemContainer container, byte slot, out List<Item> modifiedItems)
		{
			return Unequip(container, slot, out modifiedItems, applyAttributes: true);
		}

		/// <summary>
		/// Unequips an item with control over whether attribute modifiers are applied.
		/// </summary>
		private bool Unequip(IItemContainer container, byte slot, out List<Item> modifiedItems, bool applyAttributes)
		{
			if (!CanManipulate() ||
				!TryGetItem(slot, out Item item) ||
				container == null ||
				!container.CanAddItem(item))
			{
				modifiedItems = null;
				return false;
			}

			if (!container.TryAddItem(item, out modifiedItems))
			{
				return false;
			}

			if (item.IsEquippable && applyAttributes)
			{
				item.Equippable.Unequip();
			}

			SetItemSlot(null, slot);

			equipmentSnapshotDirty = true;
			Character.Invoke(onUnequipTriggers, new EquipItemEventData(Character, item, (ItemSlot)slot));
			OnItemUnequipped?.Invoke(item, (ItemSlot)slot);
			return true;
		}
	}
}