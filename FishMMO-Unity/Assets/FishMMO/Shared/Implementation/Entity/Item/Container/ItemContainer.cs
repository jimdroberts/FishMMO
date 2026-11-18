using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for item containers, providing slot and item management for inventories, equipment, banks, etc.
	/// Implements IItemContainer and extends CharacterBehaviour for character association.
	/// </summary>
	public abstract class ItemContainer : CharacterBehaviour, IItemContainer
	{
		/// <summary>
		/// Internal list of items stored in this container.
		/// </summary>
		private readonly List<Item> items = new List<Item>();

		/// <summary>
		/// Set of slot indices currently locked by active operations (e.g., consumable activation).
		/// Locked slots cannot be swapped, removed, or transferred.
		/// </summary>
		private HashSet<int> lockedSlots;

		/// <summary>
		/// Event triggered when an item slot is updated (item added, removed, or changed).
		/// </summary>
		public event Action<IItemContainer, Item, int> OnSlotUpdated;

		/// <summary>
		/// Event triggered when a slot's lock state changes.
		/// Parameters: container, slot index, isLocked.
		/// </summary>
		public event Action<IItemContainer, int, bool> OnSlotLockChanged;

		/// <summary>
		/// Gets the list of items contained in this container.
		/// </summary>
		public List<Item> Items { get { return items; } } // Note: returns mutable list reference. Only server broadcast handlers should modify.

		/// <summary>
		/// Called when the container is being destroyed. Clears event handlers and locks.
		/// </summary>
		public override void OnDestroying()
		{
			DropSubscribersAndLocks();
		}

		/// <summary>
		/// Drops the per-spawn container state before the object returns to the pool.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The locks are the reason this exists.</b> A slot is locked for the duration of an
		/// operation — a consumable activation, an equip round trip — and unlocked when that
		/// operation resolves. A despawn mid-flight resolves nothing, so the lock was still set
		/// when the object went into the pool, and <c>OnDestroying</c> was the only thing that
		/// cleared it: a path a pooled object never takes. The next character to occupy the slot
		/// inherited a permanently locked inventory slot, which
		/// <see cref="IsSlotLocked(int)"/> makes unswappable, unremovable and untransferable for
		/// the rest of that character's session, with nothing in the UI to explain it.
		/// </para>
		/// <para>
		/// The subscriber lists go with them. Both events are held by client panels bound to the
		/// character that is being torn down; leaving them attached to a recycled container would
		/// drive the previous occupant's UI from the next occupant's slot writes.
		/// </para>
		/// <para>
		/// Items are deliberately NOT cleared here. Each concrete container clears its own in its
		/// <c>ResetState</c>, and <c>EquipmentController</c> has to run work either side of that
		/// call — see its override for why the order there cannot be inverted.
		/// </para>
		/// </remarks>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			DropSubscribersAndLocks();
		}

		/// <summary>
		/// Shared teardown for <see cref="ResetState(bool)"/> (pool) and
		/// <see cref="OnDestroying"/> (destroy).
		/// </summary>
		private void DropSubscribersAndLocks()
		{
			OnSlotUpdated = null;
			OnSlotLockChanged = null;
			lockedSlots?.Clear();
		}

		/// <summary>
		/// Determines if the container can be manipulated (e.g., items moved or swapped).
		/// </summary>
		/// <remarks>
		/// Answers only whether there is anything here to manipulate. It used to refuse while the
		/// character was dead as well, and because every mutation consulted it — including the ones
		/// a client applies on the server's authority — a dead character's containers refused the
		/// server's own updates and drifted from it. Whether the character may ORIGINATE a request
		/// is the requester's question, and <see cref="CharacterStateValidation.CanAct(ICharacter)"/>
		/// is the one rule for it: the server's handlers ask it before touching a container and the
		/// client asks it before queueing a request.
		/// </remarks>
		/// <returns>True if manipulation is allowed, false otherwise.</returns>
		public virtual bool CanManipulate()
		{
			return Items.Count > 0;
		}

		/// <summary>
		/// Checks if the item slot exists (is within valid range).
		/// </summary>
		/// <param name="slot">The slot index to check.</param>
		/// <returns>True if the slot is valid, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsValidSlot(int slot)
		{
			return slot > -1 &&
				  slot < Items.Count;
		}

		/// <summary>
		/// Checks if the specified slot is empty (contains no item).
		/// </summary>
		/// <param name="slot">The slot index to check.</param>
		/// <returns>True if the slot is empty, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsSlotEmpty(int slot)
		{
			return IsValidSlot(slot) &&
				   Items[slot] == null;
		}

		/// <summary>
		/// Attempts to get the item in the specified slot. Returns false if the item doesn't exist.
		/// </summary>
		/// <param name="slot">The slot index to retrieve.</param>
		/// <param name="item">The item found in the slot, or null if not found.</param>
		/// <returns>True if an item was found, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetItem(int slot, out Item item)
		{
			if (IsValidSlot(slot))
			{
				item = Items[slot];
				return item != null;
			}
			item = null;
			return false;
		}

		/// <summary>
		/// Checks if the container contains an item with the specified template.
		/// </summary>
		/// <param name="itemTemplate">The item template to search for.</param>
		/// <returns>True if the item is found, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool ContainsItem(BaseItemTemplate itemTemplate)
		{
			for (int i = 0; i < Items.Count; ++i)
			{
				Item item = Items[i];
				// Empty slots are stored as nulls, so this has to be guarded — the sibling
				// GetItemCount below always did, and the Interactable Container's copy does too.
				if (item != null && item.Template.ID == itemTemplate.ID)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Gets the count of items matching the specified template, including stack sizes.
		/// </summary>
		/// <param name="itemTemplate">The item template to count.</param>
		/// <returns>The number of items matching the template.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int GetItemCount(BaseItemTemplate itemTemplate)
		{
			int count = 0;
			for (int i = 0; i < Items.Count; ++i)
			{
				Item item = Items[i];
				if (item != null && item.Template.ID == itemTemplate.ID)
				{
					if (item.IsStackable)
					{
						count += (int)item.Stackable.Amount;
					}
					else
					{
						count += 1;
					}
				}
			}
			return count;
		}

		/// <summary>
		/// Adds slots to the container, optionally initializing with a list of items.
		/// </summary>
		/// <param name="items">The initial items to add (can be null).</param>
		/// <param name="amount">The number of slots to add.</param>
		public void AddSlots(List<Item> items, int amount)
		{
			if (items != null)
			{
				for (int i = 0; i < items.Count; ++i)
				{
					this.Items.Add(items[i]);
				}
				return;
			}
			for (int i = 0; i < amount; ++i)
			{
				this.Items.Add(null);
			}
		}

		/// <summary>
		/// Clears all items from the container, destroying each item and setting slots to null.
		/// </summary>
		public void Clear()
		{
			for (int i = 0; i < items.Count; ++i)
			{
				Item item = items[i];
				if (item == null)
				{
					continue;
				}
				item.Destroy();
				items[i] = null;
			}
		}

		/// <summary>
		/// Checks if the container has at least one free slot.
		/// </summary>
		/// <returns>True if a free slot exists, false otherwise.</returns>
		public bool HasFreeSlot()
		{
			for (int i = 0; i < Items.Count; ++i)
			{
				if (IsSlotEmpty(i))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Gets the number of free slots in the container.
		/// </summary>
		/// <returns>The number of free slots.</returns>
		public int FreeSlots()
		{
			int count = 0;
			for (int i = 0; i < Items.Count; ++i)
			{
				if (IsSlotEmpty(i))
				{
					++count;
				}
			}
			return count;
		}

		/// <summary>
		/// Gets the number of filled slots in the container.
		/// </summary>
		/// <returns>The number of filled slots.</returns>
		public int FilledSlots()
		{
			int count = 0;
			for (int i = 0; i < Items.Count; ++i)
			{
				if (!IsSlotEmpty(i))
				{
					++count;
				}
			}
			return count;
		}

		/// <summary>
		/// Determines if the specified item can be added to the container, considering stack sizes and slot availability.
		/// </summary>
		/// <param name="item">The item to check.</param>
		/// <returns>True if the item can be added, false otherwise.</returns>
		public bool CanAddItem(Item item)
		{
			if (!CanManipulate())
			{
				return false;
			}

			// Cannot add an item with a stack size of 0; a 0 stack size means the item doesn't exist.
			if (item == null) return false;

			uint amountRemaining = item.IsStackable ? item.Stackable.Amount : 1;
			for (int i = 0; i < Items.Count; ++i)
			{
				// A locked slot is mid-operation (a consumable is being activated out of it) and
				// must not be counted as available capacity — otherwise CanAddItem promises room
				// that TryAddItem then refuses to use, and the caller sees a silent failure.
				if (IsSlotLocked(i))
				{
					continue;
				}

				// If we find an empty slot, we return instantly.
				if (IsSlotEmpty(i))
				{
					return true;
				}

				// If we find another item of the same type and its stack is not full.
				if (Items[i].IsStackable &&
					!Items[i].Stackable.IsStackFull &&
					Items[i].IsMatch(item))
				{
					// Saturating: consume as much of the outstanding amount as this stack can take.
					// RemainingCapacity clamps at zero, so a stack already sitting above MaxStackSize
					// (a template whose cap was lowered, or an older overflowed row) contributes
					// nothing instead of underflowing into ~4 billion free space.
					amountRemaining -= Math.Min(amountRemaining, Items[i].Stackable.RemainingCapacity);
				}

				if (amountRemaining < 1) return true;
			}
			return false;
		}

		/// <summary>
		/// Attempts to add an item to the container. Returns true if the entire stack size of the item has been successfully added.
		/// All modified items are returned.
		/// Handles stacking logic and slot assignment.
		/// </summary>
		/// <param name="item">The item to add.</param>
		/// <param name="modifiedItems">The list of items modified during the operation.</param>
		/// <returns>True if the item was successfully added, false otherwise.</returns>
		public bool TryAddItem(Item item, out List<Item> modifiedItems)
		{
			modifiedItems = new List<Item>();

			// Ensure we can add the entire item to the container.
			if (!CanAddItem(item))
			{
				return false;
			}

			if (item.IsStackable)
			{
				uint amount = item.Stackable.Amount;
				for (int i = 0; i < Items.Count; ++i)
				{
					// Search for items of the same type so we can stack it.
					// Never merge into a locked slot: the item in it is being consumed.
					if (Items[i] != null &&
						!IsSlotLocked(i) &&
						Items[i].IsStackable &&
						Items[i].Stackable.AddToStack(item))
					{
						// Set the remaining amount to the item's stack size.
						amount = item.Stackable.Amount;

						// The slot that actually changed is the one holding the merged stack.
						modifiedItems.Add(Items[i]);

						/* Report the item that is now IN the slot, not the donor. Subscribers repaint
						 * slot i from this argument, so passing the donor painted the leftover — or,
						 * once the donor is empty, nothing at all. */
						OnSlotUpdated?.Invoke(this, Items[i], i);
					}

					// We added the item to the container.
					if (amount < 1) return true;
				}
			}

			/* The donor joins the modified list ONLY once it has a slot of its own, below. Every
			 * caller turns this list straight into persistence rows and set-slot broadcasts, and
			 * the donor's Slot is -1 until it is placed — so adding it after a partial merge, as
			 * this used to, wrote a (slot = -1, amount = whatever was left) row whenever the
			 * leftover then merged into a SECOND stack, and wrote the donor twice whenever the
			 * leftover went into an empty slot instead. The stacks it merged into are already
			 * listed; a donor that never gets a slot has no row to write. */
			for (int i = 0; i < Items.Count; ++i)
			{
				// Find the first slot to put the remaining item in.
				if (IsSlotEmpty(i))
				{
					// SetItemSlot can now refuse (locked slot), so the result is checked rather
					// than presumed: keep looking instead of reporting a placement that did not
					// happen and dropping the item on the floor.
					if (!SetItemSlot(item, i))
					{
						continue;
					}

					// Add the modified item to the list.
					modifiedItems.Add(item);

					// Successfully added the entire item.
					return true;
				}
			}
			// We should never reach this...
			// Should probably throw an exception instead of just returning false.
			// If we get here then we have a race condition for some reason.
			return false;
		}

		/// <summary>
		/// Sets the item in the specified slot. Previous item will be lost if not referenced elsewhere.
		/// </summary>
		/// <remarks>
		/// Refuses a locked slot. This is the write primitive every other mutation funnels through,
		/// and it was the one that did not check the lock — so a cross-container move could overwrite
		/// a slot whose item was mid-consumption, and the activation would then complete against an
		/// item that had already gone somewhere else. RemoveItem and SwapItemSlots check the lock
		/// themselves before calling in here, so this guard is redundant for them by design.
		/// <para>
		/// Callers MUST check the return value. It is a genuine refusal, not a formality.
		/// </para>
		/// </remarks>
		/// <param name="item">The item to set.</param>
		/// <param name="slot">The slot index to set the item in.</param>
		/// <returns>True if the item was successfully set, false otherwise.</returns>
		public bool SetItemSlot(Item item, int slot)
		{
			if (!IsValidSlot(slot) ||
				IsSlotLocked(slot))
			{
				// Setting the slot failed.
				return false;
			}

			Items[slot] = item;
			if (item != null)
			{
				item.Slot = slot;
			}
			OnSlotUpdated?.Invoke(this, item, slot);
			return true;
		}

		/// <summary>
		/// Swaps items between two slots.
		/// </summary>
		/// <param name="from">The source slot index.</param>
		/// <param name="to">The destination slot index.</param>
		/// <returns>True if the swap was successful, false otherwise.</returns>
		public bool SwapItemSlots(int from, int to)
		{
			return SwapItemSlots(from, to, out Item fromItem, out Item toItem);
		}

		/// <summary>
		/// Swaps items between two slots and returns the items that were swapped.
		/// Fails if either slot is locked.
		/// </summary>
		/// <param name="from">The source slot index.</param>
		/// <param name="to">The destination slot index.</param>
		/// <param name="fromItem">The item originally in the source slot.</param>
		/// <param name="toItem">The item originally in the destination slot.</param>
		/// <returns>True if the swap was successful, false otherwise.</returns>
		public bool SwapItemSlots(int from, int to, out Item fromItem, out Item toItem)
		{
			if (!CanManipulate() ||
				from < 0 ||
				to < 0 ||
				from >= Items.Count ||
				to >= Items.Count ||
				IsSlotLocked(from) ||
				IsSlotLocked(to))
			{
				fromItem = null;
				toItem = null;

				// Swapping the items failed.
				return false;
			}

			fromItem = Items[from];
			toItem = Items[to];

			Items[from] = toItem;
			if (toItem != null)
			{
				toItem.Slot = from;
			}

			Items[to] = fromItem;
			if (fromItem != null)
			{
				fromItem.Slot = to;
			}

			OnSlotUpdated?.Invoke(this, toItem, from);
			OnSlotUpdated?.Invoke(this, fromItem, to);
			return true;
		}

		/// <summary>
		/// Removes an item from the specified slot and returns it. Returns null if the slot was empty.
		/// Fails if the slot is locked.
		/// </summary>
		/// <param name="slot">The slot index to remove the item from.</param>
		/// <returns>The item that was removed, or null if no item was present.</returns>
		public Item RemoveItem(int slot)
		{
			if (!CanManipulate() ||
				!IsValidSlot(slot) ||
				IsSlotLocked(slot))
			{
				return null;
			}

			Item item = Items[slot];
			if (item == null)
			{
				return null;
			}
			item.Slot = -1;
			SetItemSlot(null, slot);
			return item;
		}

		/// <summary>
		/// Returns true if the specified slot is currently locked.
		/// Locked slots cannot be swapped, removed, or transferred until unlocked.
		/// </summary>
		/// <param name="slot">The slot index to check.</param>
		/// <returns>True if the slot is locked, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsSlotLocked(int slot)
		{
			return lockedSlots != null && lockedSlots.Contains(slot);
		}

		/// <summary>
		/// Locks the specified slot, preventing it from being swapped, removed, or transferred.
		/// </summary>
		/// <param name="slot">The slot index to lock.</param>
		public void LockSlot(int slot)
		{
			if (!IsValidSlot(slot))
			{
				return;
			}
			if (lockedSlots == null)
			{
				lockedSlots = new HashSet<int>();
			}
			if (lockedSlots.Add(slot))
			{
				OnSlotLockChanged?.Invoke(this, slot, true);
			}
		}

		/// <summary>
		/// Unlocks the specified slot, allowing normal manipulation again.
		/// </summary>
		/// <param name="slot">The slot index to unlock.</param>
		public void UnlockSlot(int slot)
		{
			if (lockedSlots != null && lockedSlots.Remove(slot))
			{
				OnSlotLockChanged?.Invoke(this, slot, false);
			}
		}
	}
}