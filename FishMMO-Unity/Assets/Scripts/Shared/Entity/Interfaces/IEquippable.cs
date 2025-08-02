namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for objects that can be equipped and unequipped by an owner of type T.
	/// Implement this to allow items or entities to be equipped by a character or other owner.
	/// </summary>
	/// <typeparam name="T">The type of the owner that can equip this object.</typeparam>
	public interface IEquippable<T>
	{
		/// <summary>
		/// Equips the object to the specified owner.
		/// </summary>
		/// <param name="owner">The entity or character equipping this object.</param>
		void Equip(T owner);

		/// <summary>
		/// Unequips the object from its current owner.
		/// </summary>
		void Unequip();
	}
}