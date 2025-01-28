using System.Collections.Generic;
using System.Linq;
using Cysharp.Text;
using System;

namespace FishMMO.Shared
{
	public class ItemGenerator : BaseRNGenerator
	{
		private Dictionary<string, ItemAttribute> attributes = new Dictionary<string, ItemAttribute>();

		private Item item;
		public event Action<ItemAttribute, int, int> OnSetAttribute;

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

		public override void Generate(int seed)
		{
			this.seed = seed;

			System.Random random = new System.Random(seed);
			if (random != null)
			{
				if (attributes != null)
				{
					attributes.Clear();
					attributes = new Dictionary<string, ItemAttribute>();

					EquippableItemTemplate Equippable = item.Template as EquippableItemTemplate;
					if (Equippable == null)
						return;

					WeaponTemplate weapon = item.Template as WeaponTemplate;
					if (weapon != null)
					{
						attributes.Add(weapon.AttackPower.Name, new ItemAttribute(weapon.AttackPower.ID, random.Next(weapon.AttackPower.MinValue, weapon.AttackPower.MaxValue)));
						attributes.Add(weapon.AttackSpeed.Name, new ItemAttribute(weapon.AttackSpeed.ID, random.Next(weapon.AttackSpeed.MinValue, weapon.AttackSpeed.MaxValue)));
					}
					else
					{
						ArmorTemplate armor = item.Template as ArmorTemplate;
						if (armor != null)
						{
							attributes.Add(armor.ArmorBonus.Name, new ItemAttribute(armor.ArmorBonus.ID, random.Next(armor.ArmorBonus.MinValue, armor.ArmorBonus.MaxValue)));
						}
					}

					if (Equippable.RandomAttributeDatabases != null && Equippable.RandomAttributeDatabases.Length > 0)
					{
						int attributeCount = random.Next(0, Equippable.MaxItemAttributes);
						for (int i = 0, rng; i < attributeCount; ++i)
						{
							rng = random.Next(0, Equippable.RandomAttributeDatabases.Length);
							ItemAttributeTemplateDatabase db = Equippable.RandomAttributeDatabases[rng];
							rng = random.Next(0, db.Attributes.Count);
							ItemAttributeTemplate attributeTemplate = Enumerable.ToList(db.Attributes.Values)[rng];
							attributes.Add(attributeTemplate.Name, new ItemAttribute(attributeTemplate.ID, random.Next(attributeTemplate.MinValue, attributeTemplate.MaxValue)));
						}
					}
				}
			}

			if (item != null &&
				item.Template != null)
			{
				for (int i = 0; i < item.Template.Attributes.Count; ++i)
				{
					ItemAttributeTemplate additionalAttribute = item.Template.Attributes[i];
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