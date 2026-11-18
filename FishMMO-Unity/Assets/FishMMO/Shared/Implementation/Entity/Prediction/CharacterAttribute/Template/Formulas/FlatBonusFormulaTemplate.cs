using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "Flat Bonus Formula", menuName = "FishMMO/Character/Attribute/Formula/Flat Bonus Formula", order = 1)]
	/// <summary>
	/// Formula that returns the child attribute's final value as a flat bonus (1:1 ratio).
	/// </summary>
	public class FlatBonusFormulaTemplate : CharacterAttributeFormulaTemplate
	{
		/// <summary>Returns the child attribute's final value directly as a flat bonus.</summary>
		/// <param name="controller">The attribute controller managing this formula.</param>
		/// <param name="self">The attribute this formula belongs to.</param>
		/// <param name="bonusAttribute">The child attribute providing the bonus.</param>
		/// <returns>The child attribute's FinalValue added as a flat bonus.</returns>
		public override int CalculateBonus(ICharacterAttributeController controller, CharacterAttribute self, CharacterAttribute bonusAttribute)
		{
			return bonusAttribute.FinalValue;
		}
	}
}