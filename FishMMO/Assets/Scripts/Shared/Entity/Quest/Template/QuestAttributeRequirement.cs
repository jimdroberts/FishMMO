public class QuestAttributeRequirement
{
	public CharacterAttributeTemplate template;
	public long minRequiredValue;

	public bool MeetsRequirements(CharacterAttributeController characterAttributes)
	{
		CharacterAttribute attribute;
		if (!characterAttributes.TryGetAttribute(template.Name, out attribute) || attribute.FinalValue < minRequiredValue)
		{
			return false;
		}
		return true;
	}
}