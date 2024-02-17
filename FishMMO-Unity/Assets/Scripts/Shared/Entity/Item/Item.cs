using System;
using Cysharp.Text;

namespace FishMMO.Shared
{
	public class Item
	{
		public ItemGenerator Generator;
		public ItemEquippable Equippable;
		public ItemStackable Stackable;

		public event Action OnDestroy;
		public BaseItemTemplate Template { get; private set; }
		public long ID { get; set; }
		public int Slot { get; set; }
		public bool IsGenerated { get { return Generator != null; } }
		public bool IsEquippable { get { return Equippable != null; } }
		public bool IsStackable { get { return Stackable != null; } }

		public Item(BaseItemTemplate template, uint amount)
		{
			Slot = -1;
			Template = template;
		}
		public Item(long id, int seed, BaseItemTemplate template, uint amount)
		{
			ID = id;
			Slot = -1;
			Template = template;

			Initialize(id, amount, seed);
		}
		public Item(long id, int seed, int templateID, uint amount)
		{
			ID = id;
			Slot = -1;
			Template = BaseItemTemplate.Get<BaseItemTemplate>(templateID);

			Initialize(id, amount, seed);
		}

		public void Initialize(long id, uint amount, int seed)
		{
			ID = id;

			bool initializeEquippable = false;
			bool initializeGenerator = false;

			// check if the item is stackable
			if (Stackable == null &&
				Template.MaxStackSize > 1)
			{
				Stackable = new ItemStackable(this, amount.Clamp(1, Template.MaxStackSize));
			}

			// ensure Item Equippable is created if it's an equippable item type
			if (Equippable == null &&
				Template as EquippableItemTemplate != null)
			{
				initializeEquippable = true;
				Equippable = new ItemEquippable();
			}

			// ensure Item Generator is created if the item can be generated
			if (Generator == null &&
				Template.Generate &&
				ID != 0)
			{
				initializeGenerator = true;
				Generator = new ItemGenerator();

				// get the items seed if none is provided
				if (seed == 0)
				{
					var longBytes = BitConverter.GetBytes(ID);

					// Get integers from the first and the last 4 bytes of long
					int[] ints = new int[] {
						BitConverter.ToInt32(longBytes, 0),
						BitConverter.ToInt32(longBytes, 4)
					};
					if (ints != null && ints.Length > 1)
					{
						// we can use the ID of the item as a unique seed value instead of generating a seed value per item
						seed = ints[1] > 0 ? ints[1] : ints[0];
					}
				}
			}

			// finalize initialization of components
			if (initializeEquippable)
			{
				Equippable?.Initialize(this);

				if (initializeGenerator)
				{
					Generator.OnSetAttribute += ItemGenerator_OnSetAttribute;
				}
			}
			if (initializeGenerator)
			{
				Generator?.Initialize(this, seed);

				if (initializeEquippable)
				{
					Equippable.OnEquip += ItemEquippable_OnEquip;
					Equippable.OnUnequip += ItemEquippable_OnUnequip;
				}
			}
		}

		public void Destroy()
		{
			if (Generator != null)
			{
				if (IsEquippable)
				{
					Equippable.OnEquip -= ItemEquippable_OnEquip;
					Equippable.OnUnequip -= ItemEquippable_OnUnequip;
				}
				Generator.Destroy();
			}
			if (Equippable != null)
			{
				if (IsGenerated)
				{
					Generator.OnSetAttribute -= ItemGenerator_OnSetAttribute;
				}
				Equippable.Destroy();
			}
			/*if (Stackable != null)
			{
				Stackable.OnDestroy();
			}*/
			OnDestroy?.Invoke();
		}

		public bool IsMatch(Item other)
		{
			return Template.ID == other.Template.ID &&
					(IsGenerated && other.IsGenerated && Generator.Seed == other.Generator.Seed ||
					!IsGenerated && !other.IsGenerated);
		}

		public string Tooltip()
		{
			string tooltip = "";
			var sb = ZString.CreateStringBuilder();
			try
			{
				sb.Append("<color=#a66ef5>ID: ");
				sb.Append(ID);
				sb.AppendLine();
				sb.Append("Slot: ");
				sb.Append(Slot);
				sb.Append("</color>");
				sb.AppendLine();
				sb.Append(Template.Tooltip());
				sb.AppendLine();
				Generator?.Tooltip(ref sb);
				tooltip = sb.ToString();
			}
			finally
			{
				sb.Dispose();
			}
			return tooltip;
		}

		public void ItemGenerator_OnSetAttribute(ItemAttribute attribute, int oldValue, int newValue)
		{
			if (IsEquippable)
			{
				Character character = Equippable.Character;

				if (character != null &&
					character.TryGet(out CharacterAttributeController attributeController) &&
					attributeController.TryGetAttribute(attribute.Template.CharacterAttribute.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.AddValue(-oldValue);
					characterAttribute.AddValue(newValue);
				}
			}
		}

		public void ItemEquippable_OnEquip(Character character)
		{
			if (IsGenerated)
			{
				Generator.ApplyAttributes(character);
			}
		}

		public void ItemEquippable_OnUnequip(Character character)
		{
			if (IsGenerated)
			{
				Generator.RemoveAttributes(character);
			}
		}
	}
}