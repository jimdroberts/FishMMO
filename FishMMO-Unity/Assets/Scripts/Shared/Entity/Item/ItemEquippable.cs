using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public class ItemEquippable : IEquippable<ICharacter>
	{
		private Item item;

		public event Action<ICharacter> OnEquip;
		public event Action<ICharacter> OnUnequip;

		public ICharacter Character { get; private set; }

		public void Initialize(Item item)
		{
			this.item = item;
		}

		public void Destroy()
		{
			Unequip();
		}

		public void Equip(ICharacter owner)
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetOwner(ICharacter owner)
		{
			Character = owner;
		}
	}
}