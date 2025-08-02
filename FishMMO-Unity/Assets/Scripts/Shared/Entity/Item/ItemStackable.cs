using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the stackable component of an item, managing stack size, addition, removal, and unstacking logic.
	/// </summary>
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
		/// Returns true if the stack is full (reached max stack size).
		/// </summary>
		public bool IsStackFull { get { return Amount == item.Template.MaxStackSize; } }

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
		/// <param name="amount">The amount to remove.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Remove(uint amount)
		{
			Amount -= amount;
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

			if (Amount < 1) return false; // item no longer exists?

			if (item.Template.ID != other.Template.ID) return false;

			// The item seeds must match for stacking.
			if (!item.IsMatch(other))
			{
				return false;
			}

			// If either stack is full, we can't add any more.
			if (IsStackFull || other.Stackable != null || other.Stackable.IsStackFull) return false;

			uint remainingCapacity = item.Template.MaxStackSize - Amount;

			uint remainingAmount = remainingCapacity.AbsoluteSubtract(other.Stackable.Amount);
			// If we can't add the full amount, there will be a remainder.
			if (remainingAmount > 0) return false;

			return true;
		}

		/// <summary>
		/// Adds the other item to this stack and sets the other stack's size to the remainder, if any.
		/// Returns false on failure.
		/// </summary>
		/// <param name="other">The item to add to the stack.</param>
		/// <returns>True if the item was added, false otherwise.</returns>
		public bool AddToStack(Item other)
		{
			if (other == null) return false;

			if (Amount < 1) return false; // this should have been an empty slot!

			if (item.Template.ID != other.Template.ID) return false;

			// The item seeds must match for stacking.
			if (!item.IsMatch(other))
			{
				return false;
			}

			if (IsStackFull || other.Stackable != null || other.Stackable.IsStackFull) return false;

			uint remainingCapacity = item.Template.MaxStackSize - Amount;
			uint remainingAmount = remainingCapacity.AbsoluteSubtract(other.Stackable.Amount);
			other.Stackable.Amount = remainingAmount;

			return true;
		}

		/// <summary>
		/// Attempts to unstack a certain amount from the item. Returns true if successful and the new instance is set.
		/// Note: The logic for creating a new item instance is unfinished.
		/// </summary>
		/// <param name="amount">The amount to unstack.</param>
		/// <param name="instance">The new item instance, or null.</param>
		/// <returns>True if unstacking was successful, false otherwise.</returns>
		public bool TryUnstack(uint amount, out Item instance)
		{
			if (amount < 1)
			{
				instance = null;
				return false;
			}

			if (amount >= Amount)
			{
				instance = this.item;
				return true;
			}
			Amount -= amount;
			instance = null;
			//instance = new Item(templateID, amount, seed); // Unfinished: should create a new item instance with the specified amount.
			return true;
		}
	}
}