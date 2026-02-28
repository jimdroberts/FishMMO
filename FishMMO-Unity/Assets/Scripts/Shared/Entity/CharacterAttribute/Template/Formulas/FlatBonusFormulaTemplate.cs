using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "Flat Bonus Formula", menuName = "FishMMO/Character/Attribute/Formula/Flat Bonus Formula", order = 1)]
	public class FlatBonusFormulaTemplate : CharacterAttributeFormulaTemplate
	{
		public override int CalculateBonus(ICharacterAttributeController controller, CharacterAttribute self, CharacterAttribute bonusAttribute)
		{
			return bonusAttribute.FinalValue + bonusAttribute.FinalValue;
		}
	}
}