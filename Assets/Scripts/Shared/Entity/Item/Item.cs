using System.Collections.Generic;
using System.Linq;
using System.Text;

public class Item
{
	private Dictionary<string, ItemAttribute> attributes = new Dictionary<string, ItemAttribute>();

	/// <summary>
	/// The character the item is currently equipped on.
	/// </summary>
	public Character character;

	public int templateID;
	public ulong instanceID;
	public int seed;
	public int slot;
	public uint stackSize;

	public BaseItemTemplate Template { get { return BaseItemTemplate.Cache[templateID]; } }
	public bool IsStackFull { get { return stackSize == Template.MaxStackSize; } }

	public Item(ulong instanceID, int templateID)
	{
		this.instanceID = instanceID;
		this.templateID = templateID;
		this.stackSize = 0;
	}
	public Item(ulong instanceID, int templateID, uint amount)
	{
		this.instanceID = instanceID;
		this.templateID = templateID;
		this.stackSize = amount.Clamp(1, Template.MaxStackSize);
	}
	public Item(ulong instanceID, int templateID, uint amount, int seed)
	{
		this.instanceID = instanceID;
		this.templateID = templateID;
		this.stackSize = amount.Clamp(1, Template.MaxStackSize);
		this.seed = seed;
	}

	public string Tooltip()
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("<size=120%><color=#f5ad6e>");
		sb.Append(Template.Name);
		sb.Append("</color></size>");
		sb.AppendLine();
		sb.Append("<color=#a66ef5>InstanceID: ");
		sb.Append(instanceID);
		sb.Append("</color>");
		sb.AppendLine();
		sb.Append("<color=#a66ef5>TemplateID: ");
		sb.Append(templateID);
		sb.Append("</color>");
		sb.AppendLine();
		sb.Append("<color=#a66ef5>Seed: ");
		sb.Append(seed);
		sb.Append("</color>");
		sb.AppendLine();
		foreach (ItemAttribute attribute in attributes.Values)
		{
			sb.Append("<size=110%>");
			sb.Append(attribute.Template.Name);
			sb.Append(": <color=#32a879>");
			sb.Append(attribute.value);
			sb.Append("</color></size>");
			sb.AppendLine();
		}
		return sb.ToString();
	}

	public void GenerateAttributes()
	{
		GenerateAttributes(this.seed);
	}
	public void GenerateAttributes(int seed)
	{
		this.seed = seed;

		System.Random random = new System.Random(seed);
		if (random != null)
		{
			int r = 0;
			if (attributes != null)
			{
				// double boxing is bad? we only do it when we generate the item though..?
				EquippableItemTemplate equippable = Template as EquippableItemTemplate;
				if (equippable == null)
					return;

				WeaponTemplate weapon = Template as WeaponTemplate;
				if (weapon != null)
				{
					attributes.Add(weapon.AttackPower.Name, new ItemAttribute(weapon.AttackPower.ID, random.Next(weapon.AttackPower.MinValue, weapon.AttackPower.MaxValue)));
					attributes.Add(weapon.AttackSpeed.Name, new ItemAttribute(weapon.AttackSpeed.ID, random.Next(weapon.AttackSpeed.MinValue, weapon.AttackSpeed.MaxValue)));
				}
				else
				{
					ArmorTemplate armor = Template as ArmorTemplate;
					if (armor != null)
					{
						attributes.Add(armor.ArmorBonus.Name, new ItemAttribute(armor.ArmorBonus.ID, random.Next(armor.ArmorBonus.MinValue, armor.ArmorBonus.MaxValue)));
					}
				}

				if (equippable.AttributeDatabases != null && equippable.AttributeDatabases.Length > 0)
				{
					int attributeCount = random.Next(0, equippable.MaxItemAttributes);
					for (int i = 0; i < attributeCount; ++i)
					{
						r = random.Next(0, equippable.AttributeDatabases.Length);
						ItemAttributeTemplateDatabase db = equippable.AttributeDatabases[r];
						r = random.Next(0, db.Attributes.Count);
						ItemAttributeTemplate attributeTemplate = Enumerable.ToList(db.Attributes.Values)[r];
						attributes.Add(attributeTemplate.Name, new ItemAttribute(attributeTemplate.ID, random.Next(attributeTemplate.MinValue, attributeTemplate.MaxValue)));
					}
				}
			}
		}
	}

	public void Equip(Character owner)
	{
		if (owner != null)
		{
			character = owner; //track the item owner so we can unequip the item easily

			foreach (KeyValuePair<string, ItemAttribute> pair in attributes)
			{
				if (character.AttributeController.TryGetAttribute(pair.Value.Template.CharacterAttribute.Name, out CharacterAttribute characterAttribute))
				{
					characterAttribute.AddValue(pair.Value.value);
				}
			}
		}
	}

	public void Unequip()
	{
		if (character != null)
		{
			foreach (KeyValuePair<string, ItemAttribute> pair in attributes)
			{
				if (character.AttributeController.TryGetAttribute(pair.Value.Template.CharacterAttribute.Name, out CharacterAttribute characterAttribute))
				{
					characterAttribute.RemoveValue(pair.Value.value);
				}
			}
			character = null;
		}
	}

	public ItemAttribute GetAttribute(string name)
	{
		attributes.TryGetValue(name, out ItemAttribute attribute);
		return attribute;
	}

	public void SetAttribute(string name, int newValue)
	{
		if (attributes.TryGetValue(name, out ItemAttribute attribute))
		{
			if (attribute.value == newValue) return;

			int oldValue = attribute.value;
			attribute.value = newValue;

			if (character != null)
			{
				if (character.AttributeController != null &&
					character.AttributeController.TryGetAttribute(attribute.Template.CharacterAttribute.Name, out CharacterAttribute characterAttribute))
				{
					characterAttribute.RemoveValue(oldValue);
					characterAttribute.AddValue(newValue);
				}
			}
		}
	}

	/// <summary>
	/// Returns true only if we can add the entire item to the stack.
	/// </summary>
	public bool CanAddItemToStack(Item other)
	{
		if (other == null) return false;

		if (stackSize < 1) return false; // this should have been an empty slot!

		if (templateID != other.templateID) return false;

		// the item seeds must match
		if (seed != other.seed) return false;

		// if either stack is full we can't add any more
		if (IsStackFull || other.IsStackFull) return false;

		uint remainingCapacity = Template.MaxStackSize - stackSize;

		uint remainingAmount = remainingCapacity.AbsoluteSubtract(other.stackSize);
		// if we can't add the full amount there will be a remainder
		if (remainingAmount > 0) return false;

		return true;
	}

	/// <summary>
	/// Adds the item to the stack and sets the other stacks size to the remainder if any. Returns false on failure.
	/// </summary>
	public bool AddItem(Item other)
	{
		if (other == null) return false;

		if (stackSize < 1) return false; // this should have been an empty slot!

		if (templateID != other.templateID) return false;

		// the item seeds must match
		if (seed != other.seed) return false;

		if (IsStackFull || other.IsStackFull) return false;

		uint remainingCapacity = Template.MaxStackSize - stackSize;
		uint remainingAmount = remainingCapacity.AbsoluteSubtract(other.stackSize);
		other.stackSize = remainingAmount;

		return true;
	}

	/// <summary>
	/// Attempts to unstack a certain amount from the item. Returns true if successful and the new instance is set.
	/// </summary>
	public bool TryUnstack(uint amount, out Item instance)
	{
		if (amount < 1)
		{
			instance = null;
			return false;
		}

		if (amount >= stackSize)
		{
			instance = this;
			return true;
		}
		stackSize -= amount;
		instance = null;
		//instance = new Item(templateID, amount, seed);
		//instance.isIdentified = isIdentified;
		return true;
	}
}