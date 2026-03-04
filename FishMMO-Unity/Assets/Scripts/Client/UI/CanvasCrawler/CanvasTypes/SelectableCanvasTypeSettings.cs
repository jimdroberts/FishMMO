using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Applies ColorBlock theme colors and transition settings to any <see cref="Selectable"/>-based UI component.
	/// Enforces consistent color tint transitions with configurable fade duration.
	/// </summary>
	public sealed class SelectableCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Whether to also set the selected color on the ColorBlock.
		/// Used by Button and TMP_InputField which require a distinct selected state.
		/// </summary>
		private readonly bool applySelectedColor;

		/// <summary>
		/// Constructs a SelectableCanvasTypeSettings handler.
		/// </summary>
		/// <param name="applySelectedColor">When true, sets ColorBlock.selectedColor to the secondary color.</param>
		public SelectableCanvasTypeSettings(bool applySelectedColor = false)
		{
			this.applySelectedColor = applySelectedColor;
		}

		/// <summary>
		/// Applies theme colors, transition mode, and fade duration to the Selectable's ColorBlock.
		/// </summary>
		/// <param name="component">The UI component (must be a <see cref="Selectable"/>).</param>
		/// <param name="theme">The pre-parsed UI theme.</param>
		public override void ApplySettings(Component component, UITheme theme)
		{
			if (component is not Selectable selectable) return;

			selectable.transition = Selectable.Transition.ColorTint;

			ColorBlock cb = selectable.colors;
			cb.normalColor = theme.Primary;
			cb.pressedColor = theme.Secondary;
			cb.highlightedColor = theme.Highlight;
			cb.fadeDuration = theme.SelectableFadeDuration;

			if (applySelectedColor)
			{
				cb.selectedColor = theme.Secondary;
			}

			selectable.colors = cb;
		}
	}
}