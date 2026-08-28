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
	///
	/// Three network paths feed this container and they must agree with one another:
	/// <list type="bullet">
	/// <item>The spawn payload (<see cref="WritePayload"/>/<see cref="ReadPayload"/>) — owner
	/// shaped for the owner, template+seed only for everyone else.</item>
	/// <item>The owner's acknowledgement broadcasts and reconcile snapshot, which can arrive in
	/// either order and are reconciled against each other by instance id (see
	/// <see cref="RestoreFromReconcile"/> and the pending-request records).</item>
	/// <item><see cref="EquipmentObservedSlotBroadcast"/>, sent by the server to the character's
	/// observers after every successful equip/unequip, applied by <see cref="ApplyObservedSlot"/>.</item>
	/// </list>
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

		/// <summary>
		/// Reusable slot-index set for <see cref="RestoreFromReconcile"/>.
		/// </summary>
		/// <remarks>
		/// Preallocated because reconcile runs on every differing tick on the owner's hottest
		/// path — the same reason the buff and cooldown controllers pool their reconcile sets.
		/// Cleared on entry so it never carries state between calls.
		/// </remarks>
		private readonly HashSet<int> reconcileSlots = new HashSet<int>();

		// ── Pending client requests ─────────────────────────────────────

		/// <summary>
		/// A client-initiated equip the server has not yet answered.
		/// </summary>
		/// <remarks>
		/// The reconcile snapshot and the acknowledgement broadcast are independent messages and
		/// arrive in either order. The snapshot names the instance that ended up in the slot; the
		/// acknowledgement names the inventory index it came from. Neither alone can tell a swap
		/// that has already been applied from one that has not — after a swap the source index
		/// holds the previously equipped item, which is itself a legal equip for that slot — so the
		/// request is recorded when it is sent and both messages consult it.
		/// </remarks>
		private struct PendingEquip
		{
			public long InstanceID;
			public int InventoryIndex;
			public InventoryType FromInventory;
			/// <summary>Set once the reconcile has placed the item; the acknowledgement is then a no-op.</summary>
			public bool AppliedByReconcile;
		}

		/// <summary>A client-initiated unequip the server has not yet answered.</summary>
		private struct PendingUnequip
		{
			public long InstanceID;
			public InventoryType ToInventory;
			/// <summary>Set once the reconcile has moved the item out; the acknowledgement is then a no-op.</summary>
			public bool AppliedByReconcile;
		}

		private readonly Dictionary<byte, PendingEquip> pendingEquips = new Dictionary<byte, PendingEquip>();
		private readonly Dictionary<byte, PendingUnequip> pendingUnequips = new Dictionary<byte, PendingUnequip>();

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
			/* The owner always reconciles: this is the authority for its own equipment, and the
			 * correction that repairs a mispredicted equip.
			 *
			 * A non-owner only reconciles when the object is forwarded, because that is the mode in
			 * which the reconcile is what carries equipment to observers at all. With forwarding off
			 * an observer's equipment comes from the spawn payload and EquipmentObservedSlotBroadcast,
			 * and it would not receive this reconcile anyway — the guard states the contract rather
			 * than defending against a message that cannot arrive, so that flipping forwarding on
			 * does not quietly give one container two writers. See ObserverSyncMode. */
			if (!base.IsOwner && !ObserverSyncMode.ObserversConsumeReconcile(base.NetworkObject))
			{
				return;
			}

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
		/// </summary>
		/// <remarks>
		/// <para>
		/// The snapshot is authoritative about WHICH instance sits in each slot, and only about
		/// that. On the owner the instances themselves already exist somewhere on this client — in
		/// the inventory, the bank, or another equipment slot — so a slot that disagrees with the
		/// snapshot is settled by MOVING the real item, never by cloning it. Cloning produced two
		/// live objects with one id: the reconcile's copy in the slot and the original still in the
		/// inventory, which the later acknowledgement then dutifully equipped as well.
		/// </para>
		/// <para>
		/// Likewise an item the snapshot no longer lists is returned to a container rather than
		/// dropped. The pending unequip record names the container the player asked for; without
		/// one the inventory is tried first, then the bank. The item was still equipped a moment
		/// ago, so somewhere on the server it now exists — leaving it referenced by nothing made it
		/// vanish from the client until relog when the acknowledgement arrived second.
		/// </para>
		/// <para>
		/// An item that cannot be found anywhere on this client is still materialised from the
		/// entry, because the alternative — an empty slot the server considers filled — is worse.
		/// That is the case for a server-side equip the client never requested.
		/// </para>
		/// </remarks>
		private void RestoreFromReconcile(EquipmentReconcileEntry[] entries)
		{
			int slotCount = Items != null ? Items.Count : 0;
			if (slotCount == 0) return;

			// Build reconcile state set for fast lookup. Pooled — this runs on every differing
			// reconcile tick on the owner, so a per-call HashSet is garbage at tick rate.
			reconcileSlots.Clear();
			if (entries != null)
			{
				for (int i = 0; i < entries.Length; i++)
				{
					reconcileSlots.Add(entries[i].Slot);
				}
			}

			bool changed = false;

			/* 1. Unequip items in slots that server says are empty.
			 *
			 * Items is read directly rather than through a defensive copy: the slot list is fixed
			 * size (one entry per ItemSlot enum value, never resized), each iteration reads its
			 * slot before the SetItemSlot below mutates it, and SetItemSlot touches only that one
			 * index. The old Item[] copy existed to guard against iteration-under-mutation that
			 * cannot occur here, at the cost of an array allocation per reconcile. */
			for (int i = 0; i < slotCount; i++)
			{
				Item existing = Items[i];
				if (existing != null && !reconcileSlots.Contains(i))
				{
					RemoveFromSlotForReconcile(existing, (byte)i);
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
						// Already correct. If the acknowledgement placed it, the pending record
						// was cleared then; if a reconcile placed it earlier, the record is already
						// flagged. Either way nothing to do.
						continue;
					}

					// Locate the real instance before touching the slot, so a swap between two
					// equipment slots reads both sides before either is written.
					IItemContainer sourceContainer = null;
					int sourceIndex = -1;
					Item realItem = FindOwnedInstance(entry.InstanceID, (byte)slot, out sourceContainer, out sourceIndex);

					// Unequip old item if present. It goes back to wherever the incoming item came
					// from — that is what the server's Equip did — or, when the incoming item was
					// not found, to whichever container will take it.
					if (currentItem != null)
					{
						if (sourceContainer != null && sourceIndex >= 0 && !ReferenceEquals(sourceContainer, this))
						{
							DetachFromSlot(currentItem, (byte)slot);
							// The source index was vacated by the RemoveItem in FindOwnedInstance.
							if (!sourceContainer.SetItemSlot(currentItem, sourceIndex))
							{
								ReturnToAnyContainer(currentItem, null);
							}
						}
						else
						{
							RemoveFromSlotForReconcile(currentItem, (byte)slot);
						}
					}

					Item newItem = realItem;
					if (newItem == null)
					{
						newItem = new Item(entry.InstanceID, entry.Seed, entry.TemplateID, 1); // Equipment items are never stackable
					}

					SetItemSlot(newItem, slot);
					if (newItem.IsEquippable)
					{
						newItem.Equippable.Equip(Character);
					}

					if (pendingEquips.TryGetValue((byte)slot, out PendingEquip pending) && pending.InstanceID == entry.InstanceID)
					{
						pending.AppliedByReconcile = true;
						pendingEquips[(byte)slot] = pending;
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

		/// <summary>
		/// Finds the live instance with <paramref name="instanceID"/> on this client and removes it
		/// from wherever it was, ready to be placed in <paramref name="targetSlot"/>.
		/// </summary>
		/// <remarks>
		/// Search order: the pending equip record for the slot (exact, cheapest), the other
		/// equipment slots (a two-slot swap), the inventory, then the bank. The removal is done
		/// here rather than by the caller so the source index reported back is guaranteed vacant.
		/// </remarks>
		private Item FindOwnedInstance(long instanceID, byte targetSlot, out IItemContainer container, out int index)
		{
			container = null;
			index = -1;
			if (instanceID == 0 || Character == null)
			{
				return null;
			}

			// Pending record first: it names the exact source.
			if (pendingEquips.TryGetValue(targetSlot, out PendingEquip pending) && pending.InstanceID == instanceID)
			{
				IItemContainer named = ResolveContainer(pending.FromInventory);
				if (named != null &&
					named.TryGetItem(pending.InventoryIndex, out Item namedItem) &&
					namedItem != null && namedItem.ID == instanceID)
				{
					Item removed = named.RemoveItem(pending.InventoryIndex);
					if (ReferenceEquals(removed, namedItem))
					{
						container = named;
						index = pending.InventoryIndex;
						return removed;
					}
					if (removed != null)
					{
						named.SetItemSlot(removed, pending.InventoryIndex);
					}
				}
			}

			// Another equipment slot (swap between two sockets).
			for (int i = 0; i < Items.Count; i++)
			{
				Item other = Items[i];
				if (i != targetSlot && other != null && other.ID == instanceID)
				{
					DetachFromSlot(other, (byte)i);
					container = this;
					index = i;
					return other;
				}
			}

			if (TryTakeFromContainer(ResolveContainer(InventoryType.Inventory), instanceID, out Item fromInventory, out index))
			{
				container = ResolveContainer(InventoryType.Inventory);
				return fromInventory;
			}
			if (TryTakeFromContainer(ResolveContainer(InventoryType.Bank), instanceID, out Item fromBank, out index))
			{
				container = ResolveContainer(InventoryType.Bank);
				return fromBank;
			}
			return null;
		}

		/// <summary>Removes the item with <paramref name="instanceID"/> from <paramref name="container"/> if present.</summary>
		private static bool TryTakeFromContainer(IItemContainer container, long instanceID, out Item item, out int index)
		{
			item = null;
			index = -1;
			if (container == null || container.Items == null)
			{
				return false;
			}
			List<Item> items = container.Items;
			for (int i = 0; i < items.Count; i++)
			{
				Item candidate = items[i];
				if (candidate != null && candidate.ID == instanceID)
				{
					Item removed = container.RemoveItem(i);
					if (!ReferenceEquals(removed, candidate))
					{
						// Locked slot or refused manipulation: put back whatever came out and
						// report not found; the caller materialises a copy rather than tearing a
						// locked slot open.
						if (removed != null)
						{
							container.SetItemSlot(removed, i);
						}
						return false;
					}
					item = removed;
					index = i;
					return true;
				}
			}
			return false;
		}

		/// <summary>Clears a slot for the reconcile path and returns the item to a container.</summary>
		private void RemoveFromSlotForReconcile(Item item, byte slot)
		{
			DetachFromSlot(item, slot);

			InventoryType? preferred = null;
			if (pendingUnequips.TryGetValue(slot, out PendingUnequip pending) && pending.InstanceID == item.ID)
			{
				preferred = pending.ToInventory;
				pending.AppliedByReconcile = true;
				pendingUnequips[slot] = pending;
			}
			ReturnToAnyContainer(item, preferred);
		}

		/// <summary>Takes an item out of an equipment slot: modifiers off, slot null, listeners told.</summary>
		/// <param name="item">The item leaving the slot.</param>
		/// <param name="slot">The slot being cleared.</param>
		/// <param name="applyAttributeModifiers">
		/// True to run the normal unequip, which removes the item's generated attribute modifiers.
		/// False to clear the slot without touching attributes — used by the observer path, which
		/// never added them in the first place. See <see cref="ApplyObservedSlot"/>.
		/// </param>
		private void DetachFromSlot(Item item, byte slot, bool applyAttributeModifiers = true)
		{
			if (item.IsEquippable)
			{
				if (applyAttributeModifiers)
				{
					item.Equippable.Unequip();
				}
				else
				{
					ClearEquippedCharacterSilently(item);
				}
			}
			OnItemUnequipped?.Invoke(item, (ItemSlot)slot);
			SetItemSlot(null, slot);
		}

		/// <summary>
		/// Places an item that just left an equipment slot into a container: the preferred one
		/// when given and it has room, then the inventory, then the bank.
		/// </summary>
		/// <remarks>
		/// Fails only when every container is full or unavailable. The server cannot have unequipped
		/// into a full container, so that means this client's view is already off — the warning
		/// is the trace for that, and the item is dropped rather than left referencing a slot.
		/// </remarks>
		private void ReturnToAnyContainer(Item item, InventoryType? preferred)
		{
			if (preferred.HasValue && TryReturnTo(ResolveContainer(preferred.Value), item))
			{
				return;
			}
			if (preferred != InventoryType.Inventory && TryReturnTo(ResolveContainer(InventoryType.Inventory), item))
			{
				return;
			}
			if (preferred != InventoryType.Bank && TryReturnTo(ResolveContainer(InventoryType.Bank), item))
			{
				return;
			}
			Log.Warning("EquipmentController",
				$"Reconcile removed item {item.ID} ('{item.Name}') from equipment but no container had room for it; dropping the client copy.");
		}

		private static bool TryReturnTo(IItemContainer container, Item item)
		{
			return container != null &&
				container.CanAddItem(item) &&
				container.TryAddItem(item, out _);
		}

		/// <summary>Resolves the character's container for an <see cref="InventoryType"/>.</summary>
		private IItemContainer ResolveContainer(InventoryType type)
		{
			if (Character == null)
			{
				return null;
			}
			switch (type)
			{
				case InventoryType.Inventory:
					return Character.TryGet(out IInventoryController inventory) ? inventory : null;
				case InventoryType.Bank:
					return Character.TryGet(out IBankController bank) ? bank : null;
				case InventoryType.Equipment:
					return this;
				default:
					return null;
			}
		}

		// ── Pending request API ─────────────────────────────────────────

		/// <inheritdoc />
		public void NotifyEquipRequested(Item item, int inventoryIndex, InventoryType fromInventory, ItemSlot toSlot)
		{
			if (item == null)
			{
				return;
			}
			pendingEquips[(byte)toSlot] = new PendingEquip
			{
				InstanceID = item.ID,
				InventoryIndex = inventoryIndex,
				FromInventory = fromInventory,
				AppliedByReconcile = false,
			};
		}

		/// <inheritdoc />
		public void NotifyUnequipRequested(ItemSlot slot, InventoryType toInventory)
		{
			if (!TryGetItem((byte)slot, out Item item) || item == null)
			{
				return;
			}
			pendingUnequips[(byte)slot] = new PendingUnequip
			{
				InstanceID = item.ID,
				ToInventory = toInventory,
				AppliedByReconcile = false,
			};
		}

		/// <inheritdoc />
		public void ClearPendingRequest(ItemSlot slot)
		{
			pendingEquips.Remove((byte)slot);
			pendingUnequips.Remove((byte)slot);
		}

		/// <summary>True while a client-initiated request for this slot is unanswered. Test seam.</summary>
		internal bool HasPendingRequest(byte slot)
		{
			return pendingEquips.ContainsKey(slot) || pendingUnequips.ContainsKey(slot);
		}

		/// <inheritdoc />
		public override void OnAwake()
		{
			AddSlots(null, System.Enum.GetNames(typeof(ItemSlot)).Length);

			/* Every write to this container goes through SetItemSlot, and on the server not all of
			 * them go through Equip/Unequip — character loading fills the slots directly. Marking
			 * the snapshot dirty from the write primitive means the reconcile can never carry a
			 * stale snapshot after such a write, whichever path made it. One bool store per slot
			 * write; nothing allocates. */
			OnSlotUpdated += MarkSnapshotDirty;
		}

		private void MarkSnapshotDirty(IItemContainer container, Item item, int slot)
		{
			equipmentSnapshotDirty = true;
		}

		/// <inheritdoc />
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			Clear();
			pendingEquips.Clear();
			pendingUnequips.Clear();
			equipmentSnapshotDirty = true;
			cachedEquipmentSnapshot = null;
		}

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			/* Register the shared observer handler the first time any character starts on this
			 * client. Never unregistered, for the reason the resource and buff handlers are not:
			 * ClientManager keeps handlers across stops, so a per-character unregister would have to
			 * be reference counted or the first despawn would switch off equipment updates for
			 * every remaining character. */
			if (base.IsClientStarted)
			{
				RegisterObservedSlotBroadcast(base.NetworkManager);
			}
		}

		// ── Spawn payload ───────────────────────────────────────────────

		/// <summary>
		/// Width of the byte count that frames this behaviour's spawn payload.
		/// </summary>
		private const int EQUIPMENT_PAYLOAD_LENGTH_BYTES = 4;

		/// <summary>
		/// Upper bound on equipped items accepted from a spawn payload. Well above the number of
		/// equipment slots; exists so a corrupt count cannot drive an unbounded read loop.
		/// </summary>
		private const int MAX_PAYLOAD_EQUIPMENT = 256;

		/// <summary>Payload shape flag: the receiver owns this character and gets ids and stacks.</summary>
		private const byte PAYLOAD_SHAPE_OWNER = 1;
		/// <summary>Payload shape flag: the receiver is an observer and gets template, slot and seed only.</summary>
		private const byte PAYLOAD_SHAPE_OBSERVER = 0;

		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			/* Where this behaviour's data ends. FishNet packs every NetworkBehaviour's spawn
			 * payload into one buffer with no per-behaviour framing, so an abort here would leave
			 * every behaviour after this one reading from the wrong offset. The length is validated
			 * against what the reader holds first; Reader.Position has no bounds check. */
			uint declaredLength = reader.ReadUInt32Unpacked();
			int remainingBytes = reader.Remaining;
			if (declaredLength > (uint)remainingBytes)
			{
				Log.Error("EquipmentController",
					$"ReadPayload: framed length {declaredLength} exceeds the {remainingBytes} bytes remaining in " +
					"the spawn payload. The stream cannot be resynchronised; discarding the remainder.");
				reader.Position += remainingBytes;
				return;
			}
			int equipmentBlockLength = (int)declaredLength;
			int equipmentBlockEnd = reader.Position + equipmentBlockLength;

			/* The shape is carried in the stream rather than derived from IsOwner at read time.
			 * Ownership IS assigned before the payload is read (ObjectCaching.Iterate calls
			 * InitializeEarly with the owner first), but one byte makes the block self-describing:
			 * the reader cannot disagree with the writer about which set of fields follows, and an
			 * unspawned controller — every test — can decode both shapes. */
			byte shape = reader.ReadUInt8Unpacked();
			bool ownerShape = shape == PAYLOAD_SHAPE_OWNER;

			int itemCount = reader.ReadInt32();
			if (itemCount < 0 || itemCount > MAX_PAYLOAD_EQUIPMENT)
			{
				Log.Error("EquipmentController",
					$"ReadPayload: item count {itemCount} is outside [0, {MAX_PAYLOAD_EQUIPMENT}]. Aborting payload read.");
				reader.Position = equipmentBlockEnd;
				return;
			}

			for (int i = 0; i < itemCount; ++i)
			{
				long id = 0;
				uint stackSize = 1;
				if (ownerShape)
				{
					id = reader.ReadInt64();
				}
				int templateID = reader.ReadInt32();
				int slot = reader.ReadUInt8Unpacked();
				int seed = reader.ReadInt32();
				if (ownerShape)
				{
					stackSize = reader.ReadUInt32();
				}

				/* Resolve the template BEFORE constructing the item.
				 *
				 * Item's constructor stores whatever BaseItemTemplate.Get returns and then reads
				 * Template.MaxStackSize and Template.Generate unconditionally, so an id with no
				 * template throws inside the constructor — and this is a spawn payload, so the throw
				 * escapes ReadPayload, past the frame that exists to keep the stream aligned, and
				 * kills the read of every remaining behaviour on this object and of every object
				 * still in the spawn packet.
				 *
				 * Item templates are immutable assets registered before anything spawns, so a miss
				 * is not a version skew to tolerate quietly — it is corruption of this payload or a
				 * template that failed to load. Say so, and skip the entry: the bytes were already
				 * consumed above, so the stream stays aligned and the character simply appears
				 * without that piece. */
				BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(templateID);
				if (template == null)
				{
					Log.Error("EquipmentController",
						$"ReadPayload: no BaseItemTemplate is registered for id {templateID} (slot {slot}). " +
						"Skipping the item; this character will render without it.");
					continue;
				}

				Item item = new Item(id, seed, template, stackSize);

				/* A refused slot must not be equipped. ItemContainer.SetItemSlot documents that its
				 * return value is a genuine refusal, and the slot arrived off the wire with no range
				 * check — so an out-of-range slot applied the item's generated attribute modifiers to
				 * the character while the item itself lived in no container, leaving nothing that
				 * could ever unequip it. The frame recovers the stream; it cannot recover that. */
				if (!SetItemSlot(item, slot))
				{
					Log.Error("EquipmentController",
						$"ReadPayload: slot {slot} was refused for template {templateID}. " +
						"Skipping the item rather than equipping one no container holds.");
					continue;
				}

				if (item.IsEquippable)
				{
					item.Equippable.Equip(Character);
				}
			}

			/* Belt and braces on the success path: the frame absorbs any shape disagreement here
			 * rather than corrupting the behaviour after this one. */
			if (reader.Position != equipmentBlockEnd)
			{
				Log.Error("EquipmentController",
					$"ReadPayload consumed {reader.Position - (equipmentBlockEnd - equipmentBlockLength)} of " +
					$"{equipmentBlockLength} framed bytes. Seeking to the end of the block; the equipment " +
					"state read above may be incomplete.");
				reader.Position = equipmentBlockEnd;
			}

			equipmentSnapshotDirty = true;
		}

		/// <inheritdoc />
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			WritePayload(writer, PayloadVisibility.IsOwner(this, conn));
		}

		/// <summary>
		/// Writes the spawn payload in one of two shapes.
		/// </summary>
		/// <remarks>
		/// The owner needs the instance id (its inventory operations name items by it) and the
		/// stack size. An observer can use neither — it never holds the item's inventory identity
		/// and equipment does not stack — and only ever reads template, slot and seed to choose a
		/// mesh. Dropping 12 bytes per item off every observer's spawn of every character is what
		/// this split buys; <see cref="PayloadVisibility"/> decides which side the receiver is on.
		/// Split out from the override so both shapes can be produced without a live connection.
		/// </remarks>
		/// <param name="writer">Writer to append to.</param>
		/// <param name="ownerShape">True to write the owner-shaped block.</param>
		internal void WritePayload(Writer writer, bool ownerShape)
		{
			/* Everything below is framed by a byte count so ReadPayload can resynchronise after
			 * rejecting an untrustworthy count. See EQUIPMENT_PAYLOAD_LENGTH_BYTES. */
			writer.Skip(EQUIPMENT_PAYLOAD_LENGTH_BYTES);
			int equipmentBlockStart = writer.Position;

			writer.WriteUInt8Unpacked(ownerShape ? PAYLOAD_SHAPE_OWNER : PAYLOAD_SHAPE_OBSERVER);

			if (Items == null ||
				Items.Count < 1)
			{
				writer.WriteInt32(0);
				writer.InsertUInt32Unpacked((uint)(writer.Position - equipmentBlockStart),
					equipmentBlockStart - EQUIPMENT_PAYLOAD_LENGTH_BYTES);
				return;
			}

			writer.WriteInt32(FilledSlots());
			foreach (Item item in Items)
			{
				if (item == null)
				{
					continue;
				}
				if (ownerShape)
				{
					writer.WriteInt64(item.ID);
				}
				writer.WriteInt32(item.Template.ID);
				writer.WriteUInt8Unpacked((byte)item.Slot);
				writer.WriteInt32(item.IsGenerated ? item.Generator.Seed : 0);
				if (ownerShape)
				{
					writer.WriteUInt32(item.IsStackable ? item.Stackable.Amount : 0);
				}
			}

			writer.InsertUInt32Unpacked((uint)(writer.Position - equipmentBlockStart),
				equipmentBlockStart - EQUIPMENT_PAYLOAD_LENGTH_BYTES);
		}

		// ── Observer broadcast ──────────────────────────────────────────

		/// <summary>Scratch recipient set; <c>BroadcastExcept</c> mutates the set it is given.</summary>
		private static readonly HashSet<NetworkConnection> observerRecipients = new HashSet<NetworkConnection>();

		/// <summary>
		/// Copies <paramref name="observers"/> into <paramref name="into"/> without <paramref name="owner"/>.
		/// </summary>
		/// <remarks>
		/// <c>ServerManager.BroadcastExcept(HashSet, NetworkConnection, ...)</c> removes the
		/// exclusion from the set it is handed. Handing it <c>NetworkObject.Observers</c> directly
		/// would silently drop the owner from the character's observer list.
		/// </remarks>
		internal static void CollectObserverRecipients(IEnumerable<NetworkConnection> observers, NetworkConnection owner, HashSet<NetworkConnection> into)
		{
			into.Clear();
			if (observers == null)
			{
				return;
			}
			foreach (NetworkConnection conn in observers)
			{
				if (conn == null || ReferenceEquals(conn, owner))
				{
					continue;
				}
				into.Add(conn);
			}
		}

		/// <summary>
		/// Tells this character's observers (never its owner) what a slot now holds.
		/// </summary>
		/// <remarks>
		/// Server only, and only once the object is spawned — before that there are no observers
		/// and the spawn payload carries the state. Reliable: see
		/// <see cref="EquipmentObservedSlotBroadcast"/>. <c>ServerManager.Broadcast</c> is not
		/// subject to the observer streaming send filter (that hook lives in the unreliable
		/// ObserversRpc path), so an observer that is being rate limited still receives this.
		/// </remarks>
		private void PushObservedSlot(byte slot)
		{
			FishNet.Object.NetworkObject nob = base.NetworkObject;
			if (nob == null || !nob.IsSpawned || nob.NetworkManager == null || !nob.NetworkManager.IsServerStarted)
			{
				return;
			}

			/* Forwarded objects carry the equipment array in every reconcile, and their observers
			 * run RestoreFromReconcile against it. Sending this too would have two writers on one
			 * container — the reconcile builds items carrying instance ids, this builds visual-only
			 * ones — which is the phantom-item failure, on observers. See ObserverSyncMode. */
			if (!ObserverSyncMode.ShouldBroadcastToObservers(nob))
			{
				return;
			}

			EquipmentObservedSlotBroadcast msg = new EquipmentObservedSlotBroadcast
			{
				CharacterObjectID = nob.ObjectId,
				Slot = slot,
				TemplateID = 0,
				Seed = 0,
			};
			if (TryGetItem(slot, out Item item) && item != null && item.Template != null)
			{
				msg.TemplateID = item.Template.ID;
				msg.Seed = item.IsGenerated ? item.Generator.Seed : 0;
			}

			CollectObserverRecipients(nob.Observers, nob.Owner, observerRecipients);
			if (observerRecipients.Count == 0)
			{
				return;
			}
			nob.NetworkManager.ServerManager.Broadcast(observerRecipients, msg, true, Channel.Reliable);
		}

		/// <summary>True once this client has registered the shared observer handler.</summary>
		private static bool observedSlotBroadcastRegistered;

		/// <summary>Registers the shared observer handler for this client.</summary>
		internal static void RegisterObservedSlotBroadcast(FishNet.Managing.NetworkManager networkManager)
		{
			if (observedSlotBroadcastRegistered || networkManager == null)
			{
				return;
			}
			networkManager.ClientManager.RegisterBroadcast<EquipmentObservedSlotBroadcast>(OnObservedSlotBroadcast);
			observedSlotBroadcastRegistered = true;
		}

		/// <summary>
		/// Applies an observer broadcast to whichever character it names.
		/// </summary>
		/// <remarks>
		/// A message for a character that is not spawned here is dropped: either it has not
		/// arrived yet, in which case its spawn payload will carry this state, or it has already
		/// gone. The owner is skipped as well — the server excludes it, but the acknowledgement and
		/// reconcile own that client's slots and a stray copy must never overwrite a real item
		/// with an id-less one.
		/// </remarks>
		private static void OnObservedSlotBroadcast(EquipmentObservedSlotBroadcast msg, Channel channel)
		{
			FishNet.Managing.NetworkManager nm = FishNet.InstanceFinder.NetworkManager;
			if (nm == null || nm.ClientManager == null || nm.IsServerStarted)
			{
				return;
			}
			if (!nm.ClientManager.Objects.Spawned.TryGetValue(msg.CharacterObjectID, out FishNet.Object.NetworkObject nob) ||
				nob == null)
			{
				return;
			}
			if (nob.IsOwner)
			{
				return;
			}

			EquipmentController controller = nob.GetComponent<EquipmentController>();
			controller?.ApplyObservedSlot(msg.Slot, msg.TemplateID, msg.Seed);
		}

		/// <summary>
		/// Updates one slot from what the server told this observer, firing the same events an
		/// equip or unequip would so the visual controller redraws.
		/// </summary>
		/// <remarks>
		/// Idempotent by content: the same template and seed already in the slot is a no-op, which
		/// is what makes the spawn payload and a broadcast that raced it safe to apply in either
		/// order. The item created here has no instance id — an observer has no use for one — which
		/// matches what <see cref="ReadPayload"/> builds for observers.
		/// </remarks>
		/// <param name="slot">Equipment slot index.</param>
		/// <param name="templateID">Template now in the slot, or 0 for empty.</param>
		/// <param name="seed">Generation seed of the item now in the slot.</param>
		internal void ApplyObservedSlot(int slot, int templateID, int seed)
		{
			if (!IsValidSlot(slot))
			{
				return;
			}

			Item current = Items[slot];

			if (templateID == 0)
			{
				if (current == null)
				{
					return;
				}
				DetachFromSlot(current, (byte)slot, applyAttributeModifiers: false);
				return;
			}

			if (current != null &&
				current.Template != null &&
				current.Template.ID == templateID &&
				(current.IsGenerated ? current.Generator.Seed : 0) == seed)
			{
				return;
			}

			BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(templateID);
			if (template == null)
			{
				Log.Warning("EquipmentController",
					$"Observed equipment broadcast named unknown template {templateID} for slot {slot}; clearing the slot.");
				if (current != null)
				{
					DetachFromSlot(current, (byte)slot, applyAttributeModifiers: false);
				}
				return;
			}

			if (current != null)
			{
				DetachFromSlot(current, (byte)slot, applyAttributeModifiers: false);
			}

			Item item = new Item(0, seed, template, 1);
			if (!SetItemSlot(item, slot))
			{
				return;
			}
			if (item.IsEquippable)
			{
				/* Silently, like the owner's acknowledgement path: this is another character's
				 * sheet, and the server already sends its authoritative ExternalModifier in
				 * CharacterAttributesBroadcast. Calling Equip here ran ItemGenerator.ApplyAttributes
				 * and added the item's bonuses ON TOP of that total, so a watching client saw the
				 * peer's maximum jump on every equip and only converged when the next attribute
				 * diff happened to be non-empty. Equipment-derived attributes are server-only; the
				 * observer's interest in an equip is the mesh. The matching detach above is silent
				 * for the same reason — removing a modifier this path never added would drift the
				 * sheet the other way. */
				SetEquippedCharacterSilently(item);
			}
			OnItemEquipped?.Invoke(item, (ItemSlot)slot);
		}

		// ── Owner acknowledgements ──────────────────────────────────────

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

		private void OnClientEquipmentEquipItemBroadcastReceived(EquipmentEquipItemBroadcast msg, Channel channel)
		{
			ApplyEquipAcknowledgement(msg);
		}

		private void OnClientEquipmentUnequipItemBroadcastReceived(EquipmentUnequipItemBroadcast msg, Channel channel)
		{
			ApplyUnequipAcknowledgement(msg);
		}
