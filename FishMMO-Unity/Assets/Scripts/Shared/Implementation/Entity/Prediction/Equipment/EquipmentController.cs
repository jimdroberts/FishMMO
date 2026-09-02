using FishNet.Connection;
using FishNet.Managing.Timing;
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
	///
	/// Implements <see cref="IPredictableController"/> at Order 93 so equipment participates in the
	/// prediction pipeline: an owner's equip or unequip is INPUT, carried by
	/// <see cref="CharacterReplicateData.EquipmentRequest"/>, applied by both the owner and the
	/// server inside the replicate body for that tick, and confirmed by the reconcile. Equipment-driven
	/// attribute changes therefore land on the same tick on both peers, and the attribute reconcile
	/// (Order 95) never has a socket to correct that the replay would not also have re-applied.
	///
	/// Three network paths feed this container and they must agree with one another:
	/// <list type="bullet">
	/// <item>The spawn payload (<see cref="WritePayload"/>/<see cref="ReadPayload"/>) — owner
	/// shaped for the owner, template+seed only for everyone else.</item>
	/// <item>The owner's replicate input and reconcile snapshot: the input moves items, the
	/// snapshot says which instance sits in each socket, and <see cref="RestoreFromReconcile"/>
	/// settles any disagreement by MOVING the real instance, never by cloning it. A reconcile that
	/// predates a request restores the earlier state and the replay re-applies the request.</item>
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
		/// <inheritdoc />
		public event Action<EquipmentRequestKind, ItemSlot, InventoryType, int, bool> OnRequestResolved;
		/// <inheritdoc />
		public event Action<IEquipmentController, EquipmentChange> OnServerEquipmentChanged;

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

		/// <summary>
		/// Server-side gate consulted before a replicated request is applied: is this character
		/// allowed to act at all, and may it reach the named container right now?
		/// </summary>
		/// <remarks>
		/// Installed by the server's inventory system. It answers the two questions this shared
		/// class cannot — "can the character act" is a server-side validation rule set, and "is a
		/// banker in range" needs the scene object registry. Null on every other peer, and null on
		/// the server means no gate, which is the permissive default a test harness expects.
		/// </remarks>
		public static Func<ICharacter, InventoryType, bool> ServerRequestValidator;

		// ── IPredictableController ──────────────────────────────────────

		/// <summary>
		/// Execution order in the unified prediction pipeline.
		/// Runs after CooldownController (90) and before CharacterAttributeController (95)
		/// so equipment attribute modifiers are settled before the attribute reconcile snapshot.
		/// </summary>
		/// <remarks>
		/// <b>Must stay below <see cref="CharacterAttributeController.Order"/> (95).</b> Restoring an
		/// item restates its own ledger entry through
		/// <c>ItemGenerator.ApplyAttributes</c> → <c>CharacterAttribute.SetSource</c>; the attribute
		/// reconcile then installs the server's total as the residual over whatever those entries sum
		/// to. Raising this above 95 would have that residual computed before this tick's equipment is
		/// settled, so an item equipped on the reconciled tick counts twice. See the remarks on
		/// <see cref="CharacterAttributeController.Order"/>.
		/// </remarks>
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

		// ── Owner request queue ─────────────────────────────────────────

		/// <summary>A request waiting for the next replicate tick.</summary>
		private struct QueuedRequest
		{
			public EquipmentRequestKind Kind;
			public InventoryType Container;
			public ItemSlot Socket;
			public int Index;
		}

		private QueuedRequest? queuedRequest;

		/// <summary>
		/// What the owner's prediction did to one socket, so a reconcile that does not yet include
		/// it can put things back exactly where they were.
		/// </summary>
		/// <remarks>
		/// The snapshot names the instance in each socket and nothing else. When it disagrees with a
		/// socket the owner has predicted into, the item has to go SOMEWHERE, and "the first container
		/// with room" is a guess the replay then cannot undo: the replayed request names the index it
		/// took the item from, and if the restore put it elsewhere the replay finds an empty slot.
		/// So the origin is recorded when the prediction is applied and consulted when it is undone.
		/// </remarks>
		private struct PredictedMove
		{
			/// <summary>Replicate tick the move was applied on; a reconcile at or past it settles it.</summary>
			public uint Tick;
			/// <summary>The item that moved.</summary>
			public long ItemID;
			/// <summary>The container on the other side of the move.</summary>
			public InventoryType Container;
			/// <summary>The index in that container: the source of an equip, the landing slot of an unequip.</summary>
			public int Index;
		}

		/// <summary>Sockets the owner has predicted an item INTO, keyed by socket, with where it came from.</summary>
		private readonly Dictionary<byte, PredictedMove> predictedEquips = new Dictionary<byte, PredictedMove>();

		/// <summary>Sockets the owner has predicted an item OUT OF, keyed by socket, with where it went.</summary>
		private readonly Dictionary<byte, PredictedMove> predictedUnequips = new Dictionary<byte, PredictedMove>();

		/// <summary>Sockets whose predicted-move records are due for removal. Pooled for the reconcile path.</summary>
		private readonly List<byte> settledSockets = new List<byte>();

		/// <inheritdoc />
		public void PopulateInput(ref CharacterReplicateData input)
		{
			if (!queuedRequest.HasValue)
			{
				return;
			}

			QueuedRequest request = queuedRequest.Value;
			queuedRequest = null;

			if (!EquipmentReplicateInput.TryPack(request.Kind, request.Container, request.Socket, out byte packed))
			{
				// RequestEquip/RequestUnequip validated the fields, so this cannot happen; it is
				// refused rather than sent malformed, and the panel hears the refusal.
				OnRequestResolved?.Invoke(request.Kind, request.Socket, request.Container, request.Index, false);
				return;
			}

			input.EquipmentRequest = packed;
			input.EquipmentIndex = (short)request.Index;
		}

		/// <inheritdoc />
		public void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel)
		{
			/* Created is the engine's discriminator for "this tick carried real input". A tick the
			 * server ran with default data has no request in it by construction, but the guard
			 * states the rule rather than relying on the zero. See CharacterPredictionController. */
			if (!state.ContainsCreated() || input.EquipmentRequest == 0)
			{
				return;
			}

			bool authoritative = IsServerPeer;
			bool owner = base.NetworkObject != null && base.IsOwner;
			if (!authoritative && !owner)
			{
				// Observers do not replicate at all with forwarding off; this is the contract, not a
				// defence against a message that can arrive.
				return;
			}

			ApplyEquipmentInput(ref input, authoritative, owner, state.ContainsReplayed());
		}

		/// <summary>
		/// Applies the equipment request carried by one replicate, on whichever peer this is.
		/// </summary>
		/// <remarks>
		/// Split from <see cref="OnReplicate"/> so the rule can be exercised without a spawned
		/// NetworkObject: the flags that decide the peer's role are passed in rather than read.
		/// </remarks>
		/// <param name="input">The replicate input for this tick.</param>
		/// <param name="authoritative">True on the server.</param>
		/// <param name="owner">True on the owning client.</param>
		/// <param name="replayed">True when this is a replay after a reconcile rather than the first run of the tick.</param>
		/// <returns>True when the request was applied.</returns>
		internal bool ApplyEquipmentInput(ref CharacterReplicateData input, bool authoritative, bool owner, bool replayed)
		{
			if (!EquipmentReplicateInput.TryUnpack(input.EquipmentRequest, out EquipmentRequestKind kind,
					out InventoryType containerType, out ItemSlot socket))
			{
				return false;
			}

			uint tick = input.GetTick();
			int index = input.EquipmentIndex;
			bool applied = kind == EquipmentRequestKind.Equip
				? ApplyPredictedEquip(containerType, index, socket, tick, authoritative, owner, replayed)
				: ApplyPredictedUnequip(containerType, socket, tick, authoritative, owner, replayed);

			/* The panel that marked its slots as waiting hears the outcome once, on the first run.
			 * A replay re-applies a request the panel already heard about. */
			if (owner && !replayed)
			{
				OnRequestResolved?.Invoke(kind, socket, containerType, kind == EquipmentRequestKind.Equip ? index : -1, applied);
			}
			return applied;
		}

		/// <summary>Runs one replicated equip on this peer.</summary>
		private bool ApplyPredictedEquip(InventoryType containerType, int index, ItemSlot socket, uint tick, bool authoritative, bool owner, bool replayed)
		{
			IItemContainer source = ResolveContainer(containerType);
			if (source == null ||
				!source.TryGetItem(index, out Item item) ||
				item == null ||
				item.ID <= 0)
			{
				/* No identity means the database has not written it yet, and the ledger declines an
				 * id of zero; the item is not usable until its row exists. The panel refuses it too,
				 * so this is a server-side rule stated for a client that did not. */
				return false;
			}

			if (authoritative &&
				ServerRequestValidator != null &&
				!ServerRequestValidator(Character, containerType))
			{
				return false;
			}

			if (!Equip(item, index, source, socket, containerType, raiseTriggers: !replayed))
			{
				return false;
			}

			if (owner && !authoritative)
			{
				predictedEquips[(byte)socket] = new PredictedMove
				{
					Tick = tick,
					ItemID = item.ID,
					Container = containerType,
					Index = index,
				};
			}
			return true;
		}

		/// <summary>Runs one replicated unequip on this peer.</summary>
		private bool ApplyPredictedUnequip(InventoryType containerType, ItemSlot socket, uint tick, bool authoritative, bool owner, bool replayed)
		{
			IItemContainer destination = ResolveContainer(containerType);
			if (destination == null ||
				!TryGetItem((byte)socket, out Item item) ||
				item == null ||
				item.ID <= 0)
			{
				return false;
			}

			if (authoritative &&
				ServerRequestValidator != null &&
				!ServerRequestValidator(Character, containerType))
			{
				return false;
			}

			if (!Unequip(destination, (byte)socket, containerType, out _, raiseTriggers: !replayed))
			{
				return false;
			}

			if (owner && !authoritative)
			{
				predictedUnequips[(byte)socket] = new PredictedMove
				{
					Tick = tick,
					ItemID = item.ID,
					Container = containerType,
					Index = item.Slot,
				};
			}
			return true;
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
			 * confirmation of a predicted equip.
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

			RestoreFromReconcile(data.Equipment, data.GetTick());
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
						ItemID = item.ID,
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
		/// inventory.
		/// </para>
		/// <para>
		/// <b>A snapshot older than a predicted move is not wrong, it is early.</b> The owner applied
		/// its request on tick T; a snapshot for a tick before T describes the world without it, and
		/// restoring that world is exactly right — the replay that follows re-runs T and applies the
		/// request again. What makes that work is that the restore puts the item back where the
		/// request will look for it: the recorded origin of a predicted equip, the recorded landing
		/// slot of a predicted unequip. A snapshot at or past T is the verdict; its records are
		/// dropped once it has been applied, whether it confirmed the move or refused it.
		/// </para>
		/// <para>
		/// An item that cannot be found anywhere on this client is still materialised from the
		/// entry, because the alternative — an empty slot the server considers filled — is worse.
		/// It should not happen: every item the owner can hold arrives with its identity.
		/// </para>
		/// </remarks>
		/// <param name="entries">The server's socket contents.</param>
		/// <param name="reconcileTick">The replicate tick the snapshot describes, or <see cref="TimeManager.UNSET_TICK"/>.</param>
		internal void RestoreFromReconcile(EquipmentReconcileEntry[] entries, uint reconcileTick)
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
			 * index. */
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
						currentItem.ID == entry.ItemID)
					{
						continue;
					}

					// Locate the real instance before touching the slot, so a swap between two
					// equipment slots reads both sides before either is written.
					Item realItem = FindOwnedInstance(entry.ItemID, (byte)slot, out IItemContainer sourceContainer, out int sourceIndex);

					// Unequip old item if present. It goes back to its recorded origin when the
					// owner predicted it in, otherwise to wherever the incoming item came from —
					// that is what the server's Equip did — or, when neither is known, to whichever
					// container will take it.
					if (currentItem != null)
					{
						DetachFromSlot(currentItem, (byte)slot);
						if (!TryReturnToPredictedOrigin(currentItem, (byte)slot))
						{
							if (sourceContainer != null && sourceIndex >= 0 && !ReferenceEquals(sourceContainer, this) &&
								sourceContainer.SetItemSlot(currentItem, sourceIndex))
							{
								// Placed at the index the incoming item vacated.
							}
							else
							{
								ReturnToAnyContainer(currentItem, null);
							}
						}
					}

					Item newItem = realItem;
					if (newItem == null)
					{
						Log.Warning("EquipmentController",
							$"Reconcile names item {entry.ItemID} (template {entry.TemplateID}) in socket {slot}, but this client holds no such item; materialising it.");
						newItem = new Item(entry.ItemID, entry.Seed, entry.TemplateID, 1); // Equipment items are never stackable
					}

					SetItemSlot(newItem, slot);
					if (newItem.IsEquippable)
					{
						newItem.Equippable.Equip(Character);
					}

					OnItemEquipped?.Invoke(newItem, (ItemSlot)slot);
					changed = true;
				}
			}

			DropSettledPredictions(reconcileTick);

			if (changed)
			{
				equipmentSnapshotDirty = true;
			}
		}

		/// <summary>
		/// Forgets every predicted move a snapshot at <paramref name="reconcileTick"/> has ruled on.
		/// </summary>
		/// <remarks>
		/// A record whose tick is at or before the snapshot's has been either confirmed or refused;
		/// the tick it belongs to is not replayed after this snapshot, so nothing will consult it
		/// again. Records for later ticks stay, because those ticks ARE about to be replayed.
		/// </remarks>
		private void DropSettledPredictions(uint reconcileTick)
		{
			if (reconcileTick == TimeManager.UNSET_TICK)
			{
				return;
			}
			DropSettled(predictedEquips, reconcileTick);
			DropSettled(predictedUnequips, reconcileTick);
		}

		private void DropSettled(Dictionary<byte, PredictedMove> records, uint reconcileTick)
		{
			if (records.Count == 0)
			{
				return;
			}
			settledSockets.Clear();
			foreach (KeyValuePair<byte, PredictedMove> pair in records)
			{
				if (pair.Value.Tick <= reconcileTick)
				{
					settledSockets.Add(pair.Key);
				}
			}
			for (int i = 0; i < settledSockets.Count; ++i)
			{
				records.Remove(settledSockets[i]);
			}
		}

		/// <summary>
		/// Puts an item the owner predicted into <paramref name="socket"/> back at the index it was
		/// taken from, when that index is free.
		/// </summary>
		private bool TryReturnToPredictedOrigin(Item item, byte socket)
		{
			if (!predictedEquips.TryGetValue(socket, out PredictedMove move) || move.ItemID != item.ID)
			{
				return false;
			}
			IItemContainer origin = ResolveContainer(move.Container);
			return origin != null &&
				origin.IsSlotEmpty(move.Index) &&
				origin.SetItemSlot(item, move.Index);
		}

		/// <summary>
		/// Finds the live instance with <paramref name="instanceID"/> on this client and removes it
		/// from wherever it was, ready to be placed in <paramref name="targetSlot"/>.
		/// </summary>
		/// <remarks>
		/// Search order: the recorded landing slot of a predicted unequip out of this socket (exact,
		/// cheapest), the other equipment slots (a two-slot swap), the inventory, then the bank. The
		/// removal is done here rather than by the caller so the source index reported back is
		/// guaranteed vacant.
		/// </remarks>
		private Item FindOwnedInstance(long instanceID, byte targetSlot, out IItemContainer container, out int index)
		{
			container = null;
			index = -1;
			if (instanceID == 0 || Character == null)
			{
				return null;
			}

			// The record of a predicted unequip names exactly where the item went.
			if (predictedUnequips.TryGetValue(targetSlot, out PredictedMove move) && move.ItemID == instanceID)
			{
				IItemContainer named = ResolveContainer(move.Container);
				if (named != null &&
					named.TryGetItem(move.Index, out Item namedItem) &&
					namedItem != null && namedItem.ID == instanceID)
				{
					Item removed = named.RemoveItem(move.Index);
					if (ReferenceEquals(removed, namedItem))
					{
						container = named;
						index = move.Index;
						return removed;
					}
					if (removed != null)
					{
						named.SetItemSlot(removed, move.Index);
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

			if (TryReturnToPredictedOrigin(item, slot))
			{
				return;
			}
			ReturnToAnyContainer(item, null);
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

		/// <inheritdoc />
		public void ApplyUnequipDestination(long itemID, InventoryType container, int slot)
		{
			PlaceAtAcknowledgedSlot(itemID, container, slot);
		}

		/// <summary>
		/// Moves an item to the slot the server says it ended up in.
		/// </summary>
		/// <remarks>
		/// The item is found by identity, not by slot, because where this client put it is exactly
		/// what cannot be trusted here: both peers choose the landing slot deterministically from
		/// their own copy of the container, and the copies can differ by a grant that landed on one
		/// side first.
		/// </remarks>
		private void PlaceAtAcknowledgedSlot(long itemID, InventoryType container, int slot)
		{
			if (slot < 0 || itemID == 0)
			{
				return;
			}

			IItemContainer destination = container == InventoryType.Equipment ? null : ResolveContainer(container);
			if (destination == null || !destination.IsValidSlot(slot))
			{
				return;
			}

			// Already where the server put it, which is the common case once both sides agree.
			if (destination.TryGetItem(slot, out Item atDestination) &&
				atDestination != null &&
				atDestination.ID == itemID)
			{
				return;
			}

			if (!TryTakeByID(destination, itemID, out Item item) &&
				!TryTakeByID(ResolveContainer(InventoryType.Inventory), itemID, out item) &&
				!TryTakeByID(ResolveContainer(InventoryType.Bank), itemID, out item))
			{
				Log.Warning("EquipmentController",
					$"Unequip landed in {container} slot {slot}, but item {itemID} " +
					"is not in any container this client knows about.");
				return;
			}

			if (!destination.SetItemSlot(item, slot))
			{
				Log.Warning("EquipmentController",
					$"Could not place item {itemID} at {container} slot {slot}.");
				ReturnToAnyContainer(item, container);
			}
		}

		/// <summary>
		/// Removes an item from a container by identity, wherever it happens to be sitting.
		/// </summary>
		private static bool TryTakeByID(IItemContainer container, long itemID, out Item item)
		{
			item = null;
			if (container == null)
			{
				return false;
			}

			for (int i = 0; i < container.Items.Count; ++i)
			{
				Item candidate = container.Items[i];
				if (candidate != null && candidate.ID == itemID)
				{
					item = container.RemoveItem(i);
					return item != null;
				}
			}

			return false;
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

		/// <summary>Names the container a caller handed in, for the persistence row.</summary>
		private InventoryType ClassifyContainer(IItemContainer container)
		{
			if (ReferenceEquals(container, ResolveContainer(InventoryType.Bank)))
			{
				return InventoryType.Bank;
			}
			if (ReferenceEquals(container, this))
			{
				return InventoryType.Equipment;
			}
			return InventoryType.Inventory;
		}

		// ── Owner request API ───────────────────────────────────────────

		/// <summary>
		/// Drops a request that is still waiting for a tick, telling its panel it will not run.
		/// </summary>
		/// <remarks>
		/// A queued request lives for at most one tick on an owner that is producing input, so a
		/// second request in the same tick is the only way to reach this — but a request queued on
		/// a controller whose owner is NOT producing input would otherwise sit there forever and
		/// refuse every request after it. Last writer wins; the displaced one is reported as not
		/// applied so its slots unlock.
		/// </remarks>
		private void DiscardQueuedRequest()
		{
			if (!queuedRequest.HasValue)
			{
				return;
			}
			QueuedRequest stale = queuedRequest.Value;
			queuedRequest = null;
			OnRequestResolved?.Invoke(stale.Kind, stale.Socket, stale.Container, stale.Index, false);
		}

		/// <inheritdoc />
		public bool RequestEquip(Item item, int sourceIndex, InventoryType fromContainer, ItemSlot socket)
		{
			if (item == null ||
				item.ID <= 0 ||
				!item.IsEquippable ||
				!(item.Template is EquippableItemTemplate equippable) ||
				equippable.Slot != socket ||
				!IsValidSlot((byte)socket) ||
				IsSlotLocked((byte)socket) ||
				!CanManipulate() ||
				!CharacterStateValidation.CanAct(Character))
			{
				return false;
			}

			IItemContainer source = ResolveContainer(fromContainer);
			if (source == null ||
				ReferenceEquals(source, this) ||
				!source.TryGetItem(sourceIndex, out Item atIndex) ||
				!ReferenceEquals(atIndex, item) ||
				source.IsSlotLocked(sourceIndex))
			{
				return false;
			}

			DiscardQueuedRequest();
			queuedRequest = new QueuedRequest
			{
				Kind = EquipmentRequestKind.Equip,
				Container = fromContainer,
				Socket = socket,
				Index = sourceIndex,
			};
			return true;
		}

		/// <inheritdoc />
		public bool RequestUnequip(ItemSlot socket, InventoryType toContainer)
		{
			if (toContainer == InventoryType.Equipment ||
				!TryGetItem((byte)socket, out Item item) ||
				item == null ||
				item.ID <= 0 ||
				IsSlotLocked((byte)socket) ||
				!CanManipulate() ||
				!CharacterStateValidation.CanAct(Character))
			{
				return false;
			}

			IItemContainer destination = ResolveContainer(toContainer);
			if (destination == null || !destination.CanAddItem(item))
			{
				return false;
			}

			DiscardQueuedRequest();
			queuedRequest = new QueuedRequest
			{
				Kind = EquipmentRequestKind.Unequip,
				Container = toContainer,
				Socket = socket,
				Index = -1,
			};
			return true;
		}

		/// <summary>True while a request is waiting for the next replicate. Test seam.</summary>
		internal bool HasQueuedRequest => queuedRequest.HasValue;

		/// <summary>True while the owner has a predicted move on this socket that no reconcile has ruled on. Test seam.</summary>
		internal bool HasPredictedMove(byte socket)
		{
			return predictedEquips.ContainsKey(socket) || predictedUnequips.ContainsKey(socket);
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

		/// <summary>
		/// Forces the server role for a controller that has no spawned NetworkObject. Tests only.
		/// </summary>
		internal bool ServerAuthorityForTests;

		/// <summary>True on the server. The one place the role is read for authority decisions.</summary>
		private bool IsServerPeer => ServerAuthorityForTests || (base.NetworkObject != null && base.IsServerStarted);

		/// <summary>
		/// True when this peer applied the attribute modifiers of the items in these slots, and so
		/// is the peer that must reverse them.
		/// </summary>
		/// <remarks>
		/// The server and the owning client equip for real. Everybody else is fed by
		/// <see cref="ApplyObservedSlot"/>, which builds the item and points it at this character
		/// through <see cref="SetEquippedCharacterSilently"/> WITHOUT applying its bonuses — the
		/// server's authoritative <c>ExternalModifier</c> already contains them and arrives in
		/// <c>CharacterAttributesBroadcast</c>. Mirrors <c>BuffController.SimulatesBuffEffects</c>,
		/// which draws the same line for the same reason.
		/// </remarks>
		private bool SimulatesEquipmentEffects =>
			ServerAuthorityForTests || (base.NetworkObject != null && (base.IsServerStarted || base.IsOwner));

		/// <inheritdoc />
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			/* Detach silently first on a peer that never applied these modifiers.
			 *
			 * Clear() destroys each item, and Item.Destroy unequips BEFORE detaching its handlers
			 * (deliberately — that is what stops a real unequip orphaning its modifiers), so
			 * ItemEquippable_OnUnequip runs and ItemGenerator.RemoveAttributes subtracts. On an
			 * observer those bonuses were never added: SetEquippedCharacterSilently suppresses the
			 * OnEquip half and then RE-ATTACHES the handler, leaving the removal half live. The
			 * result was an observed character's sheet dropping by its gear's worth on teardown.
			 * Clearing Character first makes ItemEquippable.Unequip a no-op, so the destroy still
			 * runs and raises nothing. */
			if (!SimulatesEquipmentEffects)
			{
				for (int i = 0; i < Items.Count; ++i)
				{
					Item item = Items[i];
					if (item != null && item.IsEquippable)
					{
						ClearEquippedCharacterSilently(item);
					}
				}
			}

			Clear();
			queuedRequest = null;
			predictedEquips.Clear();
			predictedUnequips.Clear();
			OnRequestResolved = null;
			OnServerEquipmentChanged = null;
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
					/* The OWNER equips for real; everybody else is pointed at this character without
					 * applying the item's bonuses.
					 *
					 * Equip raises OnEquip, which runs ItemGenerator.ApplyAttributes and writes the
					 * item's ledger entries. On an observer that is a double-apply by construction:
					 * the attribute payload this same spawn carries is the OBSERVER shape, whose
					 * ExternalModifier is the server's TOTAL and already contains every equipped
					 * item — see CharacterAttributeController.WritePayload. This is the identical
					 * mistake ApplyObservedSlot was rewritten to remove for the broadcast path, and
					 * the rule SimulatesEquipmentEffects states; the payload path was simply never
					 * brought into line with it. */
					if (ownerShape)
					{
						item.Equippable.Equip(Character);
					}
					else
					{
						SetEquippedCharacterSilently(item);
					}
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

			ObserverBroadcastScope.BroadcastToObserversExceptOwner(nob, msg, Channel.Reliable);
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
		/// gone. The owner is skipped as well — the server excludes it, but the replicate and
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
				/* Silently: this is another character's sheet, and the server already sends its
				 * authoritative ExternalModifier in CharacterAttributesBroadcast. Calling Equip here
				 * ran ItemGenerator.ApplyAttributes and added the item's bonuses ON TOP of that
				 * total, so a watching client saw the peer's maximum jump on every equip and only
				 * converged when the next attribute diff happened to be non-empty. Equipment-derived
				 * attributes are server-only; the observer's interest in an equip is the mesh. The
				 * matching detach above is silent for the same reason — removing a modifier this
				 * path never added would drift the sheet the other way. */
				SetEquippedCharacterSilently(item);
			}
			OnItemEquipped?.Invoke(item, (ItemSlot)slot);
		}

		// ── Owner acknowledgements ──────────────────────────────────────
		//
		// One message survives the move to predicted equipment: where an unequipped item landed.
		// The socket itself is settled by the reconcile.

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

			ClientManager.RegisterBroadcast<EquipmentUnequipItemBroadcast>(OnClientEquipmentUnequipItemBroadcastReceived);
		}

		/// <inheritdoc />
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<EquipmentUnequipItemBroadcast>(OnClientEquipmentUnequipItemBroadcastReceived);
			}
		}

		private void OnClientEquipmentUnequipItemBroadcastReceived(EquipmentUnequipItemBroadcast msg, Channel channel)
		{
			ApplyUnequipDestination(msg.ItemID, msg.ToInventory, msg.ToSlot);
		}
#endif

		/// <inheritdoc />
		public void Activate(int index)
		{
			if (!CharacterStateValidation.CanAct(Character))
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
			return Equip(item, inventoryIndex, container, toSlot, ClassifyContainer(container), raiseTriggers: true);
		}

		/// <summary>
		/// Equips an item, applying its attribute modifiers on this peer.
		/// </summary>
		/// <param name="raiseTriggers">
		/// False during a replay: the ECA triggers already fired when the tick first ran, and a
		/// replay re-applies the state without re-announcing it.
		/// </param>
		private bool Equip(Item item, int inventoryIndex, IItemContainer container, ItemSlot toSlot, InventoryType containerType, bool raiseTriggers)
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

			// Already in place: a reconcile or an earlier run of this tick did this. Reporting
			// success keeps the caller from treating an idempotent repeat as a refusal.
			if (TryGetItem(slotIndex, out Item alreadyEquipped) && ReferenceEquals(alreadyEquipped, item))
			{
				return true;
			}

			Item displaced = null;
			if (container != null)
			{
				/* The source slot must hold the item being equipped. Callers pass the pair they
				 * read, but a request that has been queued for a tick, or an ECA action built from
				 * stale event data, can name an index something else has since moved into — and the
				 * swap branch below would overwrite that something with the previously equipped item,
				 * leaving `item` still in its own slot AND in the socket. */
				if (!container.TryGetItem(inventoryIndex, out Item atIndex) || !ReferenceEquals(atIndex, item))
				{
					return false;
				}

				if (TryGetItem(slotIndex, out Item previousItem) &&
					previousItem.IsEquippable)
				{
					previousItem.Equippable.Unequip();

					if (!container.SetItemSlot(previousItem, inventoryIndex))
					{
						previousItem.Equippable.Equip(Character);
						return false;
					}
					displaced = previousItem;
				}
				else
				{
					// RemoveItem returns null when the slot is locked, out of range, already empty,
					// or the container refuses manipulation. The result used to be discarded and the
					// equip proceeded regardless, leaving the SAME Item instance referenced by both
					// the source slot and the equipment slot: two live references, two persistence
					// rows, one duplicated item after the next login.
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
				/* Put the containers back the way they were: the socket refused (locked), and the
				 * source slot is now empty or holds the displaced item. */
				if (container != null)
				{
					if (displaced != null)
					{
						container.SetItemSlot(item, inventoryIndex);
						SetItemSlot(displaced, slotIndex);
						displaced.Equippable.Equip(Character);
					}
					else
					{
						container.SetItemSlot(item, inventoryIndex);
					}
				}
				return false;
			}

			if (item.IsEquippable)
			{
				item.Equippable.Equip(Character);
			}

			equipmentSnapshotDirty = true;
			if (raiseTriggers)
			{
				Character.Invoke(onEquipTriggers, new EquipItemEventData(Character, item, toSlot));
			}
			OnItemEquipped?.Invoke(item, toSlot);

			if (IsServerPeer)
			{
				OnServerEquipmentChanged?.Invoke(this, EquipmentChange.ForEquip(item, toSlot, container, containerType, inventoryIndex, displaced));
			}
			PushObservedSlot(slotIndex);
			return true;
		}

		/// <inheritdoc />
		public bool Unequip(IItemContainer container, byte slot, out List<Item> modifiedItems)
		{
			return Unequip(container, slot, ClassifyContainer(container), out modifiedItems, raiseTriggers: true);
		}

		/// <summary>
		/// Unequips an item, removing its attribute modifiers on this peer.
		/// </summary>
		private bool Unequip(IItemContainer container, byte slot, InventoryType containerType, out List<Item> modifiedItems, bool raiseTriggers)
		{
			if (!CanManipulate() ||
				!TryGetItem(slot, out Item item) ||
				container == null ||
				ReferenceEquals(container, this) ||
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
				item.Equippable.Unequip();
			}

			SetItemSlot(null, slot);

			equipmentSnapshotDirty = true;
			if (raiseTriggers)
			{
				Character.Invoke(onUnequipTriggers, new EquipItemEventData(Character, item, (ItemSlot)slot));
			}
			OnItemUnequipped?.Invoke(item, (ItemSlot)slot);

			if (IsServerPeer)
			{
				OnServerEquipmentChanged?.Invoke(this, EquipmentChange.ForUnequip(item, (ItemSlot)slot, container, containerType, item.Slot, modifiedItems));
			}
			PushObservedSlot(slot);
			return true;
		}

		/// <summary>
		/// Points an item's <see cref="ItemEquippable.Character"/> at this character without
		/// applying its generated attribute modifiers.
		/// </summary>
		/// <remarks>
		/// The observer paths must not apply modifiers — the server's authoritative total already
		/// contains them — but they must still record the owner, because
		/// <c>ItemGenerator.SetAttribute</c> reads <c>Equippable.Character</c> to decide whether a
		/// later attribute change has a character to propagate to. <see cref="ItemEquippable.Character"/>
		/// is only writable through <see cref="ItemEquippable.Equip"/>, which raises <c>OnEquip</c>;
		/// the item's own attribute handler is the only subscriber and is only attached to generated
		/// items, so it is detached around the call and restored afterwards. Not generated means no
		/// handler and nothing to detach.
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
