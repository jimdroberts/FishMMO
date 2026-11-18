using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Moves quantity between two stacks in place: a split takes part of one stack into another
	/// slot, a merge pours one stack into a matching one. Issue #198.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Both operations conserve quantity and are all-or-nothing.</b> Every refusal happens
	/// before anything is written, and the one write that can still fail afterwards — placing a
	/// freshly split instance into its slot — is undone by putting the amount back on the source.
	/// A failure therefore leaves the original stack exactly as it was; it never destroys the
	/// remainder. <c>ItemStackSplitMergeTests</c> pins that.
	/// </para>
	/// <para>
	/// <b>Split and merge are designed as a pair.</b> A split can land on an empty slot (a new
	/// instance is created) or on a matching stack with room (the amount is poured across), and a
	/// merge is what a whole stack dropped onto a matching stack does instead of swapping. Between
	/// them a player can move any quantity anywhere, and neither can strand quantity the other
	/// cannot recombine.
	/// </para>
	/// <para>
	/// <b>The boundaries are fixed here, not left to the arithmetic.</b> Splitting zero is refused.
	/// Splitting the whole stack, or more than it holds, is refused: taking everything is a move,
	/// and the swap that already exists is the operation for it. A destination that is neither
	/// empty nor a matching stack with room for the whole amount is refused rather than partly
	/// filled, so the server's answer is either "done as asked" or "nothing happened".
	/// </para>
	/// <para>
	/// Nothing here persists or broadcasts. The server handler owns writing the rows and telling
	/// the owner; the client never calls this at all — it learns the outcome from the set-slot
	/// messages the server sends, exactly as it does for a grant.
	/// </para>
	/// </remarks>
	public static class ItemStackTransfer
	{
		/// <summary>
		/// True when <paramref name="source"/> dropped onto <paramref name="destination"/> should
		/// merge rather than swap.
		/// </summary>
		/// <remarks>
		/// Agrees with <see cref="ItemStackable.AddToStack"/> on purpose, including its refusal of
		/// a FULL donor: pouring a full stack into a partial one leaves the donor holding exactly
		/// what the destination held, which is a swap by another name — so a swap is what happens.
		/// </remarks>
		/// <param name="destination">The stack being dropped onto.</param>
		/// <param name="source">The stack being dropped.</param>
		public static bool CanMergeInto(Item destination, Item source)
		{
			return destination != null &&
				   source != null &&
				   !ReferenceEquals(destination, source) &&
				   destination.IsStackable &&
				   source.IsStackable &&
				   destination.Stackable.Amount > 0 &&
				   source.Stackable.Amount > 0 &&
				   !destination.Stackable.IsStackFull &&
				   !source.Stackable.IsStackFull &&
				   destination.IsMatch(source);
		}

		/// <summary>
		/// True when <paramref name="amount"/> split off <paramref name="source"/> could land on
		/// the item in a destination slot, which must be null (empty) or a matching stack with
		/// room for all of it.
		/// </summary>
		/// <param name="occupant">Whatever the destination slot holds, or null.</param>
		/// <param name="source">The stack being split.</param>
		/// <param name="amount">How much is being taken off it.</param>
		public static bool CanSplitOnto(Item occupant, Item source, uint amount)
		{
			if (!IsValidSplitAmount(source, amount))
			{
				return false;
			}

			if (occupant == null)
			{
				return true;
			}

			return !ReferenceEquals(occupant, source) &&
				   occupant.IsStackable &&
				   occupant.Stackable.Amount > 0 &&
				   occupant.IsMatch(source) &&
				   occupant.Stackable.RemainingCapacity >= amount;
		}

		/// <summary>
		/// True when <paramref name="amount"/> is a quantity that can be split off
		/// <paramref name="source"/>: at least one, and strictly less than the stack holds.
		/// </summary>
		/// <remarks>
		/// The whole stack is not a valid split amount. Taking everything is a move, and the
		/// existing swap is the operation for that; a split that could take everything would be a
		/// second way of moving an item, with a second set of rules to keep in step.
		/// </remarks>
		public static bool IsValidSplitAmount(Item source, uint amount)
		{
			return source != null &&
				   source.IsStackable &&
				   amount >= 1 &&
				   amount < source.Stackable.Amount;
		}

		/// <summary>
		/// Pours the stack in <paramref name="fromSlot"/> into the matching stack in
		/// <paramref name="toSlot"/>, as much as fits.
		/// </summary>
		/// <remarks>
		/// The donor keeps whatever did not fit and stays in its slot; when everything fit the
		/// donor's slot is emptied and <paramref name="sourceEmptied"/> reports it, so the caller
		/// can delete the row. The donor instance is handed back either way and is not destroyed
		/// here — the caller still needs its identity and version for the delete.
		/// </remarks>
		/// <param name="from">Container holding the donor.</param>
		/// <param name="fromSlot">Slot of the donor.</param>
		/// <param name="to">Container holding the receiving stack. May be <paramref name="from"/>.</param>
		/// <param name="toSlot">Slot of the receiving stack.</param>
		/// <param name="source">The donor.</param>
		/// <param name="destination">The receiving stack.</param>
		/// <param name="sourceEmptied">True when the donor was fully absorbed and its slot cleared.</param>
		/// <returns>True when quantity moved. False leaves both containers untouched.</returns>
		public static bool TryMerge(IItemContainer from, int fromSlot, IItemContainer to, int toSlot,
			out Item source, out Item destination, out bool sourceEmptied)
		{
			source = null;
			destination = null;
			sourceEmptied = false;

			if (!SlotsAreUsable(from, fromSlot, to, toSlot) ||
				!from.TryGetItem(fromSlot, out source) ||
				!to.TryGetItem(toSlot, out destination) ||
				!CanMergeInto(destination, source))
			{
				return false;
			}

			// AddToStack is saturating and conserving: it takes min(incoming, room) and leaves the
			// rest on the donor. It has already refused every case CanMergeInto refuses.
			if (!destination.Stackable.AddToStack(source))
			{
				return false;
			}

			if (source.Stackable.Amount == 0)
			{
				sourceEmptied = true;
				source.Slot = -1;
				from.SetItemSlot(null, fromSlot);
			}
			else
			{
				// Re-stated so the slot-updated event carries the donor's new amount.
				from.SetItemSlot(source, fromSlot);
			}

			to.SetItemSlot(destination, toSlot);
			return true;
		}

		/// <summary>
		/// Takes <paramref name="amount"/> off the stack in <paramref name="fromSlot"/> and puts it
		/// in <paramref name="toSlot"/>: into a new instance when that slot is empty, onto the
		/// matching stack already there when it is not.
		/// </summary>
		/// <param name="from">Container holding the stack being split.</param>
		/// <param name="fromSlot">Slot of the stack being split.</param>
		/// <param name="to">Container receiving the split half. May be <paramref name="from"/>.</param>
		/// <param name="toSlot">Slot receiving the split half.</param>
		/// <param name="amount">How much to take. Must be at least 1 and less than the stack holds.</param>
		/// <param name="source">The stack that was split, now smaller by <paramref name="amount"/>.</param>
		/// <param name="destination">
		/// The item now in <paramref name="toSlot"/>: a new instance with <c>ID = 0</c> when
		/// <paramref name="destinationCreated"/> is true, otherwise the stack that was already there.
		/// </param>
		/// <param name="destinationCreated">True when a new instance was allocated for the split half.</param>
		/// <returns>True when quantity moved. False leaves both containers untouched.</returns>
		public static bool TrySplit(IItemContainer from, int fromSlot, IItemContainer to, int toSlot, uint amount,
			out Item source, out Item destination, out bool destinationCreated)
		{
			source = null;
			destination = null;
			destinationCreated = false;

			if (!SlotsAreUsable(from, fromSlot, to, toSlot) ||
				!from.TryGetItem(fromSlot, out source))
			{
				return false;
			}

			to.TryGetItem(toSlot, out Item occupant);
			if (!CanSplitOnto(occupant, source, amount))
			{
				return false;
			}

			if (occupant != null)
			{
				/* Pour across. Both assignments are provably safe: CanSplitOnto checked that the
				 * destination has room for the whole amount and that the source holds more than
				 * it. Destination first, so a stack of the split-off quantity exists at every
				 * point at which the source has been decremented. */
				occupant.Stackable.Amount += amount;
				source.Stackable.Amount -= amount;
				destination = occupant;
			}
			else
			{
				// A genuine split: TryUnstack allocates the new instance BEFORE decrementing the
				// source, and hands back the original only when asked for everything — which
				// IsValidSplitAmount has already ruled out.
				if (!source.Stackable.TryUnstack(amount, out Item split) ||
					split == null ||
					ReferenceEquals(split, source))
				{
					return false;
				}

				if (!to.SetItemSlot(split, toSlot))
				{
					// The slot refused after the decrement (it cannot, the lock was pre-flighted,
					// but the amount must not be lost if it ever does). Put it back.
					source.Stackable.Amount += amount;
					return false;
				}

				destination = split;
				destinationCreated = true;
			}

			// Re-stated so the slot-updated events carry the new amounts.
			from.SetItemSlot(source, fromSlot);
			if (!destinationCreated)
			{
				to.SetItemSlot(destination, toSlot);
			}
			return true;
		}

		/// <summary>
		/// The pre-flight both operations share: real containers, real slots, two different
		/// slots, neither locked.
		/// </summary>
		/// <remarks>
		/// Locks are checked here rather than trusted to <c>SetItemSlot</c>'s own refusal because
		/// both operations write two slots in sequence, and a refusal on the second would leave
		/// the first already changed.
		/// </remarks>
		private static bool SlotsAreUsable(IItemContainer from, int fromSlot, IItemContainer to, int toSlot)
		{
			return from != null &&
				   to != null &&
				   from.CanManipulate() &&
				   to.CanManipulate() &&
				   from.IsValidSlot(fromSlot) &&
				   to.IsValidSlot(toSlot) &&
				   !(ReferenceEquals(from, to) && fromSlot == toSlot) &&
				   !from.IsSlotLocked(fromSlot) &&
				   !to.IsSlotLocked(toSlot);
		}
	}
}
