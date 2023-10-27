namespace FishMMO.Shared
{
	public abstract class ScrollConsumableTemplate : ConsumableTemplate
	{
		public AbilityTemplate[] AbilityTemplates;
		public AbilityEvent[] AbilityEvents;

		public override bool Invoke(Character character, Item item)
		{
			if (base.Invoke(character, item))
			{
				if (character.AbilityController != null)
				{
					character.AbilityController.LearnAbilityTypes(AbilityTemplates, AbilityEvents);
				}
				return true;
			}
			return false;
		}
	}
}