using System;
using System.Runtime.CompilerServices;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the stackable component of an item, managing stack size, addition, removal, and unstacking logic.
	/// </summary>
	/// <remarks>
	/// STACK MATHS CONTRACT — read before editing.
	/// <para>
	/// Every quantity here is a <see cref="uint"/>. There is no such thing as a negative stack, so every
	/// subtraction must be provably non-negative before it runs; an underflow does not produce a small
	/// number, it produces ~4.29 billion items and mints currency out of nothing.
	/// </para>
	/// <para>
	/// This class previously used <c>UIntExtensions.AbsoluteSubtract</c> to compute "how much is left over
	/// after the merge". That helper returns |a - b|, NOT a saturating subtract, so it is only correct in
	/// the overflow branch (incoming &gt; capacity). In the ordinary fits-entirely branch it returned the
	/// unused capacity instead of zero, which inflated the donor stack and underflowed the destination:
	/// MaxStackSize 10, destination 1, source 2 produced a destination of 4,294,967,292. The call sites are
	/// fixed here rather than changing <c>AbsoluteSubtract</c> — the helper lives in the shared utility
	/// library, its name promises absolute difference, and other callers rely on that meaning.
	/// </para>
	/// </remarks>
	public class ItemStackable : IStackable<Item>
	{
		/// <summary>
		/// The item instance this stackable component belongs to.
		/// </summary>
		private Item item;

		/// <summary>
		/// The current amount in the stack.
		/// </summary>
		public uint Amount;

		/// <summary>
		/// Returns true if the stack is full (reached or exceeded max stack size).
		/// </summary>
		/// <remarks>
		/// Compares with <c>&gt;=</c> rather than <c>==</c> on purpose. A stack that was corrupted by an
		/// earlier overflow (or by a template whose MaxStackSize was lowered after the item was saved)
		/// sits above the cap; with an equality test such a stack reported "not full" forever and kept
		/// absorbing more items. The inequality lets an already-corrupted stack re-cap instead of growing.
		/// </remarks>
		public bool IsStackFull { get { return Amount >= item.Template.MaxStackSize; } }

		/// <summary>
		/// Returns the free room left in this stack, saturating at zero.
		/// A stack sitting at or above <c>MaxStackSize</c> reports no remaining capacity rather than
		/// underflowing into a ~4 billion result.
		/// </summary>
		public uint RemainingCapacity
		{
			get
			{
				uint max = item.Template.MaxStackSize;
				return Amount >= max ? 0u : max - Amount;
			}
		}

		/// <summary>
		/// Constructs a stackable component for an item with the given amount.
		/// </summary>
		/// <param name="item">The item instance.</param>
		/// <param name="amount">The initial stack amount.</param>
		public ItemStackable(Item item, uint amount)
		{
			this.item = item;
			Amount = amount;
		}

		/// <summary>
		/// Removes the specified amount from the stack. Destroys the item if the stack reaches zero.
		/// </summary>
		/// <remarks>
		/// Saturating. Removing more than the stack holds empties the stack and logs, rather than
		/// wrapping the <see cref="uint"/> around into a ~4 billion stack that also sails past the
		/// <c>Amount == 0</c> destroy check.
		/// </remarks>
		/// <param name="amount">The amount to remove.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Remove(uint amount)
		{
			if (amount > Amount)
			{
				Log.Error("ItemStackable", $"Attempted to remove {amount} from a stack of {Amount} (Template={item?.Template?.Name}). Clamping to empty.");
				Amount = 0;
			}
			else
			{
				Amount -= amount;
			}

			if (Amount == 0)
			{
				item.Destroy();
			}
		}

		/// <summary>
		/// Returns true only if the entire other item can be added to this stack.
		/// Checks for matching template, seed, and stack capacity.
		/// </summary>
		/// <param name="other">The item to add to the stack.</param>
		/// <returns>True if the item can be fully added, false otherwise.</returns>
		public bool CanAddToStack(Item other)
		{
			if (other == null) return false;

			// Merging a stack into itself would double it. Nothing legitimately does this, but
			// TryAddItem walks every slot and would happily hand us the item already sitting in one.
			if (ReferenceEquals(other, item)) return false;

			if (Amount < 1) return false; // item no longer exists?

			if (item.Template.ID != other.Template.ID) return false;

			// The item seeds must match for stacking.
			if (!item.IsMatch(other))
			{
				return false;
			}

			// If either stack is full, we can't add any more.
			if (IsStackFull || other.Stackable == null || other.Stackable.IsStackFull) return false;

			uint incomingAmount = other.Stackable.Amount;
			if (incomingAmount < 1) return false;

			// The whole donor stack has to fit; a partial merge is AddToStack's job, not ours.
			return incomingAmount <= RemainingCapacity;
		}

		/// <summary>
		/// Adds the other item to this stack and sets the other stack's size to the remainder, if any.
		/// Returns false on failure.
		/// </summary>
		/// <remarks>
		/// Conserves quantity exactly: <c>transferred</c> is clamped to whichever of the incoming amount
		/// and the remaining capacity is smaller, so <c>this.Amount + other.Amount</c> is identical before
		/// and after the call. Neither side can underflow.
		/// </remarks>
		/// <param name="other">The item to add to the stack.</param>
		/// <returns>True if the item was added, false otherwise.</returns>
		public bool AddToStack(Item other)
		{
			if (other == null) return false;

			// See CanAddToStack: self-merge would double the stack.
			if (ReferenceEquals(other, item)) return false;

			if (Amount < 1) return false; // this should have been an empty slot!

			if (item.Template.ID != other.Template.ID) return false;

			// The item seeds must match for stacking.
			if (!item.IsMatch(other))
			{
				return false;
			}

			if (IsStackFull || other.Stackable == null || other.Stackable.IsStackFull) return false;

			uint remainingCapacity = RemainingCapacity;
			uint incomingAmount = other.Stackable.Amount;
			if (remainingCapacity < 1 || incomingAmount < 1) return false;

			// Saturating transfer. Math.Min is the whole fix: take as much as fits, leave the rest
			// on the donor. Both assignments below are provably non-negative.
			uint transferred = Math.Min(incomingAmount, remainingCapacity);

			Amount += transferred;
			other.Stackable.Amount = incomingAmount - transferred;

			return true;
		}

		/// <summary>
		/// Attempts to split <paramref name="amount"/> off this stack into a new item instance.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Requesting the whole stack (or more) is not a split — the original instance is handed back
		/// untouched so the caller can move it wholesale, and this stack is left alone.
		/// </para>
		/// <para>
		/// A genuine split allocates a real new <see cref="Item"/> carrying the same template and the
		/// same generation seed, so the two halves remain mutually stackable. The new instance has
		/// <c>ID = 0</c> and <c>Slot = -1</c>: it is not yet a database row and not yet in a container.
		/// The caller owns placing it and persisting it. The previous implementation decremented this
		/// stack, set <c>instance = null</c>, and still returned <c>true</c> — silently destroying the
		/// split-off quantity for any caller that trusted the result.
		/// </para>
		/// </remarks>
		/// <param name="amount">The amount to unstack.</param>
		/// <param name="instance">The new item instance, the original item, or null on failure.</param>
		/// <returns>True if unstacking was successful, false otherwise.</returns>
		public bool TryUnstack(uint amount, out Item instance)
		{
			instance = null;

			if (amount < 1)
			{
				return false;
			}

			if (item == null || item.Template == null)
			{
				return false;
			}

			// Taking everything is a move, not a split.
			if (amount >= Amount)
			{
				instance = this.item;
				return true;
			}

			// Preserve the generation seed so the split half still matches the source (see Item.IsMatch),
			// otherwise the two halves could never be re-stacked.
			int seed = item.IsGenerated ? item.Generator.Seed : 0;

			Item split = new Item(0, seed, item.Template, amount);
			if (split.Stackable == null)
			{
				// The template is not stackable, so there is nothing to split. Report failure rather
				// than silently mutating this stack.
				Log.Error("ItemStackable", $"TryUnstack: Template '{item.Template.Name}' is not stackable (MaxStackSize={item.Template.MaxStackSize}).");
				return false;
			}

			// Only commit the decrement once the new instance exists — quantity is conserved.
			Amount -= amount;
			instance = split;
			return true;
		}
	}
}
