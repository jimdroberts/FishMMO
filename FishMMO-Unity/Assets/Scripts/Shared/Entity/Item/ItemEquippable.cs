using System;

namespace FishMMO.Shared
{
	public class ItemEquippable : IEquippable<Character>
	{
		private Item item;

		public event Action<Character> OnEquip;
		public event Action<Character> OnUnequip;

		public Character Character { get; private set; }

		public ItemEquippable(Item item)
		{
			this.item = item;
			if (item.Generator != null)
			{
				item.Generator.OnSetAttribute += ItemGenerator_OnSetAttribute;
			}
		}

		public void Destroy()
		{
			if (item.Generator != null)
			{
				item.Generator.OnSetAttribute -= ItemGenerator_OnSetAttribute;
			}

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
			Unequip();
			Character = owner;
		}

		public void ItemGenerator_OnSetAttribute(ItemAttribute attribute, int oldValue, int newValue)
		{
			if (Character != null &&
				Character.TryGet(out CharacterAttributeController attributeController) &&
				attributeController.TryGetAttribute(attribute.Template.CharacterAttribute.ID, out CharacterAttribute characterAttribute))
			{
				characterAttribute.AddValue(-oldValue);
				characterAttribute.AddValue(newValue);
			}
		}
	}
}