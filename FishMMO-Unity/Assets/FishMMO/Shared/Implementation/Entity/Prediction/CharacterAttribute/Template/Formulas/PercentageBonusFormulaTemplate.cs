using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "Percentage Bonus Formula", menuName = "FishMMO/Character/Attribute/Formula/Percentage Bonus Formula", order = 1)]
	/// <summary>
	/// Formula that returns a percentage of the child attribute's final value as a bonus.
	/// </summary>
	public class PercentageBonusFormulaTemplate : CharacterAttributeFormulaTemplate
	{
		/// <summary>The percentage multiplier applied to the child attribute's FinalValue (e.g., 0.5 = 50%).</summary>
		public float Percentage;

		/// <summary>Returns a percentage of the child attribute's final value as a bonus.</summary>
		/// <param name="controller">The attribute controller managing this formula.</param>
		/// <param name="self">The attribute this formula belongs to.</param>
		/// <param name="bonusAttribute">The child attribute providing the bonus.</param>
		/// <returns>The child attribute's FinalValue multiplied by <see cref="Percentage"/>.</returns>
		public override int CalculateBonus(ICharacterAttributeController controller, CharacterAttribute self, CharacterAttribute bonusAttribute)
		{
			return (int)(bonusAttribute.FinalValue * Percentage);
		}
	}
}