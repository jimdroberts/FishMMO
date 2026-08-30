using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FishMMO.Shared.Core;

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
		/// Populates the tooltip builder with generator information and all generated attributes.
		/// Uses <see cref="TooltipColors"/> for consistent formatting.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public void BuildTooltip(TooltipBuilder builder)
		{
			builder.AddLine($"Seed: {Seed}", 40, TooltipColors.Label);
			if (attributes.Count > 0)
			{
				builder.AddLine("Attributes", 50, TooltipColors.Label, false, "125%");
				int i = 0;
				foreach (ItemAttribute attribute in attributes.Values)
				{
					builder.AddLine($"{attribute.Template.Name}: <color={TooltipColors.Value}>{attribute.Value}</color>", 51 + i, null, false, "110%");
					i++;
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

			DeterministicRNG random = new DeterministicRNG(seed);

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
		private void GenerateItemAttributes(DeterministicRNG random, EquippableItemTemplate equippable)
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
		private void AddRandomAttributes(DeterministicRNG random, EquippableItemTemplate equippable)
		{
			int attributeCount = random.Next(0, equippable.MaxItemAttributes);
			for (int i = 0; i < attributeCount; ++i)
			{
				var rng = random.Next(0, equippable.RandomAttributeDatabases.Length);
				ItemAttributeTemplateDatabase db = equippable.RandomAttributeDatabases[rng];
				rng = random.Next(0, db.Attributes.Count);
				ItemAttributeTemplate attributeTemplate = db.Attributes.Values.ToList()[rng];

				/* Every draw is independent, so the same template can come up twice — and
				 * Dictionary.Add throws on a duplicate key. That exception escaped Item's
				 * constructor, and this constructor runs from EquipmentController.ReadPayload and
				 * from the equipment reconcile, so a duplicate roll took out the rest of the spawn
				 * payload or the reconcile body. Draw the value regardless of whether the slot is
				 * taken: the RNG stream has to advance identically on every peer, or the item this
				 * seed describes stops being the same item everywhere. */
				int rolledValue = random.Next(attributeTemplate.MinValue, attributeTemplate.MaxValue);
				if (attributes.ContainsKey(attributeTemplate.Name))
				{
					continue;
				}
				attributes.Add(attributeTemplate.Name, new ItemAttribute(attributeTemplate.ID, rolledValue));
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
					itemAttribute.Value += additionalAttribute.MinValue;
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
				if (attribute.Value == newValue) return;

				int oldValue = attribute.Value;
				attribute.Value = newValue;

				// If the item is equipped, update the character's attribute modifiers
				if (item != null && item.IsEquippable && item.Equippable?.Character != null)
				{
					var character = item.Equippable.Character;
					if (character != null && character.TryGet(out ICharacterAttributeController attributeController))
					{
						int attrId = attribute.Template.CharacterAttribute.ID;
						/* The item's whole contribution, restated. This used to be
						 * AddModifier(-oldValue) followed by AddModifier(newValue), which is only
						 * correct while oldValue is exactly what had been added — and nothing
						 * enforced that. Naming the source makes the old value irrelevant. */
						if (!TryResolveLedgerSource(attribute.Template.ID, out ModifierSource source))
						{
							return;
						}
						if (attributeController.TryGetAttribute(attrId, out CharacterAttribute characterAttribute))
						{
							characterAttribute.SetSource(source, newValue);
						}
						else if (attributeController.TryGetResourceAttribute(attrId, out CharacterResourceAttribute characterResourceAttribute))
						{
							characterResourceAttribute.SetSource(source, newValue);
						}
					}
				}
			}
		}

		/// <summary>
		/// The ledger key for this item's contribution, when it has one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>An item the database has not written yet does not contribute.</b> Its
		/// <see cref="Item.ID"/> is zero, and zero is not an identity — it is the absence of one. If
		/// two such items were both applied they would share the key <c>Item(0)</c>, and because
		/// <c>SetSource</c> STATES a contribution rather than adding to one, the second would
		/// silently replace the first and one item's bonus would vanish. Declining until the
		/// identity arrives is the only answer that cannot lose a bonus;
		/// <see cref="Item.AssignPersistentID"/> applies it the moment the id lands.
		/// </para>
		/// <para>
		/// <b>This is also what keeps an observer's sheet honest.</b> An observer builds its copy of
		/// a peer's equipment with no ids at all — <c>EquipmentController.WritePayload</c> does not
		/// send them to non-owners — so an observer now applies nothing, which is correct: the
		/// server's authoritative <c>ExternalModifier</c> already contains every equipped item's
		/// bonus and arrives through the attribute broadcast. Before this, the payload restore
		/// equipped for real and every equipped attribute read DOUBLED until
		/// <c>ReassertPayloadModifiers</c> re-installed the total on top of it.
		/// </para>
		/// <para>
		/// The cost is that a freshly created item equipped before its first persist returns carries
		/// no stats for that window — one database round trip. That is a visible-but-brief
		/// understatement, against a silent and permanent loss.
		/// </para>
		/// </remarks>
		/// <param name="attributeTemplateID">
		/// The <see cref="ItemAttributeTemplate"/> whose contribution is being keyed. One item is
		/// free to carry two attributes that raise the SAME character attribute — a weapon's base
		/// Attack Power plus a rolled Attack Power affix — and <c>SetSource</c> states a whole
		/// contribution rather than adding to one, so a single key per item kept only the last of
		/// them. The template id separates them and, unlike a list position, is stable and
		/// independent of the order <see cref="attributes"/> happens to enumerate in. See
		/// <see cref="ModifierSource.Index"/>.
		/// </param>
		/// <param name="source">The item's ledger key, when it has an identity.</param>
		/// <returns>True when this item may write to a ledger.</returns>
		private bool TryResolveLedgerSource(int attributeTemplateID, out ModifierSource source)
		{
			long id = item != null ? item.ID : 0;
			source = ModifierSource.Item(id, attributeTemplateID);
			return id > 0;
		}

		/// <summary>
		/// Applies all generated attributes to the specified character, adding values to their stats/resources.
		/// </summary>
		/// <remarks>
		/// Keyed by the item, so applying twice is applying once. A character loaded from the
		/// database, restored from a spawn payload and then corrected by a reconcile can reach this
		/// more than once for the same item; under the old accumulate-and-negate shape each arrival
		/// doubled the bonus and something downstream had to undo it.
		/// <para>
		/// Does nothing for an item with no identity yet — see <see cref="TryResolveLedgerSource"/>.
		/// </para>
		/// </remarks>
		/// <param name="character">The character to apply attributes to.</param>
		public void ApplyAttributes(ICharacter character)
		{
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}
			foreach (KeyValuePair<string, ItemAttribute> pair in attributes)
			{
				/* Keyed per ITEM ATTRIBUTE, not per item. Two of this item's attributes may raise
				 * the same character attribute, and a single key per item silently kept only the
				 * last of them. See TryResolveLedgerSource. */
				if (!TryResolveLedgerSource(pair.Value.Template.ID, out ModifierSource source))
				{
					return;
				}
				if (attributeController.TryGetAttribute(pair.Value.Template.CharacterAttribute.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.SetSource(source, pair.Value.Value);
				}
				else if (attributeController.TryGetResourceAttribute(pair.Value.Template.CharacterAttribute.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.SetSource(source, pair.Value.Value);
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
			/* Released, not negated. On a peer that never applied this item — an observer, or an
			 * owner whose ledger the reconcile had already restated — there is no entry and this is
			 * correctly a no-op. The negation it replaces subtracted regardless and drove the sheet
			 * below the server's number until the next authoritative push. */
			long itemID = item != null ? item.ID : 0;
			if (itemID <= 0)
			{
				// Nothing was ever written; see TryResolveLedgerSource.
				return;
			}

			/* Released by CONTRIBUTOR, not entry by entry. ApplyAttributes writes one ledger entry
			 * per item attribute (keyed by ItemAttributeTemplate.ID), and reproducing that key
			 * scheme here would mean the two halves must agree forever — including across a
			 * Generate() that rebuilt `attributes` from a new seed between the apply and the
			 * release, which is exactly what AssignPersistentID does. ClearSourceGroup drops
			 * everything this item holds whatever the entries were keyed as. */
			foreach (KeyValuePair<string, ItemAttribute> pair in attributes)
			{
				if (attributeController.TryGetAttribute(pair.Value.Template.CharacterAttribute.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.ClearSourceGroup(ModifierSourceKind.Item, itemID);
				}
				else if (attributeController.TryGetResourceAttribute(pair.Value.Template.CharacterAttribute.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.ClearSourceGroup(ModifierSourceKind.Item, itemID);
				}
			}
		}
	}
}