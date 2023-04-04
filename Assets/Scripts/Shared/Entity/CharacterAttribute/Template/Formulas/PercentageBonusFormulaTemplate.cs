using UnityEngine;

[CreateAssetMenu(fileName = "Percentage Bonus Formula", menuName = "Character/Attribute/Formula/Percentage Bonus Formula", order = 1)]
public class PercentageBonusFormulaTemplate : CharacterAttributeFormulaTemplate
{
	public float Percentage;

	public override int CalculateBonus(CharacterAttribute self, CharacterAttribute bonusAttribute)
	{
		return (int)(bonusAttribute.FinalValue * Percentage);
	}
}