using System.Collections.Generic;

namespace FishMMO.Shared
{
	public abstract class ScrollConsumableTemplate : ConsumableTemplate
	{
		public List<BaseAbilityTemplate> AbilityTemplates;

		public override bool Invoke(Character character, Item item)
		{
			if (base.Invoke(character, item))
			{
				if (character.AbilityController != null)
				{
					character.AbilityController.LearnAbilities(AbilityTemplates);
				}
				return true;
			}
			return false;
		}
	}
}