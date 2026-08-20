namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit stamina bar. Inherits resource bar logic from <see cref="UITKResourceBar"/>.
	/// </summary>
	public class UITKStaminaBar : UITKResourceBar
	{
		/// <inheritdoc />
		protected override string FillModifierClass => "fish-bar__fill--stam";

		/// <inheritdoc/>
		protected override string RootModifierClass => "res-bar--stam";
	}
}
