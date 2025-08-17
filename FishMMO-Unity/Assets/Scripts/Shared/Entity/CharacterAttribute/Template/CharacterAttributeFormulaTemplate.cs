namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for defining formulas that calculate bonuses for character attributes.
	/// Inherit from this FormulaTemplate to implement custom logic for how one attribute affects another.
	/// </summary>
	public abstract class CharacterAttributeFormulaTemplate : FormulaTemplate<CharacterAttribute>
	{
	}
}