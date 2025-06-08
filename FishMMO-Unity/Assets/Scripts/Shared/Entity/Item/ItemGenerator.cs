using System.Collections.Generic;
using System.Linq;
using Cysharp.Text;
using System;
using UnityEngine;

namespace FishMMO.Shared
{
	public class ItemGenerator
	{
		protected int seed;

		public int Seed
		{
			get
			{
				return seed;
			}
			set
			{
				if (seed != value)
				{
					seed = value;
					Generate();
				}
			}
		}

		private Dictionary<string, ItemAttribute> attributes = new Dictionary<string, ItemAttribute>();

		private Item item;
		public event Action<ItemAttribute, int, int> OnSetAttribute;

		public Dictionary<string, ItemAttribute> Attributes { get { return attributes; } }

		public void Initialize(Item item, int seed)
		{
			this.item = item;
			Seed = seed;
		}

		public void Destroy()
		{
			item = null;
		}

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

		public void Generate()
		{
			Generate(seed);
		}

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

				if (template is EquippableItemTemplate equippable)
				{
					GenerateItemAttributes(random, equippable);

					if (equippable.RandomAttributeDatabases?.Length > 0)
					{
						AddRandomAttributes(random, equippable);
					}
				}
			}

			if (template != null)
			{
				AddAdditionalTemplateAttributes(template);
			}
		}

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

				OnSetAttribute?.Invoke(attribute, oldValue, newValue);
			}
		}

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
					characterAttribute.AddValue(pair.Value.value);
				}
				else if (attributeController.TryGetResourceAttribute(pair.Value.Template.CharacterAttribute.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.AddValue(pair.Value.value);
				}
			}
		}

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
					characterAttribute.AddValue(-pair.Value.value);
				}
				else if (attributeController.TryGetResourceAttribute(pair.Value.Template.CharacterAttribute.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.AddValue(-pair.Value.value);
				}
			}
		}
	}
}