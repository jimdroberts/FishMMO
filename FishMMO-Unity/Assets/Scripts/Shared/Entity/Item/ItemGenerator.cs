using System.Collections.Generic;
using System.Linq;
using Cysharp.Text;
using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Handles random attribute generation and management for items, including applying/removing attributes to characters.
	/// Supports equippable and template-based attribute logic, and exposes events for attribute changes.
	/// </summary>
	public class ItemGenerator
	{
		/// <summary>
		/// The random seed used for attribute generation.
		/// </summary>
		protected int seed;

		/// <summary>
		/// Gets or sets the seed for generation. Changing the seed triggers attribute regeneration.
		/// </summary>
		public int Seed
		{
			get { return seed; }
			set
			{
				if (seed != value)
				{
					seed = value;
					Generate();
				}
			}
		}

		/// <summary>
		/// Dictionary of generated item attributes, keyed by attribute name.
		/// </summary>
		private Dictionary<string, ItemAttribute> attributes = new Dictionary<string, ItemAttribute>();

		/// <summary>
		/// The item instance this generator is attached to.
		/// </summary>
		private Item item;

		/// <summary>
		/// Exposes the generated attributes for external access.
		/// </summary>
		public Dictionary<string, ItemAttribute> Attributes { get { return attributes; } }

		/// <summary>
		/// Initializes the generator with its parent item and seed, triggering attribute generation.
		/// </summary>
		/// <param name="item">The item instance.</param>
		/// <param name="seed">The random seed for generation.</param>
		public void Initialize(Item item, int seed)
		{
			this.item = item;
			Seed = seed;
		}

		/// <summary>
		/// Cleans up the generator, detaching from the item.
		/// </summary>
		public void Destroy()
		{
			item = null;
		}

		/// <summary>
		/// Appends generator information and all generated attributes to the provided tooltip string builder.
		/// </summary>
		/// <param name="sb">The string builder to append to.</param>
		public void Tooltip(ref Utf16ValueStringBuilder sb)
		{
			sb.Append("<color=#a66ef5>Seed: ");
			sb.Append(Seed);
			sb.Append("</color>");
			if (attributes.Count > 0)
			{
				sb.AppendLine();
				sb.Append("<size=125%><color=#a66ef5>Attributes:</color></size>");
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
			}
		}

		/// <summary>
		/// Triggers attribute generation using the current seed and item template.
		/// </summary>
		public void Generate()
		{
			Generate(seed);
		}

		/// <summary>
		/// Generates item attributes using the provided seed and template.
		/// Handles equippable logic, random attributes, and additional template attributes.
		/// </summary>
		/// <param name="seed">The random seed for generation.</param>
		/// <param name="template">The item template to use. If null, uses the item's template.</param>
		public void Generate(int seed, BaseItemTemplate template = null)
		{
			this.seed = seed;

			if (item == null && template == null)
			{
				throw new UnityException("Missing item template during Generation!");
			}

			template ??= item?.Template; // Use null-coalescing operator for cleaner assignment

			System.Random random = new System.Random(seed);

			if (random != null && attributes != null)
			{
				attributes.Clear();

				// If the template is equippable, generate base and random attributes.
				if (template is EquippableItemTemplate equippable)
				{
					GenerateItemAttributes(random, equippable);

					if (equippable.RandomAttributeDatabases?.Length > 0)
					{
						AddRandomAttributes(random, equippable);
					}
				}
			}

			// Add any additional attributes defined in the template.
			if (template != null)
			{
				AddAdditionalTemplateAttributes(template);
			}
		}

		/// <summary>
		/// Generates base attributes for equippable items, such as weapon or armor stats.
		/// </summary>
		/// <param name="random">The random number generator.</param>
		/// <param name="equippable">The equippable item template.</param>
		private void GenerateItemAttributes(System.Random random, EquippableItemTemplate equippable)
		{
			if (equippable is WeaponTemplate weapon)
			{
				attributes.Add(weapon.AttackPower.Name, new ItemAttribute(weapon.AttackPower.ID, random.Next(weapon.AttackPower.MinValue, weapon.AttackPower.MaxValue)));
				attributes.Add(weapon.AttackSpeed.Name, new ItemAttribute(weapon.AttackSpeed.ID, random.Next(weapon.AttackSpeed.MinValue, weapon.AttackSpeed.MaxValue)));
			}
			else if (equippable is ArmorTemplate armor)
			{
				attributes.Add(armor.ArmorBonus.Name, new ItemAttribute(armor.ArmorBonus.ID, random.Next(armor.ArmorBonus.MinValue, armor.ArmorBonus.MaxValue)));
			}
		}

		/// <summary>
		/// Adds random attributes from the template's random attribute databases.
		/// </summary>
		/// <param name="random">The random number generator.</param>
		/// <param name="equippable">The equippable item template.</param>
		private void AddRandomAttributes(System.Random random, EquippableItemTemplate equippable)
		{
			int attributeCount = random.Next(0, equippable.MaxItemAttributes);
			for (int i = 0; i < attributeCount; ++i)
			{
				var rng = random.Next(0, equippable.RandomAttributeDatabases.Length);
				ItemAttributeTemplateDatabase db = equippable.RandomAttributeDatabases[rng];
				rng = random.Next(0, db.Attributes.Count);
				ItemAttributeTemplate attributeTemplate = db.Attributes.Values.ToList()[rng];
				attributes.Add(attributeTemplate.Name, new ItemAttribute(attributeTemplate.ID, random.Next(attributeTemplate.MinValue, attributeTemplate.MaxValue)));
			}
		}

		/// <summary>
		/// Adds additional attributes defined in the base item template, merging with existing attributes if present.
		/// </summary>
		/// <param name="template">The item template.</param>
		private void AddAdditionalTemplateAttributes(BaseItemTemplate template)
		{
			foreach (var additionalAttribute in template.Attributes)
			{
				if (attributes.TryGetValue(additionalAttribute.Name, out ItemAttribute itemAttribute))
				{
					itemAttribute.value += additionalAttribute.MinValue;
				}
				else
				{
					attributes.Add(additionalAttribute.Name, new ItemAttribute(additionalAttribute.ID, additionalAttribute.MinValue));
				}
			}
		}

		/// <summary>
		/// Gets the generated attribute by name, or null if not found.
		/// </summary>
		/// <param name="name">The attribute name.</param>
		/// <returns>The ItemAttribute instance, or null.</returns>
		public ItemAttribute GetAttribute(string name)
		{
			attributes.TryGetValue(name, out ItemAttribute attribute);
			return attribute;
		}

		/// <summary>
		/// Sets the value of a generated attribute by name, and updates the character's attribute modifiers if equipped.
		/// </summary>
		/// <param name="name">The attribute name.</param>
		/// <param name="newValue">The new value to set.</param>
		public void SetAttribute(string name, int newValue)
		{
			if (attributes.TryGetValue(name, out ItemAttribute attribute))
			{
				if (attribute.value == newValue) return;

				int oldValue = attribute.value;
				attribute.value = newValue;

				// If the item is equipped, update the character's attribute modifiers
				if (item != null && item.IsEquippable && item.Equippable?.Character != null)
				{
					var character = item.Equippable.Character;
					if (character != null && character.TryGet(out ICharacterAttributeController attributeController))
					{
						int attrId = attribute.Template.CharacterAttribute.ID;
						if (attributeController.TryGetAttribute(attrId, out CharacterAttribute characterAttribute))
						{
							characterAttribute.AddModifier(-oldValue);
							characterAttribute.AddModifier(newValue);
						}
						else if (attributeController.TryGetResourceAttribute(attrId, out CharacterResourceAttribute characterResourceAttribute))
						{
							characterResourceAttribute.AddModifier(-oldValue);
							characterResourceAttribute.AddModifier(newValue);
						}
					}
				}
			}
		}

		/// <summary>
		/// Applies all generated attributes to the specified character, adding values to their stats/resources.
		/// </summary>
		/// <param name="character">The character to apply attributes to.</param>
		public void ApplyAttributes(ICharacter character)
		{
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}
			foreach (KeyValuePair<string, ItemAttribute> pair in attributes)
			{
				if (attributeController.TryGetAttribute(pair.Value.Template.CharacterAttribute.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.AddModifier(pair.Value.value);
				}
				else if (attributeController.TryGetResourceAttribute(pair.Value.Template.CharacterAttribute.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.AddModifier(pair.Value.value);
				}
			}
		}

		/// <summary>
		/// Removes all generated attributes from the specified character, subtracting values from their stats/resources.
		/// </summary>
		/// <param name="character">The character to remove attributes from.</param>
		public void RemoveAttributes(ICharacter character)
		{
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}
			foreach (KeyValuePair<string, ItemAttribute> pair in attributes)
			{
				if (attributeController.TryGetAttribute(pair.Value.Template.CharacterAttribute.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.AddModifier(-pair.Value.value);
				}
				else if (attributeController.TryGetResourceAttribute(pair.Value.Template.CharacterAttribute.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.AddModifier(-pair.Value.value);
				}
			}
		}
	}
}