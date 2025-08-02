using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Region action that applies a value to a character attribute or resource attribute when invoked.
	/// </summary>
	[CreateAssetMenu(fileName = "New Region Apply Character Attribute Action", menuName = "FishMMO/Region/Region Apply Character Attribute", order = 1)]
	public class RegionApplyCharacterAttributeAction : RegionAction
	{
		/// <summary>
		/// The attribute template to modify on the player character.
		/// </summary>
		public CharacterAttributeTemplate attribute;

		/// <summary>
		/// The value to add to the attribute or resource attribute.
		/// </summary>
		public int value;

		/// <summary>
		/// Invokes the region action, modifying the specified attribute or resource attribute on the player character.
		/// </summary>
		/// <param name="character">The player character whose attribute will be modified.</param>
		/// <param name="region">The region in which the action is triggered.</param>
		/// <param name="isReconciling">Indicates if the action is part of a reconciliation process.</param>
		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
			// Only proceed if the attribute and character are valid and the character has an attribute controller.
			if (attribute == null ||
				character == null ||
				!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			// If the attribute is a resource (e.g., health, mana), add to its current value.
			if (attributeController.TryGetResourceAttribute(attribute, out CharacterResourceAttribute r))
			{
				r.AddToCurrentValue(value);
			}
			// Otherwise, add a modifier to the standard attribute.
			else if (attributeController.TryGetAttribute(attribute, out CharacterAttribute c))
			{
				c.AddModifier(value);
			}
		}
	}
}