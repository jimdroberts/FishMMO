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
				if (character.TryGet(out IAbilityController abilityController))
				{
					abilityController.LearnBaseAbilities(AbilityTemplates);
				}
				return true;
			}
			return false;
		}
	}
}