namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a requirement for a quest based on a character attribute and minimum value.
	/// Used to check if a character meets the attribute requirement for quest progression.
	/// </summary>
	public class QuestAttributeRequirement
	{
		/// <summary>
		/// The attribute template to check against.
		/// </summary>
		public CharacterAttributeTemplate template;

		/// <summary>
		/// The minimum required value for the attribute.
		/// </summary>
		public long minRequiredValue;

		/// <summary>
		/// Checks if the provided character attributes meet the requirement.
		/// Returns true if the attribute exists and its final value is at least the minimum required value.
		/// </summary>
		/// <param name="characterAttributes">The character's attribute controller.</param>
		/// <returns>True if requirements are met, false otherwise.</returns>
		public bool MeetsRequirements(ICharacterAttributeController characterAttributes)
		{
			CharacterAttribute attribute;
			// Try to get the attribute by template ID and check its value.
			if (!characterAttributes.TryGetAttribute(template.ID, out attribute) || attribute.FinalValue < minRequiredValue)
			{
				return false;
			}
			return true;
		}
	}
}