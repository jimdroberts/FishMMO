using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an item instance in the game, including stackable, equippable, and generated properties.
	/// Handles initialization, attribute management, and tooltip generation.
	/// Implements <see cref="ITooltip"/> for consistent UI tooltip display.
	/// </summary>
	public class Item : ITooltip
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
		public long ID;

		/// <summary>
		/// Version number for this item instance, used for client synchronization and updates.
		/// Incremented whenever the item's state changes in a way that requires client updates.
		/// </summary>
		public long Version;

		/// <summary>
		/// The slot index this item is currently assigned to.
		/// </summary>
		public int Slot;

		/// <summary>
		/// Gets the icon sprite from the item template.
		/// </summary>
		public Sprite Icon { get { return Template?.Icon; } }

		/// <summary>
		/// Gets the display name from the item template.
		/// </summary>
		public string Name { get { return Template?.Name; } }

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
		/// <para>
		/// IMPORTANT: This constructor creates items with ID=0. It must only be used for
		/// temporary or display-only items. For gameplay items that will be persisted, use
		/// <see cref="Item(long, int, BaseItemTemplate, uint)"/> instead to ensure a unique
		/// database-safe ID is assigned.
		/// </para>
		/// </summary>
		/// <param name="template">The item template.</param>
		/// <param name="amount">The stack amount.</param>
		public Item(BaseItemTemplate template, uint amount)
		{
			Slot = -1;
			Template = template;
			if (template != null && template.MaxStackSize > 1 && amount > 0)
			{
				Stackable = new ItemStackable(this, amount.Clamp(1, template.MaxStackSize));
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
			using (var builder = new TooltipBuilder())
			{
				BuildTooltip(builder);
				return builder.Build();
			}
		}

		/// <summary>
		/// Populates the tooltip builder with this item's tooltip lines.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public void BuildTooltip(TooltipBuilder builder)
		{
			builder.AddLine($"ID: {ID}", 5, TooltipColors.Label);
			builder.AddLine($"Slot: {Slot}", 6, TooltipColors.Label);
			Template?.BuildTooltip(builder);
			Generator?.BuildTooltip(builder);
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