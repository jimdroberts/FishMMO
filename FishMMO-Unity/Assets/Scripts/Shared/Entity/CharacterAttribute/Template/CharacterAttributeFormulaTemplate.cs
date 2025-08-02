using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for defining formulas that calculate bonuses for character attributes.
	/// Inherit from this ScriptableObject to implement custom logic for how one attribute affects another.
	/// </summary>
	public abstract class CharacterAttributeFormulaTemplate : ScriptableObject
	{
		/// <summary>
		/// Calculates the bonus value that <paramref name="bonusAttribute"/> contributes to <paramref name="self"/>.
		/// Override this method to implement custom attribute interaction logic (e.g., how Strength increases Health).
		/// </summary>
		/// <param name="self">The attribute receiving the bonus (the owner).</param>
		/// <param name="bonusAttribute">The attribute providing the bonus (the child or dependency).</param>
		/// <returns>The calculated bonus value to apply to <paramref name="self"/>.</returns>
		public abstract int CalculateBonus(CharacterAttribute self, CharacterAttribute bonusAttribute);
	}
}