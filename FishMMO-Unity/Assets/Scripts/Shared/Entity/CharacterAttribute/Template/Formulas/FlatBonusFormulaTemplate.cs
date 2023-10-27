using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "Flat Bonus Formula", menuName = "Character/Attribute/Formula/Percentage Bonus Formula", order = 1)]
	public class FlatBonusFormulaTemplate : CharacterAttributeFormulaTemplate
	{
		public override int CalculateBonus(CharacterAttribute self, CharacterAttribute bonusAttribute)
		{
			return bonusAttribute.FinalValue + bonusAttribute.FinalValue;
		}
	}
}