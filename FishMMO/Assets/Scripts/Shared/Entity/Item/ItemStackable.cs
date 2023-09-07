public class ItemStackable : IStackable<Item>
{
	private Item item;
	public uint Amount;

	public bool IsStackFull { get { return Amount == item.Template.MaxStackSize; } }

	public void Initialize(Item item, uint amount)
	{
		this.item = item;
		Amount = amount;
	}

	public void Remove(uint amount)
	{
		Amount -= amount;
		if (Amount == 0)
		{
			item.Destroy();
		}
	}

	/// <summary>
	/// Returns true only if we can add the entire item to the stack.
	/// </summary>
	public bool CanAddToStack(Item other)
	{
		if (other == null) return false;

		if (Amount < 1) return false; // item no longer exists?

		if (item.Template.ID != other.Template.ID) return false;

		// the item seeds must match
		if (!item.IsMatch(other))
		{
			return false;
		}

		// if either stack is full we can't add any more
		if (IsStackFull || other.Stackable != null || other.Stackable.IsStackFull) return false;

		uint remainingCapacity = item.Template.MaxStackSize - Amount;

		uint remainingAmount = remainingCapacity.AbsoluteSubtract(other.Stackable.Amount);
		// if we can't add the full amount there will be a remainder
		if (remainingAmount > 0) return false;

		return true;
	}

	/// <summary>
	/// Adds the item to the stack and sets the other stacks size to the remainder if any. Returns false on failure.
	/// </summary>
	public bool AddToStack(Item other)
	{
		if (other == null) return false;

		if (Amount < 1) return false; // this should have been an empty slot!

		if (item.Template.ID != other.Template.ID) return false;

		// the item seeds must match
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
	/// Attempts to unstack a certain amount from the item. Returns true if successful and the new instance is set. UNFINISHED
	/// </summary>
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
		//instance = new Item(templateID, amount, seed);
		return true;
	}
}