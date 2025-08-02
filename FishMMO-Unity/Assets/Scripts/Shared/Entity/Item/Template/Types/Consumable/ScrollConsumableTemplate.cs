using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for scroll consumable templates, which grant abilities when consumed.
	/// Inherits from ConsumableTemplate and adds ability learning logic.
	/// </summary>
	public abstract class ScrollConsumableTemplate : ConsumableTemplate
	{
		/// <summary>
		/// The list of ability templates that will be learned when the scroll is consumed.
		/// </summary>
		public List<BaseAbilityTemplate> AbilityTemplates;

		/// <summary>
		/// Invokes the scroll consumption logic, granting abilities to the character if successful.
		/// Calls base Invoke to handle cooldown and charge logic.
		/// </summary>
		/// <param name="character">The player character consuming the scroll.</param>
		/// <param name="item">The scroll item being consumed.</param>
		/// <returns>True if the scroll was successfully consumed and abilities granted, false otherwise.</returns>
		public override bool Invoke(IPlayerCharacter character, Item item)
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