using System;

namespace FishMMO.Shared
{
	public class ItemEquippable : IEquippable<Character>
	{
		private Item item;

		public event Action<Character> OnEquip;
		public event Action<Character> OnUnequip;

		public Character Character { get; private set; }

		public void Initialize(Item item)
		{
			this.item = item;
		}

		public void Destroy()
		{
			Unequip();
		}

		public void Equip(Character owner)
		{
			if (owner == null)
			{
				return;
			}

			Unequip();

			Character = owner;
			OnEquip?.Invoke(owner);
		}

		public void Unequip()
		{
			if (Character != null)
			{
				OnUnequip?.Invoke(Character);
				Character = null;
			}
		}

		public void SetOwner(Character owner)
		{
			Character = owner;
		}
	}
}