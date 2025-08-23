using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the equippable component of an item, handling equip/unequip logic and owner tracking.
	/// Manages events for when the item is equipped or unequipped by a character.
	/// </summary>
	public class ItemEquippable : IEquippable<ICharacter>
	{
		/// <summary>
		/// The item instance this equippable component belongs to.
		/// </summary>
		private Item item;

		/// <summary>
		/// Event triggered when the item is equipped by a character.
		/// </summary>
		public event Action<ICharacter> OnEquip;

		/// <summary>
		/// Event triggered when the item is unequipped by a character.
		/// </summary>
		public event Action<ICharacter> OnUnequip;

		/// <summary>
		/// The character currently owning/equipping this item. Null if not equipped.
		/// </summary>
		public ICharacter Character { get; private set; }

		/// <summary>
		/// Initializes the equippable component with its parent item.
		/// </summary>
		/// <param name="item">The item instance.</param>
		public void Initialize(Item item)
		{
			this.item = item;
		}

		/// <summary>
		/// Destroys the equippable component, ensuring it is unequipped and cleaned up.
		/// </summary>
		public void Destroy()
		{
			Unequip();
		}

		/// <summary>
		/// Equips the item to the specified character, firing the OnEquip event.
		/// If already equipped, unequips first.
		/// </summary>
		/// <param name="owner">The character to equip the item to.</param>
		public void Equip(ICharacter owner)
		{
			if (owner == null)
			{
				return;
			}

			// Ensure previous owner is unequipped before assigning new owner.
			Unequip();

			Character = owner;
			OnEquip?.Invoke(owner);
		}

		/// <summary>
		/// Unequips the item from the current character, firing the OnUnequip event.
		/// </summary>
		public void Unequip()
		{
			if (Character != null)
			{
				OnUnequip?.Invoke(Character);
				Character = null;
			}
		}
	}
}