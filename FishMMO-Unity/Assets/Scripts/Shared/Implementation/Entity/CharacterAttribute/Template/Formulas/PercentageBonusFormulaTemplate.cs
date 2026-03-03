using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "Percentage Bonus Formula", menuName = "FishMMO/Character/Attribute/Formula/Percentage Bonus Formula", order = 1)]
	public class PercentageBonusFormulaTemplate : CharacterAttributeFormulaTemplate
	{
		public float Percentage;

		public override int CalculateBonus(ICharacterAttributeController controller, CharacterAttribute self, CharacterAttribute bonusAttribute)
		{
			return (int)(bonusAttribute.FinalValue * Percentage);
		}
	}
}