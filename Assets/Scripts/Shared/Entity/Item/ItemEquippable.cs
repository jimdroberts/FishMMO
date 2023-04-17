using System;

public class ItemEquippable : IEquippable<Character>
{
	public Item item;
	public Character owner;
	public event Action<Character> OnEquip;
	public event Action<Character> OnUnequip;

	public Character Owner { get { return Owner; } }

	public void Initialize(Item item)
	{
		this.item = item;
		if (item.generator != null)
		{
			item.generator.OnSetAttribute += ItemGenerator_OnSetAttribute;
		}
	}

	public void Destroy()
	{
		if (item.generator != null)
		{
			item.generator.OnSetAttribute -= ItemGenerator_OnSetAttribute;
		}

		Unequip();
	}

	public void Equip(Character owner)
	{
		if (owner != null)
		{
			this.owner = owner;
			OnEquip?.Invoke(owner);
		}
	}

	public void Unequip()
	{
		if (owner != null)
		{
			OnUnequip?.Invoke(owner);
			owner = null;
		}
	}

	public void ItemGenerator_OnSetAttribute(ItemAttribute attribute, int oldValue, int newValue)
	{
		if (owner != null)
		{
			if (owner.AttributeController != null &&
				owner.AttributeController.TryGetAttribute(attribute.Template.CharacterAttribute.Name, out CharacterAttribute characterAttribute))
			{
				characterAttribute.AddValue(-oldValue);
				characterAttribute.AddValue(newValue);
			}
		}
	}
}