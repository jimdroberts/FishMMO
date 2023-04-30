public class RegionApplyCharacterAttributeAction : RegionAction
{
	public CharacterAttributeTemplate attribute;
	public int value;

	public override void Invoke(Character character, Region region)
	{
		if (attribute == null || character == null || character.AttributeController == null)
		{
			return;
		}
		if (character.AttributeController.TryGetResourceAttribute(attribute, out CharacterResourceAttribute r))
		{
			r.AddToCurrentValue(value);
		}
		else if (character.AttributeController.TryGetAttribute(attribute, out CharacterAttribute c))
		{
			c.AddModifier(value);
		}
	}
}