#endif

		/// <summary>
		/// Handles an equip acknowledgement from the server, equipping the item from the specified inventory.
		/// </summary>
		/// <remarks>
		/// A no-op when the reconcile already placed the requested instance (the pending record is
		/// flagged) — after a swap, the source index holds the previously equipped item, and
		/// re-running the equip would swap the two straight back. Also a no-op when the source
		/// index is empty, which is the non-swap form of the same race.
		/// </remarks>
		internal void ApplyEquipAcknowledgement(EquipmentEquipItemBroadcast msg)
		{
			byte slot = msg.Slot;
			if (pendingEquips.TryGetValue(slot, out PendingEquip pending))
			{
				pendingEquips.Remove(slot);
				if (pending.AppliedByReconcile ||
					(TryGetItem(slot, out Item already) && already != null && already.ID == pending.InstanceID))
				{
					return;
				}
			}

			IItemContainer container = msg.FromInventory == InventoryType.Equipment ? null : ResolveContainer(msg.FromInventory);
			if (container == null ||
				!container.TryGetItem(msg.InventoryIndex, out Item sourceItem))
			{
				return;
			}
			Equip(sourceItem, msg.InventoryIndex, container, (ItemSlot)slot, applyAttributes: false);
		}

		/// <summary>
		/// Handles an unequip acknowledgement from the server, moving the item to the specified inventory.
		/// </summary>
		/// <remarks>
		/// A no-op when the reconcile already emptied the slot and returned the item — the item is
		/// then already in the container the acknowledgement names.
		/// </remarks>
		internal void ApplyUnequipAcknowledgement(EquipmentUnequipItemBroadcast msg)
		{
			byte slot = msg.Slot;
			if (pendingUnequips.TryGetValue(slot, out PendingUnequip pending))
			{
				pendingUnequips.Remove(slot);
				if (pending.AppliedByReconcile)
				{
					return;
				}
			}
			if (IsSlotEmpty(slot))
			{
				// Reconcile-first without a pending record: the slot is already empty and the item
				// was returned to a container by RestoreFromReconcile.
				return;
			}

			IItemContainer container = msg.ToInventory == InventoryType.Equipment ? null : ResolveContainer(msg.ToInventory);
			if (container == null)
			{
				return;
			}
			Unequip(container, slot, out _, applyAttributes: false);
		}

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
		/// are updated — the prediction pipeline's reconcile handles attribute changes. The
		/// item still learns which character holds it (see <see cref="SetEquippedCharacterSilently"/>).
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

			// Already in place: the reconcile or an earlier acknowledgement did this. Reporting
			// success keeps the caller from treating an idempotent repeat as a refusal.
			if (TryGetItem(slotIndex, out Item alreadyEquipped) && ReferenceEquals(alreadyEquipped, item))
			{
				return true;
			}

			if (container != null)
			{
				if (TryGetItem(slotIndex, out Item previousItem) &&
					previousItem.IsEquippable)
				{
					if (applyAttributes)
					{
						previousItem.Equippable.Unequip();
					}
					else
					{
						ClearEquippedCharacterSilently(previousItem);
					}

					if (!container.SetItemSlot(previousItem, inventoryIndex))
					{
						if (applyAttributes)
						{
							previousItem.Equippable.Equip(Character);
						}
						else
						{
							SetEquippedCharacterSilently(previousItem);
						}
						return false;
					}
				}
				else
				{
					// RemoveItem returns null when the slot is locked, out of range, already empty,
					// or the container refuses manipulation (dead character). The result used to be
					// discarded and the equip proceeded regardless, leaving the SAME Item instance
					// referenced by both the source slot and the equipment slot: two live references,
					// two persistence rows, one duplicated item after the next login.
					//
					// The identity check matters as much as the null check. A concurrent request can
					// have moved something else into inventoryIndex between validation and here, in
					// which case we would otherwise delete an unrelated item to make room.
					Item removed = container.RemoveItem(inventoryIndex);
					if (!ReferenceEquals(removed, item))
					{
						// Put back whatever we took and refuse the equip. Nothing has been mutated
						// on this side yet, so this leaves both containers exactly as we found them.
						if (removed != null)
						{
							container.SetItemSlot(removed, inventoryIndex);
						}
						return false;
					}
				}
			}

			if (!SetItemSlot(item, slotIndex))
			{
				return false;
			}

			if (item.IsEquippable)
			{
				if (applyAttributes)
				{
					item.Equippable.Equip(Character);
				}
				else
				{
					SetEquippedCharacterSilently(item);
				}
			}

			equipmentSnapshotDirty = true;
			Character.Invoke(onEquipTriggers, new EquipItemEventData(Character, item, toSlot));
			OnItemEquipped?.Invoke(item, toSlot);

			if (applyAttributes)
			{
				PushObservedSlot(slotIndex);
			}
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

			if (item.IsEquippable)
			{
				if (applyAttributes)
				{
					item.Equippable.Unequip();
				}
				else
				{
					ClearEquippedCharacterSilently(item);
				}
			}

			SetItemSlot(null, slot);

			equipmentSnapshotDirty = true;
			Character.Invoke(onUnequipTriggers, new EquipItemEventData(Character, item, (ItemSlot)slot));
			OnItemUnequipped?.Invoke(item, (ItemSlot)slot);

			if (applyAttributes)
			{
				PushObservedSlot(slot);
			}
			return true;
		}

		/// <summary>
		/// Points an item's <see cref="ItemEquippable.Character"/> at this character without
		/// applying its generated attribute modifiers.
		/// </summary>
		/// <remarks>
		/// The client's acknowledgement path must not apply modifiers — the attribute reconcile is
		/// the authority for those — but it must still record the owner, because
		/// <c>ItemGenerator.SetAttribute</c> reads <c>Equippable.Character</c> to decide whether a
		/// later attribute change has a character to propagate to, and the payload and reconcile
		/// paths both set it. <see cref="ItemEquippable.Character"/> is only writable through
		/// <see cref="ItemEquippable.Equip"/>, which raises <c>OnEquip</c>; the item's own
		/// attribute handler is the only subscriber and is only attached to generated items, so
		/// it is detached around the call and restored afterwards. Not generated means no handler
		/// and nothing to detach.
		/// </remarks>
		private void SetEquippedCharacterSilently(Item item)
		{
			if (item == null || item.Equippable == null)
			{
				return;
			}
			if (item.IsGenerated)
			{
				item.Equippable.OnUnequip -= item.ItemEquippable_OnUnequip;
				item.Equippable.OnEquip -= item.ItemEquippable_OnEquip;
			}
			try
			{
				// Character is null on an unspawned controller; ItemEquippable.Equip(null) is a no-op.
				item.Equippable.Equip(Character);
			}
			finally
			{
				if (item.IsGenerated)
				{
					item.Equippable.OnEquip += item.ItemEquippable_OnEquip;
					item.Equippable.OnUnequip += item.ItemEquippable_OnUnequip;
				}
			}
		}

		/// <summary>Clears an item's <see cref="ItemEquippable.Character"/> without removing modifiers.</summary>
		private static void ClearEquippedCharacterSilently(Item item)
		{
			if (item == null || item.Equippable == null || item.Equippable.Character == null)
			{
				return;
			}
			if (item.IsGenerated)
			{
				item.Equippable.OnUnequip -= item.ItemEquippable_OnUnequip;
			}
			try
			{
				item.Equippable.Unequip();
			}
			finally
			{
				if (item.IsGenerated)
				{
					item.Equippable.OnUnequip += item.ItemEquippable_OnUnequip;
				}
			}
		}
	}
}
