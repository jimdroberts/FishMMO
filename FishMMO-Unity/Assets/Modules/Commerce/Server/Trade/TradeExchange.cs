using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Applies an agreed exchange of items to two in-memory inventories, all or nothing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Pure container arithmetic: no network, no persistence, no character lookups. The
	/// trade system resolves the two inventories and re-validates the offers, then hands
	/// them here; what comes back is the set of rows each character's write must carry.
	/// That split is what lets the conservation rules — nothing duplicated, nothing lost, on
	/// success or on refusal — be pinned by tests against real <c>InventoryController</c>s.
	/// </para>
	/// <para>
	/// <b>Why this is not "remove from A, then grant to B".</b> The grant path is all-or-nothing
	/// per item, but a trade is several items in both directions, and the receiver's room
	/// depends on which of its own items are leaving. So the exchange runs in two phases on the
	/// main thread with nothing between them: TAKE every offered item out of both bags, then
	/// GIVE each to the other bag. If any give refuses, every give already made is undone and
	/// every take is put back, and both bags are exactly as they were. Nothing else runs
	/// between the phases, so no third party can see or touch the intermediate state.
	/// </para>
	/// <para>
	/// <b>Identity survives the move.</b> An item that crosses whole keeps its <see cref="Item.ID"/>
	/// and is listed as CHANGED by the receiver: the row moves by being re-owned. Only an item
	/// that merged entirely into a stack the receiver already held is listed as REMOVED by the
	/// giver, because its row genuinely ceased to exist. A split-off quantity is a new
	/// <see cref="Item"/> with no identity; it is listed as changed by the receiver and as
	/// unidentified, so the write can lock its slot until the database names it.
	/// </para>
	/// </remarks>
	public static class TradeExchange
	{
		/// <summary>Why the exchange did not apply.</summary>
		public enum Failure : byte
		{
			None = 0,

			/// <summary>An offered slot no longer holds the item that was offered.</summary>
			OfferInvalid,

			/// <summary>A container refused to release an offered item (locked, or not manipulable).</summary>
			TakeRefused,

			/// <summary>A receiver had no room for what it was given.</summary>
			NoRoom,
		}

		/// <summary>
		/// One character's inventory and offers going in; the rows its write must carry coming out.
		/// </summary>
		public sealed class Side
		{
			/// <summary>The character's inventory container.</summary>
			public IItemContainer Inventory;

			/// <summary>What this side is giving away.</summary>
			public IReadOnlyList<TradeOffer> Offers;

			/// <summary>Items whose rows this character's write must upsert — see the class remarks.</summary>
			public readonly List<Item> Changed = new List<Item>();

			/// <summary>Rows this character's write must delete.</summary>
			public readonly List<RemovedItemRecord> Removed = new List<RemovedItemRecord>();

			/// <summary>Inventory slots that are now empty, so the owning client can be told.</summary>
			public readonly List<int> EmptiedSlots = new List<int>();

			/// <summary>Items placed in this inventory that have no database identity yet.</summary>
			public readonly List<Item> UnidentifiedPlaced = new List<Item>();

			/// <summary>
			/// Every slot of this inventory the exchange read or wrote: the offered slots, the
			/// slots incoming items landed in or merged into, and the slots that emptied. The
			/// caller locks these for as long as the exchange can still be undone, so that
			/// <see cref="Applied.Undo"/> restores exactly what it took and nothing has moved
			/// into a slot it needs to put something back into.
			/// </summary>
			public readonly List<int> TouchedSlots = new List<int>();

			/// <summary>Clears every output list, for a retry after a refusal.</summary>
			public void ClearOutputs()
			{
				Changed.Clear();
				Removed.Clear();
				EmptiedSlots.Clear();
				UnidentifiedPlaced.Clear();
				TouchedSlots.Clear();
			}
		}

		/// <summary>One item taken out of a giver during the TAKE phase.</summary>
		internal struct Taken
		{
			/// <summary>Which side it came from.</summary>
			public Side Giver;

			/// <summary>The giver's slot it came from.</summary>
			public int Slot;

			/// <summary>The instance being handed over: the original for a whole move, the split half otherwise.</summary>
			public Item Handed;

			/// <summary>For a partial offer, the stack that stays behind. Null for a whole move.</summary>
			public Item Source;

			/// <summary>Quantity handed over, so a rollback can restore it exactly.</summary>
			public uint Amount;

			public bool IsPartial => Source != null;
		}

		/// <summary>One stack whose amount a GIVE may have changed, remembered for rollback.</summary>
		internal struct StackSnapshot
		{
			public Item Stack;
			public uint Amount;
		}

		/// <summary>
		/// An exchange that has been applied to memory and can still be taken back.
		/// </summary>
		/// <remarks>
		/// Exists because the trade commits to the database BEFORE it is final: memory is
		/// mutated while both characters' row locks are held, the rows are written, and only
		/// then is the transaction committed. If the write is refused the memory mutation has to
		/// be undone exactly, and this holds what that takes. It is only exact while every slot
		/// in <see cref="Side.TouchedSlots"/> stays locked, which the caller guarantees.
		/// </remarks>
		public sealed class Applied
		{
			private readonly Side first;
			private readonly Side second;
			private readonly List<Taken> taken;
			private readonly List<(Taken taken, Side receiver, List<Item> modified, int snapshotStart)> gives;
			private readonly List<StackSnapshot> snapshots;
			private bool undone;

			internal Applied(Side first, Side second, List<Taken> taken,
				List<(Taken, Side, List<Item>, int)> gives, List<StackSnapshot> snapshots)
			{
				this.first = first;
				this.second = second;
				this.taken = taken;
				this.gives = gives;
				this.snapshots = snapshots;
			}

			/// <summary>True once <see cref="Undo"/> has run.</summary>
			public bool IsUndone => undone;

			/// <summary>
			/// Puts both inventories back exactly as they were before the exchange. Idempotent.
			/// </summary>
			public void Undo()
			{
				if (undone)
				{
					return;
				}
				undone = true;

				for (int g = gives.Count - 1; g >= 0; --g)
				{
					UndoGive(gives[g].taken, gives[g].receiver, gives[g].modified, snapshots, gives[g].snapshotStart);
				}
				RestoreTaken(taken);

				first.ClearOutputs();
				second.ClearOutputs();
			}
		}

		/// <summary>
		/// Moves every offer on <paramref name="first"/> into <paramref name="second"/>'s inventory
		/// and vice versa, or changes nothing.
		/// </summary>
		/// <returns>True when the exchange applied. False leaves both inventories untouched.</returns>
		public static bool TryApply(Side first, Side second, out Failure failure)
		{
			return TryApply(first, second, out failure, out _);
		}

		/// <summary>
		/// As <see cref="TryApply(Side, Side, out Failure)"/>, and hands back the handle that
		/// can take the applied exchange back.
		/// </summary>
		public static bool TryApply(Side first, Side second, out Failure failure, out Applied applied)
		{
			failure = Failure.None;
			applied = null;

			if (first?.Inventory == null || second?.Inventory == null ||
				first.Offers == null || second.Offers == null)
			{
				failure = Failure.OfferInvalid;
				return false;
			}

			first.ClearOutputs();
			second.ClearOutputs();

			// Nothing may be mutated before every offer has been checked against the live bag.
			if (!OffersStillHold(first) || !OffersStillHold(second))
			{
				failure = Failure.OfferInvalid;
				return false;
			}

			var taken = new List<Taken>(first.Offers.Count + second.Offers.Count);

			if (!TakeAll(first, taken) || !TakeAll(second, taken))
			{
				RestoreTaken(taken);
				failure = Failure.TakeRefused;
				return false;
			}

			var snapshots = new List<StackSnapshot>();
			var gives = new List<(Taken taken, Side receiver, List<Item> modified, int snapshotStart)>(taken.Count);

			for (int i = 0; i < taken.Count; ++i)
			{
				Taken t = taken[i];
				Side receiver = ReferenceEquals(t.Giver, first) ? second : first;

				int snapshotStart = snapshots.Count;
				SnapshotMatchingStacks(receiver.Inventory, t.Handed, snapshots);

				if (!receiver.Inventory.TryAddItem(t.Handed, out List<Item> modified))
				{
					/* TryAddItem may have merged part of the item into existing stacks before it
					 * found no slot for the rest. The snapshot covers those stacks; the handed
					 * item's own amount is restored from the take record below. */
					RestoreStacks(snapshots, snapshotStart);
					for (int g = gives.Count - 1; g >= 0; --g)
					{
						UndoGive(gives[g].taken, gives[g].receiver, gives[g].modified, snapshots, gives[g].snapshotStart);
					}
					RestoreTaken(taken);
					first.ClearOutputs();
					second.ClearOutputs();
					failure = Failure.NoRoom;
					return false;
				}

				gives.Add((t, receiver, modified, snapshotStart));
			}

			// Everything is placed. Classify what each write must carry.
			for (int i = 0; i < gives.Count; ++i)
			{
				(Taken t, Side receiver, List<Item> modified, _) = gives[i];

				AddUniqueSlot(t.Giver.TouchedSlots, t.Slot);

				if (t.IsPartial)
				{
					// The stack that stayed behind shrank.
					AddUnique(t.Giver.Changed, t.Source);
				}
				else
				{
					t.Giver.EmptiedSlots.Add(t.Slot);
				}

				bool handedHasSlot = t.Handed.Slot >= 0 &&
					receiver.Inventory.TryGetItem(t.Handed.Slot, out Item atSlot) &&
					ReferenceEquals(atSlot, t.Handed);

				if (handedHasSlot)
				{
					// The instance lives on in the receiver's bag: its row is re-owned, not deleted.
					AddUnique(receiver.Changed, t.Handed);
					if (t.Handed.ID <= 0)
					{
						receiver.UnidentifiedPlaced.Add(t.Handed);
					}
				}
				else if (!t.IsPartial)
				{
					/* Merged entirely into stacks the receiver already held. The instance has no
					 * slot and no future; its row must go, addressed by the item. A partial
					 * split that merged entirely never had a row, so there is nothing to delete. */
					t.Handed.Version++;
					t.Giver.Removed.Add(new RemovedItemRecord(t.Handed.ID, t.Handed.Version, t.Slot));
				}

				for (int m = 0; m < modified.Count; ++m)
				{
					Item stack = modified[m];
					if (stack != null && !ReferenceEquals(stack, t.Handed))
					{
						AddUnique(receiver.Changed, stack);
					}
					if (stack != null && stack.Slot >= 0)
					{
						AddUniqueSlot(receiver.TouchedSlots, stack.Slot);
					}
				}
				if (handedHasSlot)
				{
					AddUniqueSlot(receiver.TouchedSlots, t.Handed.Slot);
				}
			}

			applied = new Applied(first, second, taken, gives, snapshots);
			return true;
		}

		private static void AddUniqueSlot(List<int> list, int slot)
		{
			if (slot < 0 || list.Contains(slot))
			{
				return;
			}
			list.Add(slot);
		}

		/// <summary>True when every offer on a side still names the item and quantity it was made for.</summary>
		private static bool OffersStillHold(Side side)
		{
			IItemContainer inventory = side.Inventory;
			if (!inventory.CanManipulate())
			{
				return false;
			}

			for (int i = 0; i < side.Offers.Count; ++i)
			{
				TradeOffer offer = side.Offers[i];
				if (!inventory.IsValidSlot(offer.Slot) ||
					inventory.IsSlotLocked(offer.Slot) ||
					!inventory.TryGetItem(offer.Slot, out Item item) ||
					!TradeRules.OfferStillHolds(item, offer.ItemID, offer.TemplateID, offer.Amount))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>TAKE phase for one side. Appends to <paramref name="taken"/>; false leaves the caller to restore.</summary>
		private static bool TakeAll(Side side, List<Taken> taken)
		{
			for (int i = 0; i < side.Offers.Count; ++i)
			{
				TradeOffer offer = side.Offers[i];
				if (!side.Inventory.TryGetItem(offer.Slot, out Item item) || item == null)
				{
					return false;
				}

				uint held = item.IsStackable ? item.Stackable.Amount : 1u;
				if (!item.IsStackable || offer.Amount >= held)
				{
					Item removed = side.Inventory.RemoveItem(offer.Slot);
					if (!ReferenceEquals(removed, item))
					{
						// A refusal returns null and leaves the slot as it was; anything else
						// would be a container handing back a different item, which cannot be
						// safely put back either.
						return false;
					}
					taken.Add(new Taken { Giver = side, Slot = offer.Slot, Handed = removed, Source = null, Amount = held });
				}
				else
				{
					// TryUnstack allocates the split BEFORE decrementing, and hands back the
					// original only when asked for everything — ruled out by the branch above.
					if (!item.Stackable.TryUnstack(offer.Amount, out Item split) ||
						split == null ||
						ReferenceEquals(split, item))
					{
						return false;
					}
					taken.Add(new Taken { Giver = side, Slot = offer.Slot, Handed = split, Source = item, Amount = offer.Amount });
				}
			}
			return true;
		}

		/// <summary>Puts every taken item back where it came from, newest first.</summary>
		private static void RestoreTaken(List<Taken> taken)
		{
			for (int i = taken.Count - 1; i >= 0; --i)
			{
				Taken t = taken[i];
				if (t.IsPartial)
				{
					// The split half is discarded; its quantity goes back on the source.
					t.Source.Stackable.Amount += t.Amount;
					t.Giver.Inventory.SetItemSlot(t.Source, t.Slot);
				}
				else
				{
					if (t.Handed.IsStackable)
					{
						t.Handed.Stackable.Amount = t.Amount;
					}
					t.Giver.Inventory.SetItemSlot(t.Handed, t.Slot);
				}
			}
		}

		/// <summary>Remembers the amount of every receiver stack the handed item could merge into.</summary>
		private static void SnapshotMatchingStacks(IItemContainer receiver, Item handed, List<StackSnapshot> snapshots)
		{
			if (handed == null || !handed.IsStackable)
			{
				return;
			}
			List<Item> items = receiver.Items;
			for (int i = 0; i < items.Count; ++i)
			{
				Item candidate = items[i];
				if (candidate != null && candidate.IsStackable && !ReferenceEquals(candidate, handed) && candidate.IsMatch(handed))
				{
					snapshots.Add(new StackSnapshot { Stack = candidate, Amount = candidate.Stackable.Amount });
				}
			}
		}

		/// <summary>Restores the amounts recorded from <paramref name="start"/> onward and forgets them.</summary>
		private static void RestoreStacks(List<StackSnapshot> snapshots, int start)
		{
			for (int i = snapshots.Count - 1; i >= start; --i)
			{
				snapshots[i].Stack.Stackable.Amount = snapshots[i].Amount;
			}
			snapshots.RemoveRange(start, snapshots.Count - start);
		}

		/// <summary>Undoes one completed give: takes the handed item back out and restores merged stacks.</summary>
		private static void UndoGive(Taken t, Side receiver, List<Item> modified, List<StackSnapshot> snapshots, int snapshotStart)
		{
			if (t.Handed.Slot >= 0 &&
				receiver.Inventory.TryGetItem(t.Handed.Slot, out Item atSlot) &&
				ReferenceEquals(atSlot, t.Handed))
			{
				receiver.Inventory.RemoveItem(t.Handed.Slot);
			}
			RestoreStacks(snapshots, snapshotStart);
		}

		private static void AddUnique(List<Item> list, Item item)
		{
			if (item == null)
			{
				return;
			}
			for (int i = 0; i < list.Count; ++i)
			{
				if (ReferenceEquals(list[i], item))
				{
					return;
				}
			}
			list.Add(item);
		}
	}
}
