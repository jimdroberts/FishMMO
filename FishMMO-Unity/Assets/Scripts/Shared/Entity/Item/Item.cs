using System;
using Cysharp.Text;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an item instance in the game, including stackable, equippable, and generated properties.
	/// Handles initialization, attribute management, and tooltip generation.
	/// </summary>
	public class Item
	{
		/// <summary>
		/// The item generator responsible for random attributes and generation logic.
		/// </summary>
		public ItemGenerator Generator;

		/// <summary>
		/// The equippable component for this item, if applicable.
		/// </summary>
		public ItemEquippable Equippable;

		/// <summary>
		/// The stackable component for this item, if applicable.
		/// </summary>
		public ItemStackable Stackable;

		/// <summary>
		/// Event triggered when the item is destroyed.
		/// </summary>
		public event Action OnDestroy;

		/// <summary>
		/// The item template defining base properties and attributes.
		/// </summary>
		public BaseItemTemplate Template { get; private set; }

		/// <summary>
		/// The unique ID for this item instance.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// The slot index this item is currently assigned to.
		/// </summary>
		public int Slot { get; set; }

		/// <summary>
		/// Returns true if the item has a generator (is randomly generated).
		/// </summary>
		public bool IsGenerated { get { return Generator != null; } }

		/// <summary>
		/// Returns true if the item is equippable.
		/// </summary>
		public bool IsEquippable { get { return Equippable != null; } }

		/// <summary>
		/// Returns true if the item is stackable.
		/// </summary>
		public bool IsStackable { get { return Stackable != null; } }

		/// <summary>
		/// Constructs an item from a template and amount, initializing stackable logic if needed.
		/// </summary>
		/// <param name="template">The item template.</param>
		/// <param name="amount">The stack amount.</param>
		public Item(BaseItemTemplate template, uint amount)
		{
			Slot = -1;
			Template = template;
			if (amount > 0)
			{
				if (Stackable == null)
				{
					if (Template.MaxStackSize > 1)
					{
						Stackable = new ItemStackable(this, amount.Clamp(1, Template.MaxStackSize));
					}
				}
				else
				{
					Stackable.Amount = amount;
				}
			}
		}

		/// <summary>
		/// Constructs an item from an ID, seed, template, and amount, initializing all components.
		/// </summary>
		/// <param name="id">The item ID.</param>
		/// <param name="seed">The random seed for generation.</param>
		/// <param name="template">The item template.</param>
		/// <param name="amount">The stack amount.</param>
		public Item(long id, int seed, BaseItemTemplate template, uint amount)
		{
			ID = id;
			Slot = -1;
			Template = template;

			Initialize(id, amount, seed);
		}

		/// <summary>
		/// Constructs an item from an ID, seed, template ID, and amount, initializing all components.
		/// </summary>
		/// <param name="id">The item ID.</param>
		/// <param name="seed">The random seed for generation.</param>
		/// <param name="templateID">The template ID.</param>
		/// <param name="amount">The stack amount.</param>
		public Item(long id, int seed, int templateID, uint amount)
		{
			ID = id;
			Slot = -1;
			Template = BaseItemTemplate.Get<BaseItemTemplate>(templateID);

			Initialize(id, amount, seed);
		}

		/// <summary>
		/// Initializes the item, setting up stackable, equippable, and generator components as needed.
		/// Handles seed logic for random generation and event wiring for attribute changes.
		/// </summary>
		/// <param name="id">The item ID.</param>
		/// <param name="amount">The stack amount.</param>
		/// <param name="seed">The random seed for generation.</param>
		public void Initialize(long id, uint amount, int seed)
		{
			ID = id;

			bool initializeEquippable = false;
			bool initializeGenerator = false;

			// Check if the item is stackable and initialize if needed.
			if (amount > 0)
			{
				if (Stackable == null)
				{
					if (Template.MaxStackSize > 1)
					{
						Stackable = new ItemStackable(this, amount.Clamp(1, Template.MaxStackSize));
					}
				}
				else
				{
					Stackable.Amount = amount;
				}
			}

			// Ensure ItemEquippable is created if it's an equippable item type.
			if (Equippable == null &&
				Template as EquippableItemTemplate != null)
			{
				initializeEquippable = true;
				Equippable = new ItemEquippable();
			}

			// Ensure ItemGenerator is created if the item can be generated.
			if (Generator == null &&
				Template.Generate)
			{
				initializeGenerator = true;
				Generator = new ItemGenerator();

				// Get the item's seed if none is provided.
				if (seed == 0 && ID != 0)
				{
					var longBytes = BitConverter.GetBytes(ID);

					// Get integers from the first and the last 4 bytes of long.
					int[] ints = new int[] {
					   BitConverter.ToInt32(longBytes, 0),
					   BitConverter.ToInt32(longBytes, 4)
				   };
					if (ints != null && ints.Length > 1)
					{
						// Use the ID of the item as a unique seed value instead of generating a seed value per item.
						seed = ints[1] > 0 ? ints[1] : ints[0];
					}
				}
			}

			// Finalize initialization of components and wire events.
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

		/// <summary>
		/// Destroys the item, cleaning up generator, equippable, and stackable components and detaching events.
		/// </summary>
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

		/// <summary>
		/// Determines if this item matches another item, comparing template ID and generation seed.
		/// Used for stacking and item comparison logic.
		/// </summary>
		/// <param name="other">The other item to compare.</param>
		/// <returns>True if the items match, false otherwise.</returns>
		public bool IsMatch(Item other)
		{
			return Template.ID == other.Template.ID &&
					(IsGenerated && other.IsGenerated && Generator.Seed == other.Generator.Seed ||
					!IsGenerated && !other.IsGenerated);
		}

		/// <summary>
		/// Returns the formatted tooltip string for this item, including ID, slot, template tooltip, and generator info.
		/// </summary>
		/// <returns>The formatted tooltip string.</returns>
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

		/// <summary>
		/// Event handler called when an item's attribute is set by the generator.
		/// Updates character attributes or resources accordingly.
		/// </summary>
		/// <param name="attribute">The item attribute being changed.</param>
		/// <param name="oldValue">The previous value of the attribute.</param>
		/// <param name="newValue">The new value of the attribute.</param>
		public void ItemGenerator_OnSetAttribute(ItemAttribute attribute, int oldValue, int newValue)
		{
			if (IsEquippable)
			{
				ICharacter character = Equippable.Character;

				if (character != null &&
					character.TryGet(out ICharacterAttributeController attributeController))
				{
					if (attributeController.TryGetAttribute(attribute.Template.CharacterAttribute.ID, out CharacterAttribute characterAttribute))
					{
						characterAttribute.AddValue(-oldValue);
						characterAttribute.AddValue(newValue);
					}
					else if (attributeController.TryGetResourceAttribute(attribute.Template.CharacterAttribute.ID, out CharacterResourceAttribute characterResourceAttribute))
					{
						characterResourceAttribute.AddValue(-oldValue);
						characterResourceAttribute.AddValue(newValue);
					}
				}
			}
		}

		/// <summary>
		/// Event handler called when the item is equipped by a character. Applies generated attributes.
		/// </summary>
		/// <param name="character">The character equipping the item.</param>
		public void ItemEquippable_OnEquip(ICharacter character)
		{
			if (IsGenerated)
			{
				Generator.ApplyAttributes(character);
			}
		}

		/// <summary>
		/// Event handler called when the item is unequipped by a character. Removes generated attributes.
		/// </summary>
		/// <param name="character">The character unequipping the item.</param>
		public void ItemEquippable_OnUnequip(ICharacter character)
		{
			if (IsGenerated)
			{
				Generator.RemoveAttributes(character);
			}
		}
	}
}