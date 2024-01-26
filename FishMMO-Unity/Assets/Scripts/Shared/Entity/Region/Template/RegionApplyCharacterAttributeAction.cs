namespace FishMMO.Shared
{
	public class RegionApplyCharacterAttributeAction : RegionAction
	{
		public CharacterAttributeTemplate attribute;
		public int value;

		public override void Invoke(Character character, Region region)
		{
			if (attribute == null ||
				character == null ||
				!character.TryGet(out CharacterAttributeController attributeController))
			{
				return;
			}
			if (attributeController.TryGetResourceAttribute(attribute, out CharacterResourceAttribute r))
			{
				r.AddToCurrentValue(value);
			}
			else if (attributeController.TryGetAttribute(attribute, out CharacterAttribute c))
			{
				c.AddModifier(value);
			}
		}
	}
}