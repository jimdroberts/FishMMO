namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for objects that can be stacked together, such as items or resources.
	/// Provides methods for stack management and splitting.
	/// </summary>
	/// <typeparam name="T">The type of object that can be stacked.</typeparam>
	public interface IStackable<T>
	{
		/// <summary>
		/// Determines if the specified object can be added to the current stack.
		/// </summary>
		/// <param name="other">The object to check for stack compatibility.</param>
		/// <returns>True if the object can be added to the stack, false otherwise.</returns>
		bool CanAddToStack(T other);

		/// <summary>
		/// Adds the specified object to the current stack if possible.
		/// </summary>
		/// <param name="other">The object to add to the stack.</param>
		/// <returns>True if the object was successfully added, false otherwise.</returns>
		bool AddToStack(T other);

		/// <summary>
		/// Attempts to remove a specified amount from the stack, returning the split stack if successful.
		/// </summary>
		/// <param name="amount">The amount to unstack.</param>
		/// <param name="stack">The resulting stack after unstacking.</param>
		/// <returns>True if the unstack operation was successful, false otherwise.</returns>
		bool TryUnstack(uint amount, out T stack);
	}
}