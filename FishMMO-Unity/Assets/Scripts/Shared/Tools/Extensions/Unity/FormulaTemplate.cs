using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for defining formulas that calculate bonuses for types.
	/// Inherit from this ScriptableObject to implement custom logic for how one type affects another.
	/// </summary>
	public abstract class FormulaTemplate<T> : ScriptableObject
	{
		/// <summary>
		/// Calculates the bonus value that <paramref name="bonus"/> contributes to <paramref name="self"/>.
		/// </summary>
		/// <param name="self">The type receiving the bonus.</param>
		/// <param name="bonus">The type providing the bonus.</param>
		/// <returns>The calculated bonus value to apply to <paramref name="self"/>.</returns>
		public abstract int CalculateBonus(T self, T bonus);
	}
}