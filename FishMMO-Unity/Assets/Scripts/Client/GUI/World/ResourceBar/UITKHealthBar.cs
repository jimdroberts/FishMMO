namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit health bar. Inherits resource bar logic from <see cref="UITKResourceBar"/>.
	/// </summary>
	public class UITKHealthBar : UITKResourceBar
	{
		/// <inheritdoc />
		protected override string FillModifierClass => "fish-bar__fill--hp";
	}
}
