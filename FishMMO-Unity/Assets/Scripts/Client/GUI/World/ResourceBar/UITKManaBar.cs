namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit mana bar. Inherits resource bar logic from <see cref="UITKResourceBar"/>.
	/// </summary>
	public class UITKManaBar : UITKResourceBar
	{
		/// <inheritdoc />
		protected override string FillModifierClass => "fish-bar__fill--mp";

		/// <inheritdoc/>
		protected override string RootModifierClass => "res-bar--mp";
	}
}
